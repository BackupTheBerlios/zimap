//==============================================================================
// ZIMapProtocol.cs implements the ZIMapProtocol class    
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU Lesser General Public License
// This software is published under the GNU LGPL license. Please refer to the
// files COPYING and COPYING.LESSER for details. Please use the following e-mail
// address to contact me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.IO;
using System.Net;
using System.Collections.Generic;

namespace ZIMap
{
    //==========================================================================
    // ZIMapProtocol    
    //==========================================================================

    /// <summary>
    /// This class implements the protocol layer of ZIMapLib.
    /// </summary>
    /// <remarks>
    /// The most important work done here is the handling of sending IMap literal
    /// data (receiving literals is handled by the transport layer, see
    /// <see cref="ZIMapTransport"/>).
    /// <para />
    /// The second important point is the setup of TLS (Transport Layes Security).
    /// This happens when the <see cref="ServerGreeting"/> property does it's work,
    /// see <see cref="ZIMapConnection.StartTls"/>.  An alternate way of initializing
    /// TLS is the use of a special port number (IMAPS) which is handled entierly in
    /// <see cref="ZIMapConnection"/>.
    /// </remarks>
    public abstract class ZIMapProtocol : ZIMapBase
    {
        //======================================================================
        // ReceiveState, ReceiveData and ReceiveInfo
        //======================================================================

        /// <summary>
        /// Status of a response received from the IMAP server
        /// <see cref="ZIMapProtocol.Receive"/>
        /// </summary>
        public enum ReceiveState
        {   // Server sent "*" tag (untagged response)
            Info,
            // Server sent "+" tag (command continuation)
            Continue,
            
            // Response code was "OK", command completed
            Ready,
            // Response code was "NO", command failed
            Failure,
            // Response code waas "BAD", protocol error
            Error,
            
            // The connection was closed
            Closed,
            // A non-protocol error has occured
            Exception
        }

        // -----------------------------------------------------------------------------
        /// <summary>
        /// Holds the server replies to a command.
        /// </summary>
        /// <remarks>
        /// Usually created by <see cref="ZIMapProtocol.Receive(ZIMapProtocol.ReceiveData)"/>
        /// and consumed by <see cref="ZIMapCommand.Completed"/>.
        /// </remarks>
        public struct ReceiveData
        {   /// <value>The tag number is set by Send()</value>
            public uint                 Tag;
            /// <value>Returned by server (OK, BAD, FAILED)</value>
            public string               Status;
            /// <value>Returned by server, descriptive text</value>
            public string               Message;
            /// <value>Returned by server, untagged response lines</value>
            public ReceiveInfo[]        Infos;
            /// <value>Returned by server, literal data</value>
            public byte[][]             Literals;
            /// <value>The completion state of the command</value>
            public ReceiveState         State;

            private ZIMapParser parser;

            /// <summary>
            /// Resets the structure to it's initial state
            /// </summary>
            /// <remarks>
            /// Call this method to release resources (mostly the parsers). 
            /// </remarks>
            public void Reset()
            {   Tag = 0;
                Status = Message = null;
                Infos = null;
                Literals = null;
                State = ZIMapProtocol.ReceiveState.Exception;
            }
            
            /// <value>
            /// A parser for the <see cref="Message"/> property.
            /// </value>
            /// <remarks>
            /// This property remembers the last return value (e.g. the parser is
            /// created  on the 1st call and kept in memory) see <see cref="Reset"/>.
            /// </remarks>
            public ZIMapParser Parser
            {   get {   if(parser != null) return parser;
                        parser = new ZIMapParser(Message);
                        return parser;
                    }
                set {   parser = null;
                        if(value == null) return;
                        ZIMapException.Throw(null, ZIMapException.Error.MustBeZero, null);
                    }
            }

            /// <value>
            /// Returns <c>true</c> if ReceiveState is <see cref="ZIMapProtocol.ReceiveState.Ready"/>
            /// </value>
            public bool Succeeded
            {   get {   return (State == ReceiveState.Ready);   }
            }

