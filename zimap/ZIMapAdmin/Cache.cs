//==============================================================================
// Cache.cs     The ZLibAdmin data cache
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU General Public License
// This software is published under the GNU GPL license.  Please refer to the
// file COPYING for details. Please use the following e-mail address to contact
// me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.Collections.Generic;
using ZIMap;
using ZTool;

namespace ZIMapTools
{
    /// <summary>
    /// The is a "high level" software layer accesss to access IMap data via 
    /// ZIMapLib.
    /// </summary>
    public partial class CacheData : ZIMapBase
    {   /// <summary>Set by ListMailboxes to allow numeric folder references</summary>
        public MBoxRef                      ListedFolders;
        /// <summary>Set by ListUsers to allow numeric user references</summary>
        public MBoxRef                      ListedUsers;

        private ZIMapApplication            application;
        private ZIMapServer                 server;    
        private ZIMapFactory                factory;    
        private ZIMapConnection             connection;    
        private ZIMapConnection.Progress    progress;
         
        public  readonly DataRef            Data;
        private readonly DataRefImp         data;
        
        // =============================================================================
        // DataRef     
        // =============================================================================

        private class DataRefImp : DataRef
        {
            private CacheData   parent;
            
            public DataRefImp(CacheData parent)
            {   this.parent = parent;
            }
            
            protected override bool LoadData(Info what)
            {   bool bok = true;
                if((what & (Info.Folders | Info.Details)) != 0)
                {   bool details = (what & Info.Details) != 0;
                    if(!parent.FoldersLoad(details)) bok = false;
                }

                if((what & (Info.Rights | Info.Quota)) != 0)
                {   bool rights = (what & Info.Rights) != 0;
                    bool quota  = (what & Info.Quota)  != 0;
                    if(!parent.FoldersExtras(rights, quota)) bok = false;
                }
                    
                if((what & (Info.Others | Info.Shared)) != 0)
                {   bool others = (what & Info.Others) != 0;
                    if(!parent.UserLoad(others)) bok = false;
                }
                if((what & Info.Headers) != 0)
                {   if(!parent.HeadersLoad()) bok = false;
                }
                return bok;
            }
            
            public new void UpdateCurrent(MBoxRef current)
            {   base.UpdateCurrent(current);
            }
            
            public void UpdateFolders(ZIMapApplication.MailBox[] boxes, bool details)
            {   base.UpdateFolders(new MBoxRef(boxes), details);   
            }
            
            public new void UpdateExtras(bool rights, bool quota)
            {   base.UpdateExtras(rights, quota);   
            }
            
            public void UpdateUsers(ZIMapApplication.MailBox[] users, bool others)
            {   base.UpdateUsers(new MBoxRef(users), others);                
            }

            public void UpdateHeaders(ZIMapApplication.MailInfo[] headers)
            {   base.UpdateHeaders(new MailRef(headers, parent.application.MailboxName));
            }

            public override string Qualifier
            {   set {   if(value == "INBOX")
                            value = parent.server.NamespaceDataPersonal.Qualifier;
                        base.Qualifier = value;
                    }
            }

        }
        
        // =============================================================================
        // xtor     
        // =============================================================================
       
        public CacheData(ZIMapApplication application) : base(application.Parent)
        {   this.application = application;
            connection = application.Connection;
            server     = application.Server;
            factory    = connection.CommandLayer;
            progress   = connection.ProgressReporting;
            // At least deliver Info level messages ...
            MonitorLevel = application.MonitorLevel;
            if(MonitorLevel > ZIMapConnection.Monitor.Info) 
                MonitorLevel = ZIMapConnection.Monitor.Info;
            Data = data = new DataRefImp(this);
        }
            
        /// <summary>
        /// Output a top level error message via ZIMapConnection.
        /// </summary>
        /// <param name="message">
        /// Message to be displayed as an application error.
        /// </param>
        /// <param name="level">
        /// Severity or kind of the message.
        /// </param>
        /// <remarks>
        /// The ZIMapAdmin application uses an additional colon to filter
        /// top level error messages. Example: ":Top level error".
        /// </remarks>
        protected override void MonitorInvoke(ZIMapConnection.Monitor level, string message)
        {   if(MonitorLevel <= level)
                ZIMapConnection.MonitorInvoke(connection, "Cache", level, ":" + message); 
        }

        // =============================================================================
        // Properties     
        // =============================================================================

        public MBoxRef this[uint index]
        {   get {   return new MBoxRef(Data.Folders.Array, index);
                }
        }
        
        public MBoxRef this[string mailbox]
        {   get {   uint index = MailboxFind(ref mailbox, true);
                    return new MBoxRef(Data.Folders.Array, index);
                }
        }

