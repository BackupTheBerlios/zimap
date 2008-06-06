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
    public class CacheData
    {   /// <value>Enables or disables caching</value>
        public bool                         Enabled = true;
        /// <value>The result of the last <see cref="GetMailboxes"/></value>
        public ZIMapApplication.MailBox[]   Mailboxes;
        /// <value>The result of the last <see cref="GetHeaders"/></value>
        public ZIMapApplication.MailInfo[]  Headers;
        /// <value>The result of the last <see cref="GetUsers"/></value>
        public ZIMapApplication.MailBox[]   Users;

        // Full name of the current mailbox
        private string                      currentMailbox;
        // Filter for mailbox list
        private string                      mailboxFilter;
        
        private bool                        mailboxSubscrib;
        private bool                        mailboxDetailed;
        private bool                        currentReadonly;

        // Flags cache users as 'other users'
        private bool                        usersOther;
        
        // selected list prefix (from user command)
        private string                      qualifier;

        private ZIMapApplication            app;
        
        public CacheData(ZIMapApplication application)
        {   app = application;
        }
        
        /// <summary>
        /// Output a top level error message via ZIMapApplication.
        /// </summary>
        /// <param name="message">
        /// Message to be displayed as an application error.
        /// </param>
        /// <remarks>
        /// While <see cref="ZIMapApplication.MonitorError"/> prefixes the error message
        /// with it's class name, the application can use an additional colon to filter
        /// top level error messages. Example: "ZIMapApplication::Top level error".
        /// </remarks>
        private void Error(string message)
        {   app.MonitorError(":" + message);
        }

        // =============================================================================
        // Properties     
        // =============================================================================

        public string CurrentMailbox
        {   get {   return currentMailbox;  }
        }

        public bool CurrentReadonly
        {   get {   return currentReadonly;  }
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
        {   get {   return (app.Progress != 100);  }
            set {   if(value)                       // start progress bar ...   
                    {    if(app.Progress == 100) app.MonitorProgress(0);
                    }
                    else app.MonitorProgress(100);
                }
        }

        public string Qualifier
        {   get {   return qualifier;  }
            set {   string qual = value;
                    if(qual == "INBOX")
                        qual = app.Server.NamespaceDataUser.Qualifier;
                    if(qual == qualifier) return;
                    qualifier = qual;
                    Mailboxes = null;
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
        }

        // =============================================================================
        // IMap data access     
        // =============================================================================

        public ZIMapApplication.MailBox GetCurrentMailbox(bool detailed)
        {   if(!string.IsNullOrEmpty(CurrentMailbox) && 
               GetMailboxes(detailed, false) != null)
                foreach(ZIMapApplication.MailBox mbox in Mailboxes)
                    if(mbox.Name == CurrentMailbox) return mbox;
            Error("No current mailbox");
            currentMailbox = null;
            return new ZIMapApplication.MailBox();
        }

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
            Mailboxes = app.Mailboxes(qualifier, mailboxFilter, smode, detailed);
            mailboxSubscrib = subscribed;
            mailboxDetailed = detailed;
            
            // A blank (e.g. "") list prefix filters for the current user!
            uint ufil = app.Server.FindNamespaceIndex(qualifier, false);
            if(ufil != uint.MaxValue)
                app.Server.MailboxesFilter(ref Mailboxes, ufil);
            
            // Allways sort (should by fast) ...
            app.Server.MailboxSort(Mailboxes);
            return Mailboxes;
        }

        public ZIMapApplication.MailInfo[] GetHeaders()
        {
            if(string.IsNullOrEmpty(currentMailbox))        // must have current
            {   Error("No current mailbox");
                return null;
            }
            if(Headers != null && Enabled)                  // return from cache
                return Headers;

            // make sure that out current mailbox is open ...
            if(!app.MailboxOpen(currentMailbox, currentReadonly))
                return null;                                // could not reopen
            
            Headers = app.MailHeaders();
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
            ZIMapServer.Namespace ns = others ? app.Server.NamespaceDataOther :
                                                app.Server.NamespaceDataShared;
            if(!ns.Valid) return new ZIMapApplication.MailBox[0];

            Users = app.Mailboxes(ns.Prefix, "%", 0, false);
            if(Users == null) return null;
            
            // List of "other users", add current user
            if(others && ns.Valid)
            {   ZIMapApplication.MailBox[] oldu = Users;
                Users = new ZIMapApplication.MailBox[oldu.Length + 1];
                Users[0].Name = "INBOX"; //app.Server.MyMailboxName();
                Array.Copy(oldu, 0, Users, 1, oldu.Length);
            }

            // List of "shared folders", remove "user" folder and the current user
            if(!others) app.Server.MailboxesFilter(ref Users, ZIMapServer.Shared);
            app.Server.MailboxSort(Users);
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
                {   ZIMapCommand.SetACL acl = new ZIMapCommand.SetACL(app.Factory);
                    acl.Queue(user, ZIMapAdmin.Account, "lca");
                    app.MonitorProgress(60);
                }
                ZIMapCommand.MailboxBase cmd = 
                    (ZIMapCommand.MailboxBase)(app.Factory.CreateByName(command)); 
                cmd.Queue(user);
                if(cmd.Data.Succeeded)
                {   info1 = string.Format("Mailbox {0}d: {1}", command, user);
                    Users = null;
                    if(bQuota) app.MonitorProgress(50);
                    
                }
                else
                {   Error(string.Format("Could not {0} mailbox: {1}", command, user));
                    bok = false;
                }
                cmd.Dispose();
            }
            
            if(bQuota)
            {   if(app.QuotaLimit(user, storageLimit, messageLimit))
                {   info2 = string.Format("Quota limits for '{0}' updated", user);
                    Mailboxes = null;
                }
            }
            
            CommandRunning = false;
            if(info1 != null) ZIMapAdmin.Info(info1);
            if(info2 != null) ZIMapAdmin.Info(info2);
            return bok;
        }

        // =============================================================================
        // Operations on mailboxes     
        // =============================================================================
        
        /// <summary>
        /// Open a current mailbox.
        /// </summary>
        public bool OpenMailbox(string fullBoxname, bool readOnly)
        {   Headers = null;
            currentMailbox = null;
            if(!app.MailboxOpen(fullBoxname, readOnly)) return false;
            currentMailbox = fullBoxname;
            currentReadonly = readOnly;
            return true;
        }

        /// <summary>
        /// Close the current mailbox.
        /// </summary>
        public bool CloseMailbox()
        {   bool bok = !string.IsNullOrEmpty(currentMailbox);
            Headers = null;
            currentMailbox = null;
            currentReadonly = false;
            app.MailboxClose(false);
            return bok;
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
            if(boxname == "." && !ResolveDotArg(ref boxname))
                return false;

            ZIMapApplication.MailBox[] mailboxes = GetMailboxes(false);
            if(mailboxes == null) return false;
            
            string frag = boxname;
            int idx = ZIMapApplication.MailboxFind(mailboxes, boxname);
            if(idx == -2 && mustExist)
            {   Error("Name is not unique: " + boxname);
                return false;
            }
            if(idx < 0 && mustExist)
            {   Error("Mailbox not found: " + boxname);
                return false;
            }
            if(idx >= 0)
            {   boxname = mailboxes[idx].Name;
                index   = (uint)idx;
                if(mustExist) return true;
                if(boxname != frag)
                {   boxname = frag; return true;
                }
                Error("Mailbox already exists: " + boxname);
                return false;
            }
            return true;
        }
        
        // =============================================================================
        // Check Mailbox arguments     
        // =============================================================================
       
        public bool ResolveDotArg(ref string mailbox)
        {   if(mailbox != ".") return true;
            if(string.IsNullOrEmpty(currentMailbox))
            {   Error("Cannot resolve '.' without current Mailbox");
                return false;
            }
            mailbox = currentMailbox;
            return true;
        }
        
        public bool CheckMailboxArgs(ref string[] args, bool mustExist)
        {   if(args == null || args.Length < 1)
            {   Error("No mailbox(es) specified");
                return false;
            }
            ZIMapApplication.MailBox[] mailboxes = GetMailboxes(false);
            if(mailboxes == null) return false;
            
            // a single "*" expands to all mailboxes ...
            if(args.Length == 1 && args[0] == "*" && mustExist)
            {   args = ZIMapConverter.StringArray(mailboxes.Length);
                if(args == null) return false;
                for(int irun=0; irun < args.Length; irun++)
                    args[irun] = mailboxes[irun].Name;
                return true;
            }
            
            // loop over argument array to normalize names
            bool bok = true;
            for(int irun=0; irun < args.Length; irun++)
            {   if(args[irun] == "*")
                {   Error("Incorrect use of '*'");
                    return false;
                }
                if(!ResolveDotArg(ref args[irun]))
                    bok = false;
                else if(!FindMailbox(ref args[irun], mustExist))
                    bok = false;
            }
            
            // check for duplicates
            if(bok) for(int irun=0; irun < args.Length; irun++)
            {   for(int icmp=irun+1; icmp < args.Length; icmp++)
                    if(args[irun] == args[icmp])
                    {   Error("Duplicated mailbox name: " + args[irun]);
                        bok = false;
                        break;
                    }
            }
            return bok;
        }

        // =============================================================================
        // Check item number arguments     
        // =============================================================================

        public uint FindMail(string arg, bool useID, bool useUID)
        {   if(GetHeaders() == null) return uint.MaxValue;
            uint uarg;
            if(!uint.TryParse(arg, out uarg))
            {   Error("Not a number: " + arg);
                return uint.MaxValue;
            }
            if(!useID && !useUID)
            {   if(uarg == 0) return uint.MaxValue;
                uarg--;
                return (uarg < Headers.Length) ? uarg : uint.MaxValue;
            }
            for(int irun=0; irun < Headers.Length; irun++)
            {   if(useUID)
                    if(uarg == Headers[irun].UID) return (uint)irun;
                    else continue;
                if(uarg == Headers[irun].Index) return (uint)irun;
            }
            return uint.MaxValue;
        }

        public uint[] CheckItemArgs(string[] args, uint argOffset, bool useID, bool useUID)
        {   if(args == null || argOffset >= args.Length) return null;   
            if(GetHeaders() == null) return null;
            uint argCount = (uint)args.Length - argOffset;
            
            // special arg '*' ...
            uint ucnt = 0;
            uint[] uids = null;
            if(args[argOffset] == "*" && argCount == 1)
            {   ucnt = (uint)Headers.Length;
                uids = new uint[ucnt];
                for(int irun=0; irun < Headers.Length; irun++)
                    uids[irun] = Headers[irun].UID;
                useID = useUID = false;
            }
            
            // -id and -uid do no checks ...
            else if(useID | useUID)
            {   uids = new uint[argCount];
                for(int irun=0; irun < uids.Length; irun++)
                {   uint uarg;
                    if(!uint.TryParse(args[irun+argOffset], out uarg))
                    {   Error("Not a number: " + args[irun+argOffset]);
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
                    {   Error("Incorrect use of '*'");
                        return null;
                    }
                    int idot = arg.IndexOf(":");
                    if(idot > 0)
                    {   string[] range = arg.Split(":".ToCharArray(), 2);
                        uint uend;
                        if(range.Length != 2 ||
                           !uint.TryParse(range[0], out uarg) ||
                           !uint.TryParse(range[1], out uend))
                               Error("Invalid range: " + arg);
                        else
                        {   if(uend > Headers.Length || uarg > uend)
                                Error("Number out of range: " + uend);
                            else
                                while(uarg <= uend) ulis.Add(Headers[uarg++ - 1].UID);
                        }
                        continue;
                    }
                    if(!uint.TryParse(arg, out uarg))
                    {   Error("Not a number: " + arg);
                        return null;
                    }
                    if(uarg == 0 || uarg > Headers.Length)
                    {   Error("Number out of range: " + uarg);
                        return null;
                    }
                    ulis.Add(Headers[uarg-1].UID);
                    
                }
                if(ulis.Count > 0) uids = ulis.ToArray();
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
            boxes[boxes.Length-1].Delimiter = app.Factory.HierarchyDelimiter;
            boxes[boxes.Length-1].Attributes = ZIMapConverter.StringArray(0);
 
            // Allways sort (should by fast) ...
            app.Server.MailboxSort(boxes);
            return true;
        }
        
        public bool DeleteMailbox(string fullBoxname)
        {   return DeleteMailbox(ref Mailboxes, fullBoxname);
        }

        public bool DeleteMailbox(ref ZIMapApplication.MailBox[] boxes, string fullBoxname)
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
        {   if(items == null || items.Length < 1 || currentMailbox == null)
            {   Error("No matching mail found");
                return false;
            }

            // make sure that our current mailbox is open ...
            CommandRunning = true;
            if(!app.MailboxOpen(currentMailbox, false))
                return false;                       // could not reopen
            currentReadonly = false;
            mailboxDetailed = false;                // message count changes
            Headers = null;                         // flags change
            
            using(ZIMapCommand.Store cmd = new ZIMapCommand.Store(app.Factory))
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
                
                app.MonitorProgress(10);
                return cmd.Data.Succeeded;
            }
        }
            
        public bool CopyMails(uint[] items, bool useUID, string destination)
        {   if(items == null || items.Length < 1)
            {   Error("No matching mail found");
                return false;
            }

            ZIMapAdmin.Info(string.Format(":Copying {0} mail{1} to mailbox '{2}'",
                 items.Length, (items.Length == 1) ? "" : "s", destination));
            
            // make sure that our current mailbox is open ...
            CommandRunning = true;
            if(!app.MailboxOpen(currentMailbox, currentReadonly))
                return false;                       // could not reopen

            if(!FindMailbox(ref destination, true)) return false;
            app.MonitorProgress(10);
            
            if(destination == currentMailbox)       // content changes
                Headers = null;
            mailboxDetailed = false;                // message count changes
            
            using(ZIMapCommand.Copy cmd = new ZIMapCommand.Copy(app.Factory))
            {   if(cmd == null) return false;
                
                cmd.Queue(items, destination);
                cmd.UidCommand = useUID;
                app.MonitorProgress(10);
                return cmd.Data.Succeeded;
            }
        }
        
        public int ExpungeMails()
        {   // make sure that our current mailbox is open ...
            CommandRunning = true;
            if(!app.MailboxOpen(currentMailbox, false))
                return -1;                          // could not reopen
            currentReadonly = false;

            CommandRunning = true;
            using(ZIMapCommand.Expunge exp = new ZIMapCommand.Expunge(app.Factory))
            {   if(exp == null) return -1;
            
                exp.Queue();
                app.MonitorProgress(30);
                if(!exp.Data.Succeeded) return -1;
                uint[] expu = exp.Expunged;
                if(expu == null) return 0;
                int cnt = expu.Length;
                if(cnt > 0)
                {   Headers = null;
                    mailboxDetailed = false;        // message count changes
                }
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
                {   if(cmd == null) cmd = new ZIMapCommand.MyRights(app.Factory);
                    else            cmd.Reset();
                    cmd.Queue(boxes[irun].Name);
                    cmd.AutoDispose = false;
                    cmd.Execute(false);
                }

                if(wantQuota && data.QuotaRoot == null)
                {   string[] roots = app.QuotaInfos(boxes[irun].Name,
                                        out data.StorageUsage, out data.StorageLimit,
                                        out data.MessageUsage, out data.MessageLimit);   
                    if(roots == null || roots.Length < 1)
                        data.QuotaRoot = "";
                    else
                        data.QuotaRoot = roots[0];
                }

                if(wantRights && data.Rights == null && cmd.Rights != null)
                    data.Rights = cmd.Rights;
                
                app.MonitorProgress((uint)((irun * 100.0) / boxes.Length));
            }
            if(cmd != null) cmd.Dispose();
            return true;
        }
    }
}
