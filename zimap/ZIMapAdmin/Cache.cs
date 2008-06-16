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
using ZTool;

namespace ZIMap
{
    public class CacheData : ZIMapBase
    {   /// <value>Enables or disables caching</value>
        public bool                         Enabled = true;
        /// <value>The result of the last <see cref="GetMailboxes"/></value>
        public ZIMapApplication.MailBox[]   Mailboxes;
        /// <value>The result of the last <see cref="GetHeaders"/></value>
        public ZIMapApplication.MailInfo[]  Headers;
        /// <value>The result of the last <see cref="GetUsers"/></value>
        public ZIMapApplication.MailBox[]   Users;

        public MBoxRef                      ListedMailboxes;

        // Full name of the current mailbox
        private MBoxRef                     currentMailbox;
        // Filter for mailbox list
        private string                      mailboxFilter;
        
        private bool                        mailboxSubscrib;
        private bool                        mailboxDetailed;

        // Flags cache users as 'other users'
        private bool                        usersOther;
        
        // selected list prefix (from user command)
        private string                      qualifier;

        private ZIMapApplication            application;
        private ZIMapServer                 server;    
        private ZIMapFactory                factory;    
        private ZIMapConnection             connection;    
        private ZIMapConnection.Progress    progress;
        
        // set by open mailbox
        private ZIMapServer.Namespace       currentNamespace;
        
        // =============================================================================
        // MBoxRef     
        // =============================================================================

        public struct MBoxRef
        {
            private uint                        index;
            private bool                        ronly;
            private ZIMapApplication.MailBox[]  boxes;
         
            public static   MBoxRef Invalid = new MBoxRef(null, uint.MaxValue); 
            
            public MBoxRef(ZIMapApplication.MailBox[] boxes, uint index)
            {   this.index = index;
                this.boxes = boxes;
                this.ronly = true;
            }
            
            /// <summary>Advance the current index, can be used as iterator.</summary>
            /// <returns>On success <c>true</c> is returned.</returns>
            public bool Next()
            {   if(boxes == null || index+1 >= boxes.Length) return false;
                index++;  return true;
            }

            /// <summary>Sets the current index.</summary>
            /// <returns>On success <c>true</c> is returned.</returns>
            public bool Next(uint position)
            {   if (boxes == null || position >= boxes.Length) return false;
                index = position; return true;
            }
            
            public bool Open(ZIMapApplication application, bool readOnly)
            {   bool bok = application.MailboxOpen(Name, readOnly);
                ronly = application.MailboxIsReadonly;
                return bok;
            }
            
