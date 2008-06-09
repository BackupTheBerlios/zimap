//==============================================================================
// ZIMapConnection.cs implements the ZIMapConnection class
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU Lesser General Public License
// This software is published under the GNU LGPL license. Please refer to the
// files COPYING and COPYING.LESSER for details. Please use the following e-mail
// address to contact me or to send bug-reports:  info at j-pfennig dot de .
#endregion

using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;

namespace ZIMap
{
    /// <summary>
    /// A class that implements the IMAP4rev1 (RFC3501) low level transport.
    /// See: http://www.faqs.org/rfcs/rfc3501.html
    /// </summary>
    /// <remarks>
    /// <para>The problem with RFC3501 is that it mixes two types of data:
    /// 7-bit ASCII (atoms, atom-strings, quoted strings) and binary (literal).
    /// </para>
    /// <para>
    /// On this level most of the handling for mixing text and literals is
    /// implemented. Please note that while the server can send literals at any
    /// time, the client has to announce literal data before it can be sent.
    /// So sending literals must be partially implemented on the next higher
    /// protocol level.
    /// </para>
    /// <para />Another feature that gets implemented here is background reading.
    /// The code uses a separate worker thread (via a delegate) to read from the
    /// server. Please read the warning at <see cref="Poll"/>.
    /// <para />
    /// Limits: Literal data sent by an IMAP server can be large (many Megabytes).
    /// This implementation keeps this data in memory. There are no attempts made
    /// to limit the amount of buffered literals. Your system may go out of
    /// memory.
    /// </remarks>
    /// <example lang="C#">
    /// Just login and fetch a message - all error handling ignored ...
    /// <code>
    /// | public class TestTransport : ZIMapBase
    /// | {
    /// |     public class Transport : ZIMapTransport
    /// |     {
    /// |         // base has no def xtor ...
    /// |         public Transport(ZIMapBase parent) : base(parent)
    /// |         {  Setup(parent.sock, parent.strm, 0);
    /// |         }
    /// |
    /// |         // must implement, abstract in base ...
    /// |         public override void Monitor(ZIMapMonitor item, string message)
    /// |         {   Console.WriteLine("Monitor {0}: {1}", item.ToString(), message);
    /// |         }
    /// |     }
    /// |
    /// |     public const int      port = 143;                 // IMAP default port
    /// |     public const string   server = "alpha";           // Test host
    /// |     public const string   user = "internet";          // username
    /// |     public const string   passwd = "xxx";             // password
    /// |     public const string   mbox = "inbox";             // mailbox
    /// |
    /// |     public Transport      tran;
    /// |     public Socket         sock;
    /// |     public NetworkStream  strm;
    /// |
    /// |     public void Init()
    /// |     {   sock = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
    /// |                                              System.Net.Sockets.SocketType.Stream,
    /// |                                              System.Net.Sockets.ProtocolType.IP);
    /// |         sock.Connect(server, port);
    /// |         strm = new System.Net.Sockets.NetworkStream(sock);
    /// |         tran = new Transport(this);
    /// |     }
    /// |
    /// |     public void Test()
    /// |     {   // read server greeting...
    /// |         string tag, status, message;
    /// |         tran.Receive(out tag, out status, out message);
    /// |
    /// |         // send login, read reply ...
    /// |         tran.Send(1, string.Format("LOGIN {0} \"{1}\"", user, passwd));
    /// |         tran.Receive(out tag, out status, out message);
    /// |
    /// |         // select mailbox, read reply ...
    /// |         tran.Send(2, string.Format("SELECT {0}", mbox));
    /// |         while(true)
    /// |         {   tran.Receive(out tag, out status, out message);
    /// |             if(tag != "*") break;
    /// |         }
    /// |
    /// |         // fetch a mail ...
    /// |         tran.Send(3, "FETCH 1 body[text]");
    /// |         byte [][] literals;
    /// |         while(true)
    /// |         {   tran.Receive(out tag, out status, out message, out literals);
    /// |             if(tag != "*") break;
    /// |         }
    /// |     }
    /// |
    /// |     public static void Main(string[] args)
    /// |     {   TestTransport x = new TestTransport();
    /// |         x.Init();
    /// |         x.Test();
    /// |     }
    /// | }
    /// </code>
    /// </example>
    public abstract class ZIMapTransport : ZIMapBase
    {
        // Fields set by Setup() ...