            /// <summary>
            /// Renders the structure state to a string
            /// </summary>
            /// <returns>
            /// A string for debug output
            /// </returns>
            public override string ToString()
            {   System.Text.StringBuilder sb = new System.Text.StringBuilder();
                int ninf = (Infos == null) ? 0 : Infos.Length;
                sb.AppendFormat("Data:  State={0}  Tag={1:x}  Infos={2}   Literals={3}",
                                State, Tag, ninf, (Literals == null) ? 0 : Literals.Length);
                for(int irun=0; irun < ninf; irun++)
                    sb.AppendFormat("\nInfo {0}: {1}", irun+1, Infos[irun]); 
                sb.AppendFormat("\nMessage: {0}", Message); 
                return sb.ToString ();
            }
        }

        // -----------------------------------------------------------------------------        
        /// <summary>
        /// Holds an untagged server reply.
        /// </summary>
        /// <remarks>
        /// Usually this structure is a member array of <see cref="ReceiveData"/>.
        /// </remarks>
        public struct ReceiveInfo
        {
            /// <value>Returned by server, status text (might be a number)</value>
            public string      Status;
            /// <value>Returned by server, message text</value>
            public string      Message;

            private ZIMapParser parser;
            
            /// <summary>
            /// Resets the structure to it's initial state
            /// </summary>
            /// <remarks>
            /// Call this method to release resources (mostly the parser). 
            /// </remarks>
            public void Reset()
            {   Status = Message = null;
                parser = null;
            }
            
            /// <value>
            /// A parser for the <see cref="Message"/> property.
            /// </value>
            /// <remarks>
            /// This property remembers the last return value (e.g. the parser is
            /// created  on the 1st call and kept in memory) see <see cref="Reset"/>.
            /// </remarks>
            public ZIMapParser Parser
            {   get {   if(parser != null) return parser;
                        parser = new ZIMapParser(Message);
                        return parser;
                    }
                set {   parser = null;
                        if(value == null) return; 
                        ZIMapException.Throw(null, ZIMapException.Error.MustBeZero, null);
                    }
            }
            
            /// <summary>
            /// Renders the structure state to a string
            /// </summary>
            /// <returns>
            /// A string for debug output
            /// </returns>
            public override string ToString()
            {   return Status + " " + Message;
            }
        }

        //======================================================================
        // Class data
        //======================================================================

        private readonly ZIMapTransport  transport;
        private readonly ZIMapConnection connection;

        private string  greeting;        
        private uint    send_cnt;
        private bool    bye_received;
        private uint    exists_cnt = uint.MaxValue;

        public ZIMapProtocol(ZIMapBase parent, ZIMapTransport transport) : base(parent)
        {   this.transport = transport;
            this.connection = parent as ZIMapConnection;
        }

        /// <value>
        /// If this property is not set to <c>uint.MaxValue</c> (which is the default)
        /// the server reponses will be checked for "* nn EXISTS" messages and on
        /// reception of such a message this property will be updated and a
        /// <see cref="ZIMapConnection.Monitor.Messages"/> callback will be made. Set 
        /// this property to <c>0</c> to enable callbacks and to <c>uint.MaxValue</c> 
        /// to disable them. By default these callbacks are disabled to save time. 
        /// </value>
        public uint ExistsCount
        {   get {   return exists_cnt;  }
            set {   exists_cnt = value; }
        }

        public uint SendCount 
        {   get { return send_cnt; }
        }

