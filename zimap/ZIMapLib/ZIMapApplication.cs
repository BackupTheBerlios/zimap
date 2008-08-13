//==============================================================================
// ZIMapApplication.cs implements the ZIMapApplication class
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU Lesser General Public License
// This software is published under the GNU LGPL license. Please refer to the
// files COPYING and COPYING.LESSER for details. Please use the following e-mail
// address to contact me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.Text;
using System.Collections.Generic;

namespace ZIMap
{
    //==========================================================================
    // Class with Methods that execute multiple IMap commands
    //==========================================================================

    /// <summary>
    /// Class that implements some high level functionality (Application layer).
    /// </summary>
    /// <remarks>
    /// Methods in this class usually issue more than a single IMap command or
    /// depend on some application state.
    /// <para />
    /// This class belongs to the <c>Application</c> layer which is the top-most of four
    /// layers:
    /// <para />
    /// <list type="table">
    /// <listheader>
    ///   <term>Layer</term>
    ///   <description>Description</description>
    /// </listheader><item>
    ///   <term>Application</term>
    ///   <description>The application layer with the following important classes:
    ///   <see cref="ZIMapApplication"/>, <see cref="ZIMapServer"/> and <see cref="ZIMapExport"/>.
    ///   </description>
    /// </item><item>
    ///   <term>Command</term>
    ///   <description>The IMap command layer with the following important classes:
    ///   <see cref="ZIMapFactory"/> and <see cref="ZIMapCommand"/>.
    ///   </description>
    /// </item><item>
    ///   <term>Protocol</term>
    ///   <description>The IMap protocol layer with the following important classes:
    ///   <see cref="ZIMapProtocol"/> and  <see cref="ZIMapConnection"/>.
    ///   </description>
    /// </item><item>
    ///   <term>Transport</term>
    ///   <description>The IMap transport layer with the following important classes:
    ///   <see cref="ZIMapConnection"/> and  <see cref="ZIMapTransport"/>.
    ///   </description>
    /// </item></list>
    /// </remarks>
    public class ZIMapApplication : ZIMapBase
    {
        public readonly string  ServerName;
        public readonly uint    ServerPort;
        
        private ZIMapConnection connection;
        private ZIMapFactory    factory;
        private ZIMapServer     server;
        private ZIMapExport     export;
            
        private string          username;
        private uint            timeout = 30;
        private ZIMapConnection.Monitor
                                monitorLevel = ZIMapConnection.Monitor.Error;
        private bool            monitorAll = false;
        private ZIMapConnection.Progress
                                progress;
        
        private uint            fetchBlock = 50;
        
        // feature flags ...
        private bool            enableNamespaces = true;
        private bool            enableRights = true;
        private bool            enableQuota = true;
        private bool            enableUid = true;

        // see OpenMailbox ...
        private string          mailboxName;
        private uint            mailboxTag;
        private bool            mailboxReadonly;
        private ZIMapServer.Namespace
                                mailboxNamespace;

        // see ExportRead, ExportWrite
        private uint            exportSerial;
        private bool            exportWrite;
        
        // =====================================================================
        // Accessors
        // =====================================================================
        
        /// <summary>
        /// Returns a reference to the parent instance which always is the connection
        /// object that created this factory.
        /// </summary>
        public ZIMapConnection Connection
        {   get {   return connection;  }
        }

        /// <summary>
        /// Controls wether the protocol invokes a call back function when the server
        /// reports new messages.
        /// </summary>
        /// <remarks>
        /// This feature could be used to monitor an open mailbox for new messages.
        /// See <see cref="ZIMapConnection.Monitor.Messages"/>
        /// and <see cref="ZIMapProtocol.ExistsCount"/> for details.
        /// </remarks>
        public bool EnableMessagesReporting
        {   get {   return connection.ProtocolLayer.ExistsCount != uint.MaxValue;  }
            set {   if(value == EnableMessagesReporting) return;   
                    connection.ProtocolLayer.ExistsCount = value ? 0 : uint.MaxValue;
                }
        }

        /// <summary>
        /// Controls if the IMap NAMESPACE feature is used
        /// </summary>
        /// <remarks>
        /// If set <c>true</c> before <see cref="Connect(string, string)"/> the server
        /// capabilities are used to disable this feature if it is not available.
        /// This would reset the propertie's value to <c>false</c>.
        /// </remarks>
        public bool EnableNamespaces
        {   get {   if(enableNamespaces && server != null)  // really enabled? 
                        enableNamespaces = server.NamespaceDataPersonal.Valid;
                    return enableNamespaces;
                }
            set {   if(enableNamespaces == value) return;   
                    enableNamespaces = value;
                    server = null;
                }
        }

        /// <summary>
        /// Controls if the IMap QUOTA feature is used
        /// </summary>
        /// <remarks>
        /// If set <c>true</c> before <see cref="Connect(string, string)"/> the server
        /// capabilities are used to disable this feature if it is not available.
        /// This would reset the propertie's value to <c>false</c>.
        /// </remarks>
        public bool EnableQuota
        {   get {   return enableQuota;  }
            set {   enableQuota = value; }
        }

        /// <summary>
        /// Controls if the IMap ACL feature is used
        /// </summary>
        /// <remarks>
        /// If set <c>true</c> before <see cref="Connect(string, string)"/> the server
        /// capabilities are used to disable this feature if it is not available.
        /// This would reset the propertie's value to <c>false</c>.
        /// </remarks>
        public bool EnableRights
        {   get {   return enableRights;  }
            set {   enableRights = value; }
        }
        