        // =============================================================================
        // Operations on Folders     
        // =============================================================================
        
        public bool FoldersLoad(bool details)
        {   uint smode = (uint)(details ? 1 : 0);
            ZIMapApplication.MailBox[] boxes =
                application.Mailboxes(data.Qualifier, data.Filter, smode, details);
            if(boxes == null) return false;
            
            // A blank (e.g. "") list prefix filters for the current user!
            uint ufil = server.FindNamespace(data.Qualifier, false);
            if(ufil != uint.MaxValue) server.MailboxesFilter(ref boxes, ufil);
            
            // Allways sort (should by fast) ...
            server.MailboxSort(boxes);
            data.UpdateFolders(boxes, details);
            return true;
        }

        public bool FoldersExtras(bool wantRights, bool wantQuota)
        {   data.UpdateExtras(wantRights, wantQuota);
            if(!application.EnableRights) wantRights = false;
            if(!application.EnableQuota)  wantQuota  = false;
            if(!wantRights && !wantQuota) return true;
            MBoxRef boxes = data.Folders;
            uint ucnt = boxes.Count;
            if(ucnt == 0) return true;

            // progress report setup ...
            uint upro = ucnt;
            if(wantRights && wantQuota) upro += ucnt;
            progress.Update(0);
            MBoxRef mout = new MBoxRef(boxes.Array);

            // get rights ...
            uint ures = 0; boxes.Reset();
            if(wantRights)
                using(ZIMapFactory.Bulk rights = factory.CreateBulk("MyRights", 3, false))
                {   ZIMapCommand.Generic current = null;
                    while(ures < ucnt)
                    {   if(rights.NextCommand(ref current))
                        {   mout.Next((uint)current.UserData);
                            mout.ExtraRights = ((ZIMapCommand.MyRights)current).Rights;
                            current.Reset(); ures++;
                            progress.Update(ures, upro);
                        }
                        if(boxes.Next())
                        {   if(boxes.ExtraRights != null) ures++;
                            else
                            {   ((ZIMapCommand.MyRights)current).Queue(boxes.Name);
                                current.UserData = boxes.Index;
                            }
                        }
                    }
                }

            // get quota info ...
            ures = 0; boxes.Reset();
            if(wantQuota)
                using(ZIMapFactory.Bulk rights = factory.CreateBulk("GetQuotaRoot", 3, false))
                {   ZIMapCommand.Generic current = null;
                    while(ures < ucnt)
                    {   if(rights.NextCommand(ref current))
                        {   ZIMapApplication.QuotaInfo info;
                            application.QuotaInfos((ZIMapCommand.GetQuotaRoot)current, out info);
                            mout.Next((uint)current.UserData);
                            mout.ExtraSetQuota(info);               // also save on failure
                            current.Reset(); ures++;
                            progress.Update(ures, upro);
                        }
                        if(boxes.Next())
                        {   if(boxes.ExtraQuotaRoot != null) ures++;
                            else 
                            {   ((ZIMapCommand.GetQuotaRoot)current).Queue(boxes.Name);
                                current.UserData = boxes.Index;
                            }
                        }
                    }
                }
            return true;
        }
        
        // =============================================================================
        // Operations on Headers     
        // =============================================================================

        public bool HeadersLoad()
        {   if(data.Current.IsNothing)                      // must have current
            {   MonitorError("No current mailbox");
                return false;
            }
            data.Current.Reset();

            if(!data.Current.Open(application, true))       // make sure that it is open
                return false;                               // could not reopen
            
            data.UpdateHeaders(application.MailHeaders());  // load the data
            return true;
        }
        
        public bool HeadersSort(MailRef headers, string field, bool reverse)
        {   if(headers.IsNothing) return false;
            uint ulen = headers.Count;
            if(ulen == 0) return true;
            
            // no sort, do reverse?
            if(string.IsNullOrEmpty(field))
            {   if(reverse) Array.Reverse(headers.Array(0));
                return true;
            }
            
            // check the field name ...
            ZIMapMessage mail = null;
            switch(field)
            {   case "size":
                case "flags":
                case "id":
                case "uid":     break;                      // no mail parsing
                case "to":
                case "from":
                case "subject":
                case "date":    mail = new ZIMapMessage();  // must parse mail
                                break;
                default:        return false;               // bad field
            }

            // build sort array ...
            object[] keys = new object[ulen];
            for(uint urun=0; urun < ulen; urun++)
            {   if(mail != null)
                {   progress.Update(urun, ulen);
                    mail.Parse(headers[urun].Literal, true);
                }
                switch(field)
                {   case "size":    keys[urun] = headers[urun].Size; break;
                    case "flags":   keys[urun] = string.Join(" ", headers[urun].Flags); break;
                    case "id":      keys[urun] = headers[urun].Index; break;
                    case "uid":     keys[urun] = headers[urun].UID; break;
                    case "to":      keys[urun] = mail.To; break;
                    case "from":    keys[urun] = mail.From; break;
                    case "subject": keys[urun] = mail.Subject; break;
                    case "date":    keys[urun] = mail.DateBinary; break;
                 }                
            }
            Array.Sort(keys, headers.Array);
            if(reverse) Array.Reverse(headers.Array);
            return true;
        }

