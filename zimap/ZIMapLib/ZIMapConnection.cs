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
using System.Net;
using System.Collections.Generic;

namespace ZIMap
{

    /// <summary>
    /// This class encapsulates an IMAP connection and creates instances of
    /// classes to implement the transport, protocol and command layers.
    /// </summary>
    /// <remarks>
    /// The class cannot be instantiated directly, use <see cref="GetConnection(string)"/>,
    /// a static member that initializes the transport layer <see cref="ZIMapTransport"/>
    /// and returns on instance of ZIMapConnection. 
    /// </remarks>
    public class ZIMapConnection : ZIMapBase
    {
        // =====================================================================
        // Initialize Connection and Transport
        // =====================================================================

        private class Transport : ZIMapTransport
        {   readonly string name;
            
            // base has no def xtor ...
            public Transport(ZIMapConnection parent) : base(parent) 
            {   name = "ZIMapTransport";
                Setup(parent.socket, parent.stream, parent.timeout);
            }
            
            // must implement, abstract in base ...
            protected override void MonitorInvoke(Monitor level, string message)
            {   if(MonitorLevel <= level) 
                    ZIMapConnection.MonitorInvoke(Parent, name, level, message); 
            }
        }

        /// <summary>A value that controls Transport Layer Security (TLS).</summary>
        /// <remarks>
        /// This enumeration is used by the property <see cref="TlsMode"/> and by 
        /// the factory method <see cref="GetConnection(string, uint, TlsModeEnum, uint)"/>.
        /// </remarks>
        public enum TlsModeEnum
        {   
            /// <summary>TLS is disabled (except when the protocol IMAPS is used).</summary>
            Disabled = 0,
            /// <summary>Tries to setup TLS but allows to fall back to an unsecured connection.</summary>
            Automatic,
            /// <summary>Tries to setup TLS and fails if it gets not secure connection.</summary>
            Required,
            /// <summary>Automatically set for protocol IMAPS, cannot be changed.</summary>
            IMaps
        }

        // socket for the IMAP connection
        private System.Net.Sockets.Socket socket;
        // a Network or SSL stream using the socket
        private Stream      stream;
        // TLS requirements
        private TlsModeEnum tlsmode;
        // Transport layer
        private Transport   transport;
        // Server URL saved for StartTls
        private string      server;
        // Timeout value in seconds, see TransportTimeout
        private uint        timeout;
        
        /// <summary>
        /// Internal constructor. See <see cref="GetConnection(string)"/> for a method
        /// to create an instance.
        /// </summary>
        protected ZIMapConnection() : base(null) {}        
        
        public static uint GetIMapPort()
        {   return GetIMapPort("imap");
        }
        
        /// <summary>
        /// Get numeric protocol port from string
        /// </summary>
        /// <param name="protocolName">
        /// Can be a port number or a string ("imap", "imaps")
        /// </param>
        /// <returns>
        /// <c>0</c> on error or a port number.
        /// </returns>
        public static uint GetIMapPort(string protocolName)
        {   // name given?
            switch(protocolName)
            {   case ""      :
                case "imap"  : 
                case "imap2" :    return 143;
                case "imap3" :    return 220;
                case "imaps" :    return 993;
            }
            
            // numeric value given?
            uint prot;
            if(uint.TryParse(protocolName, out prot)) return prot;
            ZIMapException.Throw(null, ZIMapException.Error.UnknownProtocol, protocolName);
            return 0;                                  
        }
            
        public static ZIMapConnection GetConnection(string server)
        {   return GetConnection(server, GetIMapPort(), TlsModeEnum.Automatic, 30);
        }