        /// <summary>
        /// Controls if the IMap UID feature is used
        /// </summary>
        /// <remarks>
        /// If set <c>true</c> before <see cref="Connect(string, string)"/> the server
        /// capabilities are used to disable this feature if it is not available.
        /// This would reset the propertie's value to <c>false</c>.
        /// </remarks>
        public bool EnableUidCommands
        {   get {   return enableUid;  }
            set {   enableUid = value; }
        }
        
        /// <summary>
        /// Get or set the current Import/Export object.
        /// </summary>
        /// <remarks>
        /// Use a set value of <c>null</c> to close to current <see cref="ZIMapExport"/>.
        /// </remarks>
        public ZIMapExport Export
        {   get {   if(export != null && exportSerial == export.Serial) return export;
                    export = new ZIMapExport(this.connection);
                    export.MonitorLevel = monitorLevel;
                    exportSerial = 0;
                    return export;  
                }
            set {   if(value != null) MonitorError("Value must be null");
                    else if(export != null)
                    {   export.Dispose();
                        exportSerial = 0;
                    }
                }
        }
        
        public ZIMapFactory Factory
        {   get {   return factory;  }
        }

        public bool IsLoggedIn
        {   get {   if(username == null || connection == null) return false;
                    if(!connection.IsTransportClosed) return true;
                    username = null; return false;
                }
        }

        public string MailboxName
        {   get {   return mailboxName; }
        }

        /// <summary>The Namespace of the currently open mailbox.</summary>
        /// <returns>The return value can be <c>null</c> if no mailbox is open.</returns>
        /// <remarks>This value is set by <see cref="MailboxOpen(string,bool)"/>
        /// and is cleared by <see cref="MailboxClose"/>.</remarks>
        public ZIMapServer.Namespace MailboxNamespace
        {   get {   return mailboxNamespace; }
        }
        
        public bool MailboxIsReadonly
        {   get {    return mailboxReadonly; }
        }
        
        public new ZIMapConnection.Monitor MonitorLevel
        {   get {   return monitorLevel;  }
            set {   SetMonitorLevel(value, false);  }
        }

        public ZIMapServer Server
        {   get {   if(server == null) 
                        server = ZIMapServer.CreateInstance(factory, EnableNamespaces);   
                    return server; 
                }
            set {   if(value == null) MonitorError("Cannot assign null");
                    else              server = value;
                }
        }

        public uint Timeout
        {   get {   return timeout; }
            set {   timeout = value;
                    if(connection != null) 
                        connection.TransportTimeout = timeout;
                }
        }
        
        public string User
        {   get {   return username;  }
        }
        
        // =====================================================================
        // Constructors 
        // =====================================================================

        public ZIMapApplication(string server) : this(server, null) {}

        public ZIMapApplication(string server, string protocol) :
            this(server, ZIMapConnection.GetIMapPort(protocol)) {}

        public ZIMapApplication(string server, uint port) : base(null)
        {   ServerName = server; ServerPort = port;  
        }
            
        // must implement, abstract in base ...
        protected override void MonitorInvoke(ZIMapConnection.Monitor level, string message)
        {   if(MonitorLevel <= level)
                ZIMapConnection.MonitorInvoke(connection, "ZIMapFactory", level, message); 
        }

        // =====================================================================
        // Debug stuff
        // =====================================================================
        
        public void SetMonitorLevel(ZIMapConnection.Monitor level, bool allLayers)
        {   if(level > ZIMapConnection.Monitor.Error) return;
            monitorLevel = level;
            monitorAll = allLayers;
            if(factory == null) return;
            factory.MonitorLevel = level;
            factory.Connection.MonitorLevel = level;
            if(!allLayers && level != ZIMapConnection.Monitor.Error)
                level = ZIMapConnection.Monitor.Info; 
            factory.Connection.TransportLayer.MonitorLevel = level;
            factory.Connection.ProtocolLayer.MonitorLevel = level;
        }
        
        // =====================================================================
        // Connect and Disconnect 
        // =====================================================================

        private void CheckCapability(string capability, bool clear, ref bool flag)
        {   if(Factory.HasCapability(capability))
                MonitorInfo("Connect: server supports " + capability);
            else if(flag && !clear)
            {   flag = false;
                MonitorInfo("Connect: server does not support " + capability);
            }
        }
        
        /// <summary>
        /// Connect to the IMap server and optionally login.
        /// </summary>
        /// <param name="user">
        /// An IMap account name or <c>null</c> for no login.
        /// </param>
        /// <param name="password">
        /// The password for the given account.
        /// </param>
        /// <returns>
        /// On success <c>true</c> is returned.
        /// </returns>
        /// <remarks>This function calls <see cref="ZIMapConnection.GetConnection(string)"/>
        /// and uses <see cref="ZIMapCommand.Login"/> for login.
        /// <para />
        /// If this method is called while having an open connection <c>Disconnect</c> 
        /// gets called first.
        /// </remarks>
        public bool Connect(string user, string password)
        {   return Connect(user, password, ZIMapConnection.TlsModeEnum.Automatic);
        }
        