        // The socket on which the stream operates
        private System.Net.Sockets.Socket socket;
        // Network or TLS stream for read/write
        private Stream  stream;
        // Timeout in seconds
        private uint    timeout;
        // Set by Send() for a new Request
        private bool    startRequest;
        // Set by Poll(), cleared by Send()
        private bool    haveTimeout;
        // set by Send() for SELECT/EXAMINE
        private uint    selectTag;

        private readonly char[] space_array = { ' ' };

        /// <summary>
        /// Constructs an instance ready to talk to an IMap server.
        /// </summary>
        /// <param name="parent">
        /// The owner if this instance, usually a <see cref="ZIMapConnection"/>.
        /// </param>
        /// <remarks>
        /// The real initialization work is done by <see cref="Setup"/>.
        /// </remarks>
        public ZIMapTransport(ZIMapBase parent) : base(parent) {}

        // =====================================================================
        // Status
        // =====================================================================

        /// <summary>
        /// Can be used to check if the server has closed the connection
        /// </summary>
        /// <value>
        /// After <see cref="Close"/> has been called <c>true</c> is returned.
        /// </value>
        /// <remarks>
        /// The <see cref="ReaderLine"/> method calls <see cref="Close"/> before
        /// it returns <c>null</c> to indicate that the server has closed the
        /// connection.
        /// </remarks>
        public bool IsClosed
        {   get {   return stream == null; }
        }

        /// <summary>
        /// Can be used to check if the last error was a timeout
        /// </summary>
        /// <value>A value of <c>true</c> indicates a timeout.
        /// <remarks>Timeouts are usually detected by <see cref="Poll"/> because the
        /// timeout on socket level will be disabled after the 1st message received.
        /// </remarks>
        public bool IsTimeout
        {   get {   return haveTimeout; }
        }

        /// <summary>
        /// Can be used to check for queued server response data
        /// </summary>
        /// <value>A value of <c>true</c> indicates that a server reply is queued.
        /// This does not always indicate that Receive calls are non-blocking, because
        /// literal data may still be pending.</value>
        /// <remarks>This property is a shortcut for calling <see cref="Poll"></see>
        /// with <c>0</c> as argument.</remarks>
        public bool IsReady
        {   get {   return Poll(0); }
        }

        /// <summary>
        /// This property can be used be higher IMap implementation levels to check
        /// if the current Mailbox was changed.
        /// </summary>
        /// <value>
        /// Returns the tag number of the last SELECT or EXAMINE
        /// </value>
        /// <remarks>
        /// This value is set by <see cref="Send(uint, string)"/>.
        /// </remarks>
        public uint LastSelectTag
        {   get {   return selectTag;   }
            set {   selectTag = 0;
                    if(value != 0) Error(ZIMapErrorCode.MustBeZero);
                }
        }

        /// <summary>
        /// Closes the connection and the underlying stream.
        /// </summary>
        /// <remarks>
        /// The <see cref="ReaderLine"/> method calls <see cref="Close"/> before
        /// it returns <c>null</c> to indicate that the server has closed the
        /// connection.
        /// </remarks>
        public void Close()
        {   if(stream == null) return;
            Monitor(ZIMapMonitor.Debug, "Close: connection closed");
            stream.Close(); stream = null;
        }