        public static ZIMapConnection GetConnection(string server, uint port, 
                                                    TlsModeEnum tlsMode, uint timeout)
        {   ZIMapConnection conn = new ZIMapConnection();
            conn.tlsmode = tlsMode;
            conn.timeout = timeout;
            try
            {   conn.socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, 
                                                              System.Net.Sockets.SocketType.Stream, 
                                                              System.Net.Sockets.ProtocolType.IP);
                // The socket timeouts will be reset by the transport layer!
                if(timeout > 0)
                {   conn.socket.ReceiveTimeout = (int)timeout * 1000;
                    conn.socket.SendTimeout = (int)timeout * 1000;
                }
                conn.socket.NoDelay = true;             // nagle causes more windows trouble
                
                conn.server = server;
                conn.socket.Connect(server, (int)port);

                conn.stream = new System.Net.Sockets.NetworkStream(conn.socket);
            }
            catch(Exception inner)
            {   if(inner is System.Net.Sockets.SocketException)
                    conn.MonitorError("GetConnection: " + inner.Message);
                else
                    conn.RaiseError(ZIMapException.Error.CannotConnect, inner.Message);
                return null;
            }

            // init transport level and start async receive ...
            if(port == 993 && !conn.StartTls(0))
                return null;                                // after Throw()
            conn.transport = new Transport(conn);
            
            // start async receive and init protocol layer ...
            conn.transport.Poll(0);
            conn.protocol = new Protocol(conn, conn.transport);
            return conn;
        }
        
        public bool Close()
        {   
            if (stream == null)
                return true;
            try
            {   MonitorInfo( "Closing connection");
                
                transport.Close();
                transport = null;
                if(factory != null) factory.Dispose();
                factory = null;
                protocol = null;
                stream = null;
                socket = null;
                return true;
            }
            catch(Exception inner)
            {   RaiseError(ZIMapException.Error.CloseFailed, inner);
            }
            return false;
        }

        /// <summary>
        /// Returns the transport layer object for this connection.
        /// </summary>
        /// <value>
        /// A ZIMapTransport derived object (see remarks)
        /// </value>
        /// <remarks>
        /// After a call to <see cref="Close"/> this property returns <c>null</c>.
        /// </remarks>
        public ZIMapTransport TransportLayer {
            get {   return transport;    }
        }
        
        /// <summary>
        /// Get/sets the transport layer timeout.
        /// </summary>
        /// <value>
        /// Positive number holding the timeout in [ms]. A return value of
        /// <c>-1</c> indicates that the connection had been closed before. 
        /// </value>
        /// <remarks>
        /// This property sets the Read- and WriteTimeout properties of the
        /// underlying stream. 
        /// </remarks>
        public uint TransportTimeout 
        {   get {   return timeout;  }
            set {   timeout = value;
                    if(transport != null)
                        transport.Setup(socket, stream, timeout);
                }
        }

        /// <summary>
        /// Can be used to check if the server has closed the connection.
        /// </summary>
        /// <value>
        /// When the transport layer detects that the server has closed
        /// the connection <c>true</c> is returned.
        /// </value>
        /// <remarks>
        /// The <see cref="Close"/> method also closes the server connection,
        /// but a return value of <c>true</c> does not imply that Close() has
        /// been called, it is still possible to process completed commands. 
        /// </remarks>
        public bool IsTransportClosed 
        {   get {   if(transport == null) return true;
                    return transport.IsClosed; } 
        }

        /// <summary>
        /// Controls if TLS is used when connecting to the server.
        /// </summary>
        /// <value>
        /// The TlsModeEnum.IMaps value cannot be set, set remarks.
        /// </value>
        /// <remarks>
        /// When the IMAPS protocol is used this property cannot be changed.
        /// Is is also not possible to set the value TlsModeEnum.IMaps .
        /// The property has only effect before the server greeting is read
        /// (see <see cref="ZIMapProtocol.ServerGreeting"/>) and before a
        /// command is sent to the server via the transport layer.
        /// </remarks>
        public TlsModeEnum TlsMode
        {   get {   return tlsmode;
                }
            set {   if(tlsmode == value) return;
                    if(tlsmode == TlsModeEnum.IMaps || value == TlsModeEnum.IMaps)
                    {   RaiseError(ZIMapException.Error.InvalidArgument,
                                   "Cannot change to/from IMaps");
                        return;
                    }
                    tlsmode = value;
                }
        }
        