        // =============================================================================
        // Operations on Users     
        // =============================================================================

        public bool UserFind(ref string username, bool mustExist)
        {   if(string.IsNullOrEmpty(username) || username == ".")
                username = "INBOX";
            
            // check if numeric else strip any leading dot ...
            uint uidx;
            if(uint.TryParse(username, out uidx))
            {   if(ListedUsers.IsNothing)
                {   MonitorError("Please list users before using numeric references");
                    return false;
                }
                if(uidx == 0 || !ListedUsers.Next(uidx-1)) 
                {   MonitorError("Not a valid user reference: {0}", uidx);
                    return false;
                }
                username = ListedUsers.Name;
            }
            if(username[0] == '.') username = username.Substring(1);
            string frag = username;

            int idx = ZIMapApplication.MailboxFind(data.Users.Array, username);
            if(idx == -1 && mustExist && application.User.Contains(username))            
                idx = ZIMapApplication.MailboxFind(data.Users.Array, "INBOX");
            
            if(idx == -2 && mustExist)
            {   MonitorError("User name is not unique: " + username);
                return false;
            }
            if(idx < 0 && mustExist)
            {   MonitorError("User not found: " + username);
                return false;
            }
            if(idx >= 0)
            {   username = data.Users.Array[idx].Name;
                if(!mustExist) 
                {   if(username == frag ||
                       username.EndsWith(data.Users.Array[idx].Delimiter + frag))
                    {   MonitorError("User already exists: " + username);
                        return false;
                    }
                    username = frag;                // original name ok
                }
            }
            return true;
        }
        
        public bool UserLoad(bool others)
        {
            // reload user data
            ZIMapServer.Namespace ns = others ? server.NamespaceDataOther :
                                                server.NamespaceDataShared;
            //if(!ns.Valid) return false;

            // The "%" filter returns users even if they have in root mailbox!
            ZIMapApplication.MailBox[] users = application.Mailboxes(ns.Prefix, "%", 0, false);
            if(users == null) return false;

            if(!ns.Valid)            
                MonitorInfo("Namespaces are disabled");
            // List of "other users", add current user
            else if(others)
            {   ZIMapApplication.MailBox[] oldu = users;
                users = new ZIMapApplication.MailBox[oldu.Length + 1];
                users[0].Name = server.NamespaceDataPersonal.Prefix + "INBOX";
                Array.Copy(oldu, 0, users, 1, oldu.Length);
            }
            // List of "shared folders", remove "user" folder and the current user
            else
                server.MailboxesFilter(ref users, ns.Index);

            server.MailboxSort(users);
            data.UpdateUsers(users, others);
            return true;
        }
        
        public bool UserManage(string user, string command, 
                               uint storageLimit, uint messageLimit)
        {   if(string.IsNullOrEmpty(command)) return false;
            if(string.IsNullOrEmpty(user)) return false;
            
            bool bQuota  = false;
            bool bCreate = false;
            bool bDelete = false;
            command = command.ToLower();
            switch(command)
            {   case "quota":   bQuota = true; 
                                break;
                case "create":  bQuota = (storageLimit > 0 | messageLimit > 0);
                                bCreate = true;
                                break;
                case "delete":
                                bDelete = true;
                                break;
                default:
                                return false; 
            }

            bool bok = true;
            string info1 = null, info2 = null;

            if(bCreate || bDelete)
            {   if(bDelete)
                {   MailboxAdminRigths(user, null, "all", false);
                    progress.Update(60);
                }
                ZIMapCommand.MailboxBase cmd = 
                    (ZIMapCommand.MailboxBase)(factory.CreateByName(command)); 
                cmd.Queue(user);
                if(cmd.CheckSuccess())
                {   info1 = string.Format("Mailbox {0}d: {1}", command, user);
                    Data.Clear(Info.Others | Info.Shared);
                    if(bQuota) progress.Update(50);
                    
                }
                else
                {   MonitorError("Could not {0} mailbox: {1}", command, user);
                    bok = false;
                }
                cmd.Dispose();
            }
            
            if(bQuota)
            {   if(application.QuotaLimit(user, storageLimit, messageLimit))
                {   info2 = string.Format("Quota limits for '{0}' updated", user);
                    Data.Clear(Info.Quota);
                }
            }
            
            if(info1 != null) MonitorInfo(info1);
            if(info2 != null) MonitorInfo(info2);
            return bok;
        }