        /// <summary>
        /// Can be used to wait a short time for server responses to become available.
        /// </summary>
        /// <param name="waitMs">
        /// The maximum time to wait in [ms]. A value of <c>0</c> will
        /// return immedeately.
        /// </param>
        /// <returns>
        /// - <c>true</c> if data is queued. This does not always indicate that Receive
        /// calls are non-blocking, because literal data may still be pending.
        /// <para>- <c>false</c> is returned after a timeout.</para>
        /// </returns>
        /// <remarks>
        /// This method does not get interrupted by timeouts from the IMAP TCP/IP stream.
        /// It should also be noted here that the background reader consumes a worker
        /// thread that is kept alife while this routine is waiting. The number of worker
        /// threads that are available under Windows is limited (around 25 per cpu core).
        /// <para />
        /// After a call to <see cref="Close"/> the routine always returns
        /// <c>false</c>.</remarks>
        public bool Poll(uint waitMs)
        {
            uint ms = 600000;                           // 10 minutes
            if(ms > waitMs) ms = waitMs;
            ms = 10;

            while(true)
            {   if(ReaderPoll(false, false))            // see comment WaitOne()
                                        return true;
                if(stream == null)      return false;
                if(waitMs == 0)         return false;

                // check background status - must lock!
                lock(rdr_lines)
                {   if (rdr_lines.Count > 0)            // has line in buffer
                        return true;
                    rdr_wait = true;                    // request reader to stop
                }

                // wait for reader to exit - must call ReaderPoll() next !!!
                Monitor(ZIMapMonitor.Debug, "Poll: sleeping " + ms);
                rdr_res.AsyncWaitHandle.WaitOne((int)ms, false);
                if(ms > waitMs) waitMs = 0;
                else            waitMs -= ms;
                if(ms < 200)    ms += ms;
            }
        }

        // =====================================================================
        // Receiving
        // =====================================================================

        // internal helper for the public overloads of Receive() ...
        private bool Receive(out string fragment, out byte[] literal)
        {   fragment = null;
            literal = null;

            // implement a blocking read ...
            while(!ReaderLine(out fragment))        // wait for data
            {   if(!ReaderPoll(true, false))        // EOF or worse...
                    return false;
            }

            // Clear Socket timeouts after the 1st read
            if(socket.ReceiveTimeout > 0)
            {   socket.ReceiveTimeout = -1;
                socket.SendTimeout = -1;
            }

            // Read literal data
            int ilen = fragment.Length;
            if(ilen > 2 && fragment[ilen-1] == '}') // fragment.EndsWith("}")
            {   if(!ReaderData(out literal))
                {   Monitor(ZIMapMonitor.Error, "Receive: literal expected");
                    return false;
                }
                Monitor(ZIMapMonitor.Debug, "Receive: literal size=" + literal.Length);
            }
            return true;
        }

        /// <summary>
        /// Get a server response that can contain literal data.
        /// </summary>
        /// <param name="tag">
        /// The tag that the server sent. Special tags are <c>*</c> for untagged
        /// messages and <c>+</c> (continue) for sending literals.
        /// </param>
        /// <param name="status">
        /// For tagged responses the returned values should be <c>OK</c>, <c>BAD</c>
        /// or <c>NO</c>.
        /// </param>
        /// <param name="message">
        /// The response text (if any).
        /// </param>
        /// <param name="literalarray">
        /// Set to an array of literals if the server sent one or more literals,
        /// and <c>null</c> if the response did not contain literals.
        /// </param>
        /// <returns>
        /// - <c>true</c> on success.
        /// <para />
        /// - <c>false</c> on EOF -or- timeout. In this case tag, status and
        /// message are <c>null</c>.
        /// </returns>
        /// <remarks>
        /// The call is blocking, see <see cref="IsReady"/> and <see cref="Poll"/>.
        /// The underlying IMAP TCP/IP stream may have a timeout set, which can
        /// (when expired) cause this routine to return from blocking state. A
        /// timeout will most likely cause confusion while reading literals - be
        /// carefull when trying to recover.
        /// </remarks>
        public bool Receive(out string tag, out string status,
                            out string message, out byte[][] literalarray)
        {   tag = status = message = null;
            literalarray = null;

            // blocking read for the 1st fragment...
            string fragment;
            byte[] literal;
            if(!Receive(out fragment, out literal))     // EOF or worse...
                return false;

            string[] arr = fragment.Split(space_array, 3);

            // tag special cases: 0 -> * and return interned value ...
            tag = arr[0];
            if     (tag == "*") tag = "*";
            else if(tag == "0") tag = "*";

            // status and message, check if strings are interned ...
            status  = (arr.Length > 1) ? arr[1] : "";
            message = (arr.Length > 2) ? arr[2] : "";
            ZIMapMonitor llev = (tag == "*") ? ZIMapMonitor.Debug : ZIMapMonitor.Info;
            if(status == "")
                Monitor(ZIMapMonitor.Error, "Receive: empty message");
            else
            {   string sint = string.IsInterned(status);
                if(sint != null) status = sint;
            }
            if(message != "")
            {   string sint = string.IsInterned(message);
                if(sint != null) message = sint;
            }
            if(literal == null)
            {   if(MonitorLevel <= llev) 
                    Monitor(llev, "Receive: message: " + fragment);
                return true;
            }

            // handling of literals...
            List<byte[]> literals = new List<byte[]>();
            while(literal != null)
            {   literals.Add(literal);
                if(!Receive(out fragment, out literal)) // at least ""
                {   Monitor(ZIMapMonitor.Error, "Receive: EOF in literal");
                    break;
                }

                if(fragment == "")                      // must be the end
                {   if(literal != null)                 // implementation bug
                        Monitor(ZIMapMonitor.Error, "Receive: internal error");
                }
                else
                    message += fragment;
            }
            Monitor(llev, "Receive: message: " + message);
            literalarray = literals.ToArray();
            return true;
        }

