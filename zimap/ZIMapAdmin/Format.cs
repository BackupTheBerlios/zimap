//==============================================================================
// Format.cs    The ZLibAdmin output formatting
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
        // =============================================================================
        // Tools        
        // =============================================================================

        public static string FormatFilter(string message)
        {   string qual = ZIMapAdmin.Cache.Data.Qualifier;
            if(string.IsNullOrEmpty(qual)) return message;
            return string.Format("{0} [Filter: {1}]", message, qual);
        }

        public static bool FormatQuota(string mailbox, out string storage, out string message, out string qroot)
        {   storage = message = qroot = null;
            ZIMapApplication.QuotaInfo info;
            if(!App.QuotaInfos(mailbox, out info)) return false;
            if(!info.Valid || (info.MessageLimit == 0 && info.StorageLimit == 0)) return true;
            
            if(info.StorageLimit > 0) 
                storage = string.Format("Storage use: {0,6} kByte   limit: {1,6} kByte", 
                                        info.StorageUsage/1024, info.MessageLimit/1024);
            if(info.MessageLimit > 0) 
                message = string.Format("Message use: {0,6} mails   limit: {1,6} mails", 
                                        info.MessageUsage/1024, info.MessageLimit/1024);
            qroot = info.QuotaRoot;
            return true;
        }
        
        // =============================================================================
        // List Formatter for mailboxes         
        // =============================================================================
        
        // TODO: ListFolders header text argument          
        private static bool ListFolders(CacheData.MBoxRef mailboxes, string title,
                                        bool bSubscr, bool bDetail, bool bRights, bool bQuota)
        {   uint ubox = mailboxes.Count;
            if(ubox == 0) return false;
            if(!App.EnableRights) bRights = false;
            if(!App.EnableQuota)  bQuota  = false;
            
            uint ucol = 1;
            if(bDetail)  ucol += 3;
            if(bSubscr)  ucol++;
            if(bRights)  ucol++;
            if(bQuota)   ucol++;
            TextTool.TableBuilder table = GetTableBuilder(ucol);
            object[] data = new object[ucol];
            ucol = 0;
            data[ucol] = title;
            table.Columns[ucol].MaxWidth   = 20;
            table.Columns[ucol++].MinWidth = 34;    // name

            if(bSubscr)
                data[ucol++] = "Sub";
            if(bDetail)                             // messages
            {   data[ucol] = "Mails";
                table.Columns[ucol++].MinWidth = 6;
                data[ucol] = "Recent";
                table.Columns[ucol++].MinWidth = 6;
                data[ucol] = "Unseen";
                table.Columns[ucol++].MinWidth = 6;
            }
            if(bRights)
            {   data[ucol] = "Rights";
                table.Columns[ucol].RigthAlign = false;
                table.Columns[ucol++].MinWidth = 6;
            }
            if(bQuota)
            {   data[ucol] = "Size Quota";
                table.Columns[ucol++].MinWidth = 12;
            }

            // print a list ...
            uint nmsg = 0;
            uint nrec = 0;
            uint nuns = 0;
            uint scnt = 0;
            uint nlas = 0;                      // last NS seen
            bool bsep = false;                  // separator on NS change
                
            table.Header(data);
            string subs = Ascii ? "*" : "■";    // HACK: for mono no "✓";

            mailboxes.Reset();
            while(mailboxes.Next())
            {   // get name, copy MailBox - we need it frequently ...
                ZIMapApplication.MailBox mailbox = mailboxes.Current;
                uint   nidx = ZIMapServer.Personal;
                string name = mailbox.Name; 
                if(App.EnableNamespaces) name = App.Server.FriendlyName(name, out nidx);
                if(nidx != nlas)
                {   if(bsep) table.AddSeparator();
                    nlas = nidx; 
                }
                bsep = true;
                
                // fill table ..
                ucol = 0;
                data[ucol++] = name;
                    
                if(bSubscr)
                {   data[ucol++] = mailbox.Subscribed  ? subs : "";
                    if(mailbox.Subscribed) scnt++;
                }
                if(bDetail)
                {   data[ucol++] = mailbox.Messages;
                    data[ucol++] = mailbox.Recent;
                    data[ucol++] = mailbox.Unseen;
                }
                if(bRights)
                    data[ucol++] = mailboxes.ExtraRights;
                if(bQuota)
                {   ZIMapApplication.QuotaInfo info;
                    if(mailboxes.ExtraGetQuota(out info))
                    {   if(info.QuotaRoot != mailbox.Name)
                            data[ucol++] = string.Format("[Root={0}]", mailboxes.Search(info.QuotaRoot)+1);
                        else
                            data[ucol++] = string.Format("{0}k {1,3}%", info.StorageUsage,
                                (uint)((info.StorageUsage*100.0)/info.StorageLimit), info.QuotaRoot);
                    }
                    else
                        data[ucol++] = "";
                }
                table.AddRow(data);
                
                // build sums for footer line ...
                nmsg += mailbox.Messages;
                nrec += mailbox.Recent;
                nuns += mailbox.Unseen;
            }
            if(bSubscr && bDetail)
                table.Footer("Total", scnt, nmsg, nrec, nuns);
            else if(bDetail)
                table.Footer("Total", nmsg, nrec, nuns);
            else if(bSubscr)
                table.Footer("Total", scnt);
            else        
                table.Footer("");
            table.PrintTable();
            return true;
        }
        
        // =============================================================================
        //         
        // =============================================================================
        public static bool ListMails(ZIMapApplication.MailInfo[] mails,
                           bool bTo, bool bFrom, bool bSubject, bool bDate, 
                           bool bSize, bool bFlags, bool bUID, bool bID)
        {   if(mails == null) return false;
            if(mails.Length < 1)
            {   Message("No mails");
                return true;
            }
            
            uint ucol = 0;
            if(bTo)      ucol++;
            if(bFrom)    ucol++;
            if(bSubject) ucol++;
            if(bDate)    ucol++;
            if(bSize)    ucol++;
            if(bFlags)   ucol++;
            if(bUID)     ucol++;
            if(bID)      ucol++;
            
            TextTool.TableBuilder tb = GetTableBuilder(ucol);
            object[] data = new object[ucol];
            ucol = 0;
            if(bID)
            {   data[ucol] = "ID";  
                tb.Columns[ucol].RigthAlign = true;
                tb.Columns[ucol++].MaxWidth = 6;
            }
            if(bUID)
            {   data[ucol] = "UID";  
                tb.Columns[ucol].RigthAlign = true;
                tb.Columns[ucol++].MaxWidth = 6;
            }
            if(bFrom)
            {   data[ucol] = "From";  
                tb.Columns[ucol].RigthAlign = false;
                tb.Columns[ucol++].MaxWidth = 20;
            }
            if(bTo)
            {   data[ucol] = "To";  
                tb.Columns[ucol].RigthAlign = false;
                tb.Columns[ucol++].MaxWidth = 20;
            }
            if(bDate)
            {   data[ucol] = "Date";  
                tb.Columns[ucol].RigthAlign = true;
                tb.Columns[ucol++].MaxWidth = 20;
            }
            if(bSize)
            {   data[ucol] = "kByte";  
                tb.Columns[ucol].RigthAlign = true;
                tb.Columns[ucol++].MaxWidth = 6;
            }
            if(bFlags)
            {   data[ucol] = "Flags";
                tb.Columns[ucol].RigthAlign = false;
                tb.Columns[ucol++].MaxWidth = 32;
            }
            if(bSubject)
            {   data[ucol] = "Subject";
                tb.Columns[ucol].RigthAlign = false;
                tb.Columns[ucol++].MinWidth = 32;
            }
            tb.Header(data);

            ZIMapMessage mail = new ZIMapMessage();
            for(int irun = 0; irun < mails.Length; irun++)
            {   if(!mail.Parse(mails[irun].Literal, false))
                       continue;
                ucol = 0;
                if(bID)      data[ucol++] = mails[irun].Index;
                if(bUID)     data[ucol++] = mails[irun].UID;
                if(bFrom)    data[ucol++] = mail.From;
                if(bTo)      data[ucol++] = mail.To;
                if(bDate)    data[ucol++] = mail.DateISO;
                if(bSize)    data[ucol++] = (mails[irun].Size + 1023) / 1024;
                if(bFlags)   data[ucol++] = string.Join(" ", mails[irun].Flags);
                if(bSubject) data[ucol++] = mail.Subject;
                tb.AddRow(data);
            }
            tb.Footer("");
            tb.PrintTable();
            return true;
        }

        // =============================================================================
        // Public interface to List Mailboxes, Subscriptions and Users         
        // =============================================================================

        /// <summary>List all mailboxes</summary>
        public static bool ListFolders(CacheData.MBoxRef mailboxes,
                                       bool bDetail, bool bRights, bool bQuota)
        {   if(mailboxes.IsNothing) return false;
            Cache.ListedFolders = mailboxes;
            if(mailboxes.Count == 0)
            {   Message(FormatFilter("No Mailboxes")); 
                return true;
            }
            return ListFolders(mailboxes, FormatFilter("Mailboxes"), 
                               true, bDetail, bRights, bQuota);
        }

        /// <summary>List subscribed mailboxes only</summary>
        public static bool ListSubscribed(CacheData.MBoxRef mailboxes,
                                          bool bDetail, bool bRights, bool bQuota)
        {   if(mailboxes.IsNothing) return false;
            Cache.ListedFolders = mailboxes;
            
            // count subscriptions ...
            uint usub = 0;
            mailboxes.Reset();
            while(mailboxes.Next())
                if(mailboxes.Subscribed) usub++;
            if(usub == 0)
            {   Message(FormatFilter("No subscribed mailboxes")); 
                return true;
            }

            // create subset ...
            if(usub != mailboxes.Count)
            {   ZIMapApplication.MailBox[] subs = new ZIMapApplication.MailBox[usub];
                usub = 0;  
                mailboxes.Reset();
                while(mailboxes.Next()) 
                    if(mailboxes.Subscribed) subs[usub++] = mailboxes.Current;
                mailboxes = new CacheData.MBoxRef(subs);
            }
            
            return ListFolders(mailboxes, FormatFilter("Subscribed Mailboxes"),
                               false, bDetail, bRights, bQuota);
        }

        /// <summary>List the user root mailboxes in a brief format</summary>
        public static bool ListUsers()
        {   CacheData.MBoxRef users = Cache.Data.Users;
            Cache.ListedUsers = users;
            if(users.Count == 0)
            {   Message("No users");
                return true;
            }
            return ListFolders(users, "IMap Users", false, false, false, false);
        }

        // =============================================================================
        //         
        // =============================================================================

        /// <summary>Prints a list of rights per mailbox</summary>
        /// <param name="entries">
        /// An array of string arrays.  Each entry stand for a table row.  The entries
        /// are simply arrays of 3 or 4 strings.  If the 1st string is empty this denotes
        /// another right for the same mailbox (mailbox name not printed again). 
        /// </param>
        public static bool ListRights(string[][] entries)
        {   if(entries == null || entries.Length < 1)
            {   Error("No matching mailboxes");
                return true;
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

            string last = null;
            for(int ibox=0; ibox < entries.Length; ibox++)
            {   string[] entry = entries[ibox];
                string curr = entry[0];
                if(curr != "")                      // space flags more rights
                {   if(last != curr && last != null) table.AddSeparator();
                    last = curr;
                    uint nidx;                    
                    if(App.EnableNamespaces)        // use account name
                        entry[0] = App.Server.FriendlyName(curr, out nidx);
                }
                table.AddRow(entry);
            }
            table.Footer("");
            table.PrintTable();
            return true;
        }

        // =============================================================================
        //         
        // =============================================================================

        // helper for ShowInfo
        private static uint ListOutput(uint mode, string prefix, string message)
        {   if(message == null) message = "";
            if(prefix[0] == ' ') prefix += "  ";
            else                 prefix += ": ";
            if(mode != 0) message = prefix + message;
            
            TextTool.Decoration deco = TextTool.Decoration.Double;
            if(mode == 1)       deco = TextTool.Decoration.None;
            if(mode == 2)       deco = TextTool.Decoration.Single;
            if(mode == 5)       deco = TextTool.Decoration.Single | TextTool.Decoration.Double;
            uint umax = 100;
            if(TextTool.TextWidth < umax) umax = TextTool.TextWidth;
            uint upre = (uint)prefix.Length;
            if(upre > umax) umax = upre;
            if(mode == 0)
                TextTool.PrintAdjust(0, umax, TextTool.Adjust.Center, deco, message);
            else
                TextTool.PrintIndent((int)upre, umax, deco, message);
            return umax - upre;
        }
        
        public static bool ShowInfo(object what)
        {   if(what == null) return false;
            
            // -----------------------------------------------------------------
            // Server
            // -----------------------------------------------------------------
            if(what is string)
            {
                ListOutput(0, "          ", "Server Information for: " + App.ServerName);
                ListOutput(1, "Security  ", App.Connection.TlsMode.ToString());
                ListOutput(1, "Timeout   ", App.Connection.TransportTimeout.ToString() + " [s]");

                ListOutput(2, "Greeting  ", App.Connection.ProtocolLayer.ServerGreeting);
                ListOutput(1, "Type      ", App.Server.ServerType + " (Subtype: " + 
                                            App.Server.ServerSubtype + ")");
                ListOutput(1, "Admin     ", App.Server.IsAdmin ? "yes" : "no");
                
                ListOutput(2, "Capability", string.Join(" ", App.Factory.Capabilities));
                ListOutput(1, "Rights    ", App.EnableRights ? "enabled" : "disabled");
                string sout = "disabled";
                if(App.EnableQuota)
                    sout = string.Format("enabled (STORAGE={0} MESSAGE={1})",
                                         App.Server.HasLimit("STORAGE") ? "yes" : "no",
                                         App.Server.HasLimit("MESSAGE") ? "yes" : "no");
                ListOutput(1, "Quota     ", sout);

                ListOutput(5, "Namespace ", "Personal=" + App.Server.NamespaceDataPersonal + "\n" +
                                            "Others  =" + App.Server.NamespaceDataOther + "\n" +
                                            "Shared  =" + App.Server.NamespaceDataShared + "\n" +
                                            "Search  =" + App.Server.NamespaceDataSearch);
                return true;
            }            
            
            // -----------------------------------------------------------------
            // Application
            // -----------------------------------------------------------------
            if(what == App)
            {
                ListOutput(0, "          ", "Application Information");
                ListOutput(1, "Runtime   ", 
                           System.Environment.Version.ToString() + " (" +
                           System.Environment.OSVersion.ToString() + ")" );
                ListOutput(1, "Version   ", 
                           System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()
#if DEBUG
                           + " (DEBUG build)"
#endif                           
                           );

                App.Export.Open(null, (char)0, false, false);
                string bfld = ZIMapExport.BaseFolder;
                ListOutput(2, "Path      ", (bfld == "") ? "[not set]" : bfld);
                
                long mem1 = GC.GetTotalMemory(false) / 1024; Cache.Data.Clear(CacheData.Info.All);
                long mem2 = GC.GetTotalMemory(true)  / 1024;
                ListOutput(5, "Memory    ", string.Format("{0} kByte (minimum is {1} kByte)",
                                                          mem1, mem2));
                return true;
                
            }
            
            // -----------------------------------------------------------------
            // Mailbox
            // -----------------------------------------------------------------
            if(what is ZIMapApplication.MailBox)
            {
                ZIMapApplication.MailBox mbox = (ZIMapApplication.MailBox)what;
                ListOutput(0, "          ", "Mailbox Information");
                string msg = mbox.Name;
                if(mbox.Name == Cache.Data.Current.Name)
                    msg = string.Format("{0} (current {1})", msg,
                          Cache.Data.Current.ReadOnly ? "Read-Only" : "Writable");
                else
                    msg += " (not current)";
                ListOutput(1, "Mailbox   ", msg);
                ListOutput(1, "Messages  ",
                              string.Format("{0} mails ({1} recent {2} unseen)",
                              mbox.Messages, mbox.Recent, mbox.Unseen));
                ListOutput(1, "Subscribed", mbox.Subscribed ? "yes" : "no");
                ListOutput(2, "Flags     ", string.Join(" ", mbox.Flags));
                ListOutput(1, "Attributes", string.Join(" ", mbox.Attributes));
                string qroot, storage, message;
                FormatQuota(mbox.Name, out storage, out message, out qroot);
                if(qroot == null)
                    ListOutput(5, "Quota     ",      "-none-");
                else
                {   if(message != null) message = "\n" + message;
                    if(storage != null) storage = "\n" + storage;
                    ListOutput(5, "Quota     ",      "Quota Root : " + 
                                   qroot + message + storage);
                }
                return true;
            }

            // -----------------------------------------------------------------
            // Mail Item
            // -----------------------------------------------------------------
            if(what is uint[])
            {   uint[] uarg = (uint[])what;
                ZIMapApplication.MailInfo[] mails = Cache.Data.Headers;
                
                uint uuid = uarg[0];
                uint urun;
                for(urun=0; urun < mails.Length; urun++)
                    if(mails[urun].UID == uuid) break;
                if(urun >= mails.Length)
                {   Error("UID not found: " + uuid);
                    return false;
                }
                ZIMapApplication.MailInfo mail = mails[urun];
                ZIMapMessage mesg = new ZIMapMessage();
                if(!mesg.Parse(mail.Literal, false)) return false;

                ZIMapMessage.BodyInfo info = null;
                ZIMapCommand.Fetch cmd = new ZIMapCommand.Fetch(App.Factory);
                cmd.UidCommand = true;
                if((uarg[1] & 2) != 0)
                    cmd.Queue(uuid, "BODY BODY.PEEK[TEXT]");
                else
                    cmd.Queue(uuid, "BODY");
                if(!cmd.CheckSuccess("Failed to get status")) return false;
                if((uarg[1] & 2) != 0)
                {   if(cmd.Result.Literals == null || cmd.Result.Literals.Length < 1)
                    {  Info("Message has no body");
                       return true;
                    }
                    mesg.ParseBody(cmd.Result.Literals[0], 0);
                }
                string[] parts = cmd.Items[0].Parts;
                if(parts != null && parts.Length > 1 && parts[0] == "BODY")
                    info = ZIMapMessage.ParseBodyInfo(parts[1]);

                uint utxt = ListOutput(0, "          ", "Mail Information");
                
                ListOutput(1, "Item      ", string.Format("{0} (ID={1}  UID={2})",
                                            urun+1, mail.Index, mail.UID));
                ListOutput(1, "From      ", mesg.From); 
                ListOutput(1, "To        ", mesg.To); 
                ListOutput(1, "Subject   ", mesg.Subject); 
                ListOutput(1, "Date      ", mesg.DateISO); 
                ListOutput(1, "Size      ", mail.Size.ToString()); 

                ListOutput(2, "Flags     ", string.Join(" ", mail.Flags));

                if(info != null && info.Parts != null)
                    for(int irun=0; irun < info.Parts.Length; irun++)
                    {   string text = info.Parts[irun].ToString();
                        int icol = text.IndexOf(':');
                        if(icol > 0) text = text.Substring(icol+2);
                        ListOutput((uint)((irun == 0) ? 2 : 1), 
                                   ("Part [" + info.Parts[irun].Level + "]").PadRight(10), text);
                     }

                if((uarg[1] & 1) != 0)
                {
                    List<string> llis = new List<string>();
                    //string[] names = item.FieldNames;
                    for(int irun=0; irun < mesg.HeaderCount; irun++)
                    {   string[] lines = TextTool.TextIndent(mesg.FieldKey(irun).PadRight(15) + "  " +
                                                  mesg.FieldText(irun), 17, utxt);
                        llis.AddRange(lines);
                    }
                    if(llis.Count == 0)
                        ListOutput(5, "Headers   ", "-none-");
                    if(llis.Count == 1)
                        ListOutput(5, "Headers   ", llis[0]);
                    else for(int irun=0; irun < llis.Count; irun++)
                    {   if(irun == 0)
                            ListOutput(2, "Headers   ", llis[0]);
                        else
                            ListOutput(1, "          ", llis[irun]);
                    }
                }
                
                if(mesg.BodyCount > 0)
                {   StringBuilder sb = new StringBuilder();
                    for(int irun=0; irun < mesg.BodyCount; irun++)
                        sb.AppendLine(mesg.BodyLine(irun, null));
                    ListOutput(2, "Body Text ", sb.ToString());
                }
                TextTool.Formatter.WriteLine(
                    TextTool.DecoLine(TextTool.Decoration.Double, 0, utxt+12));
                return true;
            }
            
            return false;
        }

        // =============================================================================
        // List Exports         
        // =============================================================================
        public static void ListExports()
        {   ZIMapExport.DumpMailFiles(App.Export.Existing);
        }
    }
}