        // =============================================================================
        // Operations on mailboxes     
        // =============================================================================

        /// <summary>
        /// Reopen the current mailbox or enable write mode. 
        /// </summary>
        /// <param name="readOnly">
        /// A value of <c>false</c> (re-)opens the mailbox in write mode. 
        /// </param>
        /// <returns>
        /// On success <c>true</c> is returned.
        /// </returns>        
        public bool MailboxOpen(bool readOnly)
        {   return MailboxOpen(this["."], readOnly);
        }
        
        /// <summary>
        /// Open a mailbox and make it current or enable write mode. 
        /// </summary>
        /// <param name="mbox">
        /// Reference to the mailbox the is to be opened. 
        /// </param>
        /// <param name="readOnly">
        /// A value of <c>false</c> (re-)opens the mailbox in write mode. 
        /// </param>
        /// <returns>
        /// On success <c>true</c> is returned.
        /// </returns>        
        /// <remarks>
        /// The mailbox reference must be valid for this function to succeed. See
        /// <see cref="OpenMailbox(bool)"/> for a simple way to reopen the current
        /// mailbox.
        /// </remarks>
        public bool MailboxOpen(MBoxRef mbox, bool readOnly)
        {   if(!mbox.IsValid) return false;
            
            // we want to use the current mailbox ...
            if(mbox.Name == application.MailboxName)
            {   if(!readOnly && application.MailboxIsReadonly)
                {   if(!ZIMapAdmin.Confirm("Current mailbox '{0}' is readonly. Change mode", 
                                           mbox.Name)) return false;
                    MonitorInfo("Setting write mode for current mailbox");
                }
            }
            else
                data.Clear(Info.Headers);

            // (re-)open the mailbox
            data.UpdateCurrent(MBoxRef.Nothing);
            if(!mbox.Open(application, readOnly)) return false;
            data.UpdateCurrent(mbox);
            return true;
        }

        /// <summary>
        /// Close the current mailbox.
        /// </summary>
        public bool MailboxClose()
        {   string curr = data.Current.Name;
            data.Clear(Info.Headers);
            data.UpdateCurrent(MBoxRef.Nothing);
            if(curr == null) return false;
            application.MailboxClose(false);
            MonitorInfo("Mailbox closed: {0}", curr);
            return true;
        }

        public bool MailboxCreate(string boxname)
        {   using(ZIMapCommand.Create cmd = new ZIMapCommand.Create(factory))
            {   if(cmd == null) return false;
                cmd.Queue(boxname);
                if(!cmd.CheckSuccess(string.Format(
                    ":Failed to create mailbox: {0}: {1}",
                    boxname, cmd.Result.Message))) return false;
            }
            MonitorInfo("Mailbox created: {0}", boxname);
            uint nsid = server.FindNamespace(boxname);
            data.FolderAppend(boxname, server[nsid].Delimiter);
            server.MailboxSort(data.Folders.Array);
            data.Clear(CacheData.Info.Quota | CacheData.Info.Rights);
            return true;
        }

        /// <summary>
        /// An admin can SetACL to get the required rights for an object 
        /// </summary>
        public bool MailboxAdminRigths(string mbox, string curr, string want, bool wait)
        {   if(!server.IsAdmin) return true;            // not an administrator
            want = server.RightsCheck(want, curr);
            if(want == null) return true;
            MonitorInfo("Change rigths from '{0}' to '{1}': {2}", curr, want, mbox);
            ZIMapCommand.SetACL cmd = new ZIMap.ZIMapCommand.SetACL(factory);
            if(cmd == null) return false;
            cmd.Queue(mbox, application.User, want);
            if(!wait && cmd.AutoDispose) return true;   // no wait, autodisposing 
            bool bok = cmd.CheckSuccess(); cmd.Dispose();
            return bok;
        }
        
        /// <summary>
        /// Delete a mailbox from the server and update the cached information.
        /// </summary>
        /// <param name="mbox">
        /// A <see cref="MBoxRef"/>
        /// </param>
        /// <returns>
        /// A <see cref="System.Boolean"/>
        /// </returns>
        public bool MailboxDelete(MBoxRef mbox)
        {   if(!mbox.IsValid) return false;
            using(ZIMapCommand.Delete cmd = new ZIMapCommand.Delete(factory))
            {   if(cmd == null) return false;
                if(mbox.Name == data.Current.Name) MailboxClose();

                // TODO: MailboxDelete must update QuotaRoot                
                MailboxAdminRigths(mbox.Name, mbox.ExtraRights, "all", false);
                cmd.Queue(mbox.Name);
                if(!cmd.CheckSuccess(string.Format(
                       ":Failed to delete '{0}': {1}",
                       mbox.Name, cmd.Result.Message))) return false;
                MonitorInfo("Mailbox deleted: {0}", mbox.Name);
                data.FolderDelete(mbox);
            }
            return true;
        }
        
