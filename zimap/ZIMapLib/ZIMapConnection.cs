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
            protected override void Monitor(ZIMapMonitor level, string message)
            {   if(MonitorLevel <= level) ZIMapConnection.Monitor(Parent, name, level, message); 
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
            ZIMapException.Throw(null, ZIMapErrorCode.UnknownProtocol, protocolName);
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
                    conn.Monitor(ZIMapMonitor.Error, "GetConnection: " + inner.Message);
                else
                    conn.Error(ZIMapErrorCode.CannotConnect, inner.Message);
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
            {   Monitor(ZIMapMonitor.Info, "Closing connection");
                
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
            {   Error(ZIMapErrorCode.CloseFailed, inner);
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
                    {   Error(ZIMapErrorCode.InvalidArgument, "Cannot change to/from IMaps");
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
                Monitor(ZIMapMonitor.Info, "StartTls: using TLS via imaps");
            else
            {   if (tlsmode == TlsModeEnum.Disabled || tlsmode == TlsModeEnum.IMaps)
                    return true;                    // nothing to do

                Monitor(ZIMapMonitor.Info, "StartTls: sending STARTLS");
                transport.Send(uTag, "STARTTLS");
                string tag, status, message;
                while (true)
                {   if (!transport.Receive(out tag, out status, out message)) break;
                    if (tag == uTag.ToString()) break;
                }
                if (status != "OK")
                {   if (tlsmode == TlsModeEnum.Required)
                    {   Error(ZIMapErrorCode.CannotConnect,
                          "STARTTLS failed: " + message);
                        return false;               // required but not ready
                    }
                    Monitor(ZIMapMonitor.Info, "STARTTLS failed: " + message);
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
                {   Error(ZIMapErrorCode.CannotConnect, ex.Message);
                    return false;                   // required but not ready
                }
                if(ex is ZIMapException) throw ex;
                Monitor(ZIMapMonitor.Error, "TLS failure: " + ex.Message);
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
            Error(ZIMapErrorCode.CannotConnect, "No TLS support available: " + server);
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
                Monitor(ZIMapMonitor.Info,
                    string.Format("Server certificate error {0:X}", certificateErrors[irun]));
            Monitor(ZIMapMonitor.Error, "Server certificate is invalid. Error ignored!"); 
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
            Monitor(ZIMapMonitor.Error, "Server certificate is invalid. Error ignored!");
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
            protected override void Monitor(ZIMapMonitor level, string message)
            {   if(MonitorLevel <= level) ZIMapConnection.Monitor(Parent, name, level, message); 
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
            protected override void Monitor(ZIMapMonitor level, string message)
            {   if(MonitorLevel <= level) ZIMapConnection.Monitor(Parent, name, level, message); 
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
#endregion

#region Debug Support
        // =====================================================================
        // Debug support 
        // =====================================================================

        static private string MonitorProgress;
        
        public static void Monitor(ZIMapConnection origin, string name, ZIMapMonitor level, string message)
        {   if(message == null) message = "<null>";
            if(level == ZIMapMonitor.Progress)
            {   if(message == MonitorProgress) return;
                MonitorProgress = message;
            }
            else if(level != ZIMapMonitor.Messages)
                MonitorProgress = null;

            if(name == null) name = "<null>";
            try
            {   if(origin != null) System.Threading.Monitor.Enter(origin);
                if(ZIMapConnection.Callback.Monitor(origin, level, name + ": " + message))
                    return;
                
                if(level == ZIMapMonitor.Progress)
                {   if(origin == null || origin.MonitorLevel > ZIMapMonitor.Info) return;
                    Console.Write("{0} {1}: {2}\r", name, level.ToString(), message);
                }
                else if(level != ZIMapMonitor.Messages)
                    Console.WriteLine("{0} {1}: {2}", name, level.ToString(), message);
            }
            finally
            {   if(origin != null) System.Threading.Monitor.Exit(origin);
            }
        }

        public static void Monitor(ZIMapBase origin, string name, ZIMapMonitor level, string message)
        {   ZIMapConnection conn = (origin == null) ? null : origin.Parent as ZIMapConnection;
            Monitor(conn, name, level, message);
        }
            
        protected override void Monitor(ZIMapMonitor level, string message)
        {   if(this.MonitorLevel <= level) Monitor(this, "ZIMapConnection", level, message);
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
            bool Monitor(ZIMapConnection connection, ZIMapMonitor level, string message);
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
        {   public virtual bool Monitor(ZIMapConnection connection, ZIMapMonitor level, string message)
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
    }
}
