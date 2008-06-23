//==============================================================================
// Execute.cs   The ZLibAdmin command executor
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU General Public License
// This software is published under the GNU GPL license.  Please refer to the
// file COPYING for details. Please use the following e-mail address to contact
// me or to send bug-reports:  info at j-pfennig dot de .
#endregion

using System;
using System.Text;
using System.Collections.Generic;
using ZIMap;
using ZTool;

namespace ZIMapTools
{
    public partial class ZIMapAdmin
    {
        // =====================================================================
        // Miscelaneous operations: Cache and Debug
        // =====================================================================

        public static bool ExecuteCache(string[] opts)
        {   bool bOn    = HasOption(opts, "on");
            bool bOff   = HasOption(opts, "off");
            bool bClear = HasOption(opts, "clear");

            if(bOff || bClear)
            {   Cache.Data.Clear(CacheData.Info.All);
                Info("Cache cleared");
            }
            if(bOn)  Cache.Data.Caching = true;
            if(bOff) Cache.Data.Caching = false;
            Message("Caching {0}", Cache.Data.Caching ? "enabled" : "disabled");
            return true;
        }

        public static bool ExecuteDebug(string arg)
        {   string message;
            if(string.IsNullOrEmpty(arg))
                message = "Current Debug level: {0}";
            else
            {   uint level;
                if(!CheckNumber(arg, out level)) return false;
                DebugLevel(level);
                message = "Changed Debug level to: {0}";
            }
            Message(message, Debug);
            return true;
        }

        // =====================================================================
        // Mailbox operations: Create, Delete, Rename and Subscribe
        // =====================================================================
        
        public static bool ExecuteCreate(string[] args)
        {   if(!Cache.CheckMailboxArgs(ref args, false))    // none must exist
                return false;
            string pref = Cache.CheckQualifier();           // use filter setting
            if(pref == null) return false;                  // not confirmed

            bool bok = true;
            for(int irun=0; irun < args.Length; irun++)     // create them
                if(!Cache.MailboxCreate(pref+args[irun])) bok = false;
            return bok;
        }

        public static bool ExecuteDelete(string[] opts, string[] args)
        {   bool bRecurse = HasOption(opts, "recurse");

            if(!Cache.Data.Load(CacheData.Info.Details))    // want message counts
                return false;
            if(!Cache.CheckMailboxArgs(ref args, true))     // all args must exist
                return false;

            uint udel = 0;
            string warn = null;
            for(int irun=0; irun < args.Length; irun++)     // count non-empty mboxs
            {   CacheData.MBoxRef umbx = Cache[args[irun]];
                if(bRecurse)
                {   CacheData.MBoxRef ubox = CacheData.MBoxRef.Nothing;
                    while(umbx.Recurse(ref ubox, App.Server))
                        if(ubox.Messages > 0) { udel++; warn = ubox.Name; }
                        
                }
                else
                    if(umbx.Messages > 0) { udel++; warn = umbx.Name; }
            }
            if(udel > 1 &&                                  // multiple non-empty
                !Confirm("Do you want to delete {0} non empty mailboxes", udel))
                    return false;
            if(udel < 2 && warn != null &&                  // one non-empty mbox
                !Confirm("Mailbox '{0}' is not empty. Continue", warn)) 
                    return false;
            
            bool bok = true;
            for(int irun=0; irun < args.Length; irun++)     // loop to delete
            {   CacheData.MBoxRef umbx = Cache[args[irun]];
                if(bRecurse)
                {   CacheData.MBoxRef ubox = CacheData.MBoxRef.Nothing;
                    while(umbx.Recurse(ref ubox, App.Server))
                    {   if(umbx.Index == ubox.Index) continue;  // skip root
                        if(!Cache.MailboxDelete(ubox)) bok = false;
                    }
                }
                if(!Cache.MailboxDelete(umbx)) bok = false; // delete root
            }
            return bok;
        }