        // =============================================================================
        // Search a mailbox an the cached mailbox array     
        // =============================================================================

        public uint MailboxFind(ref string boxname, bool mustExist)
        {   uint index = mustExist ? uint.MaxValue : 0;
            if(string.IsNullOrEmpty(boxname) || boxname == ".")
            {   if(data.Current.IsNothing)
                {   MonitorError("No current Mailbox");
                    return index;
                }
                data.Current.Reset();
                boxname = data.Current.Name;
            }

            if(!data.Load(Info.Folders)) return index;   // get mboxes or die
            
            // check if numeric else strip any leading dot ...
            uint uidx;
            if(uint.TryParse(boxname, out uidx))
            {   if(ListedFolders.IsNothing)
                {   MonitorError("Please run 'list' before using numeric folder references");
                    return index;
                }
                if(uidx == 0 || !ListedFolders.Next(uidx-1)) 
                {   MonitorError("Not a valid folder reference: {0}", uidx);
                    return index;
                }
                boxname = ListedFolders.Name;
            }
            if(boxname[0] == '.') boxname = boxname.Substring(1);
            string frag = boxname;

            int idx = ZIMapApplication.MailboxFind(data.Folders.Array, boxname);
            if(idx == -2 && mustExist)
            {   MonitorError("Folder name is not unique: " + boxname);
                return index;
            }
            if(idx < 0 && mustExist)
            {   string name = boxname;
                // undo server.FriendlyName ...
                if(application.EnableNamespaces) boxname = server.FormalName(boxname);
                if(boxname == name)
                {   MonitorError("Folder not found: " + boxname);
                    return index;
                }
                return MailboxFind(ref boxname, mustExist);
            }
            if(idx >= 0)
            {   boxname = data.Folders.Array[idx].Name;
                index   = (uint)idx;
                if(mustExist) return index;
                string tail = data.Folders.Array[idx].Delimiter + frag;
                if(boxname != frag && !boxname.EndsWith(tail))
                {   boxname = frag;
                    return uint.MaxValue;
                }
                MonitorError("Folder already exists: " + boxname);
            }
            return index;
        }
        
        // =============================================================================
        // Check if Prefix is a Mailbox name     
        // =============================================================================

        public string CheckQualifier()
        {   string qualifier = data.Qualifier;
            if(string.IsNullOrEmpty(qualifier)) return "";
            uint nidx = server.FindNamespace(qualifier, false);
            if(nidx != ZIMapServer.Nothing)              // is a ns qualifier
                return server[nidx].Prefix;
            
            if(application.EnableNamespaces) qualifier = server.FormalName(qualifier);
            uint uqua = data.Folders.Search(qualifier);
            if(uqua == uint.MaxValue) 
                return ZIMapAdmin.Confirm("Qualifier is not a mailbox and will be ignored. Continue")
                       ? "" : null;
            return qualifier + server[nidx].Delimiter;
        }
        
        // =============================================================================
        // Check Mailbox arguments     
        // =============================================================================
       
        public bool CheckMailboxArgs(ref string[] args, bool mustExist)
        {   if(args == null || args.Length < 1)
            {   MonitorError("No mailbox(es) specified");
                return false;
            }
            
            if(!data.Load(CacheData.Info.Folders))          // want mbox list
                return false;
/*            
            // a single "*" expands to all mailboxes ...
            if(args.Length == 1 && args[0] == "*" && mustExist)
            {   args = ZIMapConverter.StringArray(mailboxes.Length);
                if(args == null) return false;
                for(int irun=0; irun < args.Length; irun++)
                    args[irun] = mailboxes[irun].Name;
                return true;
            }
*/            
            // loop over argument array to normalize names
            bool bok = true;
            for(int irun=0; irun < args.Length; irun++)
            {   if(args[irun] == "*")
                {   MonitorError("Incorrect use of '*'");
                    return false;
                }
                else if(MailboxFind(ref args[irun], mustExist) == uint.MaxValue)
                    bok = false;
            }
            
            // check for duplicates
            if(bok) for(int irun=0; irun < args.Length; irun++)
            {   for(int icmp=irun+1; icmp < args.Length; icmp++)
                    if(args[irun] == args[icmp])
                    {   MonitorError("Duplicated mailbox name: " + args[irun]);
                        bok = false;
                        break;
                    }
            }
            return bok;
        }