        /// <value>
        /// Returns the server greeting.
        /// </value>
        /// <remarks>
        /// The server sends this after the client got connected. If the greeting has
        /// not yet arrived this code sends a <c>NOOP</c> command to synchronized with
        /// the server.
        /// <para />
        /// This property is implicitly called before the protocol layer sends the first
        /// command to the server. The server response stays cached. 
        /// <para />
        /// Because of being called before the first send, this property is also used
        /// to initialze TLS, see <see cref="ZIMapConnection.StartTls"/>.
        /// </remarks>
        public string ServerGreeting 
        {   get {   if(greeting != null) return greeting;
                    if(transport == null) return "";
                    MonitorDebug("ServerGreeting: polling");
                    bool bok = false;
                    uint tag = 0;
                    string status, message;
                    ReceiveState rsta;
                    bok = transport.Poll(1000);              // should be in buffer!
                    if(bok)
                    {   rsta = Receive(out tag, out status, out greeting);
                        if(rsta != ReceiveState.Info) greeting = null;
                    }
                                                            // use a NOOP to sync                
                    if(greeting == null && 
                        transport.Send(string.Format("{0} NOOP", ++send_cnt)))
                        // using the "fragment" send because the "normal" send would
                        // disable the socket timeout which we want for the 1st msg!
                    {   MonitorInfo("ServerGreeting: Got no greeting, try to resync");
                        while (true)
                        {   rsta = Receive(out tag, out status, out message);
                            if(rsta == ReceiveState.Info)
                                greeting = message;
                            else 
                                break;
                        }
                    }
                    if(greeting == null)
                    {   greeting = "";   
                        RaiseError(ZIMapException.Error.CannotConnect,
                                   "Invalid or missing greeting");
                    }
                    connection.StartTls(++send_cnt);
                    return greeting;
                } 
        }
        
        /// <summary>
        /// Low level routine to send an IMap command to the server. Usually
        /// this routine is not called directly - use ZIMapCommand::Execute()
        /// instead. When this routine is used the server response must be
        /// collected using the Receive() method.
        /// </summary>
        /// <param name="message">
        /// The command to be sent
        /// </param>
        /// <returns>
        /// On success: the tag number of this command. On error 0 is returned. 
        /// </returns>
        /// <remarks>
        /// This method does not handle literals, see <see cref="Send(object[], string)"/>.
        /// </remarks>
        public uint Send(string message)
        {   if(transport == null)                           // connection is closed
            {   RaiseError(ZIMapException.Error.DisposedObject);
                return 0;
            }

            object info;
            if(greeting == null) info = ServerGreeting;     // must fetch greeting
         
            try
            {   uint cnt = ++send_cnt;
                ZIMapConnection.Callback.Request(connection, cnt, message);

                if(transport.Send(cnt, message))            // exception for message=null
                    return cnt;

                if(transport.IsClosed)
                {   info = "transport closed";
                    ZIMapConnection.Callback.Closed(connection);
                }                    
                else   
                    info = "transport timeout";
            }
            catch(Exception inner)
            {   info = inner;
            }
            RaiseError(ZIMapException.Error.SendFailed, info);
            return 0;
        }