        /// <summary>
        /// Connect to the IMap server and optionally login.
        /// </summary>
        /// <param name="user">
        /// An IMap account name or <c>null</c> for no login.
        /// </param>
        /// <param name="password">
        /// The password for the given account.
        /// </param>
        /// <param name="tlsMode">
        /// Controls TLS (tranport layer security).
        /// </param>
        /// <returns>
        /// On success <c>true</c> is returned.
        /// </returns>
        /// <remarks>This function calls <see cref="ZIMapConnection.GetConnection(string)"/>
        /// and uses <see cref="ZIMapCommand.Login"/> for login.
        /// <para />
        /// If this method is called while having an open connection <c>Disconnect</c> 
        /// gets called first.
        /// </remarks>
        public bool Connect(string user, string password, ZIMapConnection.TlsModeEnum tlsMode)
        {
            // step 1: Open a new connection and get factory ...
            ZIMapConnection.ProgressUpdate(null, 0);
            if(connection != null) Disconnect();

            connection = ZIMapConnection.GetConnection(ServerName, ServerPort,
                                                       tlsMode, timeout);
            if(connection != null)
            {   connection.MonitorLevel = MonitorLevel;
                progress = connection.ProgressReporting;
                progress.Update(20);
                factory = connection.CommandLayer;
            }
            if(factory == null)
            {   connection = null;
                MonitorError("Connect: failed to open connection");
                return false;
            }
            
            // defaults for logging ...
            SetMonitorLevel(monitorLevel, monitorAll);
            
            // step 2: login
            string greeting = connection.ProtocolLayer.ServerGreeting;
            MonitorInfo("Connect: server greeting: " + greeting);
            progress.Update(40);
            
            ZIMapCommand.Login cmd = new ZIMapCommand.Login(factory);
            cmd.Queue(user, password);
            if(!cmd.CheckSuccess())
            {   MonitorError("Connect: login failed");
                return false;
            }
            username = user;
            progress.Update(60);
            
            // set 3: get server configuration

            bool bIMap = true;
            CheckCapability("IMAP4rev1", false, ref bIMap);
            CheckCapability("NAMESPACE", bIMap, ref enableNamespaces);
            CheckCapability("QUOTA",     false, ref enableQuota);
            CheckCapability("ACL",       false, ref enableRights);
            CheckCapability("UIDPLUS",   bIMap, ref enableUid);
            progress.Update(80);
            
            // step4: get Namespace info
            if(enableNamespaces)
            {   if(Server.NamespaceDataPersonal.Valid)
                    MonitorInfo("Connect: NAMESPACE enabled");
                else
                {   MonitorInfo("Connect: NAMESPACE disabled (not supported)");
                    enableNamespaces = false;
                }
                factory.HierarchyDelimiter = server.DefaultDelimiter;
            }
            progress.Done();
            return true;
        }

        /// <summary>
        /// Logout and close the IMap connection.
        /// </summary>
        /// <remarks>
        /// If a user is logged in this command sends an IMap LOGOUT command.  If also
        /// closes the connection if it is open.  The <see cref="ZIMapFactory"/>
        /// object gets released.  After a call to this method a connection can be
        /// reestablished by calling <see cref="Connect(string, string)"/>. 
        /// </remarks>
        public void Disconnect()
        {
            if(factory != null && IsLoggedIn)
            {   ZIMapCommand.Logout cmd = new ZIMapCommand.Logout(factory);
                cmd.Execute(true);
                cmd.Dispose();
            }
            if(connection != null)
            {   connection.Close();
                MonitorInfo("Disconnect: done");
            }
            
            connection = null;
            factory = null;
            username = null;
            mailboxName = null;
        }
                                       
        // =====================================================================
        // Get MailBox list
        // =====================================================================

        /// <summary>
        /// Structure that describes a MailBox (or Folder). Returned from
        /// <see cref="Mailboxes"/>. 
        /// </summary>
        /// <remarks>
        /// This structure originally reflects data received from the server.
        /// Updates of this local information will not automatically affect the state
        /// on the server. 
        /// </remarks>
        public struct  MailBox
        {   /// <summary>The mailbox name as reported by the server.</summary>
            public string   Name;
            /// <summary>An array of mailbox attributes.</summary>
            public string[] Attributes;
            /// <summary>An array of mailbox flags.</summary>
            public string[] Flags;
            /// <summary>Number of messages in the mailbox.</summary>
            public uint     Messages;
            /// <summary>Number of recent messages in the mailbox.</summary>
            public uint     Recent;
            /// <summary>Number of unseen messages in the mailbox.</summary>
            public uint     Unseen;
            /// <summary>Can be used by an application to store private data.</summary>
            public object   UserData;
            private uint    data;     
            
            /// <summary>Returns (or sets) the hierarchy delimiter information for this mailbox.</summary>
            public char Delimiter
            {   get {   return (char)data;  }
                set {   data = (data & 0xffff0000) + value; }
            }

            /// <summary>Get or set the Subscribed state information.</summary>
            public bool Subscribed
            {   get {   return (data & 0x10000) != 0;  }
                set {   data = (uint)(value ? (data|0x10000) : (data&~0x10000));  }  
            }
            
            /// <summary>Get or set a flag that indicates the presence of subscription
            /// information (see <see cref="Subscribed"/>).</summary>
            public bool HasSubscription
            {   get {   return (data & 0x100000) != 0;  }
                set {   data = (uint)(value ? (data|0x100000) : (data&~0x100000));  }  
            }

            /// <summary>Get or set a flag that indicates the presence of Detailed
            /// information (Message counts).</summary>
            public bool HasDetails
            {   get {   return (data & 0x400000) != 0;  }
                set {   data = (uint)(value ? (data|0x400000) : (data&~0x400000));  }  
            }
            
        }