        public static bool ExecuteRename(string[] args)
        {   if(!CheckArgCount(args, 2, 2)) return false;
            CacheData.MBoxRef umbx = Cache[args[0]];
            if(!umbx.IsValid) return false;

            string pref = Cache.CheckQualifier();           // use filter setting
            if(pref == null) return false;                  // not confirmed
            string newname = pref + args[1];
                        
            if(Cache.MailboxFind(ref newname, false) != uint.MaxValue)
                return false;                               // destination exists
            if(umbx.Name == newname)
            {   Error("Both names are equal: " + umbx.Name);
                return false;
            }
            if(umbx.Name == Cache.Data.Current.Name) Cache.MailboxClose();

            ZIMapCommand.Rename cmd = new ZIMapCommand.Rename(App.Factory);
            if(cmd == null) return false;
            cmd.Queue(umbx.Name, newname);
            bool bok = cmd.CheckSuccess();
            if(!bok) Error("Rename to '{0}' failed: {1}",
                           newname, cmd.Result.Message);
            else
            {   Cache.Data.Clear(CacheData.Info.Folders);
                Message("Renamed '{0}' to '{1}'", umbx.Name, newname);
            }
            cmd.Dispose();
            return bok;
        }

        public static bool ExecuteSubscribe(string[] opts, string[] args)
        {   bool bAdd    = HasOption(opts, "add");
            bool bRemove = HasOption(opts, "remove");
            if(CheckExclusive(opts, "add", "remove") > 1) return false;

            // -add/-remove: manage subscriptions
            string action = null;
            if(bAdd)            action = "subscribe";
            else if(bRemove)    action = "unsubscribe";

            // no option, execute "list -subscriptions"
            else
            {   if(args.Length > 1)
                {   Error("List mode, only one {filter} argument allowed");
                    return false;
                }
                return ExecuteList(new string[] { "subscription" }, 
                                   (args.Length == 1) ? args[0] : "");
            }

            if(!Cache.Data.Load(CacheData.Info.Details))    // want message counts
                return false;
            if(!Cache.CheckMailboxArgs(ref args, true))     // mailboxes must exist
                return false;

            ZIMapCommand.MailboxBase cmd =                  // create command 
                (ZIMapCommand.MailboxBase)App.Factory.CreateByName(action);
            if(cmd == null) return false;
            
            bool bok = true;
            for(int irun=0; irun < args.Length; irun++)
            {   CacheData.MBoxRef umbx = Cache[args[irun]]; // should never fail
                cmd.Reset();
                cmd.Queue(umbx.Name);
                if(!cmd.CheckSuccess(string.Format(":Failed to {0} mailbox: {1}: {2}",
                                     action, umbx.Name, cmd.Result.Message)))
                {   bok = false; continue;
                }
                Message("Mailbox {0}d: {1}", action, umbx.Name);
                // take care to modify the correct (shared) MBoxInfo
                Cache.Data.Folders.Array[umbx.Index].Subscribed = bAdd;
            }
            cmd.Dispose();
            return bok;
        }

        // =====================================================================
        // Listings: List, Show
        // =====================================================================

        public static bool ExecuteList(string[] opts, string filter)
        {   bool bAll    = HasOption(opts, "all");
            bool bDetail = bAll || HasOption(opts, "counts");
            bool bRights = bAll || HasOption(opts, "rights");
            bool bQuota  = bAll || HasOption(opts, "quota");
            bool bSubscr = HasOption(opts, "subscription");
            if(!(bRights | bQuota)) bDetail = true;

            // what data do we need?
            Cache.Data.Filter = filter;
            CacheData.Info info = CacheData.Info.Folders;
            if(bDetail) info |= CacheData.Info.Details;
            if(bRights) info |= (CacheData.Info.Rights | CacheData.Info.Details);
            if(bQuota)  info |= (CacheData.Info.Quota  | CacheData.Info.Details);
            
            // load and display ...
            if(!Cache.Data.Load(info)) return false;
            return bSubscr ? ListSubscribed(Cache.Data.Folders, bDetail, bRights, bQuota)
                           : ListFolders(Cache.Data.Folders, bDetail, bRights, bQuota);
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
               bTo = bFrom = bSubject = true;               // set default options

            if(!Cache.Data.Load(CacheData.Info.Headers))    // load headers into cache
                return false;
            return ListMails(Cache.Data.Headers,            // format output
                             bTo, bFrom, bSubject, bDate, bSize, bFlags, bUID, bID);
        }

        // =====================================================================
        // Command for the current mailbox: Open, Close, Copy
        // =====================================================================

