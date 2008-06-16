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
    /// Class that implement the Application layer of ZIMap.
    /// </summary>
    /// <remarks>
    /// The application layer is the top-most of the four ZIMapLib layers. The others
    /// are the command, the protocol and the transport layers.
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
        private uint            exportSerial;
        
        // feature flags ...
        private bool            enableNamespaces = true;
        private bool            enableRights = true;
        private bool            enableQuota = true;
        private bool            enableUid = true;

        private bool            enableMessages;
            
        // see OpenMailbox ...
        private string          mailboxName;
        private uint            mailboxTag;
        private bool            mailboxReadonly;
        
        // =====================================================================
        // Accessors
        // =====================================================================
        
        public ZIMapConnection Connection
        {   get {   return connection;  }
        }

// TODO: implement EnableMessagesReporting        
        public bool EnableMessagesReporting
        {   get {   return enableMessages;  }
            set {   enableMessages = value; }
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
        {   get {   return enableNamespaces;  }
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
        
        public bool MailboxIsReadonly
        {   get {    return mailboxReadonly; }
        }
        
        public new ZIMapConnection.Monitor MonitorLevel
        {   get {   return monitorLevel;  }
            set {   SetMonitorLevel(value, false);  }
        }

        public ZIMapServer Server
        {   get {   if(server == null) 
                        server = ZIMapServer.Create(factory, EnableNamespaces);   
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
        
        public bool Connect(string user, string password)
        {   return Connect(user, password, ZIMapConnection.TlsModeEnum.Automatic);
        }
        
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
            {   if(Server.NamespaceData(ZIMapServer.Personal).Valid)
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

        public struct  MailBox
        {   public string   Name;
            public string[] Attributes;
            public string[] Flags;
            public char     Delimiter;   
            public bool     Subscribed;            
            public uint     Messages;
            public uint     Recent;
            public uint     Unseen;
            public object   UserData;
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
                else if(items != null)
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
            
            factory.ExecuteCommands(true);
            ZIMapCommand[] cmds = factory.CompletedCommands;
            progress.Push(30, 100);
            
            foreach(ZIMapCommand c in cmds)
            {   ZIMapCommand.Examine cmd = c as ZIMapCommand.Examine;
                if(cmd == null) continue;
                irun = (int)(cmd.UserData);
                mbox[irun].Messages = cmd.Messages;
                mbox[irun].Recent   = cmd.Recent;
                mbox[irun].Unseen   = cmd.Unseen;
                mbox[irun].Flags    = cmd.Flags;
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
        {   if(string.IsNullOrEmpty(what)) what = "UID FLAGS RFC822.SIZE BODY.PEEK[HEADER]";
            if(ids == null || ids.Length < 1) return null;
            if(factory == null) return null;

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
            mailboxName = fullName;
            mailboxReadonly = readOnly;
            mailboxTag = cmd.Tag;
            cmd.Dispose();
            progress.Done();
            return true;
        }
        
        public bool MailboxClose(bool waitForResult)
        {   mailboxName = null;  mailboxTag = 0;
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
        
        public uint MailDelete(uint [] items, bool expunge)
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
            ZIMapCommand.Search cmd = new ZIMapCommand.Search(factory);
            if(cmd == null) return null;
            cmd.UidCommand = enableUid;

            progress.Update(0);
            cmd.Queue(charset, search, extra);
            progress.Update(5);
            
            uint [] matches = cmd.Matches;
            if(matches == null)
                MonitorError("MailSearch: command failed: " + cmd.Result.Message);
            else if(matches.Length == 0)
                MonitorInfo("MailSearch: nothing found");
            else
            {   MonitorInfo("MailSearch: got " + matches.Length + " matches");
            }
            
            cmd.Dispose();
            progress.Update(10);
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
        /// Return quota information for a single Mailbox.
        /// </summary>
        /// <param name="mailboxFullName">
        /// Must be a full mailbox name.
        /// </param>
        /// <param name="storageUsage">
        /// Number of kBytes used for message storage.
        /// </param>
        /// <param name="storageLimit">
        /// Quota limit in kBytes for message storage.
        /// </param>
        /// <param name="messageUsage">
        /// Number of messages used.
        /// </param>
        /// <param name="messageLimit">
        /// Quota limit for messages.
        /// </param>
        /// <returns>
        /// Returns an array (usually containing one element) of quota root names.
        /// Child folders may for example return the same root name as their parent
        /// if the have no separate quota set. On error <c>null</c> is returned.
        /// </returns>
        /// <remarks>
        /// The quota root name is not always the same as the mailbox name, see
        /// the description of the return value.
        /// </remarks>
        public string[] QuotaInfos(string mailboxFullName,
                              out uint storageUsage, out uint storageLimit,
                              out uint messageUsage, out uint messageLimit)   
        {   messageUsage = messageLimit = storageUsage = storageLimit = 0;
            
            if(factory == null) return null;
            if(string.IsNullOrEmpty(mailboxFullName))
                mailboxFullName = mailboxName;
            if(mailboxFullName == null) return null;

            ZIMapCommand.GetQuotaRoot gqr = new ZIMapCommand.GetQuotaRoot(factory);
            if(gqr == null) return null;
            gqr.Queue(mailboxFullName);
            ZIMapCommand.GetQuotaRoot.Item[] quota = gqr.Quota;
            
            if(quota == null)
                MonitorError("QuotaInfo: command failed");
            else if(quota.Length == 0)
                MonitorInfo("QuotaInfo: nothing found");
            else
            {   MonitorInfo("QuotaInfo: got " + quota.Length + " quota");
                foreach(ZIMapCommand.GetQuotaRoot.Item item in quota)
                {   if(item.Resource == "MESSAGE")
                    {   messageUsage += item.Usage;
                        messageLimit += item.Limit;
                    }
                    else if(item.Resource == "STORAGE")
                    {   storageUsage += item.Usage;
                        storageLimit += item.Limit;
                    }
                }
            }
            string[] roots = gqr.Roots;
            gqr.Dispose();
            return roots;
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

            ZIMapCommand.GetQuotaRoot gqr = new ZIMapCommand.GetQuotaRoot(factory);
            if(gqr == null) return false;
            if(!gqr.Queue(mailboxFullName))
            {   MonitorError("QuotaLimits: command failed: " + gqr.Result.Message);
                gqr.Dispose();
                return false;
            }
            string root = gqr.Roots[0];

            if(!Server.HasLimit("storage")) storageLimit = 0;            
            if(!Server.HasLimit("message")) messageLimit = 0;            
            
            ZIMapCommand.SetQuota cmd = new ZIMapCommand.SetQuota(factory);
            StringBuilder sb = new StringBuilder();
            if(messageLimit > 0) sb.Append("MESSAGE " + messageLimit);
            if(storageLimit > 0)
            {   if(sb.Length > 0) sb.Append(' ');
                sb.Append("STORAGE " + storageLimit);
            }
            cmd.Queue(root, sb.ToString());
            bool bok = cmd.Result.Succeeded;
            if(!bok) MonitorError("QuotaLimits: command failed: " + cmd.Result.Message);
            cmd.Dispose();
            return bok;
        }
                
        // =====================================================================
        // Import/Export 
        // =====================================================================

        public ZIMapExport OpenExport(string path, bool allowFile, bool preferVersions)
        {   ZIMapExport expo = Export;
            expo.Versioning = true;
            exportSerial = expo.Open(path, Server.DefaultDelimiter, allowFile, true, true);
            if(exportSerial == 0) return null;              // open failed
            return expo;
        }
        
        public ZIMapExport OpenImport(string path, bool allowFile)
        {   ZIMapExport expo = Export;
            exportSerial = expo.Open(path, Server.DefaultDelimiter, allowFile, false, false);
            if(exportSerial == 0) return null;              // open failed
            return expo;
        }
    }
}
