//==============================================================================
// Execute.cs   The ZLibAdmin command executor
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
    public partial class ZIMapAdmin
    {
        private static bool CheckIdAndUidUse(string[] opts, out bool id, out bool uid)
        {   id  = HasOption(opts, "id");
            uid = HasOption(opts, "uid");
            if(id | uid)
            {   if(!Confirm("Are you sure that you really understand how to use -id or -uid"))
                    return false;
            }
            return true;
        }

        // =============================================================================
        // Application Level Tests     
        // =============================================================================
        

            /*
            long memData = 0;
            foreach(ZIMapCommand.Fetch.Item i in mails)
                if(i.Literal != null) memData += i.Literal.Length;
            
            App.Disconnect();
            long memTotal = GC.GetTotalMemory(true) - memData;
            Console.WriteLine("Memory: {0} bytes for {1} mails ({2} byte per mail)",
                              memTotal, mails.Length, (memTotal + mails.Length - 1) / mails.Length);
            mails = null;
            memTotal = GC.GetTotalMemory(true);
            Console.WriteLine("Memory: {0} bytes residual, {1} for data.", memTotal, memData);
            */

        
        public static void MailSearch(string mailbox, 
                                      string search, string [] extra)
        {
            if(!App.MailboxOpen(mailbox, true))
            {   Error("Cannot open mailbox");
                return;
            }
            ZIMapApplication.MailInfo[] mails = App.MailSearch(null, "", search, extra);
            if(mails == null)
            {   Message("Search: failed to get headers");
                return;
            }
            
            TextTool.TableBuilder tb = GetTableBuilder(4);
            tb.Columns[0].MaxWidth = 20;
            tb.Columns[1].MaxWidth = 20; tb.Columns[1].RigthAlign = false;
            tb.Columns[2].MaxWidth = 54; tb.Columns[2].RigthAlign = false;
            
            ZIMapRfc822 mail = new ZIMapRfc822();
            for(int irun = 0; irun < mails.Length; irun++)
            {   if(!mail.Parse(mails[irun].Literal))
                       continue;
                tb.AddRow(mail.From, mail.To, mail.Subject);
            }
            Message("                     ");
            tb.Footer("");
            tb.PrintTable();
        }

        
        // =============================================================================
        // ExecuteXXX     
        // =============================================================================

        public static bool ExecuteCache(string[] opts)
        {   bool bOn    = HasOption(opts, "on");
            bool bOff   = HasOption(opts, "off");
            bool bClear = HasOption(opts, "clear");

            if(bOff || bClear) 
            {   Cache.Clear();
                Info("Cache cleared");
            }
            if(bOn)  Cache.Enabled = true;
            if(bOff) Cache.Enabled = false;
            Message("Caching {0}", Cache.Enabled ? "enabled" : "disabled");
            return true;
        }        

        // =============================================================================
        // Mailbox operations: Create, Delete, Rename and Subscribe     
        // =============================================================================

        public static bool ExecuteCreate(string[] args)
        {   if(!Cache.CheckMailboxArgs(ref args, false))    // none must exist 
                return false;

            bool bok = true;
            using(ZIMapCommand.Create cmd = App.Factory.CreateCreate())
            {   if(cmd == null) return false;
            
                for(int irun=0; irun < args.Length; irun++)
                {   string boxname = args[irun];
                    cmd.Reset();
                    cmd.Queue(boxname);
                    if(!cmd.Data.Succeeded)
                    {   Error("Failed to create mailbox: {0}: {1}", boxname, cmd.Data.Message);
                        bok = false; continue;
                    }
                    Message("Mailbox created: {0}", boxname);
                    Cache.AddMailbox(boxname);
                }
            }
            return bok;
        }        

        public static bool ExecuteDelete(string[] args)
        {   ZIMapApplication.MailBox[] boxes = Cache.GetMailboxes(true);             
            if(boxes == null) return false;                 // want message counts
            if(!Cache.CheckMailboxArgs(ref args, true))     // all must exist 
                return false;
            
            bool bok = true;
            using(ZIMapCommand.Delete cmd = App.Factory.CreateDelete())
            {   if(cmd == null) return false;
            
                for(int irun=0; irun < args.Length; irun++)
                {   string boxname = args[irun];
                    
                    // get mailbox and check if empty ...
                    uint idx;
                    if(!Cache.FindMailbox(ref boxname, true, out idx)) return false;
                    if(boxes[idx].Messages > 0)
                    {   if(!Confirm("Mailbox '{0}' is not empty. Continue",
                                    boxname)) continue;
                    }
                               
                    // now delete it ...
                    if(boxname == Cache.CurrentMailbox) ExecuteClose();
                    cmd.Reset();
                    cmd.Queue(boxname);
                    if(!cmd.Data.Succeeded)
                    {   Error("Failed to delete '{0}': {1}", boxname, cmd.Data.Message);
                        bok = false; continue;
                    }
                    Message("Mailbox deleted: {0}", boxname);
                    Cache.DeleteMailbox(boxname);
                }
            }
            return bok;
        }        

        public static bool ExecuteRename(string[] args)
        {   if(args.Length != 2)
            {   Error("Invalid argument count (must be 2)");
                return false;
            }
            string boxname = args[0];
            string newname = args[1];
            if(!Cache.ResolveDotArg(ref boxname)) return false;
            if(!Cache.FindMailbox(ref boxname, true)) return false;
            if(Cache.FindMailbox(ref newname, false)) return false;
            if(boxname == newname)
            {   Error("Both names are equal: " + boxname);
                return false;
            }
            if(boxname == Cache.CurrentMailbox) ExecuteClose();
            
            ZIMapCommand.Rename cmd = App.Factory.CreateRename();
            if(cmd == null) return false;
            cmd.Queue(boxname, newname);
            bool bok = cmd.Data.Succeeded;
            if(!bok) Error("Rename to '{0}' failed: {1}",
                           newname, cmd.Data.Message);
            else 
            {   Cache.Clear();
                Message("Renamed '{0}' to '{1}'", boxname, newname);
                cmd.Dispose();
            }
            return bok;
        }
        
        public static bool ExecuteSubscribe(string[] opts, string[] args)
        {   int irun=0;
            bool bAdd    = HasOption(opts, "add");
            bool bRemove = HasOption(opts, "remove");
            
            ZIMapApplication.MailBox[] boxes = Cache.GetMailboxes(true);             
            if(boxes == null) return false;             // need subscription info
            
            string action = null;
            ZIMapCommand.MailboxBase cmd = null;
            
            // -add: action add subscriptions
            if(bAdd)
            {   cmd = App.Factory.CreateSubscribe();
                if(cmd == null) return false;
                bRemove = false;
                action = "subscribe";
            }

            // -remove: action remove subscriptions
            else if(bRemove)
            {   cmd = App.Factory.CreateUnsubscribe();
                if(cmd == null) return false;
                action = "unsubscribe";
            }
            
            // no option, execute "list -subscriptions"
            else
            {   if(args.Length > 0)
                {   Error("List mode, only one {filter} argument allowed");
                    return false;
                }
                string[] subs = { "subscription" };
                return ExecuteList(subs, (args.Length == 1) ? args[0] : "");
            }
            
            if(!Cache.CheckMailboxArgs(ref args, true)) // mailboxes must exist
            {   cmd.Dispose();
                return false;
            }

            bool bok = true;
            for(; irun < args.Length; irun++)
            {   string boxname = args[irun];
                uint idx;
                if(!Cache.FindMailbox(ref boxname, true, out idx)) break;
                if(cmd == null)
                {   Message("{0}: {1}", (boxes[idx].Subscribed ?
                        "    subscribed" : "not subscribed"), boxes[idx].Name);
                    continue;
                }
                cmd.Reset();
                cmd.Queue(boxname);
                if(!cmd.Data.Succeeded)
                {   Error("Failed to {0} mailbox '{1}': {2}", action, boxname, cmd.Data.Message);
                    bok = false; continue;
                }
                Message("Mailbox {0}d: {1}", action, boxname);
                boxes[idx].Subscribed = bAdd;
            }
            cmd.Dispose();
            return bok;
        }        

        // =============================================================================
        // Listings: List, Show     
        // =============================================================================

        public static bool ExecuteList(string[] opts, string filter)
        {   bool bAll    = HasOption(opts, "all");
            bool bDetail = bAll || HasOption(opts, "counts");
            bool bRights = bAll || HasOption(opts, "rights");
            bool bQuota  = bAll || HasOption(opts, "quota");
            bool bSubscr = HasOption(opts, "subscription");
            if(!(bRights | bQuota)) bDetail = true;

            Cache.MailboxFilter = filter;
            ZIMapApplication.MailBox[] mailboxes = Cache.GetMailboxes(bDetail, bSubscr);
            return ListMailboxes(mailboxes, false, bSubscr, bDetail, bRights, bQuota);
        }

        public static bool ExecuteShow(string[] opts, string mailbox)
        {   bool bBrief  = HasOption(opts, "brief");
            bool bTo     = bBrief | HasOption(opts, "to");
            bool bFrom   = bBrief | HasOption(opts, "from");
            bool bSubject= bBrief | HasOption(opts, "subject");
            bool bDate   = HasOption(opts, "date");
            bool bSize   = HasOption(opts, "size");
            bool bFlags  = HasOption(opts, "flags");
            bool bUID    = HasOption(opts, "uid");
            bool bID     = HasOption(opts, "id");
            if(!(bTo | bFrom | bSubject | bDate | bSize | bFlags | bUID | bID))
               bTo = bFrom = bSubject = true;
            
            // implicit open ...
            if(mailbox != "" && !ExecuteOpen(null, mailbox))
                return false;
            
            ZIMapApplication.MailInfo[] mails = Cache.GetHeaders();
            return ListMails(mails, bTo, bFrom, bSubject,
                             bDate, bSize, bFlags, bUID, bID);
        }

        // =============================================================================
        // Command for the current mailbox: Open, Close, Copy    
        // =============================================================================
        
        public static bool ExecuteOpen(string[] opts, string boxname)
        {   bool bRead  = HasOption(opts, "read");
            bool bWrite = HasOption(opts, "write");

            if(string.IsNullOrEmpty(boxname) || boxname == ".")
            {   if(Cache.CurrentMailbox == null)
                {   Message("No current mailbox");
                    return true;
                }
                boxname = Cache.CurrentMailbox;
            }
            
            ZIMapApplication.MailBox[] boxes = Cache.GetMailboxes(true);             
            if(boxes == null) return false;             // want message counts
            uint idx;
            if(!Cache.FindMailbox(ref boxname, true, out idx)) return false;

            string action = "current";
            if(Cache.CurrentMailbox != boxname)      action = "opened";
            else if(bRead && !App.MailboxIsReadonly) action = "reopened";
            else if(bWrite && App.MailboxIsReadonly) action = "reopened";
            
            if(action != "current")
            {   if(Cache.CurrentMailbox != null) ExecuteClose();
                if(!Cache.OpenMailbox(boxname, !bWrite)) return false;
            }
            Message("Mailbox {0}: {1} ({2} mails {3})", 
                    action, boxes[idx].Name, boxes[idx].Messages,
                    App.MailboxIsReadonly ? "Readonly" : "Writable" );
            return true;
        }

        public static bool ExecuteClose()
        {
            string curr = Cache.CurrentMailbox;
            if(Cache.CloseMailbox())
            {   Info("Mailbox closed: {0}", curr);
                return true;
            }
            Error("No open mailbox");
            return false;
        }        

        public static bool ExecuteCopy(string[] opts, string[] args)
        {    if(args.Length < 2)
            {   Error("Missing arguments");
                return false;
            }
            bool bID, bUID;
            if(!CheckIdAndUidUse(opts, out bID, out bUID)) return false;

            Cache.CommandRunning = true;
            uint[] uids = Cache.CheckItemArgs(args, 1, bID, bUID);
            if(uids == null) return false;              // bad arg list

            return Cache.CopyMails(uids, !bID, args[0]);            
        }
        
        // =============================================================================
        // Command for the current mailbox: Set, Unset and Expunge    
        // =============================================================================

        public static bool ExecuteSet(string[] opts, string[] args)
        {   return ExecuteSet(opts, args, true);
        }

        public static bool ExecuteUnset(string[] opts, string[] args)
        {   return ExecuteSet(opts, args, false);
        }
        
        public static bool ExecuteSet(string[] opts, string[] args, bool bSet)
        {   bool bDeleted = HasOption(opts, "deleted");
            bool bSeen    = HasOption(opts, "seen");
            bool bFlagged = HasOption(opts, "flagged");
            bool bCustom  = HasOption(opts, "custom");

            if(args.Length < 1)
            {   Error("Missing arguments");
                return false;
            }
            
            bool bID, bUID;
            if(!CheckIdAndUidUse(opts, out bID, out bUID)) return false;

            string mailbox = ".";
            if(!Cache.FindMailbox(ref mailbox, true)) return false;
            if(Cache.CurrentReadonly)
            {   if(!Confirm("Current mailbox '" + mailbox + "' is readonly. Change mode"))
                    return false;
                Info("Setting write mode for current mailbox");
            }
            
            uint uofs = 0;
            string[] custom = null;
            if(bCustom)                                 // scan for custom flags...
            {   List<string> list = null;
                while(uofs < args.Length)
                {   string cust = args[uofs];
                    if(cust[0] >= '0' && cust[0] <= '9') break; // item number stops
                    if(list == null) list = new List<string>();
                    list.Add(cust); uofs++;
                }
                if(list != null) custom = list.ToArray();
            }
            
            Cache.CommandRunning = true;
            uint[] uids = Cache.CheckItemArgs(args, uofs, bID, bUID);
            if(uids == null) return false;              // bad arg list

            return Cache.FlagMails(uids, !bID, bSet, bDeleted, bSeen, bFlagged, custom);            
        }

        public static bool ExecuteExpunge()
        {   string mailbox = ".";
            if(!Cache.FindMailbox(ref mailbox, true)) return false;
            if(Cache.CurrentReadonly)
            {   if(!Confirm("Current mailbox '" + mailbox + "' is readonly. Change mode"))
                    return false;
                Info("Setting write mode for current mailbox");
            }
        
            int cnt = Cache.ExpungeMails();
            if(cnt < 0) return false;
            Cache.CommandRunning = false;           // TODO: Info should do CommandRunning=false
            Info("Expunged {0} mail{1}", cnt, (cnt == 1) ? "" : "s");
            return true;
        }
        
        // =============================================================================
        // Command for the current mailbox: Sort    
        // =============================================================================

        public static bool ExecuteSort(string[] opts)
        {   bool bRevert = HasOption(opts, "revert");
            bool bTo     = HasOption(opts, "to");
            bool bFrom   = HasOption(opts, "from");
            bool bSubject= HasOption(opts, "subject");
            bool bDate   = HasOption(opts, "date");
            bool bSize   = HasOption(opts, "size");
            bool bFlags  = HasOption(opts, "flags");
            bool bID     = HasOption(opts, "id");
            bool bUID    = HasOption(opts, "uid");

            uint ucnt = 0;
            if(bTo)      ucnt++;
            if(bFrom)    ucnt++;
            if(bSubject) ucnt++;
            if(bDate)    ucnt++;
            if(bSize)    ucnt++;
            if(bFlags)   ucnt++;
            if(bID)      ucnt++;
            if(bUID)     ucnt++;
            if(ucnt > 1 || (ucnt == 0 && !bRevert))
            {   Error("Exactly one sort key (and/or -revert) must be specified");
                return false;
            }
            
            Cache.CommandRunning = true;
            ZIMapApplication.MailInfo[] headers = Cache.GetHeaders();
            if(headers == null) return false;
            App.MonitorProgress(30);

            if(ucnt > 0)
            {   object[] keys = new object[headers.Length];
                ZIMapRfc822 mail = new ZIMapRfc822();
                // build sort array ...
                for(uint urun=0; urun < headers.Length; urun++)
                {   if     (bSize)  keys[urun] = headers[urun].Size;
                    else if(bFlags) keys[urun] = string.Join(" ", headers[urun].Flags);
                    else if(bID)    keys[urun] = headers[urun].Index;
                    else if(bUID)   keys[urun] = headers[urun].UID;
                    else
                    {   mail.Parse(headers[urun].Literal);
                        if     (bTo)      keys[urun] = mail.To;
                        else if(bFrom)    keys[urun] = mail.From;
                        else if(bSubject) keys[urun] = mail.Subject;
                        else              keys[urun] = mail.DateBinary;
                    }
                }
                App.MonitorProgress(50);
                Array.Sort(keys, headers);
            }
            App.MonitorProgress(80);
            if(bRevert) Array.Reverse(headers);
            return true;
        }
        
        // =============================================================================
        // Run any IMap command: imap     
        // =============================================================================
        public static bool ExecuteImap(string[] opts, string[] args)
        {   bool bVerba = HasOption(opts, "verbatim");
            if(args.Length < 1)
            {   Error("Missing arguments");
                return false;
            }
            ZIMapCommand.Generic cmd = App.Factory.CreateGeneric(args[0]);
            if(bVerba && !string.IsNullOrEmpty(UnparsedCommand) && args.Length > 1)
            {   string low = UnparsedCommand.ToLower();   
                string sub = null;   
                for(int irun=0; irun < UnparsedCommand.Length - 1; irun++)
                {   if(low[irun]  != ' ') continue;
                    if(low[irun+1] < 'a') continue;
                    if(low[irun+1] > 'z') continue;
                    sub = UnparsedCommand.Substring(irun+1);
                    break;
                }
                if(sub == null) return false;
                string[] arga = sub.Split(" ".ToCharArray(), 2);
                if(arga.Length != 2) return false;
                cmd.AddDirect(arga[1]);
            }
            else for(int irun=1; irun < args.Length; irun++)
            {   if(args[irun] == "?")
                {   string lit = LineTool.Prompt("Input text");
                    if(lit == null) return false;
                    cmd.AddString(lit, true);
                }
                else
                    cmd.AddDirect(args[irun]);
            }
            cmd.Execute(true);
            Message(cmd.ToString());
            Cache.Clear();
            return true;
        }

        // =============================================================================
        // Namespace support: user and shared     
        // =============================================================================
        public static bool ExecuteShared(string[] opts, string[] args)
        {   return ExecuteUser(opts, args, false);
        }

        public static bool ExecuteUser(string[] opts, string[] args)
        {   return ExecuteUser(opts, args, true);
        }
                
        private static bool ExecuteUser(string[] opts, string[] args, bool bUser)
        {   bool bAdd    = HasOption(opts, "add");
            bool bRemove = HasOption(opts, "remove");
            bool bQuota  = HasOption(opts, "quota");
            bool bList   = HasOption(opts, "list");

            if(opts.Length > 1)
            {   Error("Can have only one option at a time");
                return false;
            }

            // -----------------------------------------------------------------
            // Implement set or clear ListPrefix, -list
            // -----------------------------------------------------------------            
            
            // no option or argument: clear the current prefix
            if(opts.Length == 0 && args.Length == 0) 
            {   if(ListPrefix != null) Cache.Clear(); 
                ListPrefix = null;              // differs from "" !!!
                Info("Namespace prefix cleared");
                return true;
            }

            // Get right namspace for ExecuteUser() or ExecuteShared() 
            ZIMapServer.Namespace ns = bUser ? App.Server.NamespaceDataOther :
                                               App.Server.NamespaceDataShared;
            string qual = ns.Prefix;
            if(qual == null) qual = "";

            // load list of users, handle -list option
            ZIMapApplication.MailBox[] users = Cache.GetUsers(bUser, bList);
            if(users == null) return false;
            
            if(bList)
                return ListMailboxes(users, true, false, false, true, true);

            // no option, set namespace
            if(opts.Length == 0)
            {   if(args.Length != 1)
                {   Error("Needs a single {user} argument");
                    return false;
                }
                if(args[0] == ".")
                    ListPrefix = App.Server.NamespaceDataUser.Prefix;
                else if(args[0] == "*")
                {   if(users.Length == 0)
                    {   Error("No visible users");
                        return false;
                    }
                    ListPrefix = qual;
                }
                else if(args[0].IndexOf(ns.Delimiter) >= 0)
                    ListPrefix = args[0];
                else
                {   string boxname = args[0];
                    int idx = ZIMapApplication.MailboxFind(users, boxname);
                    if(idx == -2)
                    {   Error("Name is not unique: " + boxname);
                        return false;
                    }
                    if(idx < 0)
                    {   if(Account.StartsWith(args[0]))
                            ListPrefix = App.Server.MyMailboxName();
                        else
                        {    Error("User not found: " + boxname);
                            return false;
                        }
                    }
                    else
                        ListPrefix = users[idx].Name;
                }
                if(ListPrefix.Length > 0 &&
                   ListPrefix[ListPrefix.Length-1] == App.Factory.HierarchyDelimiter)
                    ListPrefix = ListPrefix.Substring(0, ListPrefix.Length-1);
                Info("Namespace prefix now: '{0}'", ListPrefix);
                Cache.Clear();
                return true;
            }
            
            // -----------------------------------------------------------------
            // Implement -add -remove and -quota
            // -----------------------------------------------------------------            
            
            if((bRemove && args.Length > 1) || args.Length > 3)
            {   Error("Extra arguments not allowed");
                return false;
            }

            // get user, storage and message quota
            bool bFail = false;
            uint uStorage = 0, uMessage = 0;
            string user = null;
            if(args.Length == 3)
            {   user = args[0];
                if(!uint.TryParse(args[1], out uStorage)) bFail = true;
                if(!uint.TryParse(args[2], out uMessage)) bFail = true;
            }
            else if( args.Length == 2)
            {   if(uint.TryParse(args[0], out uStorage))
                {   if(!uint.TryParse(args[1], out uMessage)) bFail = true;
                }
                else
                {   user = args[0];
                    if(!uint.TryParse(args[1], out uStorage)) bFail = true;
                }
            }
            else if( args.Length == 1)
                user = args[0];
            if(bFail)
            {   Error("Numeric argument expected");
                return false;
            }
                
            // use the ListPrefix as default, extract user name
            if(user == null) user = ListPrefix;
            if(string.IsNullOrEmpty(user))
            {   Error("No name given and no default set");
                return false;
            }

            if(user == "" || user == Account)
                user = App.Server.MyMailboxName();
            else if(user.IndexOf(ns.Delimiter) < 0)
                user = ns.Prefix + user;

            // Does the user exist?
            uint uidx = 0;
            for(; uidx < users.Length; uidx++)
                if(string.Compare(users[uidx].Name, user, true) == 0)
                {   user = users[uidx].Name;
                    break;
                }
            if(bAdd)
            {   if(uidx < users.Length)
                {   Error("A mailbox already exists: {0}", user);
                    return false;
                }
            }
            else if(uidx >= users.Length)
            {   Error("No mailbox found for: {0}", user);
                return false;
            }

            // Implement -add and -quota
            if(bAdd | bQuota)
            {   System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendFormat(bAdd ? "Add new mailbox '{0}'" : "Set quota for '{0}'", user);
                if(uStorage > 0) sb.AppendFormat(", {0} kByte storage limit", uStorage);
                if(uMessage > 0) sb.AppendFormat(", max {0} messages", uMessage);
                if(!Confirm(sb.ToString())) return false;
                
                if(bAdd)
                {   ZIMapCommand.Create cmd = App.Factory.CreateCreate();
                    cmd.Queue(user);
                    if(cmd.Data.Succeeded) Info("Created mailbox '{0}'", user);
                    cmd.Dispose();
                }
                if(uMessage > 0 || uStorage > 0)
                {   if(App.QuotaLimit(user, uMessage, uStorage))
                        Info("Quota limits for '{0}' updated", user);
                }
            }
            
            // Implement -remove
            else if(bRemove)
            {   if(!Confirm(string.Format("Remove mailbox '{0}'", user))) return false;
                {   ZIMapCommand.SetACL acl = App.Factory.CreateSetACL();
                    acl.Queue(user, Account, "lca");
                    ZIMapCommand.Delete cmd = App.Factory.CreateDelete();
                    cmd.Queue(user);
                    if(cmd.Data.Succeeded) Info("Removed mailbox: {0}", user);
                    else                   Error("Remove failed: {0}", cmd.Data.Message);
                    cmd.Dispose();
                }
            }
            else                                            // should not go here
                return false;
            Cache.Clear();
            return true;
        }

        // =============================================================================
        // Quota support: quota     
        // =============================================================================
        public static bool ExecuteQuota(string[] opts, string[] args)
        {   bool bMega  = HasOption(opts, "mbyte");
            if(args.Length < 1)
            {   Error("No mailbox specified");
                return false;
            }
            string mailbox = args[0];
            if(!Cache.FindMailbox(ref mailbox, true)) return false;

            if(args.Length == 1)
            {   uint uMsgUse, uMsgLim, uStoUse, uStoLim;
                string[] root = App.QuotaInfos(mailbox, out uStoUse, out uStoLim, out uMsgUse, out uMsgLim);
                if(root == null) return false;
                if(root.Length < 1 || (uMsgLim == 0 && uStoLim == 0))
                {   Message("Mailbox '{0}' has no quota limits", mailbox);
                    return true;
                }
                Message("Mailbox '{0}' has quota root '{1}':", mailbox, root[0]);
                if(uStoLim > 0) Message("Storage use: {0,6} kByte   limit: {1,6} kByte", 
                                        uStoUse/1024, uStoLim/1024);
                if(uMsgLim > 0) Message("Message use: {0,6} mails   limit: {1,6} mails", 
                                        uMsgUse/1024, uMsgLim/1024);
                return true;
            }
            
            uint uMessage = 0, uStorage = 0;
            bool bok = true;
            if(                   !uint.TryParse(args[1], out uStorage)) bok = false;
            if(args.Length > 2 && !uint.TryParse(args[2], out uMessage)) bok = false;
            if(!bok)
            {   Error("Invalid quota argument");
                return false;
            }
            if( bMega) uStorage *= 1024;
            if(!App.QuotaLimit(mailbox, uMessage, uStorage))
                return false;
            Info("Quota limits for '{0}' updated", mailbox);
            Cache.Clear();
            return true;
        }

        // =============================================================================
        // ACL support: rights
        // =============================================================================
        public static bool ExecuteRights(string[] opts, string[] args)
        {   bool bAll   = HasOption(opts, "all");
            bool bRead  = HasOption(opts, "read");
            bool bWrite = HasOption(opts, "write");
            bool bNone  = HasOption(opts, "none");
            bool bCust  = HasOption(opts, "custom");
            bool bDeny  = HasOption(opts, "deny");
            int maxOpt = 1;
            if(bDeny) maxOpt++;
            if(opts.Length > maxOpt)
            {   Error("Invalid use of options");
                return false;                
            }
            if(bCust && args.Length < 2)
            {   Error("Missing argument(s)");
                return false;
            }

            string mailbox = (args.Length > 0) ? args[0] : "%";
            if(!bAll && !bRead && !bWrite && !bNone && !bCust)
            {   ZIMapCommand.GetACL cmd = new ZIMapCommand.GetACL(App.Factory);
                
                ZIMapApplication.MailBox[] boxes = App.Mailboxes(ListPrefix, mailbox, 0, false);
                if(boxes == null || boxes.Length < 1)
                {   Error("No matching mailboxes");
                    return false;
                }
                
                TextTool.TableBuilder table = GetTableBuilder(4);
                table.IndexMode = 1;
                table.Columns[0].MinWidth = 10;
                table.Columns[1].MinWidth = 10;
                table.Columns[1].RigthAlign = false;
                table.Columns[2].MinWidth = 10;
                table.Columns[2].RigthAlign = false;
                table.Columns[3].MinWidth = 10;
                table.Columns[3].RigthAlign = false;
                table.Header("Mailbox", "User", "Grant", "Deny");
                bool bSep = false;
                for(int ibox=0; ibox < boxes.Length; ibox++)
                {   string curr = boxes[ibox].Name; 
                    cmd.Reset();
                    cmd.Queue(curr);
                    ZIMapCommand.GetACL.Item[] items = cmd.Items;
                    if(items == null) continue;

                    if(bSep) table.AddSeparator();
                    bSep = true;

                    foreach(ZIMapCommand.GetACL.Item item in items)
                    {   if(item.Negative)
                            table.AddRow(curr, item.Name, "", item.Rights);
                        else
                            table.AddRow(curr, item.Name, item.Rights);
                        curr = "";
                    }
                }
                table.Footer("");
                table.PrintTable();
                cmd.Dispose();
                return true;
            }
            
            if(!Cache.FindMailbox(ref mailbox, true)) return false;
            string rights = "";
            if(bAll)        rights = "lrswipcda";
            else if(bRead)  rights = "lrs";
            else if(bWrite) rights = "lrswipcd";
            else if(bCust)  rights = args[1];
            if(bDeny) rights = "-" + rights;

            ZIMapCommand acl = bNone ? (ZIMapCommand)(new ZIMapCommand.DeleteACL(App.Factory))
                                     : (ZIMapCommand)(new ZIMapCommand.SetACL(App.Factory));
            string user = Account;
            int irun = bCust ? 2 : 1;
            bool bok = true;
            do 
            {   if(irun < args.Length) user = args[irun];
                acl.Reset();
                if(bNone)
                    ((ZIMapCommand.DeleteACL)acl).Queue(mailbox, user);
                else
                    ((ZIMapCommand.SetACL)acl).Queue(mailbox, user, rights);
                if(!acl.Data.Succeeded)
                {   Error("Set ACL failed for: {0}", user);
                    bok = false;
                }
                
            } while(++irun < args.Length);
            acl.Dispose();
            return bok;
        }
    }
}