        public static bool ExecuteOpen(string[] opts, string boxname)
        {   bool bRead  = HasOption(opts, "read");
            bool bWrite = HasOption(opts, "write");
            if(CheckExclusive(opts, "read", "write") > 1) return false;

            if(!Cache.Data.Load(CacheData.Info.Details))    // want message counts
                return false;
            CacheData.MBoxRef umbx = Cache[boxname];        // get the mailbox
            if(!umbx.IsValid) return false;                 // no such mailbox

            string action = "current";
            if(Cache.Data.Current.Name != umbx.Name) action = "opened";
            else if(bRead && !App.MailboxIsReadonly) action = "reopened";
            else if(bWrite && App.MailboxIsReadonly) action = "reopened";

            if(action != "current")                         // open or reopen
            {   Cache.MailboxClose();
                if(!Cache.MailboxOpen(umbx, !bWrite)) return false;
            }
            Message("Mailbox {0}: {1} ({2} mails {3})",
                    action, umbx.Name, umbx.Messages,
                    App.MailboxIsReadonly ? "Readonly" : "Writable" );
            return true;
        }

        public static bool ExecuteClose()
        {   if(!Cache.MailboxClose()) Error("No current mailbox");
            return true;                                // ignore errors
        }

        public static bool ExecuteCopy(string[] opts, string[] args)
        {   if(!CheckArgCount(args, 2, 0)) return false;
            CacheData.MBoxRef umbx = Cache[args[0]];    // get the mailbox
            if(!umbx.IsValid) return false;             // no such mailbox
            bool bID, bUID;
            if(!CheckIdAndUidUse(opts, out bID, out bUID))
                return false;                           // not confirmed
            uint[] uids = Cache.CheckItemArgs(args, 1, bID, bUID, false);
            if(uids == null) return false;              // bad arg list

            Message("Copying {0} mail{1} to mailbox '{2}'",
                    uids.Length, (uids.Length == 1) ? "" : "s", umbx.Name);
            return Cache.CommandCopy(uids, !bID, umbx);
        }

        // =====================================================================
        // Command for the current mailbox: Set, Unset and Expunge
        // =====================================================================

        public static bool ExecuteSet(string[] opts, string[] args)
        {   return ExecuteSet(opts, args, true);
        }

        public static bool ExecuteUnset(string[] opts, string[] args)
        {   return ExecuteSet(opts, args, false);
        }

        // helper that implement Set and Unset
        private static bool ExecuteSet(string[] opts, string[] args, bool bSet)
        {   bool bDeleted = HasOption(opts, "deleted");
            bool bSeen    = HasOption(opts, "seen");
            bool bFlagged = HasOption(opts, "flagged");
            bool bCustom  = HasOption(opts, "custom");

            if(!CheckArgCount(args, 1, 0))                      // need a least 1 arg
                return false;
            bool bID, bUID;
            if(!CheckIdAndUidUse(opts, out bID, out bUID))      // output a warning
                return false;
            if(!Cache.MailboxOpen(false))                       // clear readonly mode
                return false;

            uint uofs = 0;
            string[] custom = null;
            if(bCustom)                                         // scan for custom flags...
            {   List<string> list = null;
                while(uofs < args.Length)
                {   string cust = args[uofs];
                    if(cust[0] >= '0' && cust[0] <= '9') break; // item number stops
                    if(list == null) list = new List<string>();
                    list.Add(cust); uofs++;
                }
                if(list != null) custom = list.ToArray();
            }

            uint[] uids = Cache.CheckItemArgs(args, uofs, bID, bUID, false);
            if(uids == null) return false;                      // bad arg list

            return Cache.CommandFlag(uids, !bID, bSet, bDeleted, bSeen, bFlagged, custom);
        }