        /// <summary>
        /// Get information about mailboxes and subfolders.
        /// </summary>
        /// <param name="qualifier">
        /// Can be a prefix like "user" or "MyFolder.Something" that gets prepended
        /// to the filter argument.  There is no need to put a hierarchy delimiter
        /// at the end, this function automatically inserts it to separate qualifier
        /// and filter. 
        /// </param>
        /// <param name="filter">
        /// Can be a folder name optionally containing a '*' or a '%' character.
        /// </param>
        /// <param name="subscribed">
        /// A number indicating if the subscription status should by loaded (for
        /// <c>subscribed != 0</c>, if the status should be loaded (<c>1</c>) or
        /// if only subscribed mailboxes should be returned (<c>2</c>). 
        /// </param>
        /// <param name="detailed">
        /// When <c>false</c> only the mailbox name and the subscription status are
        /// returned (which is quite fast). A value of <c>true</c> also gets the
        /// message, seen and recent counts for each mailbox.
        /// </param>
        /// <returns>
        /// On success an array of Mailbox info structures or <c>null</c> on error.
        /// </returns>
        public MailBox[] Mailboxes(string qualifier, string filter, 
                                   uint subscribed, bool detailed)
        {
            if(factory == null) return null;
            ZIMapCommand.List cmdList = 
                (ZIMapCommand.List)factory.CreateByName((subscribed == 2) ? "LSUB" : "LIST");
            if(cmdList == null) return null;
            progress.Update(0);

            ZIMapCommand.Lsub cmdLSub = null;
            if(subscribed == 1) cmdLSub = new ZIMapCommand.Lsub(factory); 

            // does server specific things ...
            Server.NormalizeQualifierFilter(ref qualifier, ref filter);
            
            // send the commands ...
            cmdList.Queue(qualifier, filter);            
            if(cmdLSub != null) cmdLSub.Queue(qualifier, filter);
            progress.Update(5);
            
            // wait for the mailbox list ...            
            ZIMapCommand.List.Item[] items = cmdList.Items;
            cmdList.Dispose();
            if(items == null)
            {   MonitorError("Mailboxes: got no mailboxes");
                return null;
            }
            progress.Update(15);
            
            // create mailbox data ...
            MailBox[] mbox = new MailBox[items.Length];
            int irun = 0;
            foreach(ZIMapCommand.List.Item i in items)
            {   mbox[irun].Name = i.Name;
                mbox[irun].Delimiter = i.Delimiter;
                mbox[irun].Attributes = i.Attributes;
                mbox[irun].Subscribed = (subscribed == 2);
                if(detailed)
                {   ZIMapCommand.Examine cmd = new ZIMapCommand.Examine(factory);
                    cmd.UserData = irun;
                    cmd.Queue(i.Name);
                }
                irun++;
            }

            // get subscription info ...
            if(cmdLSub != null)
            {   items = cmdLSub.Items;
                progress.Update(20);
                if(!cmdLSub.CheckSuccess())
                    MonitorError("Mailboxes: got no subscription info");
                else
                {   for(uint usub=0; usub < mbox.Length; usub++) 
                        mbox[usub].HasSubscription = true;
                    if(items != null)
                    {   int icur = 0;
                        foreach(ZIMapCommand.List.Item i in items)
                        {   for(int imax=irun; imax > 0; imax--)
                            {   if(icur >= irun) icur = 0;
                                if(i.Name  == mbox[icur].Name)
                                {   mbox[icur++].Subscribed = true;
                                    break;
                                }
                                icur++;
                            }
                        }
                    }
                }
                cmdLSub.Dispose();
            }

            // fetch details ...
            if(!detailed)
            {   progress.Done();
                return mbox;
            }
            items = null;
            MonitorInfo("Mailboxes: Fetching " + irun + " details");
            progress.Update(25);
    // TODO: ZIMapApplication - The Mailboxes() code needs rewrite using bulk        
            factory.ExecuteCommands(true);
            ZIMapCommand[] cmds = factory.CompletedCommands;
            progress.Push(30, 100);
            
            foreach(ZIMapCommand c in cmds)
            {   ZIMapCommand.Examine cmd = c as ZIMapCommand.Examine;
                if(cmd == null) continue;
                irun = (int)(cmd.UserData);
                mbox[irun].HasDetails = cmd.CheckSuccess();
                mbox[irun].Messages   = cmd.Messages;
                mbox[irun].Recent     = cmd.Recent;
                mbox[irun].Unseen     = cmd.Unseen;
                mbox[irun].Flags      = cmd.Flags;
                progress.Update((uint)irun, (uint)mbox.Length); 
            }
            
            progress.Pop();
            factory.DisposeCommands(null, false);
            progress.Done();
            return mbox;
        }
         
        // =====================================================================
        // Fetch mail headers
        // =====================================================================
        
        public struct  MailInfo
        {
            public uint     Index;              // index in mailbox
            public uint     UID;                // uid (needs UID part)
            public uint     Size;               // data size (needs RFC822.SIZE)
            public string[] Parts;              // unparsed parts
            public string[] Flags;              // flags (needs FLAGS)
            public byte[]   Literal;            // literal data
            public object   UserData;
            
            public MailInfo(ZIMapCommand.Fetch.Item item)
            {   Index   = item.Index;
                UID     = item.UID;
                Size    = item.Size;
                Parts   = item.Parts;
                Flags   = item.Flags;
                Literal = item.Literal(0);
                UserData= null;
            }
        }
        
        public MailInfo[] MailHeaders()
        {   return MailHeaders(1, uint.MaxValue, null);
        }
        
