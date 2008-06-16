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
using ZTool;

namespace ZIMap
{
    public partial class ZIMapAdmin
    {     
        // =============================================================================
        // Tools        
        // =============================================================================

        public static bool FormatQuota(string mailbox, out string storage, out string message, out string qroot)
        {   storage = message = qroot = null;
            uint uMsgUse, uMsgLim, uStoUse, uStoLim;

            string[] root = App.QuotaInfos(mailbox, out uStoUse, out uStoLim, out uMsgUse, out uMsgLim);
            if(root == null) return false;
            if(root.Length < 1 || (uMsgLim == 0 && uStoLim == 0)) return true;
            
            qroot = root[0];
            if(uStoLim > 0) storage = string.Format("Storage use: {0,6} kByte   limit: {1,6} kByte", 
                                                    uStoUse/1024, uStoLim/1024);
            if(uMsgLim > 0) message = string.Format("Message use: {0,6} mails   limit: {1,6} mails", 
                                                    uMsgUse/1024, uMsgLim/1024);
            return true;
        }
        
        // =============================================================================
        //         
        // =============================================================================
        public static bool ListMailboxes(ZIMapApplication.MailBox[] mailboxes,
                                         bool bDetail, bool bRights, bool bQuota)
        {
            return ListMailboxes_(mailboxes, false, false, bDetail, bRights, bQuota);
        }
        