        /// <summary>
        /// An overload of Receive() that throws away literal data.
        /// </summary>
        /// <param name="tag">
        /// The tag that the server sent. Special tags are <c>*</c> for untagged
        /// messages and <c>+</c> (continue) for sending literals.
        /// </param>
        /// <param name="status">
        /// For tagged responses the returned values should be <c>OK</c>, <c>BAD</c>
        /// or <c>NO</c>.
        /// </param>
        /// <param name="message">
        /// The response text (if any).
        /// </param>
        /// <returns>
        /// - <c>true</c> on success.
        /// <para />
        /// - <c>false</c> on EOF -or- timeout. In this case tag, status and
        /// message are <c>null</c>.
        /// </returns>
        /// <remarks>
        /// The call is blocking, see <see cref="IsReady"/> and <see cref="Poll"/>.
        /// The underlying IMAP TCP/IP stream may have a timeout set, which can
        /// (when expired) cause this routine to return from blocking state. A
        /// timeout will most likely cause confusion while reading literals - be
        /// carefull when trying to recover.
        /// </remarks>
        public bool Receive(out string tag, out string status, out string message)
        {   tag = status = message = null;
            byte[][] literals;
            if(!Receive(out tag, out status, out message, out literals))
                return false;
            if(literals != null)
                Monitor(ZIMapMonitor.Error, "Receive: literal data ignored");
            return true;
        }

        // =====================================================================
        // Sending
        // =====================================================================

        /// <summary>
        /// Sends a message tag followed by a text.
        /// </summary>
        /// <param name="tag">
        /// The tag number -or- <c>0</c> to send an untagged message.
        /// </param>
        /// <param name="message">
        /// The message text.
        /// </param>
        /// <returns>
        /// - <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// CR/LF are automatically appended. This is also correct if the message
        /// text ends with "{nnn}" to indicate that the client wants to send a
        /// literal. The command flushes the stream.
        /// <para />
        /// This overload of Send is aware of some special commands:
        /// <list type="table">
        /// <item>
        /// <term>SELECT</term><description>stores the tag value for
        /// <see cref="LastSelectTag"/></description>
        /// <term>EXAMINE</term><description>stores the tag value for
        /// <see cref="LastSelectTag"/></description>
        /// <term>CLOSE</term><description>stores the tag value for
        /// <see cref="LastSelectTag"/></description>
        /// <term>STARTTLS</term><description>Enters a special single
        /// line read mode</description>
        /// </item>
        /// </list>
        /// </remarks>
        public bool Send(uint tag, string message)
        {
            if(message == null || message.Length < 1)
            {   Error(ZIMapErrorCode.InvalidArgument, "No command to send");
                return false;
            }

            // Extras for the LastSelectTag property and STARTTLS
            int ibrk = message.IndexOf(' ');
            string cmd = ibrk > 0 ? message.Substring(0, ibrk) : message;
            cmd = cmd.ToUpper();
            if(cmd == "SELECT" || cmd == "EXAMINE" || cmd == "CLOSE")
                selectTag = tag;
            else if (cmd == "STARTTLS")             // enter single line mode
                ReaderStop(0);

            // send to command ...
            System.Text.StringBuilder sb = new System.Text.StringBuilder(message.Length + 12);
            if(tag == 0)
                sb.Append("* ");
            else
                sb.AppendFormat("{0:x} ", tag);
            sb.Append(message);
            startRequest = true;
            bool bok = Send(sb.ToString());

            if(cmd == "STARTTLS") ReaderStop(1);    // extras for STARTTLS
            return bok;
        }