        public MailInfo[] MailHeaders(uint firstIndex, uint lastIndex, string what) 
        {   if(what == null || what == "") what = "UID FLAGS RFC822.SIZE BODY.PEEK[HEADER]";
            if(lastIndex < firstIndex) return null;
            if(factory == null) return null;

            ZIMapCommand.Fetch fetch = new ZIMapCommand.Fetch(factory);
            if(fetch == null) return null;
            progress.Update(0);

            uint count = lastIndex - firstIndex;
            uint block = fetchBlock;
            uint progrcnt = 0;
            uint progrmax = count;
            List<MailInfo> items = null;
            ZIMapCommand.Fetch.Item[] part = null;
            
            while(count > 0)
            {   uint chunk = count; // Math.Min(count, block); // bug on mono?
                if(chunk > block) chunk = block;
                fetch.Reset();
                fetch.Queue(firstIndex, firstIndex+chunk-1, what);

                // semi logarithmic progress ...
                if(lastIndex == uint.MaxValue)
                {   if(progrcnt < 32*16)
                        progrcnt += 64;
                    else if(progrcnt < 64*16)
                        progrcnt += 16;
                    else if(progrcnt < 99)
                        progrcnt += 1;
                    progress.Update(progrcnt / 16);
                }
                // exact progress ...
                else
                {   progrcnt += block;
                    progress.Update(progrcnt, progrmax);
                }
                
                part = fetch.Items;
                if(part == null)
                {   if(fetch.CheckSuccess())
                        break;                      // server said: OK no more data
                    if(fetch.Result.State == ZIMapProtocol.ReceiveState.Error && items != null)
                        break;                      // server said: BAD no more data
                    if(fetch.Result.State == ZIMapProtocol.ReceiveState.Failure)
                        break;                      // server said: No no matching data
                    MonitorError("MailHeaders: FETCH failed: " + fetch.Result.Message);
                    factory.DisposeCommands(null, false);
                    return null;
                }
                
                // only one chunk, directly return the array
                if(count == chunk && items == null) break;
                
                // multiple chunks, store items in a list
                if(items == null)
                    items = new List<MailInfo>();
                foreach(ZIMapCommand.Fetch.Item item in part)
                    items.Add(new MailInfo(item));
                count -= chunk;
                firstIndex += chunk;
            }

            factory.DisposeCommands(null, false);
            MailInfo[] rval = null;
            if     (items != null) rval = items.ToArray();
            else if(part == null)  rval = new MailInfo[0];
            progress.Done();
            MonitorInfo("MailHeaders: Got " + rval.Length + " mails");
            return rval;
        }
        
        /// <summary>
        /// Fetch Mail Header for a list of UIDs (or IDs).
        /// </summary>
        /// <param name="ids">
        /// Depending on <see cref="EnableUidCommands"/> this is an array of UIDs 
        /// (<c>true</c>) or IDs (<c>false</c>).
        /// </param>
        /// <param name="what">
        /// A list of IMap items to be returned. If empty or null the default is:
        /// "<c>(UID FLAGS RFC822.SIZE BODY.PEEK[HEADER])</c>".
        /// </param>
        /// <returns>
        /// An array of mail headers (may be zero-sized) or <c>null</c> on error.
        /// </returns>
        /// <remarks>
        /// This method is used by <see cref="MailSearch(string, string, string[])"/>.
        /// It is recommended to set <see cref="EnableUidCommands"/> to <c>true</c>. 
        /// </remarks>
        public MailInfo[] MailHeaders(uint[] ids, string what) 
        {   if(factory == null || ids == null) return null;
            if(ids.Length <= 0) return new MailInfo[0];
            if(string.IsNullOrEmpty(what)) what = "UID FLAGS RFC822.SIZE BODY.PEEK[HEADER]";
            
            ZIMapCommand.Fetch fetch = new ZIMapCommand.Fetch(factory);
            if(fetch == null) return null;
            progress.Update(0);
            fetch.UidCommand = enableUid;

            uint count = (uint)ids.Length;
            uint block = fetchBlock;
            ZIMapCommand.Fetch.Item[] part = null;
            MailInfo[] rval = null;
            
            if(count <= block)
            {   fetch.Queue(ids, what);
                progress.Update(20);
                part = fetch.Items;
                if(part == null)
                {   MonitorError("MailHeaders: FETCH failed: " + fetch.Result.Message);
                    factory.DisposeCommands(null, false);
                    return null;
                }
                rval = new MailInfo[part.Length];
                for(uint irun=0; irun < part.Length; irun++)
                    rval[irun] = new MailInfo(part[irun]);
            }
            else
            {   List<MailInfo> items = new List<MailInfo>();
                uint last = 0;
                uint offs = 0;
                uint[] sub = null;
                while(count > 0)
                {   uint chunk = Math.Min(count, block);
                    if(chunk != last)
                    {   sub = new uint[chunk];
                        last = chunk;
                    }
                    Array.Copy(ids, offs, sub, 0, chunk);
                    progress.Update(offs, (uint)ids.Length);
                    fetch.Queue(sub, what);

                    part = fetch.Items;
                    if(part == null)
                    {   MonitorError("MailHeaders: FETCH failed: " + fetch.Result.Message);
                        factory.DisposeCommands(null, false);
                        return null;
                    }

                    foreach(ZIMapCommand.Fetch.Item item in part)
                        items.Add(new MailInfo(item));
                    fetch.Reset();
                    count -= chunk; offs += chunk;
                }
                rval = items.ToArray();
            }
            
            factory.DisposeCommands(null, false);
            if(rval == null) rval = new MailInfo[0];
            MonitorInfo("MailHeaders: Got " + rval.Length + " mails");
            progress.Done();
            return rval;
        }   
      
        // =====================================================================
        // Mailbox Selection 
        // =====================================================================