        // TODO: ListMailboxes header text argument          
        public static bool ListMailboxes_(ZIMapApplication.MailBox[] mailboxes, bool bUsers,
                                         bool bSubscr, bool bDetail, bool bRights, bool bQuota)
        {   if(mailboxes == null) return false;
            Cache.ListedMailboxes = new CacheData.MBoxRef(mailboxes, 0);
            if(mailboxes.Length <= 0)
            {   if(!bUsers && !string.IsNullOrEmpty(ZIMapAdmin.Cache.Qualifier))
                    Message("No mailboxes, prefix '{0}'", ZIMapAdmin.Cache.Qualifier);
                else
                    Message(bUsers  ? "No visible users" : "No mailboxes");
                return true;
            }
            
            uint ucol = 1;
            if(bDetail)  ucol += 3;
            if(!bSubscr && !bUsers) ucol++;
            if(bRights)  ucol++;
            if(bQuota)   ucol++;
            TextTool.TableBuilder table = GetTableBuilder(ucol);
            object[] data = new object[ucol];
            ucol = 0;

            if    (bSubscr) data[ucol] = "Subscribed mailboxes";
            else if(bUsers) data[ucol] = "IMap Users";
            else if(string.IsNullOrEmpty(ZIMapAdmin.Cache.Qualifier))
                data[ucol] = "Mailboxes";
            else
                data[ucol] = string.Format("Mailboxes ('{0}')", ZIMapAdmin.Cache.Qualifier);
            table.Columns[ucol].MaxWidth   = 20;
            table.Columns[ucol++].MinWidth = 34;    // name

            if(!bSubscr && !bUsers)
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

            Cache.LoadMailboxExtra(mailboxes, bRights, bQuota);

            // print a list ...
            uint nmsg = 0;
            uint nrec = 0;
            uint nuns = 0;
            uint scnt = 0;
            uint nlas = 0;                      // last NS seen
            bool bsep = false;                  // separator on NS change
                                                // do we use namespaces?
            bool nsok = App.Server.NamespaceDataOther.Valid;
            ZIMapServer.Namespace nsUser = App.Server.NamespaceDataUser;
                
            table.Header(data);
            string subs = Ascii ? "*" : "■";    // HACK: for mono no "✓";
            for(uint irun = 0; irun < mailboxes.Length; irun++) 
            {   uint   nidx = ZIMapServer.Personal;
                string name = mailboxes[irun].Name;
                if(nsok)                        // ok, use namespace
                {   if(name != "INBOX")
                        nidx = App.Server.FindNamespaceIndex(name, true);
                    if(nidx != nlas)
                    {   if(bsep) table.AddSeparator();
                        nlas = nidx; 
                    }
                    bsep = true;
                }
                else                            // don't use namespace
                    nidx = ZIMapServer.Shared;
                
                CacheData.MailboxExtra extra = (CacheData.MailboxExtra)mailboxes[irun].UserData; 
                ucol = 0;
                if(nidx == ZIMapServer.Personal)
                {   if(name == "INBOX")
                        name = Account + " [INBOX]";
                    else
                    {   if(nsUser.Prefix != "")
                           name = name.Substring(nsUser.Prefix.Length);
                        name = Account + nsUser.Delimiter + name;
                    }
                }
                data[ucol++] = name;
                    
                if(!bSubscr && !bUsers)
                {   data[ucol++] = mailboxes[irun].Subscribed  ? subs : "";
                    if(mailboxes[irun].Subscribed) scnt++;
                }
                if(bDetail)
                {   data[ucol++] = mailboxes[irun].Messages;
                    data[ucol++] = mailboxes[irun].Recent;
                    data[ucol++] = mailboxes[irun].Unseen;
                }
                if(bRights)
                    data[ucol++] = extra.Rights;
                if(bQuota)
                {   string quota = "";
                    if(!string.IsNullOrEmpty(extra.QuotaRoot) && extra.StorageLimit > 0)
                    {   uint uuse = extra.StorageUsage; // / 1024;
                        uint ulim = extra.StorageLimit; // / 1024;
                        quota = string.Format("{0}k {1,3}%", uuse, (uint)((uuse*100.0)/ulim));
                    }
                    data[ucol++] = quota;
                }
                table.AddRow(data);
                nmsg += mailboxes[irun].Messages;
                nrec += mailboxes[irun].Recent;
                nuns += mailboxes[irun].Unseen;
            }
            if(bDetail && !bSubscr)
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
        //         
        // =============================================================================
        public static bool ListSubscribed(ZIMapApplication.MailBox[] mailboxes,
                                          bool bDetail, bool bRights, bool bQuota)
        {   if(mailboxes == null) return false;
            uint nbox = 0;
            for(uint irun = 0; irun < mailboxes.Length; irun++) 
                if(mailboxes[irun].Subscribed) nbox++;
            if(nbox == 0)
            {   Message("No subscribed mailboxes"); 
                return true;
            }
            ZIMapApplication.MailBox[] subs;
            if(nbox == mailboxes.Length)
                subs = mailboxes;
            else
            {   subs = new ZIMapApplication.MailBox[nbox]; nbox = 0;  
                for(uint irun = 0; irun < mailboxes.Length; irun++) 
                    if(mailboxes[irun].Subscribed) subs[nbox++] = mailboxes[irun];
            }
            return ListMailboxes_(subs, false, true, bDetail, bRights, bQuota);
        }

        // =============================================================================
        //         
        // =============================================================================
        public static bool ListUsers(bool otherUsers)
        {
            ZIMapApplication.MailBox[] users = ZIMapAdmin.Cache.GetUsers(otherUsers);
            if(users == null) return false;
            return ListMailboxes_(users, true, false, false, true, true);
        }

        // =============================================================================
        //         
        // =============================================================================
        public static bool ListRights()
        {
            return true;
        }

        // =============================================================================
        //         
        // =============================================================================
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
                
                ListOutput(2, "Capability", string.Join(" ", App.Factory.Capabilities));
                ListOutput(1, "Rights    ", App.EnableRights ? "enabled" : "disabled");
                string sout = "disabled";
                if(App.EnableQuota)
                    sout = string.Format("enabled (STORAGE={0} MESSAGE={1})",
                                         App.Server.HasLimit("STORAGE") ? "yes" : "no",
                                         App.Server.HasLimit("MESSAGE") ? "yes" : "no");
                ListOutput(1, "Quota     ", sout);

                ListOutput(5, "Namespace ", "Personal=" + App.Server.NamespaceDataUser + "\n" +
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
                
                long mem1 = GC.GetTotalMemory(false) / 1024; Cache.Clear();
                long mem2 = GC.GetTotalMemory(true)  / 1024;
                ListOutput(3, "Memory    ", string.Format("{0} kByte (minimum is {1} kByte)",
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
                if(mbox.Name == Cache.CurrentMailbox.Name)
                    msg = string.Format("{0} (current {1})", msg,
                          Cache.CurrentMailbox.ReadOnly ? "Read-Only" : "Writable");
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
                ZIMapApplication.MailInfo[] mails = Cache.Headers;
                
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
                {   if(cmd.Result.Literals.Length != 1) return false;
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