            public uint Index
            {   get {   return index;   }
            }
            public bool ReadOnly
            {   get {   return ronly;   }
            }
            public bool Valid
            {   get {   if(boxes == null) return false;   
                        return index < boxes.Length;
                    }
            }
            public string Name
            {   get {   return Valid ? boxes[index].Name : null;
                    }
            }
            public uint Messages
            {   get {   return Valid ? boxes[index].Messages : uint.MaxValue;
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
            currentNamespace = server.NamespaceDataUser;
            // At least deliver Info level messages ...
            MonitorLevel = application.MonitorLevel;
            if(MonitorLevel > ZIMapConnection.Monitor.Info) 
                MonitorLevel = ZIMapConnection.Monitor.Info;
        }
            
        /// <summary>
        /// Output a top level error message via ZIMapConnection.
        /// </summary>
        /// <param name="message">
        /// Message to be displayed as an application error.
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

        public ZIMapServer.Namespace Namespace
        {   get {   return currentNamespace;    }
        }
        
        public MBoxRef CurrentMailbox
        {   get {   return currentMailbox;  }
        }

        public string MailboxFilter
        {   get {   return mailboxFilter;  }
            set {   string val = value;
                    if(val == "") val = null;
                    if(val == mailboxFilter) return;
                    mailboxFilter = val;
                    Mailboxes = null;
                }
        }

        public bool CommandRunning
        {   get {   return (progress.Percent < 100);  }
            set {   if(value) progress.Reset();
                    else      progress.Done();
                }
        }

        public string Qualifier
        {   get {   return qualifier;  }
            set {   string qual = value;
                    if(qual == "INBOX")
                        qual = server.NamespaceDataUser.Qualifier;
                    if(qual == qualifier) return;
                    qualifier = qual;
                    Mailboxes = null;
                }
        }

        public MBoxRef this[uint index]
        {   get {   return new MBoxRef(Mailboxes, index);
                }
        }
        
        public MBoxRef this[string mailbox]
        {   get {   uint index;
                    FindMailbox(ref mailbox, true, out index);
                    return new MBoxRef(Mailboxes, index);
                }
        }

        // =============================================================================
        // Cache methods     
        // =============================================================================
        
        /// <summary>
        /// Clear cached data.
        /// </summary>
        /// <remarks>
        /// The method does not clear or close the current mailbox.
        /// </remarks>
        public void Clear()
        {   Mailboxes = null;
            Headers = null;
            mailboxFilter = null;
            mailboxSubscrib = false;
            mailboxDetailed = false;
            currentMailbox = MBoxRef.Invalid;
        }

        // =============================================================================
        // IMap data access     
        // =============================================================================

        /*
        public ZIMapApplication.MailBox GetCurrentMailbox(bool detailed)
        {   if(!string.IsNullOrEmpty(CurrentMailbox) && 
               GetMailboxes(detailed, false) != null)
                foreach(ZIMapApplication.MailBox mbox in Mailboxes)
                    if(mbox.Name == CurrentMailbox) return mbox;
            Error("No current mailbox");
            currentMailbox = null;
            return new ZIMapApplication.MailBox();
        }
        */

        public ZIMapApplication.MailBox[] GetMailboxes(bool detailed)
        {   return GetMailboxes(detailed, false);
        }
        
        public ZIMapApplication.MailBox[] GetMailboxes(bool detailed, bool subscribed)
        {
            if(Mailboxes != null)                           // return from cache
            {   if( (!detailed || mailboxDetailed) &&
                    (subscribed || !mailboxSubscrib)) 
                       return Mailboxes;
            }
                                                            // fetch from server ...
            uint smode = 0;
            if(detailed) smode = 1;
            if(subscribed) smode = 2;
            Mailboxes = application.Mailboxes(qualifier, mailboxFilter, smode, detailed);
            mailboxSubscrib = subscribed;
            mailboxDetailed = detailed;
            
            // A blank (e.g. "") list prefix filters for the current user!
            uint ufil = server.FindNamespaceIndex(qualifier, false);
            if(ufil != uint.MaxValue)
                server.MailboxesFilter(ref Mailboxes, ufil);
            
            // Allways sort (should by fast) ...
            server.MailboxSort(Mailboxes);
            return Mailboxes;
        }

        public ZIMapApplication.MailInfo[] GetHeaders()
        {   if(!currentMailbox.Valid)                       // must have current
            {   MonitorError("No current mailbox");
                return null;
            }
            if(Headers != null && Enabled)                  // return from cache
                return Headers;

            // make sure that our current mailbox is open ...
            if(!currentMailbox.Open(application, true))
                return null;                                // could not reopen
            Headers = application.MailHeaders();
            return Headers;
        }

        // =============================================================================
        // Operations on Users     
        // =============================================================================

        public ZIMapApplication.MailBox[] GetUsers(bool others)
        {
            if(usersOther == others && Users != null)       // get from cache 
                return Users;
            Users = null; usersOther = others;
            
            // reload user data
            ZIMapServer.Namespace ns = others ? server.NamespaceDataOther :
                                                server.NamespaceDataShared;
            if(!ns.Valid) return new ZIMapApplication.MailBox[0];

            Users = application.Mailboxes(ns.Prefix, "%", 0, false);
            if(Users == null) return null;
            
            // List of "other users", add current user
            if(others && ns.Valid)
            {   ZIMapApplication.MailBox[] oldu = Users;
                Users = new ZIMapApplication.MailBox[oldu.Length + 1];
                Users[0].Name = "INBOX";
                Array.Copy(oldu, 0, Users, 1, oldu.Length);
            }

            // List of "shared folders", remove "user" folder and the current user
            if(!others) server.MailboxesFilter(ref Users, ZIMapServer.Shared);
            server.MailboxSort(Users);
            return Users;
        }
        
        public bool UserManage(string user, string command, 
                               uint storageLimit, uint messageLimit)
        {   if(string.IsNullOrEmpty(user)) return false;
            
            bool bQuota  = false;
            bool bCreate = false;
            bool bDelete = false;
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

            CommandRunning = true;
            bool bok = true;
            string info1 = null, info2 = null;

            if(bCreate || bDelete)
            {   if(bDelete)
                {   ZIMapCommand.SetACL acl = new ZIMapCommand.SetACL(factory);
                    acl.Queue(user, factory.User, "lca");
                    progress.Update(60);
                }
                ZIMapCommand.MailboxBase cmd = 
                    (ZIMapCommand.MailboxBase)(factory.CreateByName(command)); 
                cmd.Queue(user);
                if(cmd.CheckSuccess())
                {   info1 = string.Format("Mailbox {0}d: {1}", command, user);
                    Users = null;
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
                    Mailboxes = null;
                }
            }
            
            CommandRunning = false;
            if(info1 != null) MonitorInfo(info1);
            if(info2 != null) MonitorInfo(info2);
            return bok;
        }

        // =============================================================================
        // Operations on mailboxes     
        // =============================================================================

        public bool OpenMailbox(bool readOnly)
        {   return OpenMailbox(this["."], readOnly);
        }
        
        /// <summary>
        /// Open a mailbox and make it current or enable write for the current mailbox. 
        /// </summary>
        /// <param name="mailbox">
        /// A full Mailbox name or "." for the current mailbox
        /// </param>
        /// <param name="readOnly">
        /// If <c>true</c> the mailbox is opened readonly.
        /// </param>
        /// <returns>
        /// On success <c>true</c> is returned.
        /// </returns>        
        /// <remarks>
        /// Use the "." as name to check for a current mailbox or to reopen the current 
        /// mailbox in write mode.
        /// </remarks>
        public bool OpenMailbox(MBoxRef mbox, bool readOnly)
        {   if(!mbox.Valid) return false;
            
            // we want to use the current mailbox ...
            if(mbox.Name == "." || mbox.Index == currentMailbox.Index)
            {   if(!currentMailbox.Valid)
                {   MonitorError("No current Mailbox");
                    return false;
                }
                if(!readOnly && currentMailbox.ReadOnly)
                {   if(!ZIMapAdmin.Confirm("Current mailbox '{0}' is readonly. Change mode", 
                                           mbox.Name)) return false;
                    MonitorInfo("Setting write mode for current mailbox");
                }
            }
            else
                Headers = null;

            // (re-)open the mailbox
            currentMailbox = MBoxRef.Invalid;
            if(!mbox.Open(application, readOnly)) return false;
            currentMailbox = mbox;
            // TODO: move currentNamespace stuff to ZIMapApplication
            currentNamespace = server.NamespaceData(server.FindNamespaceIndex(mbox.Name, true));
            return true;
        }

        /// <summary>
        /// Close the current mailbox.
        /// </summary>
        public bool CloseMailbox()
        {   bool bok = currentMailbox.Valid;
            string curr = currentMailbox.Name;
            Headers = null;
            currentMailbox = MBoxRef.Invalid;
            application.MailboxClose(false);
            if(bok) MonitorInfo("Mailbox closed: {0}", curr);
            return bok;
        }

        public bool CreateMailbox(string boxname)
        {   using(ZIMapCommand.Create cmd = new ZIMapCommand.Create(factory))
            {   if(cmd == null) return false;
                cmd.Queue(boxname);
                if(!cmd.CheckSuccess(string.Format(
                    ":Failed to create mailbox: {0}: {1}",
                    boxname, cmd.Result.Message))) return false;
                MonitorInfo("Mailbox created: {0}", boxname);
                AddMailbox(boxname);
            }
            return true;
        }

        public bool DeleteMailbox(MBoxRef mbox)
        {   if(!mbox.Valid) return false;
            using(ZIMapCommand.Delete cmd = new ZIMapCommand.Delete(factory))
            {   if(cmd == null) return false;
                if(mbox.Index == CurrentMailbox.Index) CloseMailbox();
                cmd.Queue(mbox.Name);
                if(!cmd.CheckSuccess(string.Format(
                       ":Failed to delete '{0}': {1}",
                       mbox.Name, cmd.Result.Message))) return false;
                MonitorInfo("Mailbox deleted: {0}", mbox.Name);
                DeleteMailboxInfo(mbox.Name);
            }
            return true;
        }
        
        // =============================================================================
        // Search a mailbox an the cached mailbox array     
        // =============================================================================

        public bool FindMailbox(ref string boxname, bool mustExist)
        {   uint index;
            return FindMailbox(ref boxname, mustExist, out index);
        }
        
        public bool FindMailbox(ref string boxname, bool mustExist, out uint index)
        {   index = uint.MaxValue;
            if(string.IsNullOrEmpty(boxname) || boxname == ".")
            {   if(!currentMailbox.Valid)
                {   MonitorError("No current Mailbox");
                    return false;
                }
                boxname = currentMailbox.Name;
            }

            ZIMapApplication.MailBox[] mailboxes = GetMailboxes(false);
            if(mailboxes == null) return false;
            
            // check if numeric else strip any leading dot ...
            uint uidx;
            if(uint.TryParse(boxname, out uidx))
            {   if(!ListedMailboxes.Valid)
                {    MonitorError("Please run 'list' before using mailbox numbers");
                    return false;
                }
                if(uidx == 0 || !ListedMailboxes.Next(uidx-1)) 
                {   MonitorError("Not a valid mailbox number: {0}", uidx);
                    return false;
                }
                boxname = ListedMailboxes.Name;
            }
            if(boxname[0] == '.') boxname = boxname.Substring(1);
            string frag = boxname;

            int idx = ZIMapApplication.MailboxFind(mailboxes, boxname);
            if(idx == -2 && mustExist)
            {   MonitorError("Name is not unique: " + boxname);
                return false;
            }
            if(idx < 0 && mustExist)
            {   if(boxname == "~" || application.User.StartsWith(boxname))
                {   boxname = server.NamespaceDataUser.Prefix + "INBOX";
                    MonitorInfo("Assuming '{0}' as root mailbox name", boxname);
                    return FindMailbox(ref boxname, mustExist, out index);
                }
                MonitorError("Mailbox not found: " + boxname);
                return false;
            }
            if(idx >= 0)
            {   boxname = mailboxes[idx].Name;
                index   = (uint)idx;
                if(mustExist) return true;
                if(boxname != frag)
                {   boxname = frag; return true;
                }
                MonitorError("Mailbox already exists: " + boxname);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Iterator to enumerate all descendents of a root mailbox.
        /// </summary>
        /// <param name="rootIndex">
        /// Index of the root in <see cref="Mailboxes"/>.
        /// </param>
        /// <param name="currentIndex">
        /// Set on return, must initially be <see cref="uint.MaxValue"/>.
        /// </param>
        /// <returns>
        /// On success <c>true</c> is returned.
        /// </returns>
        public bool RecurseMailboxes(MBoxRef rootMailbox, ref MBoxRef currentMailbox)
        {   if(!rootMailbox.Valid)
            {   currentMailbox = MBoxRef.Invalid;
                return false;
            }
            if(!currentMailbox.Valid) 
            {   currentMailbox = rootMailbox;
                return true;
            }

            // get the friendly root name and append a hierarchie delimiter
            uint rnsi;
            string root = rootMailbox.Name;
            root = server.FriendlyName(root, out rnsi);
            root += server.NamespaceData(rnsi).Delimiter;
            currentMailbox.Next();

            // now scan the list of mailboxes ...
            while(currentMailbox.Valid)
            {   if(currentMailbox.Index != rootMailbox.Index)
                {   uint cnsi;
                    string name = server.FriendlyName(currentMailbox.Name, out cnsi);
                    if(rnsi == cnsi && name.StartsWith(root)) return true;
                }
                currentMailbox.Next();
            }
            currentMailbox = MBoxRef.Invalid;
            return false;
        }
        
        // =============================================================================
        // Check Mailbox arguments     
        // =============================================================================
       
        public bool CheckMailboxArgs(ref string[] args, bool mustExist)
        {   if(args == null || args.Length < 1)
            {   MonitorError("No mailbox(es) specified");
                return false;
            }
            ZIMapApplication.MailBox[] mailboxes = GetMailboxes(false);
            if(mailboxes == null) return false;
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
                else if(!FindMailbox(ref args[irun], mustExist))
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
            if(GetHeaders() == null) return null;
            uint argCount = (argLeng > argOffset) ? argLeng - argOffset : 0;

            // special arg '*' ...
            uint ucnt = 0;
            uint[] uids = null;
            if(argCount == 0 || (args[argOffset] == "*" && argCount == 1))
            {   ucnt = (uint)Headers.Length;
                uids = new uint[ucnt];
                for(uint irun=0; irun < ucnt; irun++)
                    uids[irun] = Headers[irun].UID;
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
                        {   if(uend > Headers.Length || uarg > uend)
                                MonitorError("Number out of range: " + uend);
                            else
                                while(uarg <= uend) ulis.Add(Headers[uarg++ - 1].UID);
                        }
                        continue;
                    }
                    if(!uint.TryParse(arg, out uarg))
                    {   MonitorError("Not a number: " + arg);
                        return null;
                    }
                    if(uarg == 0 || uarg > Headers.Length)
                    {   MonitorError("Number out of range: " + uarg);
                        return null;
                    }
                    ulis.Add(Headers[uarg-1].UID);
                    
                }
                uids = ulis.ToArray();
            }
            return uids;
        }

        // =============================================================================
        // Add/Delete a Mailbox to/from the cached mailbox array     
        // =============================================================================
        
        public bool AddMailbox(string fullBoxname)
        {   return AddMailbox(ref Mailboxes, fullBoxname);
        }

        public bool AddMailbox(ref ZIMapApplication.MailBox[] boxes, string fullBoxname)
        {   if(boxes == null) return false;

            Array.Resize(ref boxes, boxes.Length + 1);
            boxes[boxes.Length-1].Name = fullBoxname;
            boxes[boxes.Length-1].Delimiter = factory.HierarchyDelimiter;
            boxes[boxes.Length-1].Attributes = ZIMapConverter.StringArray(0);
 
            // Allways sort (should by fast) ...
            server.MailboxSort(boxes);
            return true;
        }
        
        public bool DeleteMailboxInfo(string fullBoxname)
        {   return DeleteMailboxInfo(ref Mailboxes, fullBoxname);
        }

        public bool DeleteMailboxInfo(ref ZIMapApplication.MailBox[] boxes, string fullBoxname)
        {   if(boxes == null) return false;
            
            for(int irun=0; irun < boxes.Length; irun++)
            {   if(fullBoxname == boxes[irun].Name)
                {   ZIMapApplication.MailBox[] dest = 
                        new ZIMapApplication.MailBox[boxes.Length-1];
                    if(irun > 0) Array.Copy(boxes, dest, irun);
                    int tail = boxes.Length - irun - 1;
                    if(tail > 0) Array.Copy(boxes, irun+1, dest, irun, tail);
                    boxes = dest;
                    return true;
                }
            }
            return false;
        }

        // =============================================================================
        //     
        // =============================================================================

        public bool FlagMails(uint[] items, bool useUID, bool bSet, bool bDeleted, 
                              bool bSeen, bool bFlagged, string[] custom)
        {   if(items == null || items.Length < 1 || !currentMailbox.Valid)
            {   MonitorError("No matching mail found");
                return false;
            }

            // make sure that our current mailbox is open ...
            CommandRunning = true;
            if(!currentMailbox.Open(application, false))
                return false;                       // could not reopen
            mailboxDetailed = false;                // message count changes
            Headers = null;                         // flags change
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
                    foreach(string cust in custom)
                        cmd.AddName(cust);
                cmd.Queue();
                
                progress.Done();
                return cmd.CheckSuccess(":");
            }
        }
            