        /// <summary>
        /// Search a MailBox array for a mailbox.
        /// </summary>
        /// <param name="mailboxes">
        /// The array to be searched.
        /// </param>
        /// <param name="partialName">
        /// A full name or a partial name to search for.
        /// </param>
        /// <returns>
        /// An index <c>&gt;= 0</c> on succes. <c>-1</c> is returned when the
        /// mailbox was not found or if an argument was invalid. <c>-2</c> indicates
        /// a partial name the was not unique.
        /// </returns>
        /// <remarks>
        /// If the given <c>partialName</c> argument exactly matches a mailbox name
        /// (case sensitive) the search stops and the index is returned. Only when a
        /// substring is matched the search continues to detetect ambiguities.
        /// </remarks>
        public static int MailboxFind(MailBox[] mailboxes, string partialName)
        {   if(mailboxes == null || mailboxes.Length < 1 || string.IsNullOrEmpty(partialName))
                return -1;

            List<string> boxes = new List<string>();
            foreach(ZIMapApplication.MailBox mb in mailboxes) 
                boxes.Add(mb.Name);
            string[] boxea = boxes.ToArray();
            int idx = ZIMapFactory.FindInStrings(boxea, 0, partialName, true);
            if(idx < 0) return -1;                      // not found
            if(partialName == boxea[idx]) return idx;
            
            int amb = ZIMapFactory.FindInStrings(boxea, idx+1, partialName, true);
            if(amb < 0) return idx;
            if(partialName == boxea[amb]) return amb;
            return -2;                                  // ambigious
        }

        public bool MailboxOpen(string fullName, bool readOnly) 
        {   MailBox dummy;
            return MailboxOpen(fullName, readOnly, false, out dummy);
        }
        
        /// <summary>
        /// Open a mailbox if not already open.
        /// </summary>
        /// <param name="fullName">
        /// Fully qualified mailbox name.
        /// </param>
        /// <param name="readOnly">
        /// When <c>true</c> the IMap "EXAMINE" command is used, otherwiese the
        /// "SELECT" command.
        /// </param>
        /// <param name="returnDetails">
        /// When <c>true</c> an IMap request is always made and the <paramref name="info"/>
        /// data returns valid message counts.
        /// </param>
        /// <param name="info">
        /// Always returns the mailbox name, message counts are only set when
        /// <paramref name="returnDetails"/> is set.
        /// </param>
        /// <returns>
        /// True <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// If the mailbox is still open, <paramref name="returnDetails"/> is not set
        /// and if the read only state will not change no IMap commmand ist sent.  The
        /// neccessary information about the validity of the current mailbox is provided
        /// by <see cref="ZIMapTransport.LastSelectTag"/>.
        /// </remarks>
        public bool MailboxOpen(string fullName, bool readOnly, 
                                bool returnDetails, out MailBox info) 
        {   info = new MailBox();
            if(factory == null) return false;

            // recent mailbox still valid? But not if returnDetails is set... 
            if(mailboxName != null)
            {   if(returnDetails || mailboxReadonly != readOnly || mailboxName != fullName)
                    mailboxName = null;
                else if(connection.TransportLayer.LastSelectTag != mailboxTag)
                    mailboxName = null;
                else
                {   MonitorInfo("MailboxOpen: still valid: " + fullName);
                    info.Name = fullName;
                    return true;
                }
            }

            // run a SELECT or EXAMINE command
            ZIMapCommand.Select cmd = 
                (ZIMapCommand.Select)factory.CreateByName(readOnly ? "EXAMINE" : "SELECT");
            if(cmd == null) return false;
            progress.Update(0);
            
            cmd.Queue(fullName);
            if(!cmd.CheckSuccess())
            {   MonitorError("MailboxOpen: command failed: " + cmd.Result.Message);
                cmd.Dispose();
                return false;
            }
            readOnly = cmd.IsReadOnly;
            if(returnDetails)
            {   if(!cmd.Parse())
                {   MonitorError("MailboxOpen: got invalid data");
                    cmd.Dispose();
                    return false;
                }
                info.Messages = cmd.Messages;
                info.Recent   = cmd.Recent;
                info.Unseen   = cmd.Unseen;
            }
            info.Name = fullName;
            
            // save state ...
            uint nsid = Server.FindNamespace(fullName);
            mailboxNamespace = Server[nsid];
            mailboxName = fullName;
            mailboxReadonly = readOnly;
            mailboxTag = cmd.Tag;
            cmd.Dispose();
            progress.Done();
            return true;
        }
        
        public bool MailboxClose(bool waitForResult)
        {   mailboxName = null;  mailboxTag = 0;  mailboxNamespace = null;
            if(connection.TransportLayer.LastSelectTag == 0)
                return true;
            
            connection.TransportLayer.LastSelectTag = 0;
            ZIMapCommand.Close cmd = new ZIMapCommand.Close(Factory);
            if(cmd == null) return false;

            cmd.Queue();
            if(!Factory.EnableAutoDispose) waitForResult = true;
            if(!waitForResult) return cmd.Execute(false);
            
            bool bok = cmd.Result.Succeeded;
            cmd.Dispose();
            return bok;
        }
        
        // =====================================================================
        // Deleting
        // =====================================================================

        public uint MailDelete(uint item, bool expunge)
        {   return MailDelete(item, item, expunge);
        }
        