        // =============================================================================
        // Check item number arguments     
        // =============================================================================

        public uint CheckItemArgs(string arg, bool useID, bool useUID)
        {   uint[] uids = CheckItemArgs(new string[] { arg }, 0, useID, useUID, false);
            if(uids == null || uids.Length < 1) return uint.MaxValue;
            if(uids.Length > 1)
            {   MonitorError("Only one item allowed");
                return uint.MaxValue;
            }
            return uids[0];
        }

        public uint[] CheckItemArgs(string[] args, uint argOffset, bool useID, bool useUID, bool defaultAll)
        {   uint argLeng = (args == null) ? 0 : (uint)args.Length;
            if(argOffset >= argLeng && !defaultAll)
            {   MonitorError("No mail items selected");
                return null;
            }
            
            ZIMapApplication.MailInfo[] headers = data.Headers;
            if(headers == null)
            {   if(!HeadersLoad()) return null;
                headers = data.Headers;
            }
            uint argCount = (argLeng > argOffset) ? argLeng - argOffset : 0;

            // special arg '*' ...
            uint ucnt = 0;
            uint[] uids = null;
            if(argCount == 0 || (args[argOffset] == "*" && argCount == 1))
            {   ucnt = (uint)headers.Length;
                uids = new uint[ucnt];
                for(uint irun=0; irun < ucnt; irun++)
                    uids[irun] = headers[irun].UID;
                useID = useUID = false;
            }
            
            // -id and -uid do no checks ...
            else if(useID | useUID)
            {   uids = new uint[argCount];
                for(int irun=0; irun < uids.Length; irun++)
                {   uint uarg;
                    if(!uint.TryParse(args[irun+argOffset], out uarg))
                    {   MonitorError("Not a number: " + args[irun+argOffset]);
                        return null;
                    }
                    uids[irun] = uarg;
                }
            }

            // check item numbers, get uids ...
            else
            {   List<uint> ulis = new List<uint>();
                for(int iarg=(int)argOffset; iarg < args.Length; iarg++)
                {   uint uarg;
                    string arg = args[iarg];
                    if(arg == "*")
                    {   MonitorError("Incorrect use of '*'");
                        return null;
                    }
                    int idot = arg.IndexOf(":");
                    if(idot > 0)
                    {   string[] range = arg.Split(":".ToCharArray(), 2);
                        uint uend;
                        if(range.Length != 2 ||
                           !uint.TryParse(range[0], out uarg) ||
                           !uint.TryParse(range[1], out uend))
                               MonitorError("Invalid range: " + arg);
                        else
                        {   if(uend > headers.Length || uarg > uend)
                                MonitorError("Number out of range: " + uend);
                            else
                                while(uarg <= uend) ulis.Add(headers[uarg++ - 1].UID);
                        }
                        continue;
                    }
                    if(!uint.TryParse(arg, out uarg))
                    {   MonitorError("Not a number: " + arg);
                        return null;
                    }
                    if(uarg == 0 || uarg > headers.Length)
                    {   MonitorError("Number out of range: " + uarg);
                        return null;
                    }
                    ulis.Add(headers[uarg-1].UID);
                    
                }
                uids = ulis.ToArray();
            }
            return uids;
        }

        // =============================================================================
        // Commands: Flags, Copy, Expunge
        // =============================================================================

        public bool CommandACL(MBoxRef mailbox, bool bRecurse, string user, string rights)
        {   if(!mailbox.IsValid) return false;
            progress.Update(0);

            using(ZIMapCommand acl = (ZIMapCommand)application.Factory.CreateByName(
                                       (rights == null) ? "DeleteACL" : "SetACL"))
            {   CacheData.MBoxRef ubox = mailbox;
                if(string.IsNullOrEmpty(user)) user = application.User;
                uint ucnt = 0;
                if(bRecurse)                            // get count for progress report
                {   ubox = CacheData.MBoxRef.Nothing;
                    while(mailbox.Recurse(ref ubox, application.Server)) ucnt++;
                }

                uint urun = 0;
                while(bRecurse ? mailbox.Recurse(ref ubox, application.Server) : (urun == 0))
                {   string name = ubox.Name;
                    if(rights == null) ((ZIMapCommand.DeleteACL)acl).Queue(name, user);
                    else               ((ZIMapCommand.SetACL)acl).Queue(name, user, rights);
                    ubox.ExtraRights = null;
                    if(ucnt > 0) progress.Update(urun, ucnt);
                    else         progress.Update(30);
                    if(!acl.CheckSuccess(":Cannot change rights for: " + user)) return false;
                    acl.Reset(); urun++;
                }
            }
            return true;            
        }
        
