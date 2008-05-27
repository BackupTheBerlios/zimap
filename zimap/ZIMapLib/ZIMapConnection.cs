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
    /// This class encapsulates an IMAP connection
    /// </summary>
    public class ZIMapConnection : ZIMapBase
    {
        // =====================================================================
        // Initialize Connection and Transport
        // =====================================================================

        private class Transport : ZIMapTransport
        {   readonly string name;
            
            // base has no def xtor ...
            public Transport(Stream stream) : base(stream) 
            {   name = "ZIMapTransport";
            }
            
            // must implement, abstract in base ...
            protected override void Monitor(ZIMapMonitor level, string message)
            {   if(MonitorLevel <= level) ZIMapConnection.Monitor(Parent, name, level, message); 
            }
        }

        private System.Net.Sockets.Socket        svr_sock;
        private System.Net.Sockets.NetworkStream svr_imap;
        private Transport                        transport;
        
        protected ZIMapConnection() : base(null) {}        
        
        public static uint GetIMapPort()
        {   return GetIMapPort("imap");
        }
        public static uint GetIMapPort(string protocolName)
        {   switch(protocolName)
            {   case "imap" : 
                case "imap2" :    break;
                case "imap3" :    return 220;
                case "imaps" :    return 993;
                default:          ZIMapException.Throw(null, ZIMapErrorCode.UnknownProtocol, protocolName);
                                  return 0;                                  
            }
            return 143;
        }
            
        public static ZIMapConnection GetConnection(string server)
        {   return GetConnection(server, GetIMapPort());
        }
        
        public static ZIMapConnection GetConnection(string server, uint port)
        {   ZIMapConnection conn = new ZIMapConnection();

            try
            {   conn.svr_sock = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, 
                                                              System.Net.Sockets.SocketType.Stream, 
                                                              System.Net.Sockets.ProtocolType.IP);
                conn.svr_sock.Connect(server, (int)port);
                conn.svr_imap = new System.Net.Sockets.NetworkStream(conn.svr_sock);
                
                // init transport level and start async receive ...
                conn.transport = new Transport(conn.svr_imap);
                conn.transport.Poll(0);
                
                // init protocol layer
                conn.protocol = new Protocol(null, conn.transport);
            }
            catch(Exception inner)
            {   if(inner is System.Net.Sockets.SocketException)
                    conn.Monitor(ZIMapMonitor.Error, "GetConnection: " + inner.Message);
                else
                    conn.Error(ZIMapErrorCode.CannotConnect, inner);
                return null;
            }
            return conn;
        }

        public bool Close()
        {   
            if (svr_imap == null)
                return true;
            try
            {   Monitor(ZIMapMonitor.Info, "Closing connection");
                
                transport.Close();
                transport = null;
                if(factory != null) factory.Dispose();
                factory = null;
                protocol = null;
                svr_imap = null;
                svr_sock = null;
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
        public int TransportTimeout {
            get {   if(transport == null) return -1;
                    return svr_imap.ReadTimeout;  }
            set {   if(transport == null) return;
                    svr_imap.ReadTimeout = value; svr_imap.WriteTimeout = value; }
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
        ///
        /// </summary>
        /// <value>
        ///
        /// </value>
        /// <remarks>
        /// 
        /// </remarks>
        public ZIMapProtocol ProtocolLayer {
            get {   return protocol;    }
        }
        
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
        ///
        /// </summary>
        /// <value>
        ///
        /// </value>
        /// <remarks>
        /// 
        /// </remarks>
        public ZIMapFactory CommandLayer {
            get {   if(factory == null && svr_imap != null) 
                        factory = new Factory(this);
                    return factory;    }
        }


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
            if(ZIMapConnection.Callback.Monitor(origin, level, name + ": " + message))
                return;
            
            if(level == ZIMapMonitor.Progress)
            {   if(origin == null || origin.MonitorLevel > ZIMapMonitor.Info) return;
                Console.Write("{0} {1}: {2}\r", name, level.ToString(), message);
            }
            else if(level != ZIMapMonitor.Messages)
                Console.WriteLine("{0} {1}: {2}", name, level.ToString(), message);
        }

        public static void Monitor(ZIMapBase origin, string name, ZIMapMonitor level, string message)
        {   ZIMapConnection conn = (origin == null) ? null : origin.Parent as ZIMapConnection;
            Monitor(conn, name, level, message);
        }
            
        protected override void Monitor(ZIMapMonitor level, string message)
        {   if(this.MonitorLevel <= level) Monitor(this, "ZIMapConnection", level, message);
        }

        // =====================================================================
        // Callback support 
        // =====================================================================

        //internal enum CallbackMethod { Monitor, Closed, Request, Result, Error };

        public interface ICallback
        {   bool Monitor(ZIMapConnection connection, ZIMapMonitor level, string message);
            bool Closed(ZIMapConnection connection);
            bool Request(ZIMapConnection connection, uint tag, string command);
            bool Result(ZIMapConnection connection, object info);
            bool Error(ZIMapConnection connection, ZIMapException error);
        }

        public class CallbackDummy : ZIMapConnection.ICallback
        {   public bool Monitor(ZIMapConnection connection, ZIMapMonitor level, string message)
            {   return false;   }
            public bool Closed(ZIMapConnection connection)
            {   return false;   }
            public bool Request(ZIMapConnection connection, uint tag, string command)
            {   return false;   }
            public bool Result(ZIMapConnection connection, object info)
            {   return false;   }
            public bool Error(ZIMapConnection connection, ZIMapException error)
            {   return false;   }
        }
        
        private static ICallback   callback = new CallbackDummy();
        
        public static ICallback Callback
        {   get {   return callback;    }
            set {   if(value == null)   callback = new CallbackDummy();
                    else                callback = value; 
                }
        }
    }
}