        public static bool ExecuteExpunge(string[] opts, string[] args)
        {   bool bID, bUID;
            if(!CheckIdAndUidUse(opts, out bID, out bUID))      // output a warning
                return false;
            if(!Cache.MailboxOpen(false))                       // clear readonly mode
                return false;

            // flag messages as deleted ...
            if(args.Length > 0)
            {   uint[] uids = Cache.CheckItemArgs(args, 0, bID, bUID, false);
                if(uids == null) return false;                  // bad arg list
                if(!Cache.CommandFlag(uids, !bID, true, true, false, false, null))
                    return false;
            }

            // get the number of deleted messages ...
            ZIMapCommand.Search cmd = new ZIMapCommand.Search(App.Factory);
            cmd.Queue("deleted");
            if(!cmd.CheckSuccess(":")) return false;
            if(cmd.MatchCount == 0)
            {   Info("No messages are marked as deleted");
                return true;
            }
            if(!Confirm("Expunge {0} messages from mailbox '{1}'",
                        cmd.Matches.Length, Cache.Data.Current.Name)) return false;

            // and finally do it ...
            uint cnt = Cache.CommandExpunge();
            if(cnt == uint.MaxValue) return false;
            Info("Expunged {0} mail{1}", cnt, (cnt == 1) ? "" : "s");
            return true;
        }

        // =====================================================================
        // Command for the current mailbox: Sort
        // =====================================================================

        public static bool ExecuteSort(string[] opts)
        {   bool   bRevert = HasOption(opts, "revert");
            uint   ucnt  = 0;
            string field = null;
            if(HasOption(opts, "to"))       {   ucnt++; field = "to";      }
            if(HasOption(opts, "from"))     {   ucnt++; field = "from";    }
            if(HasOption(opts, "subject"))  {   ucnt++; field = "subject"; }
            if(HasOption(opts, "date"))     {   ucnt++; field = "date";    }
            if(HasOption(opts, "size"))     {   ucnt++; field = "size";    }
            if(HasOption(opts, "flags"))    {   ucnt++; field = "flags";   }
            if(HasOption(opts, "id"))       {   ucnt++; field = "id";      }
            if(HasOption(opts, "uid"))      {   ucnt++; field = "uid";     }
            if(ucnt > 1 || (ucnt == 0 && !bRevert))
            {   Error("Exactly one sort key (and/or -revert) must be specified");
                return false;
            }

            if(Cache.Data.Headers == null)                  // headers not cached
            {   ProgressReporting.Update(0);
                ProgressReporting.Push(0, 66);
                if(!Cache.Data.Load(CacheData.Info.Headers)) return false;
                ProgressReporting.Pop();
                ProgressReporting.Push(66,100);
            }
            return Cache.HeadersSort(Cache.Data.Headers, field, bRevert);
        }

        // =====================================================================
        // Run any IMap command: imap
        // =====================================================================
        
        public static bool ExecuteImap(string[] opts, string[] args)
        {   bool bVerba = HasOption(opts, "verbatim");
            if(!CheckArgCount(args, 1, 0)) return false;

            ZIMapCommand.Generic cmd = App.Factory.CreateGeneric(args[0]);
            if(bVerba && !string.IsNullOrEmpty(UnparsedCommand))
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
                else if(args[irun] == "#")
                {   string lit = LineTool.Prompt("Mailbox name");
                    if(lit == null) return false;
                    cmd.AddMailbox(lit);
                }
                else
                    cmd.AddDirect(args[irun]);
            }
            cmd.Execute(true);
            Message(cmd.ToString());
            Cache.Data.Clear(CacheData.Info.All);
            return true;
        }

        // =====================================================================
        // Namespace support: user and shared
        // =====================================================================

        public static bool ExecuteShared(string[] opts, string[] args)
        {   return ExecuteUser(opts, args, ZIMapServer.Shared);
        }

        public static bool ExecuteUser(string[] opts, string[] args)
        {   return ExecuteUser(opts, args, ZIMapServer.Others);
        }