        /// <summary>
        /// Overload of Send() that can be used for requests with literal data.
        /// </summary>
        /// <param name="fragments">
        /// An array containing strings and byte arrays. The first array element
        /// must be a string. Multiple strings will be sent with a separating
        /// space, byte arrays will be sent as literal data.
        /// </param>
        /// <param name="error">
        /// On error this parameter returns a descriptive text (might be a message
        /// returned by the server).
        /// </param>
        /// <returns>
        /// On success: the tag number of this command. On error 0 is returned. 
        /// </returns>
        /// <remarks>
        /// Make sure that any queued command gets sent and that the server reply
        /// is processed before issuing a command containing literal data. This
        /// layer cannot handle pending command output.
        /// <para />
        /// If an application uses the command layer (see <see cref="ZIMapFactory"/>)
        /// output of commands executed by the factory will be read automatically.
        /// </remarks>
        public uint Send(object[] fragments, out string error) {
            error = "send error";
            if(transport == null)                           // connection is closed
            {   RaiseError(ZIMapException.Error.DisposedObject);
                return 0;
            }
            
            // --- need a string to start ---
            
            if(fragments == null || fragments.Length <= 0 || !(fragments[0] is string))
            {   RaiseError(ZIMapException.Error.InvalidArgument);
                return 0;
            }
            
            object info;
            if(greeting == null) info = ServerGreeting;     // must fetch greeting

            try
            {   uint cnt = ++send_cnt;                      // tag number to send
                int  idx = 0;
                bool bok  = true;
                bool blit = false;                          // last frag was literal
                bool btag = false;                          // sent tag flag           
                string message; byte[] literal;

                ZIMapConnection.Callback.Request(connection, cnt, "[]");
                                                
                // --- loop over arguments ---
                
                while(bok)
                {   if(idx >= fragments.Length)             // array done
                    {   if(blit)                            // literal needs CR/LF
                        {   bok = transport.Send("");
                            if(!bok) break;
                        }
                        error = null;
                        return cnt;
                    }

                    info = fragments[idx++];                // next argument

                    message = info as string;
                    if(message != null)                     // string fragment
                    {   if(message.Length <= 0)
                        {   RaiseError(ZIMapException.Error.InvalidArgument,
                                       "String fragment must not be empty");
                            return 0;
                        }
                                                            // concatenate strings...
                        while(idx < fragments.Length && fragments[idx] is string)
                            message = string.Format("{0} {1}", message, fragments[idx++]);
                                                            // append literal size...
                        if(idx < fragments.Length && fragments[idx] is byte[])
                            message = string.Format("{0} {{{1}}}", 
                                                message, ((byte[])fragments[idx]).Length);

                        if(!btag)                           // initial arg with tag
                        {   transport.Send(cnt, message);
                            ZIMapFactory fact = connection.GetFactoryInUse();
                            if(fact != null)                // do we have a factory?
                            {   MonitorDebug("Send: literal causes ExecuteRunning");
                                fact.ExecuteRunning(null);
                            }
                        }
                        else if(message[0] == ')' || message[0] == ']')
                            bok = transport.Send(message);
                        else
                            bok = transport.Send(" " + message);
                        blit = false; btag = true;
                        continue;
                    }

                    literal = info as byte[];
                    if(literal != null)                     // literal
                    {   string tag, status;
                        if(blit)                            // needs " {{xxx}}}"
                        {   message = string.Format(" {{{0}}}", literal.Length);
                            bok = transport.Send(message);
                            if(!bok) break;
                        }
                        blit = true;
                        while(true) {
                            bok = transport.Receive(out tag, out status, out error);
                            if(!bok || tag == "+") break;
                            string hexcnt = cnt.ToString("x");
                            if(tag == hexcnt)               // oops! Server says error
                                MonitorError("Send: literal not accepted: " + error);
                            else
                                RaiseError(ZIMapException.Error.UnexpectedTag, string.Format(
                                    "Want '+' or '{0}' but got '{1}'", hexcnt, tag));
                            return 0;
                        }
                        if(bok)
                        {   bok = transport.Send(literal);
                            continue;
                        }
                    }
                    
                    if(info == null)
                    {   RaiseError(ZIMapException.Error.MustBeNonZero);
                        return 0;
                    }
                }
                if(transport.IsClosed)
                {   info = "transport closed";
                    ZIMapConnection.Callback.Closed(connection);
                }
                if(transport.IsTimeout)
                    info = "transport timeout";
                else   
                    info = "transport IO error";
            }
            catch(Exception inner)
            {   info = inner;
            }
            RaiseError(ZIMapException.Error.SendFailed, info);
            return 0;
        }
            
        // internal helper to process a single response line
        private ReceiveState Receive(ref ReceiveData data, out byte[][] literals)
        {   literals = null;
            if(transport == null)                        // connection is closed
            {   RaiseError(ZIMapException.Error.DisposedObject);
                return ReceiveState.Exception;
            }

            object info;
            try
            {   string tags;
                if(transport.Receive(out tags, out data.Status,
                                     out data.Message, out literals))
                {     
                    // --- check the tag ---

                    if(tags == "*" || tags == "0")
                    {   if(data.Status == "BYE")
                        {   bye_received = true;
                            MonitorInfo( "Receive: 'BYE' will close transport");
                        }
                        return ReceiveState.Info;
                    }
                    if(tags == "+")
                        return ReceiveState.Continue;
                    if(!uint.TryParse(tags, System.Globalization.NumberStyles.AllowHexSpecifier, 
                                      null, out data.Tag))
                    {   RaiseError(ZIMapException.Error.UnexpectedTag, "Tag: " + tags);
                        return ReceiveState.Exception;
                    }
                    
                    // --- check the status ---
                    
                    data.State = ReceiveState.Error;
                    if(data.Status == "OK")
                    {   if(bye_received)                    // server sent untagged BYE
                        {   transport.Close();
                            ZIMapConnection.Callback.Closed(connection);
                        }
                        data.State = ReceiveState.Ready;
                    }
                    else if(data.Status == "NO")
                        data.State = ReceiveState.Failure;
                    else if(data.Status != "BAD")
                    {   RaiseError(ZIMapException.Error.UnexpectedData, "Status: " + data.Status);
                        return ReceiveState.Exception;
                    }
                    
                    ZIMapConnection.Callback.Result(connection, data);
                    return data.State;
                }
                else if(transport.IsClosed)
                {   ZIMapConnection.Callback.Closed(connection);
                    return ReceiveState.Closed;
                }
                info = "transport timeout";
            }
            catch(Exception inner)
            {   info = inner;
            }
            RaiseError(ZIMapException.Error.ReceiveFailed, info);
            return ReceiveState.Exception;
        }
        