        public bool CopyMails(uint[] items, bool useUID, MBoxRef mbox)
        {   if(!mbox.Valid || items == null || items.Length < 1) return false;
            
            // make sure that our current mailbox is open ...
            CommandRunning = true;
            if(!currentMailbox.Open(application, true))
                return false;                           // could not reopen
            progress.Update(10);
            
            string dest = mbox.Name; 
            if(mbox.Index == currentMailbox.Index)      // content changes
                Headers = null; 
            mailboxDetailed = false;                    // message count changes

            using(ZIMapCommand.Copy cmd = new ZIMapCommand.Copy(factory))
            {   if(cmd == null) return false;
                cmd.Queue(items, dest);
                cmd.UidCommand = useUID;
                return cmd.CheckSuccess(":");
            }
        }
        
        public uint ExpungeMails()
        {   // make sure that our current mailbox is open ...
            CommandRunning = true;
            if(!currentMailbox.Open(application, false))
                return uint.MaxValue;                   // could not reopen

            CommandRunning = true;
            using(ZIMapCommand.Expunge exp = new ZIMapCommand.Expunge(factory))
            {   if(exp == null) return uint.MaxValue;
            
                exp.Queue();
                progress.Update(30);
                if(!exp.CheckSuccess(":")) return uint.MaxValue;
                uint[] expu = exp.Expunged;
                if(expu == null) return 0;
                uint cnt = (uint)expu.Length;
                if(cnt > 0)
                {   Headers = null;
                    mailboxDetailed = false;            // message count changes
                }
                progress.Done();
                return cnt;
            }
        }