        public bool CommandFlag(uint[] items, bool useUID, bool bSet, bool bDeleted, 
                                bool bSeen, bool bFlagged, string[] custom)
        {   if(items == null || items.Length < 1 || data.Current.IsNothing)
            {   MonitorError("No matching mail found");
                return false;
            }
            progress.Update(0);
            data.Current.Reset();

            // make sure that our current mailbox is open ...
            if(!data.Current.Open(application, false))
                return false;                       // could not reopen
            data.Clear(Info.Details|Info.Headers);  // message count, flags changed
            progress.Update(20);
            
            using(ZIMapCommand.Store cmd = new ZIMapCommand.Store(factory))
            {   if(cmd == null) return false;
                cmd.UidCommand = useUID;

                cmd.AddSequence(items);
                cmd.AddDirect(bSet ? "+FLAGS.SILENT" : "-FLAGS.SILENT");
                cmd.AddBeginList();
                if(bDeleted) cmd.AddDirect("\\Deleted");
                if(bSeen)    cmd.AddDirect("\\Seen");
                if(bFlagged) cmd.AddDirect("\\Flagged");
                if(custom != null)
                    foreach(string cust in custom) cmd.AddName(cust);
                cmd.Queue();
                progress.Update(30);
                bool bok = cmd.CheckSuccess(":");                
                progress.Done();
                return bok;
            }
        }
            
        public bool CommandCopy(uint[] items, bool useUID, MBoxRef mbox)
        {   if(!mbox.IsValid || items == null || items.Length < 1) return false;
            progress.Update(0);
            
            // make sure that our current mailbox is open ...
            if(!data.Current.Open(application, true))
                return false;                           // could not reopen
            
            string dest = mbox.Name; 
            if(dest == data.Current.Name)               // content changes
                data.Clear(Info.Headers); 
            data.Clear(Info.Details);                   // message count changes

            using(ZIMapCommand.Copy cmd = new ZIMapCommand.Copy(factory))
            {   if(cmd == null) return false;
                cmd.UidCommand = useUID;
                cmd.Queue(items, dest);
                progress.Update(30);
                bool bok = cmd.CheckSuccess(":");                
                progress.Done();
                return bok;
            }
        }
        
        public uint CommandExpunge()
        {   // make sure that our current mailbox is open ...
            progress.Update(0);
            if(!data.Current.Open(application, false))
                return uint.MaxValue;                   // could not reopen

            using(ZIMapCommand.Expunge exp = new ZIMapCommand.Expunge(factory))
            {   if(exp == null) return uint.MaxValue;
            
                exp.Queue();
                progress.Update(30);
                if(!exp.CheckSuccess(":")) return uint.MaxValue;
                uint[] expu = exp.Expunged;
                if(expu == null) return 0;
                uint cnt = (uint)expu.Length;
                if(cnt > 0)                             // message count changes
                    data.Clear(Info.Headers|Info.Details);
                progress.Done();
                return cnt;
            }
        }

        // =============================================================================
        // Mailbox Export     
        // =============================================================================