        // =====================================================================
        // TLS support
        // =====================================================================

        /// <summary>
        /// Send IMap STARTTLS command and initialize client side TLS
        /// </summary>
        /// <param name="uTag">
        /// <c>0</c> when called by GetConnection to initialize imaps. Otherwise
        /// a valid tag number causes the STARTTLS imap command to be sent.
        /// </param>
        /// <returns>
        /// <c>true</c> if no problem occurred, <c>false</c> on errors.
        /// For TLS related error a <see cref="ZIMapException"/> is thrown.
        /// </returns>
        /// <remarks>
        /// This routine throws an exception when TLS is "Required" but not
        /// availlable. In "Auto" mode invalid server certificates are accepted,
        /// in mode "Required" an exception is thrown.
        /// </remarks>
        public bool StartTls(uint uTag)
        {
            if(uTag == 0)
                MonitorInfo( "StartTls: TLS via imaps");
            else
            {   if (tlsmode == TlsModeEnum.Disabled || tlsmode == TlsModeEnum.IMaps)
                    return true;                    // nothing to do

                MonitorInfo( "StartTls: send STARTLS");
                transport.Send(uTag, "STARTTLS");
                string tag, status, message;
                while (true)
                {   if (!transport.Receive(out tag, out status, out message)) break;
                    if (tag == uTag.ToString()) break;
                }
                if (status != "OK")
                {   if (tlsmode == TlsModeEnum.Required)
                    {   RaiseError(ZIMapException.Error.CannotConnect,
                                   "STARTTLS failed: " + message);
                        return false;               // required but not ready
                    }
                    MonitorInfo( "StartTls: STARTTLS failed: " + message);
                    tlsmode = TlsModeEnum.Disabled;
                    return true;
                }
            }

            // now get the TLS stream

            Stream strm = null;
            try                                     // all errors throw...
            {   strm = GetTlsStream();
                if(strm == null) return true;       // tls disabled
                stream = strm;
            }
            catch(Exception ex)
            {   if(tlsmode == TlsModeEnum.Required)
                {   RaiseError(ZIMapException.Error.CannotConnect, ex.Message);
                    return false;                   // required but not ready
                }
                if(ex is ZIMapException) throw ex;
                MonitorError("TLS failure: " + ex.Message);
                return false;
            }

            if(uTag == 0)
                tlsmode = TlsModeEnum.IMaps;
            else
                transport.Setup(socket, stream, timeout);
            return true;
        }

        // Get a TLS stream ...
        private Stream GetTlsStream()
        {
#if MONO_BUILD               
            Mono.Security.Protocol.Tls.SslClientStream sssl =
                 new Mono.Security.Protocol.Tls.SslClientStream(stream, server, true);
            if(sssl != null)
            {   if(tlsmode != TlsModeEnum.Required)
                    // hook server certificate validation to ignore errors
                    sssl.ServerCertValidationDelegate =
                        new Mono.Security.Protocol.Tls.CertificateValidationCallback(ValCert);

                // dummy write to cause TLS handshake
                byte[] data = {}; 
                sssl.Write(data, 0, 0);
                return sssl;
            }
#elif MS_BUILD
            System.Net.Security.SslStream sssl = new System.Net.Security.SslStream(stream, true, //false,
                new System.Net.Security.RemoteCertificateValidationCallback(ValCert));
            if(sssl != null)
            {   sssl.AuthenticateAsClient(server);
                return sssl;
            }
#endif
            RaiseError(ZIMapException.Error.CannotConnect, "No TLS support available: " + server);
            return null;
        }