        private static bool ExecuteUser(string[] opts, string[] args, uint nsIndex)
        {   bool bAdd    = HasOption(opts, "add");
            bool bRemove = HasOption(opts, "remove");
            bool bQuota  = HasOption(opts, "quota");
            bool bList   = HasOption(opts, "list");
            if(CheckExclusive(opts, "add", "remove", "quota", "list") > 1) return false;

            // -----------------------------------------------------------------
            // Implement set or clear ListPrefix, -list
            // -----------------------------------------------------------------

            // no option or argument: clear the current prefix
            if(opts.Length == 0 && args.Length == 0)
            {   Cache.Data.Qualifier = null;            // differs from "" !!!
                Info("Qualifier cleared");
                return true;
            }

            // load user data into cache
            if(!Cache.Data.Load((nsIndex == ZIMapServer.Others) ? CacheData.Info.Others 
                                                                : CacheData.Info.Shared))
                return false;

            // handle -list option
            if(bList) return ListUsers();
                
            // no option, set namespace
            if(opts.Length == 0)
            {   if(args.Length != 1)
                {   Error("Needs a single {user} argument");
                    return false;
                }

                string qualifier = args[0];
                bool enableNs = (qualifier == "+");
                if (qualifier == "-" || enableNs)
                {   App.EnableNamespaces = enableNs;
                    Cache = new CacheData(App);         // throw away old cache
                    Info("Namespaces {0}abled", enableNs ? "en" : "dis");
                    return true;
                }
                else if(qualifier == "*")                // filter other/shared
                {   if(Cache.Data.Users.Count == 0)
                    {   Error("No visible users");
                        return false;
                    }
                    qualifier = App.Server[nsIndex].Qualifier;
                }
                else if(qualifier != "." &&
                        App.Server.DelimiterIndex(qualifier, 0, nsIndex) >= 0)
                {   qualifier = App.Server.DelimiterTrim(qualifier, nsIndex);
                    Info("Name containing delimiter is used without validation");
                }
                else
                {   if(!Cache.UserFind(ref qualifier, true)) return false;
                    // Inbox is a special name, fix things like INBOX.INBOX ...
                    if(qualifier == App.Server.NamespaceDataPersonal.Prefix + "INBOX")
                        qualifier = "INBOX";
                }

                Cache.Data.Qualifier = qualifier;
                Info("Qualifier now: '{0}'", Cache.Data.Qualifier);
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
            uint uStorage = 0, uMessage = 0;
            string user = null;
            if(args.Length == 3)
            {   user = args[0];
                if(!CheckNumber(args, 1, out uStorage)) return false;
                if(!CheckNumber(args, 2, out uMessage)) return false;
            }
            else if( args.Length == 2)
            {   if(uint.TryParse(args[0], out uStorage))
                {   if(!CheckNumber(args, 1, out uMessage)) return false;
                }
                else
                {   user = args[0];
                    if(!CheckNumber(args, 1, out uStorage)) return false;
                }
            }
            else if( args.Length == 1)
                user = args[0];

            // use the ListPrefix as default, extract user name
            if(user == null) user = Cache.Data.Qualifier;
            if(string.IsNullOrEmpty(user))
            {   Error("Required {user} argument missing");
                return false;
            }
            
            if(App.Server.DelimiterIndex(user, 0, nsIndex) >= 0)
            {   user = App.Server.DelimiterTrim(user, nsIndex);
                if(user == "")
                {   Error("Invalid name"); return false;
                }
                Info("Name containing delimiter is used without validation");
            }
            else if(!Cache.UserFind(ref user, !bAdd)) 
                return false;

            // Implement -add and -quota
            if(bAdd | bQuota)
            {   System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendFormat(bAdd ? "Add new mailbox '{0}'" : "Set quota for '{0}'", user);
                if(uStorage > 0) sb.AppendFormat(", {0} kByte storage limit", uStorage);
                if(uMessage > 0) sb.AppendFormat(", max {0} messages", uMessage);
                if(!Confirm(sb.ToString())) return false;
                return Cache.UserManage(user, bAdd ? "create" : "quota", uStorage, uMessage);
            }

            // Implement -remove
            if(bRemove)
            {   if(!Confirm(string.Format("Remove mailbox '{0}'", user))) return false;
                return Cache.UserManage(user, "delete", 0, 0);
            }
            return false;                                   // should never go here
        }

        // =====================================================================
        // Quota support: quota
        // =====================================================================

        public static bool ExecuteQuota(string[] opts, string[] args)
        {   bool bMega  = HasOption(opts, "mbyte");
            if(!CheckArgCount(args, 1, 3)) return false;        // need mailbox
            CacheData.MBoxRef umbx = Cache[args[0]];
            if(!umbx.IsValid) return false;                     // no such mailbox

            if(args.Length == 1)
            {   string qroot, storage, message;
                if(!FormatQuota(umbx.Name, out storage, out message, out qroot))
                    return false;
                if(qroot == null)
                {   Message("Mailbox '{0}' has no quota limits", umbx.Name);
                    return true;
                }
                Message("Mailbox '{0}' has quota root '{1}'", umbx.Name, qroot);
                if(storage != null) Message(storage);
                if(message != null) Message(message);
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
            if(!App.QuotaLimit(umbx.Name, uStorage, uMessage))
                return false;
            Info("Quota limits for '{0}' updated", umbx.Name);
            umbx.ExtraQuotaRoot = null;
            Cache.Data.Clear(CacheData.Info.Quota);
            return true;
        }

        // =====================================================================
        // ACL support: rights
        // =====================================================================

        public static bool ExecuteRights(string[] opts, string[] args)
        {   bool bAll   = HasOption(opts, "all");
            bool bRead  = HasOption(opts, "read");
            bool bWrite = HasOption(opts, "write");
            bool bNone  = HasOption(opts, "none");
            bool bCust  = HasOption(opts, "custom");
            bool bDeny  = HasOption(opts, "deny");
            bool bTree  = HasOption(opts, "recurse");
            uint uopt = CheckExclusive(opts, "all", "read", "write", "none", "custom");
            if(uopt > 1) return false;
            if(bCust && args.Length < 2)
            {   Error("Missing argument(s)");
                return false;
            }

            string mailbox = (args.Length > 0) ? args[0] : "";
                
            if(uopt == 0)
            {   ZIMapCommand.GetACL cmd = new ZIMapCommand.GetACL(App.Factory);
                ZIMapApplication.MailBox[]
                    boxes = App.Mailboxes(Cache.Data.Qualifier, mailbox, 0, false);
                if(boxes == null) return false;

                List<string[]> entries = new List<string[]>();                
                for(int ibox=0; ibox < boxes.Length; ibox++)        // mailbox loop
                {   string curr = boxes[ibox].Name;
                    cmd.Reset();
                    cmd.Queue(curr);
                    ZIMapCommand.GetACL.Item[] items = cmd.Items;
                    if(items == null) continue;
                    foreach(ZIMapCommand.GetACL.Item item in items) // rights loop
                    {   if(item.Negative)
                            entries.Add(new string[] {curr, item.Name, "", item.Rights});
                        else
                            entries.Add(new string[] {curr, item.Name, item.Rights});
                        curr = "";                                  // don't rep. name
                    }
                }
                cmd.Dispose();
                return ListRights(entries.ToArray());               // print as table
            }

            string rights = "";
            if(bAll)        rights = "lrswipcda";
            else if(bRead)  rights = "lrs";
            else if(bWrite) rights = "lrswipcd";
            else if(bCust)  rights = args[1];
            if(bDeny) rights = "-" + rights;
            if(bNone) rights = null;
            
            string user = Account;
            int irun = bCust ? 2 : 1;
            bool bok = true;
            do  {   if(irun < args.Length) user = args[irun];
                    if(!Cache.CommandACL(Cache[mailbox], bTree, user, rights)) bok = false;
                } while(++irun < args.Length);
            Cache.Data.Clear(CacheData.Info.Rights);
            return bok;
        }

        // =====================================================================
        // Info
        // =====================================================================
        public static bool ExecuteInfo(string[] opts, string arg)
        {   bool bMailbox = HasOption(opts, "mailbox");
            bool bServer  = HasOption(opts, "server");
            bool bApp     = HasOption(opts, "application");
            bool bID      = HasOption(opts, "id");
            bool bUID     = HasOption(opts, "uid");
            bool bHead    = HasOption(opts, "headers");
            bool bBody    = HasOption(opts, "body");

            if(CheckExclusive(opts, "mailbox", "server", "application",
                                     "id", "uid") > 1) return false;
            if(CheckExclusive(opts, "mailbox", "server", "application",
                                     "body") > 1) return false;
            if(CheckExclusive(opts, "mailbox", "server", "application",
                                     "headers") > 1) return false;
            if((bServer || bApp) && arg != "")
            {   Error("Extra arguments not allowed");
                return false;
            }

            // info about server or application ...
            if(bServer)
                return ShowInfo("server");
            if(bApp)
                return ShowInfo(App);

            // info about a mailbox ...
            if(bMailbox)
            {   if(!Cache.Data.Load(CacheData.Info.Details))
                    return false;
                return ShowInfo(Cache[arg]);
            }

            // info about a mail item ...
            uint uidx = Cache.CheckItemArgs(arg, bID, bUID);
            if(uidx == uint.MaxValue) return false;
            uint umsk = 0;
            if(bHead) umsk += 1;
            if(bBody) umsk += 2;
            return ShowInfo(new uint[] { uidx, umsk });
        }

        // =====================================================================
        // Search
        // =====================================================================
        public static bool ExecuteSearch(string[] opts, string[] args)
        {   bool bHeader = HasOption(opts, "header");
            bool bBody   = HasOption(opts, "body");
            bool bQuery  = HasOption(opts, "query");
            bool bOr     = HasOption(opts, "or");

            if(CheckExclusive(opts, "query", "or") > 1)     // any conflict?
                return false;
            if(!CheckArgCount(args, 1, 0))                  // need an argument
                return false;
            if(!Cache.MailboxOpen(true))                    // need current mbox
                return false;

            uint uarg = (uint)args.Length;
            string[] extra = null;
            string search;
            string what = "TEXT";
            if(bBody && !bHeader) what = "BODY";
            if(!bBody && bHeader) what = "SUBJECT";

            if(bQuery)
            {   string text = string.Format("({0})", args[0].Trim());
                ZIMapParser parser = new ZIMapParser(text);
                if(parser.Length != 1 ||                    // should not happen
                   parser[0].Type != ZIMapParser.TokenType.List) return false;
                ZIMapParser.Token query = parser[0];
                                                            // remove extra () ...
                while(query.Type == ZIMapParser.TokenType.List &&
                      query.List.Length == 1 &&
                      query.List[0].Type == ZIMapParser.TokenType.List)
                    query = query.List[0];

                ZIMapParser.Token[] qlist = query.List;
                if(qlist.Length == 0)
                {   Error("Query text must not be empty");
                    return false;
                }
                uint uval = 0;
                QueryCountArgs(qlist, ref uval);
                uarg--;
                if(uval != uarg)
                {   Error("Need {0} values but got {1}", uval, uarg);
                    return false;
                }
                if(uarg > 0)
                {   extra = new string[uarg];
                    Array.Copy(args, 1, extra, 0, uarg);
                }
                search = query.ToString();
            }
            else
            {   extra = args;
                StringBuilder sb = new StringBuilder();
                int ibra = 0;
                for(uint urun=0; urun < uarg; urun++)
                {
                    if(bOr && urun+2 < uarg)
                    {    sb.Append("OR " + what + " ? (");
                         ibra++;
                    }
                    else if(bOr && urun+1 < uarg)
                        sb.Append("OR " + what + " ? ");
                    else
                        sb.Append(what + " ? ");
                }
                sb.Append(')', ibra);
                search = sb.ToString();
            }

            Info("Query has {0} arguments: {1}", extra == null ? 0 : extra.Length, search);
            ZIMapApplication.MailInfo[] mails = App.MailSearch(null, "", search, extra);
            if(mails == null) return false;
            ListMails(mails, true, true, true, false, false, false, false, false);
            return true;
        }

        private static void QueryCountArgs(ZIMapParser.Token[] qlist, ref uint uvals)
        {   for(uint urun=0; urun < qlist.Length; urun++)
                if(qlist[urun].Type == ZIMapParser.TokenType.List)
                    QueryCountArgs(qlist[urun].List, ref uvals);
                else
                    if(qlist[urun].Text == "?") uvals++;
        }

        // =====================================================================
        // Export and Import
        // =====================================================================
        //
        //  export -list path
        //  export [-override] -recurse path [mailbox]
        //  export [-override] [-id|-uid] path [mailbox [items...]]
        //
        public static bool ExecuteExport(string[] opts, string[] args)
        {   bool bList     = HasOption(opts, "list");
            bool bRecurse  = HasOption(opts, "recurse");
            bool bOverride = HasOption(opts, "override");
            bool bQuoted   = HasOption(opts, "quoted");
            bool bID, bUID;
            if(CheckExclusive(opts, "list", "recurse")  > 1) return false;
            if(CheckExclusive(opts, "list", "override") > 1) return false;
            if(CheckExclusive(opts, "list", "quoted")   > 1) return false;
            if(CheckExclusive(opts, "recurse", "id", "uid") > 1) return false;
            if(!CheckIdAndUidUse(opts, out bID, out bUID)) return false;

            // get folder or file and do we have a mailbox argument?
            string path = (args.Length > 0) ? args[0] : "";
            if(path == "")
            {   Error("Missing path argument");
                return false;
            }
            if((bList && args.Length > 1) || (bRecurse && args.Length > 2))
            {   Error("Invalid extra arguments");
                return false;
            }

            // get the root mailbox of this export (details required)
            CacheData.MBoxRef umbx = CacheData.MBoxRef.Nothing;
            if(!bList)
            {   if(!Cache.Data.Load(CacheData.Info.Details)) // want message counts
                    return false;
                string mailbox = (args.Length > 1) ? args[1] : ".";
                umbx = Cache[mailbox];
                if(!umbx.IsValid) return false;
                if(!Cache.MailboxOpen(umbx, true)) return false;
            }
            
            // get item list
            uint[] uids = null;
            if(args.Length > 2)
            {   uids = Cache.CheckItemArgs(args, 2, bID, bUID, true);
                if(uids == null) return false;              // bad arg list
            }

            if(!App.ExportWrite(path, !bRecurse, !bOverride)) return false;
            if(bList)
            {   ListExports();
                return true;
            }

            uint usum = 0;
            uint uerr = 0;
            if(bRecurse)
            {   CacheData.MBoxRef ubox = CacheData.MBoxRef.Nothing;
                uint utot = 0;
                while(umbx.Recurse(ref ubox, App.Server))
                    utot += ubox.Messages;
                while(umbx.Recurse(ref ubox, App.Server))
                    if(!Cache.MailboxExport(ubox, bQuoted, null, utot, ref usum)) uerr++;
            }
            else
                if(!Cache.MailboxExport(umbx, bQuoted, uids, 0, ref usum)) uerr++;
            App.Export.IOStream = null;
            return (uerr == 0);
        }

        public static bool ExecuteImport(string[] opts, string[] args)
        {   bool bList    = HasOption(opts, "list");
            bool bRecurse = HasOption(opts, "recurse");
            bool bNoFlags = HasOption(opts, "noflags");
            bool bClean   = HasOption(opts, "clean");
            if(CheckExclusive(opts, "list", "recurse") > 1) return false;
            if(CheckExclusive(opts, "list", "noflags") > 1) return false;
            if(CheckExclusive(opts, "list", "clean")   > 1) return false;

            // get folder or file and do we have a mailbox argument?
            string path = (args.Length > 0) ? args[0] : "";
            if(path == "")
            {   Error("Missing path argument");
                return false;
            }
            if(!bList)
            {   CacheData.MBoxRef umbx = Cache[(args.Length > 1) ? args[1] : "."];
                if(!umbx.IsValid) return false;
                if(!Cache.MailboxOpen(umbx, true)) return false;
            }
            
            // open file or folder
            if(!App.ExportRead(path, !bRecurse)) return false;
            if(bList)
            {   ListExports();
                return true;
            }

            ZIMapExport.MailFile[] files = App.Export.Existing;
            if(files == null || files.Length == 0)
            {   Error("No mbox data found");
                return false;
            }
            uint ucnt = 0;
            long lsiz = 0;
            foreach(ZIMapExport.MailFile file in files)
            {   if(!file.Valid && !file.Empty)
                {   Message("File skipped (no mbox): {0}", file.FileName);
                    continue;
                }
                lsiz += file.FileInfo.Length;
                ucnt++;

            }
            Message("Importing {0} mbox file{1} ({2} kByte)", ucnt, (ucnt == 1) ? "" : "s", lsiz/1024);

            long lcur = 0;
            long llen;
            uint uerr = 0;
            foreach(ZIMapExport.MailFile file in files)
            {   if(!file.Valid) continue;
                llen = file.FileInfo.Length;
                if(!Cache.MailboxImport(file.MailboxName, bRecurse, bNoFlags, bClean,
                                        lcur, llen, lsiz)) uerr++;
                lcur += llen;
            }
            Cache.Data.Clear(CacheData.Info.All);
            return (uerr == 0);
        }
    }
}