        /// <summary>
        /// Sends a message fragment, usually after a literal.
        /// </summary>
        /// <param name="fragment">
        /// The message text (usually starting whith a space if the command continues
        /// after a literal).
        /// </param>
        /// <returns>
        /// - <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// CR/LF are automatically appended. This is also correct if the message
        /// text ends with "{nnn}" to indicate that the client wants to send
        /// another literal. The command flushes the stream.
        /// </remarks>
        public bool Send(string fragment)
        {   if(fragment == null)
            {   Error(ZIMapErrorCode.MustBeNonZero);
                return false;
            }
            if(stream == null)
            {   Monitor(ZIMapMonitor.Error, "Send: closed");
                return false;
            }

            if(fragment.Length > 0)
            {   if(this.MonitorLevel <= ZIMapMonitor.Info)
                    Monitor(ZIMapMonitor.Info, (startRequest ?
                        "Send: request: " : "Send: fragment: ") + fragment);
                byte[] data = System.Text.ASCIIEncoding.ASCII.GetBytes(fragment);
                stream.Write(data, 0, data.Length);
            }
            else
                Monitor(ZIMapMonitor.Info, "Send: void");
            startRequest = false;
            haveTimeout = false;
            stream.WriteByte(13);
            stream.WriteByte(10);
            stream.Flush();
            return true;
        }

        /// <summary>
        /// Sends binary data, usually a literal.
        /// </summary>
        /// <param name="data">
        /// The data.
        /// </param>
        /// <returns>
        /// - <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// No CR/LF are automatically appended and the stream is not flushed.
        /// A client must at least call <c>Send("")</c> to complete the command
        /// and to flush the stream.
        /// </remarks>
        public bool Send(byte[] data)
        {   if(stream == null)
            {   Monitor(ZIMapMonitor.Error, "Send: closed");
                return false;
            }
            Monitor(ZIMapMonitor.Info, "Send: " + data.Length + " bytes");
            stream.Write(data, 0, data.Length);
            return true;
        }

        // =====================================================================
        // Support for async Receive
        // =====================================================================

        private delegate void   Reader();               // calls ReaderImpl()
        private Reader          rdr_dlg;                // for backgnd reading
        private IAsyncResult    rdr_res;                // status of rdr_dlg
        private List<object>    rdr_lines;              // received text/literals
        private bool            rdr_wait;               // forces rdr_dlg to complete
        private byte[]          rdr_buffer = null;      // line-breaking buffer
        private int             rdr_bused  = 0;         // bytes in use