        // Validate the server certificate ...
#if MONO_BUILD
        // Callback for SslClientStream to ignore server certificate errors
        private bool ValCert(System.Security.Cryptography.X509Certificates.X509Certificate certificate,
                             int[] certificateErrors)
        {
            if(certificateErrors == null || certificateErrors.Length <= 0) return true;
            for(int irun=0; irun < certificateErrors.Length; irun++)
                MonitorInfo(
                    string.Format("Server certificate error {0:X}", certificateErrors[irun]));
            MonitorError("Server certificate is invalid. Error ignored!"); 
            return true;
        }
#elif MS_BUILD
        private bool ValCert(object sender,
            System.Security.Cryptography.X509Certificates.X509Certificate certificate,
            System.Security.Cryptography.X509Certificates.X509Chain chain, 
            System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None) return true;
            if (tlsmode == TlsModeEnum.Required) return false;
            MonitorError("Server certificate is invalid. Error ignored!");
            return true;
        }
#endif

        // =====================================================================
        // Protocol level support 
        // =====================================================================

        private class Protocol : ZIMapProtocol
        {   readonly string name;
            
            // base has no def xtor ...
            public Protocol(ZIMapBase parent, ZIMapTransport transport) : base(parent, transport) 
            {   name = "ZIMapProtocol";
            }
            
            // must implement, abstract in base ...
            protected override void MonitorInvoke(ZIMapConnection.Monitor level, string message)
            {   if(MonitorLevel <= level)
                    ZIMapConnection.MonitorInvoke(Parent, name, level, message); 
            }
        }

        private Protocol    protocol;

        /// <summary>
        /// Returns a reference to the protocol layer implementation.
        /// </summary>
        /// <value>
        /// Reference of an object that is derived from ZIMapProtocol.
        /// </value>
        /// <remarks>
        /// This factory method should be used to obtain a reference to the
        /// protocol layer. On the initial call an instance is created.
        /// </remarks>
        public ZIMapProtocol ProtocolLayer {
            get {   return protocol;    }
        }

#region Command level support
        // =====================================================================
        // Command level support 
        // =====================================================================
  
        private class Factory : ZIMapFactory
        {   readonly string name;
            
            // base has no def xtor ...
            public Factory(ZIMapConnection parent) : base(parent) 
            {   name = "ZIMapFactory";
            }
            
            // must implement, abstract in base ...
            protected override void MonitorInvoke(ZIMapConnection.Monitor level, string message)
            {   if(MonitorLevel <= level)
                    ZIMapConnection.MonitorInvoke(Parent, name, level, message); 
            }
        }
      
        private Factory     factory;

        /// <summary>
        /// Returns a reference to the command layer implementation
        /// </summary>
        /// <value>
        /// Reference of an object that is derived from ZIMapFactory.
        /// </value>
        /// <remarks>
        /// This factory method should be used to obtain a reference to the
        /// command layer. On the initial call an instance is created.
        /// </remarks>
        public ZIMapFactory CommandLayer
        {   get {   if(factory == null && stream != null) 
                        factory = new Factory(this);
                    return factory;    
                }
        }
        
        /// <summary>
        /// Return the <see cref="ZIMapFactory"/> object that was created by
        /// a call to <see cref="CommandLayer"/>.
        /// </summary>
        /// <returns>
        /// Returns <c>null</c> if <see cref="CommandLayer"/> was never used to
        /// reate a factory.
        /// </returns>      
        /// <remarks>
        /// This method is a convenient way to detect if an application uses
        /// the command layer.
        /// </remarks>
        public ZIMapFactory GetFactoryInUse()
        {   return factory;
        }
        
#endregion