        // =============================================================================
        // Mailbox Extra Info (rights and quota     
        // =============================================================================

        public class MailboxExtra
        {   public string   Rights;
            public string   QuotaRoot;
            public uint     StorageLimit, StorageUsage;       
            public uint     MessageLimit, MessageUsage;       
        }

        public bool LoadMailboxExtra(ZIMapApplication.MailBox[] boxes,
                                            bool wantRights, bool wantQuota)
        {   if(boxes == null) return false;
            if(boxes.Length < 1) return true;
            if(!wantRights && !wantQuota) return true;
            CommandRunning = true;
            
            ZIMapCommand.MyRights cmd = null;
            for(int irun=0; irun < boxes.Length; irun++)
            {   MailboxExtra data = (MailboxExtra)(boxes[irun].UserData);
                if( data != null &&                             // have data
                    !((wantRights && data.Rights == null) ||    // rights needed?
                      (wantQuota  && data.QuotaRoot == null))   // quota needed?
                  ) continue;
                
                if(data == null)
                {   data = new MailboxExtra();
                    boxes[irun].UserData = data;
                }

                if(wantRights && data.Rights == null)
                {   if(cmd == null) cmd = new ZIMapCommand.MyRights(factory);
                    else            cmd.Reset();
                    cmd.Queue(boxes[irun].Name);
                    cmd.AutoDispose = false;
                    cmd.Execute(false);
                }

                if(wantQuota && data.QuotaRoot == null)
                {   string[] roots = application.QuotaInfos(boxes[irun].Name,
                                        out data.StorageUsage, out data.StorageLimit,
                                        out data.MessageUsage, out data.MessageLimit);   
                    if(roots == null || roots.Length < 1)
                        data.QuotaRoot = "";
                    else
                        data.QuotaRoot = roots[0];
                }

                if(wantRights && data.Rights == null && cmd.Rights != null)
                    data.Rights = cmd.Rights;
                
                progress.Update((uint)irun, (uint)boxes.Length);
            }
            if(cmd != null) cmd.Dispose();
            return true;
        }

