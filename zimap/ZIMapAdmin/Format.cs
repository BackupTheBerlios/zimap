//==============================================================================
// Format.cs    The ZLibAdmin output formatting
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
        // =============================================================================
        // Tools        
        // =============================================================================

        public static bool ListMailHeaders(ZIMapCommand.Fetch.Item[] items, uint index, uint offset, bool skipX)
        {   if(items == null) return false;
            
            ZIMapRfc822 mail = new ZIMapRfc822();
            if(!mail.Parse(items[index].Literal)) return false;
            
            TextTool.TableBuilder tb = GetTableBuilder(2); 
            tb.Columns[1].MaxWidth = 68;
            tb.Columns[1].RigthAlign = false;

            string[] names = mail.FieldNames;
            for(int irun=0; irun < names.Length; irun++)
            {   if(skipX && names[irun].StartsWith("X-")) continue;
                tb.AddRow(names[irun], mail.FieldValue(irun));
            }
            tb.Header("Headers for mail #" + (index + offset));
            tb.Footer("");
            tb.PrintTable();
            return true;
        }
        
        // TODO: ListMailboxes header text argument          
        public static bool ListMailboxes(ZIMapApplication.MailBox[] mailboxes, bool bUsers,
                                         bool bSubscr, bool bDetail, bool bRights, bool bQuota)
        {   if(mailboxes == null) return false;
            bool nbox = mailboxes.Length > 0;
            if(nbox && bSubscr)
            {   nbox = false;
                foreach(ZIMapApplication.MailBox mb in mailboxes) 
                    if(mb.Subscribed) {  nbox = true; break; }   
            }
            if(!nbox)
            {   if(!bSubscr && !bUsers && !string.IsNullOrEmpty(ListPrefix))
                    Message("No mailboxes, prefix '{0}'", ListPrefix);
                else
                    Message(bSubscr ?  "No subscribed mailboxes" : 
                            bUsers  ? "No users" : "No mailboxes");
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
            else if(string.IsNullOrEmpty(ListPrefix))
                data[ucol] = "Mailboxes";
            else
                data[ucol] = string.Format("Mailboxes ('{0}')", ListPrefix);
            table.Columns[ucol++].MinWidth = 34;    // name

            string nsOther  = null;
            string nsShared = null;
            string nsCurr   = null;
            if(!bUsers)
            {   nsOther  = App.Server.NamespaceDataOther.Prefix;
                nsShared = App.Server.NamespaceDataShared.Prefix;
                if(nsOther != null && nsShared != null) nsCurr = "";
            }
            
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

            // print a list ...
            uint nmsg = 0;
            uint nrec = 0;
            uint nuns = 0;
            uint scnt = 0;

            table.Header(data);
            string subs = Ascii ? "*" : "■";  // HACK: for mono no "✓";
            for(uint irun = 0; irun < mailboxes.Length; irun++) 
            {   if(bSubscr && !mailboxes[irun].Subscribed) continue;

                string name = mailboxes[irun].Name; 
                if(nsCurr != null)
                {   if(name.StartsWith(nsOther) && nsCurr != nsOther)
                    {   if(nsCurr != "") table.AddSeparator();
                        nsCurr = nsOther;
                    }
                    else if(name.StartsWith(nsShared) && nsCurr != nsShared)
                    {   if(nsCurr != "") table.AddSeparator();
                        nsCurr = nsShared;
                    }
                    else if(nsCurr == "")
                        nsCurr = ".";
                }
                
                CacheData.MailboxExtra extra = (bRights || bQuota) ?
                        Cache.GetMailboxExtra(ref mailboxes[irun], bRights, bQuota) : null;
                
                ucol = 0;
                if(bUsers)
                {   int isep = name.IndexOf(App.Server.NamespaceDataOther.Delimiter);
                    if(isep >= 0) name = name.Substring(isep+1);
                    if(name == "INBOX") name = Account + " [INBOX]";
                    data[ucol++] = name;
                }
                else if(nsCurr == "" || nsCurr == ".")
                {   if(name == "INBOX") name = Account + " [INBOX]";
                    else                name = Account + "." + name;
                    data[ucol++] = name;
                }
                else
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
                tb.Columns[ucol++].MaxWidth = 54;
            }
            tb.Header(data);

            ZIMapRfc822 mail = new ZIMapRfc822();
            for(int irun = 0; irun < mails.Length; irun++)
            {   if(!mail.Parse(mails[irun].Literal))
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
    }
}