        /// <summary>
        /// Export mail from a single mailbox.
        /// </summary>
        /// <param name="mbox">
        /// A reference to the mailbox.
        /// </param>
        /// <param name="quoted">
        /// If <c>true</c> export using "From" quoting instead of "Content-Length" headers.
        /// </param>
        /// <param name="uids">
        /// A list of uids to be exported or <c>null</c>
        /// </param>
        /// <param name="mailTotal">
        /// Can specify a total number of mails when multiple mailboxes are going to be
        /// exported.
        /// </param>
        /// <param name="mailCounter">
        /// Incremented for each exported mail.
        /// </param>
        /// <returns>
        /// <c>true</c> on success.
        /// </returns>
        public bool MailboxExport(MBoxRef mbox, bool quoted, 
                                  uint[] uids, uint mailTotal, ref uint mailCounter)
        {   if(!mbox.IsValid) return false;
            
            uint ucnt = (uids == null) ? mbox.Messages : (uint)uids.Length;
            if(ucnt > mailTotal) mailTotal = ucnt;
            string mailbox = mbox.Name;
            
            // get rid of INBOX and prefix personal mailboxes with the account name
            uint rnsi;
            string fullname = server.FriendlyName(mailbox, out rnsi);
            string prefix = server[rnsi].Prefix;
            if(prefix != "" && fullname.StartsWith(prefix))     // strip the ns prefix 
                fullname = fullname.Substring(prefix.Length);

            // create the output file
            if (!application.ExportMailbox(fullname, rnsi)) return false;
            if(ucnt == 0) {
                MonitorInfo("Mailbox exported: {0} [no mails]", fullname.PadRight(32));
                return true;
            }
            
            // open the IMAP mailbox
            if(data.Current.Name != mailbox)
            {   ZIMapCommand.Examine exa = new ZIMapCommand.Examine(factory);
                exa.Queue(mailbox);
                if(!exa.CheckSuccess(":Cannot open Mailbox: " + mailbox))
                    return false;
            }
            
            // loop over mail items
            ZIMapCommand.Fetch.Item item = new ZIMapCommand.Fetch.Item();
            byte[] head = null;
            byte[] body = null;
            ZIMapCommand.Generic current = null;
            ZIMapFactory.Bulk bulk = factory.CreateBulk("FETCH", 4, uids != null);
            ZIMapMessage msg = new ZIMapMessage();
            uint ucur = 0;                      // current item/UID
            uint uerr = 0;                      // error counter
            uint urun = 0;                      // mail counter
            uint urdy = 0;
            string key = (uids == null) ? "item" : "UID";
            while(urdy < ucnt)
            {   // step 1: queue request and check for response ...
                bool done = bulk.NextCommand(ref current);
                ZIMapCommand.Fetch cmd = (ZIMapCommand.Fetch)current;

                // step 2: check server reply for error ...
                if(done) 
                {   ucur = (uids == null) ? ++urdy : uids[urdy++];
                    if (cmd.Items == null || cmd.Items.Length != 1) 
                    {   MonitorError(!cmd.CheckSuccess() ?
                            "Cannot fetch mail ({0}={1}): {2}" :
                            "Mail not existing ({0}={1})", key, ucur, cmd.Result.Message);
                        done = false; uerr++; 
                    }
                }

                // step 3: process data sent by server ...
                if(done)
                {   item = cmd.Items[0];
                    head = item.Literal(0);
                    body = item.Literal(1);
                    if (head == null)
                    {   MonitorError("Mail data incomplete ({0}={1})", key, ucur);
                        done = false; uerr++; 
                    }
                    if(body == null) body = new byte[0];        // message without body
                }
                if(done)
                {   msg.Parse(head, true);

                    // use the server's INTERNALDATE if we got it...
                    DateTime date;
                    string sdat;
                    if (ZIMapFactory.FindInParts(item.Parts, "INTERNALDATE", out sdat))
                        date = ZIMapConverter.DecodeIMapTime(sdat, true);
                    else
                        date = msg.DateBinary;

                    // finally write mail data to file ...
                    string flags = (item.Flags == null) ? null : string.Join(" ", item.Flags);
                    if (!application.Export.WriteMail(msg.From, date, flags, head, body, quoted))
                    {   MonitorError("Mail could not be written ({0}={1})", key, ucur);
                        uerr++;
                    }
                    progress.Update(mailCounter++, mailTotal);
                }

                // step 4: create a new request
                if(urun < ucnt)
                {   ucur = (uids == null) ? ++urun : uids[urun++];
                    cmd.Reset();
                    cmd.Queue(ucur, "FLAGS INTERNALDATE BODY.PEEK[HEADER] BODY.PEEK[TEXT]");
                }
            }
            bulk.Dispose();
            MonitorInfo("Mailbox exported: {0} [{1,4} mails]", fullname.PadRight(32), urun);
            return (uerr == 0);
        }
        
        public bool MailboxImport(string mailbox, bool bFolders,
                                  bool bNoFlags, bool bClean, long lcur, long llen, long lsiz)
        {
            ZIMapExport expo = application.Export;

            // strip root name to see if a folder must be created
            string root = data.Current.Name; 
            if(bFolders)
            {   // TODO: Mail import ns: MailFile.MailboxNameDecode()
                char delimiter = application.MailboxNamespace.Delimiter;
                string[] parts = mailbox.Split(new char[] { delimiter });
                parts[0] = root;
                uint upar = (uint)parts.Length;
                for(uint urun=1; urun < upar; urun++)
                {   root = string.Join(delimiter.ToString(), parts, 0, (int)urun+1);
                    if(ZIMapApplication.MailboxFind(data.Folders.Array, root) < 0 && !MailboxCreate(root))
                        return false;
                }
            }
            
            if(!application.ExportMailbox(mailbox, application.MailboxNamespace.Index)) return false;

            uint position = 0;
            string flags;
            byte[] mail;
            DateTime date;
            ZIMapCommand.Append app0 = new ZIMapCommand.Append(factory);

            for(;;) 
            {   progress.Update((double)(lcur + position) / lsiz);
                if(!expo.ReadMail(out mail, out date, out flags, out position, bClean))
                    break;
                app0.Queue(root, flags, date, mail);
                if(!app0.CheckSuccess(":")) return false;
                app0.Reset();
            }

            expo.IOStream = null;
            return true;
        }
    }
}