        // =============================================================================
        // Mailbox Export     
        // =============================================================================

        /// <summary>
        /// Export mail from a single mailbox.
        /// </summary>
        /// <param name="mboxIndex">
        /// Index of the mailbox in Mailboxes
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
        public bool ExportMailbox(MBoxRef mbox, bool bQuoted, 
                                  uint[] uids, uint mailTotal, ref uint mailCounter)
        {   if(!mbox.Valid) return false;
            
            uint ucnt = (uids == null) ? mbox.Messages : (uint)uids.Length;
            if(ucnt > mailTotal) mailTotal = ucnt;
            string mailbox = mbox.Name;
            
            // get rid of INBOX and prefix personal mailboxes with the account name
            uint rnsi;
            string fullname = server.FriendlyName(mailbox, out rnsi);
            ZIMapServer.Namespace ns = server.NamespaceData(rnsi);
            string prefix = ns.Prefix;
            if(prefix != "" && fullname.StartsWith(prefix))     // strip the ns prefix 
                fullname = fullname.Substring(prefix.Length);

            // create the output file
            application.Export.Delimiter = ns.Delimiter;
            if (!application.Export.WriteMailbox(fullname)) return false;
            if(ucnt == 0) {
                MonitorInfo("Mailbox exported: {0} [no mails]", fullname.PadRight(32));
                return true;
            }
            
            // open the IMAP mailbox
            if(currentMailbox.Index != mbox.Index)
            {   ZIMapCommand.Examine exa = new ZIMapCommand.Examine(factory);
                exa.Queue(mailbox);
                if(!exa.CheckSuccess(":Cannot open Mailbox: " + mailbox))
                    return false;
            }
            
            // loop over mail items
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
                {   ZIMapCommand.Fetch.Item item = cmd.Items[0];
                    byte[] head = item.Literal(0);
                    byte[] body = item.Literal(1);
                    if (head == null || body == null) {
                        MonitorError("Mail data incomplete ({0}={1})", key, ucur);
                        uerr++; continue;
                    }
                    msg.Parse(head, true);

                    // use the server's INTERNALDATE if we got it...
                    DateTime date;
                    string sdat;
                    if (ZIMapFactory.FindInParts(item.Parts, "INTERNALDATE", out sdat))
                        date = ZIMapConverter.DecodeIMapTime(sdat, true);
                    else
                        date = msg.DateBinary;

                    // finally write mail data to file ...
                    string flags = (item.Flags == null) ? null : string.Join(" ", item.Flags);
                    if (!application.Export.WriteMail(msg.From, date, flags, head, item.Literal(1), bQuoted)) {
                        MonitorError("Mail could not be written ({0}={1})", key, ucur);
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
            MonitorInfo("Mailbox exported: {0} [{1,4} mails]", fullname.PadRight(32), urun - 1);
            return (uerr == 0);
        }
        
        public bool ImportMailbox(string mailbox, bool bFolders,
                                  bool bNoFlags, bool bClean, long lcur, long llen, long lsiz)
        {
            ZIMapExport expo = application.Export;

            // strip root name to see if a folder must be created
            string root = currentMailbox.Name;
            if(bFolders)
            {   string[] parts = mailbox.Split(new char[] { currentNamespace.Delimiter });
                parts[0] = currentMailbox.Name;
                uint upar = (uint)parts.Length;
                for(uint urun=1; urun < upar; urun++)
                {   root = string.Join(currentNamespace.Delimiter.ToString(), parts, 0, (int)urun+1);
                    if(ZIMapApplication.MailboxFind(Mailboxes, root) < 0 && !CreateMailbox(root))
                        return false;
                }
            }
            
            if(!expo.ReadMailbox(mailbox)) return false;

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