        public uint MailDelete(uint firstIndex, uint lastIndex, bool expunge)
        {   if(lastIndex < firstIndex) return 0;

            ZIMapCommand.Store store = new ZIMapCommand.Store(factory);
            ZIMapCommand disp = store;
            if(store == null)     return 0;
            store.UidCommand = enableUid;
            
            bool bok = store.Queue(firstIndex, lastIndex, "+FLAGS (/Deleted)");
            uint count = 0;
            if(bok && expunge) 
            {   ZIMapCommand.Expunge expu = new ZIMapCommand.Expunge(factory);
                if(expu != null)
                {   uint[] resu = expu.Expunged;
                    if(resu != null) count = (uint)resu.Length;
                    disp = expu;
                }
            }
            disp.Dispose();
            return count;
        }
        
        public uint MailDelete(uint[] items, bool expunge)
        {   if(items == null)     return 0;
            if(items.Length <= 0) return 0;

            ZIMapCommand.Store store = new ZIMapCommand.Store(factory);
            ZIMapCommand disp = store;
            if(store == null)     return 0;
            store.UidCommand = enableUid;
            
            bool bok = store.Queue(items, "+FLAGS (/Deleted)");
            uint count = 0;
            if(bok && expunge) 
            {   ZIMapCommand.Expunge expu = new ZIMapCommand.Expunge(factory);
                if(expu != null)
                {   uint[] resu = expu.Expunged;
                    if(resu != null) count = (uint)resu.Length;
                    disp = expu;
                }
            }
            disp.Dispose();
            return count;
        }
        
        // =====================================================================
        // Searching
        // =====================================================================
        
        public MailInfo[] MailSearch(string what, string charset, string search,
                                     params string [] extra)
        {   if(factory == null) return null;
            progress.Push(0, 20);
            uint [] matches = MailSearch(charset, search, extra);
            progress.Pop();
            return this.MailHeaders(matches, what);
        }
        
        public uint[] MailSearch(string charset, string search, params string [] extra)
        {   if(factory == null) return null;
            ZIMapCommand.Search cmd = new ZIMapCommand.Search(factory);
            if(cmd == null) return null;
            cmd.UidCommand = enableUid;

            progress.Update(0);
            cmd.Queue(charset, search, extra);
            progress.Update(90);
            
            uint [] matches = cmd.Matches;
            if(matches == null)
                MonitorError("MailSearch: command failed: " + cmd.Result.Message);
            else if(matches.Length == 0)
                MonitorInfo("MailSearch: nothing found");
            else
            {   MonitorInfo("MailSearch: got " + matches.Length + " matches");
            }
            
            cmd.Dispose();
            progress.Done();
            return matches;
        }
            
        // =====================================================================
        // Quota 
        // =====================================================================

        /// <summary>
        /// A structure that holds quota information, returned by the QuotaInfo() methods. 
        /// </summary>
        public struct QuotaInfo
        {   /// <summary>Name of the folder from which these quota are inherited.</summary>
            /// <remarks>
            /// The IMap command <c>GetQuotaRoot</c> returns an array (usually containing one
            /// element) of quota root names. Child folders may for example return the same root
            /// name as their parent if the have no separate quota set.  This implementation
            /// uses only the 1st returned QuotaRoot.
            /// </remarks>
            public  string      QuotaRoot;
            /// <summary>Number of kBytes used for message storage.</summary>
            public  uint        StorageUsage;
            /// <summary>Quota limit in kBytes for message storage.</summary>
            public  uint        StorageLimit;
            /// <summary>Number of messages used.</summary>
            public  uint        MessageUsage;
            /// <summary>Quota limit for messages.</summary>
            public  uint        MessageLimit;
            
            public  bool Valid
            {   get {   return QuotaRoot != null;   }
            }
        }
        
        /// <summary>
        /// Return quota information for a single Mailbox.
        /// </summary>
        /// <param name="mailboxFullName">
        /// Must be a full mailbox name.
        /// </param>
        /// <param name="info">
        /// A structure that receives the returned quota information.
        /// </param>
        /// <returns>
        /// <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// The quota root name is not always the same as the mailbox name, see
        /// the description of the return value.
        /// </remarks>
        public bool QuotaInfos(string mailboxFullName, out QuotaInfo info)
        {   if(factory == null)
                mailboxFullName = null;
            else if(string.IsNullOrEmpty(mailboxFullName))
                mailboxFullName = mailboxName;
            if(mailboxFullName == null) 
            {   info = new QuotaInfo();
                return false;
            }
            
            ZIMapCommand.GetQuotaRoot gqr = new ZIMapCommand.GetQuotaRoot(factory);
            if(gqr != null) gqr.Queue(mailboxFullName);
            return QuotaInfos(gqr, out info); 
        }
                              
        /// <summary>
        /// Return quota information by executing a <c>GetQuotaRoot</c> command passed
        /// as an argument.
        /// </summary>
        /// <param name="gqr">
        /// A command for which the Query() has been called.
        /// </param>
        /// <param name="info">
        /// A structure that receives the returned quota information.
        /// </param>
        /// <returns>
        /// <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// The quota root name is not always the same as the mailbox name, see
        /// the description of the return value.
        /// </remarks>
        public bool QuotaInfos(ZIMapCommand.GetQuotaRoot gqr, out QuotaInfo info)
        {   info = new QuotaInfo();
            if(factory == null || gqr == null) return false;
            
            // check result ...
            if(!gqr.CheckSuccess("")) return false;
            ZIMapCommand.GetQuotaRoot.Item[] quota = gqr.Quota;
            if(quota == null || quota.Length == 0)
            {   MonitorInfo("QuotaInfo: nothing found");
                return false;
            }
            
            // return results ...
            MonitorInfo("QuotaInfo: got " + quota.Length + " quota");
            foreach(ZIMapCommand.GetQuotaRoot.Item item in quota)
            {   if(item.Resource == "MESSAGE")
                {   info.MessageUsage += item.Usage;
                    info.MessageLimit += item.Limit;
                }
                else if(item.Resource == "STORAGE")
                {   info.StorageUsage += item.Usage;
                    info.StorageLimit += item.Limit;
                }
            }
            info.QuotaRoot = (gqr.Roots.Length > 0) ? gqr.Roots[gqr.Roots.Length-1] : ""; 
            return true;
        }