#region Monitoring Support
        // =====================================================================
        // Support for Debug and Monitoring 
        // =====================================================================

        /// <summary>
        /// Indicates the type of information passed to a Monitor() function.
        /// </summary>
        public enum Monitor
        {   /// <summary>Debug information is sent.</summary>
            Debug,
            /// <summary>Something that may be of interest is reported.</summary>
            Info,
            /// <summary>An error is reported.</summary>
            Error,
            /// <summary>
            /// Used to indicate progress (text arg must contain 0 ... 100).
            /// </summary>
            Progress,
            /// <summary>
            /// Used to indicate an EXISTS server response, e.g. thats the number of
            /// messages in a mailbox has changed (text arg must contain 0 ... 100).
            /// </summary>
            Messages
        }

        static private string MonitorProgress;
        
        public static void MonitorInvoke(ZIMapConnection origin, string name,
                                         Monitor level, string message)
        {   if(message == null) message = "<null>";
            if(level == Monitor.Progress)
            {   if(message == MonitorProgress) return;
                MonitorProgress = message;
            }
            else if(level != Monitor.Messages)
                MonitorProgress = null;

            if(name == null) name = "<null>";
            
            // must use locking to make this thread safe (see ZIMapTransport.Reader) ...
            try
            {   if(origin != null) System.Threading.Monitor.Enter(origin);
                
                if(level == Monitor.Progress)
                {   uint percent;
                    if(uint.TryParse(message, out percent) &&
                       ZIMapConnection.Callback.Progress(origin, percent))
                            return;
                    Console.Write("{0} {1}: {2}\r", name, level, message);
                }
                else if(level == Monitor.Messages)
                {   uint existing;
                    if(!uint.TryParse(message, out existing)) return;
                    ZIMapConnection.Callback.Message(origin, existing);
                }
                else
                {   if(ZIMapConnection.Callback.Monitor(origin, level, name, message))
                        return;
                    Console.WriteLine("{0} {1}: {2}", name, level, message);
                }
            }
            finally
            {   if(origin != null) System.Threading.Monitor.Exit(origin);
            }
        }

        public static void MonitorInvoke(ZIMapBase origin, string name, 
                                         Monitor level, string message)
        {   ZIMapConnection conn = (origin == null) ? null : origin.Parent as ZIMapConnection;
            MonitorInvoke(conn, name, level, message);
        }
            
        protected override void MonitorInvoke(Monitor level, string message)
        {   if(this.MonitorLevel > level) return; 
            MonitorInvoke(this, "ZIMapConnection", level, message);
        }
#endregion

#region Callback Support
        // =====================================================================
        // Callback support 
        // =====================================================================

        /// <summary>
        /// Inferface that allows to hook some ZIMap library funtions
        /// </summary>
        public interface ICallback
        {   /// <summary>Provides Error, Debug and Progress information</summary>
            bool Monitor(ZIMapConnection connection, ZIMapConnection.Monitor level,
                         string source, string message);
            /// <summary>Used by the <see cref="ZIMapConnection.Progress"/> class to
            ///          report progress</summary>
            bool Progress(ZIMapConnection connection, uint percent);
            /// <summary>Called when an EXISING untagged message was received</summary>
            bool Message(ZIMapConnection connection, uint existing);
            /// <summary>Called when the IMap connection got closed</summary>
            bool Closed(ZIMapConnection connection);
            /// <summary>The protocol layer calls this before sending an IMap command</summary>
            bool Request(ZIMapConnection connection, uint tag, string command);
            /// <summary>The protocol layer calls this after reception of an IMap result</summary>
            bool Result(ZIMapConnection connection, object info);
            /// <summary>Called by ZMapException before an exception is thrown.</summary>
            bool Error(ZIMapConnection connection, ZIMapException error);
        }

        /// <summary>
        /// A NOOP implementation of ICallback, can be used as a base class
        /// </summary>
        public class CallbackDummy : ZIMapConnection.ICallback
        {   public virtual bool Monitor(ZIMapConnection connection, ZIMapConnection.Monitor level,
                                        string source, string message)
            {   return false;   }
            public virtual bool Progress(ZIMapConnection connection, uint percent)
            {   return false;   }
            public virtual bool Message(ZIMapConnection connection, uint existing)
            {   return false;   }
            public virtual bool Closed(ZIMapConnection connection)
            {   return false;   }
            public virtual bool Request(ZIMapConnection connection, uint tag, string command)
            {   return false;   }
            public virtual bool Result(ZIMapConnection connection, object info)
            {   return false;   }
            public virtual bool Error(ZIMapConnection connection, ZIMapException error)
            {   return false;   }
        }
        
        private static ICallback   callback = new CallbackDummy();

        /// <summary>
        /// Applications can hook the library to get error or debug information, to
        /// implement exceptions and to support progress bars.
        /// </summary>
        /// <value>
        /// Reference of an object that implemts the ICallback interface.
        /// </value>
        public static ICallback Callback
        {   get {   return callback;    }
            set {   if(value == null)   callback = new CallbackDummy();
                    else                callback = value; 
                }
        }