        /// <summary>
        /// A helper called asynchronously from a delegate to read server
        /// responses in background.
        /// </summary>
        /// <remarks>
        /// The routine stores received lines or literals as strings or byte arrays
        /// in a list (rdr_lines). The access to this list is threadsave. When the
        /// input stream is closed the routine stores a <c>null</c> item in this list
        /// and returns.
        /// <para>
        /// The flag rdr_wait can be set from another thread to make the
        /// routine return after a line was read. This is used to implement
        /// blocking reads.</para>
        /// <para>
        /// Unfortunately we must be able to read binary data (literals), so we
        /// have to implement our own line-breaking algorithm instead of using a
        /// TextReader.</para>
        /// </remarks>
        protected void ReaderImpl()
        {   //Monitor(ZIMapMonitor.Debug, "ReaderImpl: start");
            if(stream == null) return;                  // called after close()

            bool bquit = false;                         // return from delegate req.
            int ndone = 0;                              // skip leading bytes
            int nlast;                                  // last byte not LF
            int nsize;                                  // current buffer size
            const int ninit = 2048;                     // initial buffer size (2**n)

            while(!bquit)
            {   nlast = rdr_bused;                      // no LF in buffer!

                // initial buffer allocation
                if(rdr_buffer == null)
                {   ndone = nlast = rdr_bused = 0; nsize = ninit;
                    rdr_buffer = new byte[nsize];
                }
                else
                    nsize = rdr_buffer.Length;

                // let buffer grow as needed
                if(rdr_bused >= nsize)
                {   nsize *= 4;
                    Array.Resize(ref rdr_buffer, nsize);
                    Monitor(ZIMapMonitor.Debug, "ReaderImpl: buffer size=" + nsize.ToString());
                }

                // read from socket

                int bcnt = ReaderRead(rdr_buffer, rdr_bused, nsize-rdr_bused);
                if(bcnt < 0)                            // exception (timeout)
                     break;
                if(bcnt == 0)                           // closed...
                {   lock(rdr_lines)
                    {   rdr_lines.Add(null);
                    }
                    Monitor(ZIMapMonitor.Info, "ReaderImpl: connection closed");
                    return;
                }
                rdr_bused += bcnt;

                // search for line breaks ...
                while(nlast < rdr_bused)
                {   bcnt = Array.IndexOf<byte>(rdr_buffer, (byte)10, nlast, rdr_bused-nlast);
                    if(bcnt < 0) break;                 // no more line ...

                    // extract line ...
                    nlast = bcnt + 1;                   // start index of next line
                    if(bcnt > 0 && rdr_buffer[bcnt-1] == (byte)13)
                        bcnt--;                         // remove the CR of CR/LF
                    string line = System.Text.ASCIIEncoding.ASCII.GetString(rdr_buffer, ndone, bcnt-ndone);
                    ndone = nlast;

                    // does the server announce literal data?
                    byte[] data = null;                 // for literal data
                    if(line.EndsWith("}"))
                    {   bcnt = line.LastIndexOf('{');
                        if(bcnt >= 0)
                        {   string num = line.Substring(bcnt+1, line.Length-bcnt-2);
                            if(num != "" && int.TryParse(num, out bcnt))
                            {   data = new byte[bcnt];
                                int bdata = 0;
                                int bleft = rdr_bused - ndone;
                                if(bleft > bcnt) bleft = bcnt;
                                Monitor(ZIMapMonitor.Debug, "ReaderImpl: literal of size " + bcnt);

                                // copy data that is still in the buffer...
                                if(bleft > 0)
                                {   Array.Copy(rdr_buffer, ndone, data, bdata,  bleft);
                                    bcnt -= bleft; bdata += bleft; ndone += bleft;
                                    nlast = ndone;                      // skip for CR/LF search
                                }

                                // read more data from server...
                                while(bcnt > 0)
                                {   nlast = ndone = rdr_bused;          // no data left in buffer
                                    bleft = ReaderRead(data, bdata, bcnt);
                                    if(bleft <= 0)
                                    {   data = null;
                                        Monitor(ZIMapMonitor.Error, "ReaderImpl: truncated literal");
                                        break;
                                    }
                                    bcnt -= bleft; bdata += bleft;
                                }
                            }
                        }
                    }

                    // anything left in the buffer? Reset or delete buffer if no...
                    if(ndone >= rdr_bused)
                    {   ndone = rdr_bused = 0;
                        if(nsize > ninit) rdr_buffer = null;
                    }
                    //Monitor(ZIMapMonitor.Debug, "ReaderImpl: line: '" + line + "'");

                    // add text (and literal) to the data list ...
                    lock(rdr_lines)
                    {   rdr_lines.Add(line);
                        if(data != null) rdr_lines.Add(data);
                        bquit = rdr_wait;               // request to complete?
                    }
                }

                // Remove any consumed bytes from the buffer ...
                if(ndone > 0)
                {   bcnt = rdr_bused - ndone;           // remaining byte count
                    nsize = (bcnt + ninit) & ~(ninit - 1);
                    byte[] curr = rdr_buffer;
                    rdr_buffer = new byte[nsize];
                    Array.Copy(curr, ndone, rdr_buffer, 0, bcnt);
                    ndone = 0; rdr_bused = bcnt;
                }
            }
            Monitor(ZIMapMonitor.Debug, "ReaderImpl: quit");
        }