        /// <summary>
        /// Set or update quota limits.
        /// </summary>
        /// <param name="mailboxFullName">
        /// Must be a full mailbox name.
        /// </param>
        /// <param name="storageLimit">
        /// Quota limit in kBytes for message storage or <c>0</c> for no storage limit.
        /// </param>
        /// <param name="messageLimit">
        /// Quota limit for messages or <c>0</c> for no message limit.
        /// </param>
        /// <returns>
        /// A <see cref="System.Boolean"/>
        /// </returns>
        /// <remarks>
        /// The <see cref="ZIMapServer.HasLimit"/> is checked to see if an 
        /// attempt to set a storage limit is ignored because the server does not
        /// support storage limits.  The same behaviour applies to message limits.
        /// In these cases no error status is returned.
        /// </remarks>
        public bool QuotaLimit(string mailboxFullName,
                               uint storageLimit, uint messageLimit)
        {
            if(factory == null) return false;
            if(string.IsNullOrEmpty(mailboxFullName))
                mailboxFullName = mailboxName;
            if(mailboxFullName == null) return false;

            ZIMapCommand.GetQuotaRoot gqr;
            string root;
            using(gqr = new ZIMapCommand.GetQuotaRoot(factory))
            {   gqr.Queue(mailboxFullName);
                if(!gqr.CheckSuccess("QuotaLimits failed: {1}")) return false;
                root = gqr.Roots[0];
            }

            if(!Server.HasLimit("storage")) storageLimit = 0;            
            if(!Server.HasLimit("message")) messageLimit = 0;            
            
            ZIMapCommand.SetQuota cmd;
            using(cmd = new ZIMapCommand.SetQuota(factory))
            {   StringBuilder sb = new StringBuilder();
                if(messageLimit > 0) sb.Append("MESSAGE " + messageLimit);
                if(storageLimit > 0)
                {   if(sb.Length > 0) sb.Append(' ');
                    sb.Append("STORAGE " + storageLimit);
                }
                cmd.Queue(root, sb.ToString());
                return cmd.CheckSuccess("QuotaLimits failed: {1}");
            }
        }
                
        // =====================================================================
        // Import/Export 
        // =====================================================================

        /// <summary>
        /// Open an export file or folder for writing.
        /// </summary>
        /// <param name="path">
        /// A path or filename depending on <paramref name="allowFile"/>
        /// </param>
        /// <param name="allowFile">
        /// When <c>false</c> the <paramref name="path"/> argument must specify a folder.
        /// </param>
        /// <param name="preferVersions">
        /// If <c>true</c> the files will be versioned, if <c>false</c> all existing
        /// versions of a file will be removed before a new file is created.
        /// </param>
        /// <returns>
        /// On success <c>true</c> is returned.
        /// </returns>
        /// <remarks>
        /// This routine checks for the existence of a folder or a file and eventually
        /// creates it, but no file will be opened for reading or writing.  Success
        /// does not mean that you have the neccessary rights to read or write data.
        /// <para/>
        /// This routine configures the use of the personal namespace delimiter (returned by 
        /// <see cref="ZIMapServer.DefaultDelimiter"/>).
        /// </remarks>
        public bool ExportWrite(string path, bool allowFile, bool preferVersions)
        {   ZIMapExport expo = Export;
            expo.Versioning = preferVersions;
            exportSerial = expo.Open(path, Server.DefaultDelimiter, allowFile, true);
            if(exportSerial == 0) return false;             // open failed
            exportWrite = true;
            return true;
        }
        
        /// <summary>
        /// Open an export file or folder for reading.
        /// </summary>
        /// <param name="path">
        /// A path or filename depending on <paramref name="allowFile"/>
        /// </param>
        /// <param name="allowFile">
        /// When <c>false</c> the <paramref name="path"/> argument must specify a folder.
        /// </param>
        /// <returns>
        /// On success <c>true</c> is returned.
        /// </returns>
        /// <remarks>
        /// This routine checks for the existence of a folder or a file, but no file will be
        /// opened for reading.  Success does not mean that you have the neccessary rights
        /// to read data.
        /// <para/>
        /// This routine configures the use of the personal namespace delimiter (returned by 
        /// <see cref="ZIMapServer.DefaultDelimiter"/>).
        /// </remarks>
        public bool ExportRead(string path, bool allowFile)
        {   ZIMapExport expo = Export;
            exportSerial = expo.Open(path, Server.DefaultDelimiter, allowFile, false);
            if(exportSerial == 0) return false;             // open failed
            exportWrite = false;
            return true;
        }
        
        public bool ExportMailbox(string fullName, uint nsIndex)
        {   if(string.IsNullOrEmpty(fullName)) return false;
            if(export == null || exportSerial == 0 || exportSerial != export.Serial)
            {   MonitorError("ExportMailbox: No open export file or folder");
                return false;
            }
            // get nsIndex and friendly name ...
            if(exportWrite && nsIndex == ZIMapServer.Nothing)
                    fullName = Server.FriendlyName(fullName, out nsIndex);
            export.Delimiter = Server[nsIndex].Delimiter;

            return exportWrite ? export.WriteMailbox(fullName)
                               : export.ReadMailbox(fullName);
        }
    }
}