        /// <summary>
        /// Receive a single line of an IMap reply (no literals)
        /// </summary>
        /// <param name="tag"><c>0</c> for an untagged response or the
        /// tag number of the command.</param>
        /// <param name="status">The status text like OK, NO or BAD</param>
        /// <param name="message">The response text.</param>
        /// <returns>A value indicating success or failure.</returns>
        /// <remarks>
        /// A convenience overload that ignores literals.
        /// </remarks>
        public ReceiveState Receive(out uint tag, out string status, out string message)
        {   ReceiveData data = new ReceiveData();
            ReceiveState stat = Receive(ref data, out data.Literals);
            tag = data.Tag;
            status = data.Status;
            message = data.Message;
            if(data.Literals != null)
                MonitorError("Receive: literal data ignored");
            return stat;
        }

        /// <summary>
        /// Receive a complete IMap reply including literals and untagged
        /// responses.
        /// </summary>
        /// <param name="data">A structure containing the received data.</param>
        /// <returns><c>true</c> on success.</returns>
        public bool Receive(out ReceiveData data)
        {   data = new ReceiveData();
            data.State = Receive(ref data, out data.Literals);
            bool exists = false;                    // flag to invoke Messages() callback
            
            // fetch all info data and all literals ...
            if(data.State == ReceiveState.Info)
            {   List<string>    infos = new List<string>();
                List<byte[]>    multi = null;
                do {
                    infos.Add(data.Status);
                    infos.Add(data.Message);
                    byte[][] literals;
                    data.State = Receive(ref data, out literals);
                    if(literals != null)
                    {   if(data.Literals == null)
                            data.Literals = literals;
                        else 
                        {   if(multi == null)
                            {   multi = new List<byte[]>();
                                foreach(byte[] elt in data.Literals) multi.Add(elt);
                            }
                            foreach(byte[] elt in literals) multi.Add(elt);
                        }
                    }
                } while(data.State == ReceiveState.Info);

                // process the received info data array ...
                int icnt = infos.Count / 2;
                data.Infos = new ZIMapProtocol.ReceiveInfo[icnt];
                for(int irun=0; irun < icnt; irun++)
                {   data.Infos[irun].Status  = infos[irun*2];
                    string message           = infos[irun*2+1];
                    data.Infos[irun].Message = message;
                    
                    // check for "* nn EXISTS" messages ...
                    if(exists_cnt != uint.MaxValue && message == "EXISTS")
                    {   uint ecnt = 0;
                        if(uint.TryParse(infos[irun*2], out ecnt)) 
                        {   exists_cnt = ecnt;  exists = true;
                        }
                    }
                }
                if(multi != null)
                    data.Literals = multi.ToArray();
            }

            // EXISTS count callback
            if(exists)
                MonitorInvoke(ZIMapConnection.Monitor.Messages, exists_cnt.ToString());
            
            // check for errors
            switch(data.State)
            {   case ReceiveState.Info:
                case ReceiveState.Error:
                case ReceiveState.Failure:
                case ReceiveState.Ready:   return true;
                default:                   return false;
            }
        }
    }
}