#endregion

#region Progress Reporting
        // =====================================================================
        // Progress reporting
        // =====================================================================
        /// <summary>
        /// Update the progress display even before having a <see cref="ZIMapConnection"/>
        /// instance.
        /// </summary>
        /// <param name="connection">
        /// Should be the current connection but can also be <c>null</c> if no connection
        /// has been created until the time of the call.
        /// </param>
        /// <param name="percent">
        /// The progress value <see cref="ZIMapConnection.Progress.Update(uint)"/>.
        /// </param>
        /// <remarks>
        /// This wrapper is efficient and can be used by library routines as a standard
        /// call to update the progress display.  Another way for applications is to use
        /// the <see cref="ProgressReporting"/> protperty to obtain a reference to an
        /// instance of the <see cref="ZIMapConnection.Progress"/> class that offers more
        /// features.
        /// </remarks>
        public static void ProgressUpdate(ZIMapConnection connection, uint percent)
        {   if(connection == null)
            {   if(percent > 100) percent = 100;
                MonitorInvoke(null, "ZIMapConnection", Monitor.Progress, percent.ToString());
            }
            else if(connection.progress != null)
                connection.progress.Update(percent);
            else
                connection.ProgressReporting.Update(percent);
        }

        // keep one Progress instance alife
        private ZIMapConnection.Progress   progress;

        /// <summary>
        /// Obtain a cached instance of <see cref="ZIMapConnection.Progress"/> that can
        /// be used for progress reporting. 
        /// </summary>
        public ZIMapConnection.Progress ProgressReporting
        {   get {   if(progress == null) progress = new Progress(this);
                    return progress;
                }
        }

        /// <summary>
        /// Provides support for progress reporting.
        /// </summary>
        /// <remarks>
        /// Progress is measured as a percent value ranging from <c>0</c> to <c>100</c>.
        /// The <see cref="Update(uint)"/> method is used to signal a progress change.
        /// <para />
        /// An important feature of this class is that progress reports can be nested:
        /// Some worker routine that reports progress ranging from <c>0</c> to <c>100</c>
        /// might have a caller that later calls other worker that also reports progress
        /// in this way.  The solution to this problem is scaling - the method 
        /// <see cref="Push"/> is called before running a worker with parameters that,
        /// for example scale the progress reported by that worker to an absolute range
        /// of <c>0</c> to <c>40</c>.  After the worker has returned <see cref="Pop"/>
        /// is called to remove scaling, and before the last worker is called Push is
        /// used again to set an absolute scaling of <c>40</c> to <c>100</c>. Scalings
        /// can be nested to any level.
        /// </remarks>
        public class Progress
        {
            private ZIMapConnection connection;
            private uint percent;
            
            public Progress(ZIMapConnection parent)
            {   connection = parent;
                min = 0; max = 100;
            }

            /// <summary>
            /// This field can be used to disable progress reporting.
            /// </summary>
            public static bool Enabled = true;
            
            private uint            min, max;
            private uint            level;
            private Progress        chain;
                
            /// <value>
            /// Get/Set the current progress percentage.
            /// </value>
            /// <remarks>
            /// The set property does not call <see cref="Update(uint)"/>, e.g. a change
            /// will not become immedeately visible.
            /// </remarks>
            public uint Percent
            {   get {   return percent; }
                set {   percent = value; }
            }

            public void Update(uint current, uint maximum)
            {   if(!Enabled || maximum == 0) return;
                Update((double)current / maximum);
            }
            
            public void Update(double fraction)
            {   if(!Enabled) return;
                if     (fraction <= 0.0) Update(0);
                else if(fraction >= 1.0) Update(99);
                else                     Update((uint)(100*fraction));
            }

            /// <summary>
            /// Start or Update the progress display
            /// </summary>
            /// <param name="percent">
            /// Indicates the current progress, where <c>0</c> is a special value.
            /// </param> 
            /// <remarks>
            /// This routine does several optimizations to avoid redundant calls
            /// to the consumer that performs the real output on a display.  The
            /// most important thing is that progress can only advance - subsequent
            /// calls with a smaller percentage are ignored.  An exception to this
            /// is the value <c>0</c> which resets the progress to start again.
            /// <para/>
            /// The maximum value is <c>99</c>, all values above will be treated
            /// as if <c>99</c> were passed as an argument.  Use <see cref="Done"/>
            /// if you want to indicate that an action has completed.
            /// </remarks>
            public void Update(uint percent)
            {   if(!Enabled) return;
                if(percent > 99) percent = 99;
                if(percent <= this.percent && percent > 0) return;
                this.percent = percent;
                uint udif = max - min;
                if(udif < 100)
                {   if(udif == 0) return;
                    percent = (udif * percent) / 100 + min;
                }
                MonitorInvoke(percent.ToString());
            }

            /// <summary>
            /// Indicate that an action is complete.
            /// </summary>
            /// <remarks>
            /// If the value of <see cref="Percent"/> is greater <c>0</c>
            /// this routine will pass a value of <c>100</c> to the consumer
            /// and reset the percent value to zero.
            /// <para/>
            /// The call is ignored if scaling is in place, e.g. it usually
            /// only has effect on the top level (see <see cref="Push"/>).
            /// </remarks>
            public void Done()
            {   if(!Enabled || percent == 0) return;
                if((max - min) < 100)        return;
                MonitorInvoke("100");
                percent = 0;
            }

            /// <summary>
            /// Discard all scaling levels and go back to the initial state.
            /// </summary>
            /// <remarks>
            /// If the value of <see cref="Percent"/> is greater <c>0</c>
            /// this routine will pass a value of <c>100</c> to the consumer.
            /// </remarks>
            public void Reset()
            {   if(!Enabled) return;
                if(percent != 0) MonitorInvoke("100");
                chain = null;
                level = 0;
                min   = 0;
                max   = 100;
                percent = 0;
            }
            
            public uint Push(uint min, uint max)
            {   if(min > 100) min = 100;
                if(max > 100) max = 100;
                if(max < min) max = min;

                uint udif = this.max - this.min;
                if(udif == 0)
                {   min = max = 0;
                }
                else if(udif < 100)
                {
                    min = (udif * min) / 100 + this.min;
                    max = (udif * max) / 100 + this.min;
                }
                
                Progress next = new Progress(connection);
                next.chain = chain;
                next.level = level;
                next.min   = this.min;
                next.max   = this.max;
                next.percent = percent;
                chain = next; level++;
                this.min = min; this.max = max; percent = 0;
                return next.level;
            }
            
            public void Pop()
            {   if(level == 0) return;
                Pop(level - 1);
            }

            public void Pop(uint level)
            {
                Progress next = this;
                do  {   if(next.level < level) return;
                        if(next.level > level) continue;
                        if(next == this)       return;
                        this.chain = next.chain;
                        this.level = next.level;
                        this.min   = next.min;
                        this.max   = next.max;
                        this.percent = next.percent;
                        return;
                    }   while((next = next.chain) != null);
            }
            
            // helper to call the consumer
            private void MonitorInvoke(string text)
            {   ZIMapConnection.MonitorInvoke(connection, "ZIMapConnection.Progress", 
                    ZIMapConnection.Monitor.Progress, text);
            }
        }
    }
#endregion
}