        // Wrapper mainly to catch timeouts
        private int ReaderRead(byte[] buffer, int offs, int length)
        {
            try
            {   return stream.Read(buffer, offs, length);
            }
            catch(Exception ex)
            {   // HACK: ms leaves socket non-blocking after timeout
                socket.Blocking = true;

                if(rdr_dlg == null)
                {   Monitor(ZIMapMonitor.Debug, "ReaderRead: Reader was stopped");
                    return -1;
                }
                Exception inner = ex.InnerException;
                if(inner.InnerException != null && inner is IOException) inner = inner.InnerException;
                if(inner is System.Net.Sockets.SocketException &&
                   (inner.Message.Contains("timed out") ||
                    inner.Message.Contains("period of time")))
                {   haveTimeout = true;
                    Monitor(ZIMapMonitor.Debug, "ReaderRead: Timeout");
                }
                else
                {   Monitor(ZIMapMonitor.Error, "ReaderRead: exception: " + ex.Message);
                    if(inner != null && ex.Message != inner.Message)
                        Monitor(ZIMapMonitor.Info, "ReaderRead: exception: " + inner.Message);
                }
                return -1;
            }
        }

        /// <summary>
        /// Get the next buffered server response or return an error
        /// status if nothing is buffered.
        /// </summary>
        /// <remarks>
        /// This routine will never wait for response data, see ReaderPoll()
        /// instead. The routine returns only string data, see ReaderData()
        /// on how to read binary data (literals).
        /// <para>This routine will call <see cref="Close"/> when it detects
        /// that the server has closed the connection. In this case the return
        /// value is <c>true</c> but the line parameter returns <c>null</c>.</para>
        /// </remarks>
        /// <param name="line">
        /// Returns the response string - can be null on error or when the
        /// connection is closed.
        /// </param>
        /// <returns>
        /// - Returns <c>true</c> to indicate that data was read -or- that the
        /// connection just got closed.
        /// <para>
        /// - When called after a call to <see cref="Close"/> this routine always returns
        /// <c>false</c>.</para>
        /// </returns>
        protected bool ReaderLine(out string line)
        {   line = null;
            if(rdr_dlg == null)  return false;      // no init, see ReaderPoll()
            if(stream == null)   return false;      // called after close()

            // check background status - must lock!
            object data = null;
            lock(rdr_lines)
            {   if(rdr_lines.Count == 0)            // no data
                    return false;
                data = rdr_lines[0];
                rdr_lines.RemoveAt(0);
            }

            // check the returned data item ...
            if(data == null)                        // EOF detected
                Close();
            else
            {   line = data as string;
                if(line == null)
                {   Monitor(ZIMapMonitor.Error, "ReaderLine: unexpected literal");
                    return false;                   // handle like 'no data'
                }
            }
            return true;                            // have data (or EOF)
        }

        /// <summary>
        /// Retrieves literal data from the receiver list.
        /// </summary>
        /// <remarks>A call to <see cref="ReaderLine"/> must be used to read string
        /// data. This routine will not discard string data, so it might be used to
        /// check if a string is followed by a literal.</remarks>
        /// <param name="data">
        /// Returns an IMap literal -or- null on EOF.
        /// </param>
        /// <returns>
        /// - <c>true</c> on success (even on the EOF).
        /// <para />
        /// - <c>false</c> if there is no literal data (timeout) -or-
        /// after <see cref="Close"/>.
        /// </returns>
        protected bool ReaderData(out byte[] data)
        {   data = null;
            if(rdr_dlg == null)  return false;      // no init, see ReaderPoll()
            if(stream == null)   return false;      // called after close()

            // check background status - must lock!
            lock(rdr_lines)
            {   if(rdr_lines.Count == 0)            // no data
                    return false;
                data = rdr_lines[0] as byte[];
                if(data == null)                    // not a literal
                    return false;
                rdr_lines.RemoveAt(0);
            }
            return true;                            // have data (or EOF)
        }

        /// <summary>
        /// Checks if received data is queued and optionally waits.
        /// </summary>
        /// <param name="bWait">
        /// A value of <c>true</c> makes this call blocking.
        /// </param>
        /// <param name="bSingle">
        /// A value of <c>true</c> causes the reader thread to stop
        /// after the next line received (single line mode).
        /// </param>
        /// <returns>
        /// - <c>true</c> if data is queued.
        /// </returns>
        /// <remarks>
        /// The underlying IMAP TCP/IP stream may have a timeout set, which can
        /// (when expired) cause this routine to return from blocking state.
        /// </remarks>
        protected bool ReaderPoll(bool bWait, bool bSingle)
        {   if(stream == null) return false;        // called after close()

            // initialize the reader
            if(rdr_dlg == null)                     // see also ReaderStop()
            {   if(rdr_lines == null)               // only once
                    rdr_lines = new List<object>();
                rdr_dlg = new Reader(ReaderImpl);
                Monitor(ZIMapMonitor.Debug, "ReaderPoll: Initialise Reader");
                rdr_res = rdr_dlg.BeginInvoke(null, null);
            }

            // check background status - must lock!
            lock(rdr_lines)
            {   if(rdr_res.IsCompleted && stream != null)
                {   Monitor(ZIMapMonitor.Debug, "ReaderPoll: Continue Reader");
                    rdr_dlg.EndInvoke(rdr_res);     // would re-throw exception
                    rdr_res = rdr_dlg.BeginInvoke(null, null);
                }
                
                bool bok = rdr_lines.Count > 0;
                if(bSingle) 
                {   rdr_wait = true;                // single line: request stop
                    if(bok) return true;
                }
                else
                {   if(bok) return true;
                    rdr_wait = bWait;               // for wait: request stop
                }
            }

            // lock released - have no input
            if(!bWait)                              // no wait
                return false;

            // wait for the reader thread
            if(timeout == 0)
                rdr_res.AsyncWaitHandle.WaitOne();  // wait forever
            else
                if(!rdr_res.AsyncWaitHandle.WaitOne((int)timeout*1000, false))
                {   Monitor(ZIMapMonitor.Error, "ReaderPoll: Timeout ");
                    haveTimeout = true;
                    return false;
                }

            // restart reader (recurse without wait) ...
            return ReaderPoll(false, bSingle);
        }

        // ---------------------------------------------------------------------
        // enter single line mode
        // ---------------------------------------------------------------------
        // Code required for STARTTLS: transport must not have a pending socket
        // read because this would block the TLS negotiation. For the windows
        // version this is really a problem: if we try to read, causing a wait,
        // the socket lib would somehow keep to socket blocked, even after the
        // data was returned by the read request. For windows the whole thing
        // only works if we can read the data entirely from the stream buffer...
        // ---------------------------------------------------------------------
        private bool ReaderStop(uint umod)
        {   if(stream == null) return false;        // called after close()
            if(rdr_res == null) return false;       // unexpected

            if (umod == 0)
            {   Monitor(ZIMapMonitor.Debug, "ReaderStop: single line");
                ReaderPoll(false, true);
            }
            else
            {
                // HACK: wait 300ms (MS Socket lib blocks when read causes wait)
                //if(socket.Poll(300000, System.Net.Sockets.SelectMode.SelectRead))
                //    Monitor(ZIMapMonitor.Debug, "ReaderStop: ready");
                System.Threading.Thread.Sleep(300);
            }
            return true;
        }

        /// <summary>
        /// Changes the current IMap data stream.
        /// </summary>
        /// <param name="socket">The socket on whicht the stream is based.</param>
        /// <param name="stream">The IMap data stream that is to be used.</param>
        /// <param name="timeout">Command timeout in [s].</param>
        /// <remarks>
        /// Throws an ArgumentException if the stream is not readable
        /// or not writable.
        /// <para />
        /// No socket timeouts are used by this class, socket reading is done in
        /// background by a worker thread.  The blocking read timeouts after the
        /// given time (see <see cref="Receive"/>).
        /// </remarks>
        public void Setup(System.Net.Sockets.Socket socket,
                          Stream stream, uint timeout)
        {
            this.timeout = timeout;
            if (socket != null)
                this.socket = socket;
            if (stream != null)
            {   if (!stream.CanRead || !stream.CanWrite)
                    throw new ArgumentException("Not a read+write stream");
                this.stream = stream;
            }
        }
    }
}
