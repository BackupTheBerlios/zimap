//==============================================================================
// TextTool.cs implements a table formatter
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU Lesser General Public License
// This software is published under the GNU LGPL license. Please refer to the
// files COPYING and COPYING.LESSER for details. Please use the following e-mail
// address to contact me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.Text;
using System.Collections.Generic;

namespace ZTool
{
    //==========================================================================
    // TextTool class - A table formatter
    //==========================================================================

    /*
            TextTool.TableBuilder tb = new TextTool.TableBuilder(5);
            tb.Columns[2].RigthAlign = false;
            tb.AddRow("Bratwurst mit Brötchen",   3.20M, "extra Senf", 0.30M);
            tb.AddRow("Rindswurst mit Brötchen",  3.80M, "extra Senf", 0.30M); //, "extra Curry-Soße", 0.70M);
            tb.AddRow("Thüringer mit Sauerkraut", 4.20M, DateTime.Now);
            tb.Header("Hier ist unser Angebot an köstlichen Speisen");
            tb.Footer("Wir wünschen: Guten Appetit!");
            tb.PrintTable();
            
            string [] liste = { "eins", "zwei drei", "und noch viel mehr" };
            TextTool.PrintTable(null, liste, 0);
            
    */

    public class TextTool
    {
        public interface IFormatter
        {
            char   GetLine1();              // like "-"
            char   GetLine2();              // like "="
            string GetCross1();             // like "-+-" (Header)
            string GetCross2();             // like "-+-" (Footer)
            string GetIndexSeparator();     // like " | "
            string GetColumnSeparator();    // like "  " (2 spaces)
            void WriteLine(string line);
        }
        
        // Default Formatter
        public class DefaultFormatter : IFormatter
        {
            public virtual char   GetLine1()               {   return '-';     }
            public virtual char   GetLine2()               {   return '=';     }
            public virtual string GetCross1()              {   return "-+-";   }
            public virtual string GetCross2()              {   return "-+-";   }
            public virtual string GetIndexSeparator()      {   return " | ";   }
            public virtual string GetColumnSeparator()     {   return "  ";    }
            public virtual void WriteLine(string line)     {   Console.WriteLine(line);    }
        }
        
        public struct ColumnInfo
        {   public  uint        MaxWidth;
            public  uint        MinWidth;
            public  bool        RigthAlign;
        }
        
        public class TableBuilder
        {
            public  ColumnInfo[]    Columns;
            public  int             IndexMode = -1;
            
            private List<string[]>  rows;
            private string[]        formatted;
            private object          header;
            private object          footer;
            private IFormatter      formatter;
            
            public TableBuilder(uint columNumber)
            {   if(columNumber < 1) columNumber = 1;
                Columns = new ColumnInfo[columNumber];
                for(int irun=1; irun < columNumber; irun++)
                    Columns[irun].RigthAlign = true;
            }
            
            public void Header(params object[] cells)
            {   if(cells == null || cells.Length < 1) return;
                if(cells.Length == 1)
                {   if(cells[0] == null) return;
                    header = cells[0].ToString();
                }
                else
                {   header = AddRow(cells);
                    rows.Remove((string[])header);
                }
            }
            
            public void Footer(params object[] cells)
            {   if(cells == null || cells.Length < 1) return;
                if(cells.Length == 1)
                {   if(cells[0] == null) return;
                    footer = cells[0].ToString();
                }
                else
                {   footer = AddRow(cells);
                    rows.Remove((string[])footer);
                }
            }
            
            public string[] AddRow(params object[] cells)
            {   formatted = null;   
                if(cells == null) return null;
                if(rows == null) rows = new List<string[]>();
                int icol = Columns.Length;
                int ient = Math.Min(icol, cells.Length);
                string[] row = new string[ient];
                for(int irun=0; irun < cells.Length; irun++)
                {   object c = cells[irun];
                    string txt = (c == null) ? "" : c.ToString();
                    int icur = Math.Min(icol-1, irun);
                    if(irun < icol) row[icur] = txt;
                    else            row[icur] += " " + txt;
                    if(row[icur].Length > Columns[icur].MinWidth)
                        Columns[icur].MinWidth = (uint)row[icur].Length;
                }
                rows.Add(row);
                return row;
            }
            
            public void AddArray(Array array)
            {   if(array == null) return;
                foreach(object o in array)
                {   if(o == null) continue;
                    if(o is Array)
                        AddRow((object[])o);
                    else
                        AddRow(o);
                }
            }
            
            public void AddSeparator()
            {   rows.Add(null);
            }
            
            public string[] Format()
            {   if(formatted != null)
                    return formatted;
                
                int icnt = (rows == null) ? 0 : rows.Count;
                formatted = new string[icnt];
                int icol = Columns.Length;
                
                // Find column widths
                uint[] widths = new uint[icol];
                for(int irun=0; irun < icol; irun++)
                {   if(Columns[irun].MaxWidth > 0)
                        widths[irun] = Columns[irun].MaxWidth;
                    else
                        widths[irun] = Columns[irun].MinWidth;
                }
                
                // Do the formatting
                icnt=0;
                StringBuilder sb = new StringBuilder();
                string csep = Formatter.GetColumnSeparator();
                int isep = (IndexMode > 0) ? 1 : 0;
                if(rows != null) foreach(string[] cells in rows)
                {   if(cells == null)
                    {   formatted[icnt++] = null;
                        continue;
                    }
                    sb.Length = 0;
                    for(int irun=0; irun < cells.Length; irun++)
                    {   if(irun > isep) sb.Append(csep);
                        sb.Append(AdjustInCell(cells[irun], widths[irun], 
                                  Columns[irun].RigthAlign));
                    }
                    formatted[icnt++] = sb.ToString();
                }

                // Format header and footer
                string[] more;
                more = header as string[];
                if(more != null)
                {   sb.Length = 0;
                    for(int irun=0; irun < more.Length; irun++)
                    {   if(irun > isep) sb.Append(csep);
                        sb.Append(AdjustInCell(more[irun], widths[irun], 
                                  Columns[irun].RigthAlign));
                    }
                    header = sb.ToString();
                }
                
                more = footer as string[];
                if(more != null)
                {   sb.Length = 0;
                    for(int irun=0; irun < more.Length; irun++)
                    {   if(irun > isep) sb.Append(csep);
                        sb.Append(AdjustInCell(more[irun], widths[irun], 
                                  Columns[irun].RigthAlign));
                    }
                    footer = sb.ToString();
                }
                    
                rows = null;
                return formatted;
            }

            public void PrintTable()
            {   PrintTable(formatter, 0);
            }
            
            public void PrintTable(IFormatter formatter, int maxwidth)
            {   Format();
                string header = this.header as string;
                string footer = this.footer as string;
                this.footer = this.header = null;
                int index = IndexMode;
                if(IndexMode > 0) 
                {   index = (int)Columns[0].MaxWidth;
                    if(index <= 0) index = (int)Columns[0].MinWidth;
                }
                TextTool.PrintTable(formatter, index, header, formatted, maxwidth, footer);
                formatted = null;
            }
            
            public IFormatter Formatter
            {   get {   if(formatter == null)
                            formatter = new DefaultFormatter();
                        return formatter;   }
                set {   formatter = value;  }
            }
        }
        
        // =====================================================================
        // static methods
        // =====================================================================

        public static string AdjustInCell(string text, uint cellWidth, bool bRight)
        {   uint ipad = 0;
            uint icnt = (uint)text.Length;            
            if(icnt >= cellWidth) icnt = cellWidth;
            else                  ipad = cellWidth - icnt;
            StringBuilder sb = new StringBuilder((int)cellWidth);
            if(bRight) sb.Append(' ', (int)ipad);
            for(int irun=0; irun < icnt; irun++)
            {   char c = text[irun];
                sb.Append(c < ' ' ? '§' : c);
            }
            if(!bRight) sb.Append(' ', (int)ipad);
            return sb.ToString();
        }
        
        /// <summary>
        /// Outputs a string array as a formatted table.
        /// </summary>
        /// <param name="formatter">
        /// An optional output formatter (see <see cref="IFormatter"/>) or <c>null</c>.
        /// </param>
        /// <param name="index">
        /// Specifies how to print an index column at the left, see remarks.
        /// </param>
        /// <param name="header">
        /// An optional header string or <c>null</c> to print no header. 
        /// </param>
        /// <param name="entries">
        /// The array of table lines. A value of <c>null</c> prints a separator line.
        /// </param>
        /// <param name="maxwidth">
        /// Optional maximum table width, text will be truncated.  Can be zero.
        /// </param>
        /// <param name="total">
        /// A optional footer string or <c>null</c> to print no footer. An empty string
        /// prints just a bottom line.
        /// </param>
        /// <returns>
        /// The maximum used width.
        /// </returns>
        /// <remarks>
        /// For <c>index = 0<0> no index column is printed, <c>-1</c> prints index numbers
        /// starting at 1, <c>-2</c> prints index numbers starting at 0. For positive index
        /// values the index is the count of characters for header and table lines that are
        /// used as index. 
        /// </remarks>
        public static int PrintTable(IFormatter formatter, int index, 
                                     string header, string[] entries, int maxwidth, string total)
        {   if(formatter == null) formatter = new DefaultFormatter();
            
            // empty array as default
            if(entries == null) entries = new string[0];
            
            // create header text
            if(header == null) header = "{0} entries";
            header = String.Format(header, entries.Length);
            
            // find minimum width
            string idxsep = (index == 0) ? null : formatter.GetIndexSeparator();
            int sepwidth = (idxsep == null) ? 0 : idxsep.Length;
            int idxwidth = (index >= 0) ? index : String.Format("{0}", entries.Length).Length;
            int mintext = 8;
            int hdrleng = header.Length;
            if(index > 0) hdrleng -= index;
            if(hdrleng > mintext) mintext = hdrleng;
            int minwidth = idxwidth + sepwidth + mintext;

            if(maxwidth > 0 && minwidth > maxwidth) maxwidth = minwidth;
            int maxtext = (maxwidth > 0) ? maxwidth - (idxwidth + sepwidth) : 0;
            
            // find max used width
            string cross = formatter.GetCross1();
            int width = mintext;
            foreach(string s in entries)
            {   if(s == null) continue;
                int txtwidth = s.Length;
                if(index > 0) txtwidth -= index;
                width = Math.Max(width, txtwidth);
                if(maxtext > 0 && width > maxtext)
                {   width = maxtext;  break;
                }
            }
            if(maxtext <= 0) maxtext = width;
            maxwidth = idxwidth + cross.Length + maxtext;
            
            // now print a header ...
            StringBuilder sb = new StringBuilder();
            string line; 
            if(index != 0)
            {   if(index > 0)
                {   line = header.Substring(0, idxwidth);
                    header = header.Substring(idxwidth);
                }
                else 
                    line = AdjustInCell("", (uint)idxwidth, false);
                formatter.WriteLine(string.Format("{0}{1}{2}", line, idxsep, header));
                sb.Append(formatter.GetLine1(), idxwidth);
                sb.Append(cross);
            }
            else
                formatter.WriteLine(header);
                
            sb.Append(formatter.GetLine1(), maxtext);
            formatter.WriteLine(sb.ToString());

            // and the list ...
            int irun = 0;
            foreach(string s in entries)
            {   sb.Length = 0;
                if(s == null)            
                {   if(index != 0)   
                    {   sb.Append(formatter.GetLine1(), idxwidth);
                        sb.Append(cross);
                    }
                    sb.Append(formatter.GetLine1(), maxtext);
                }
                else
                {   line = s;   
                    if(index != 0)   
                    {   if(index == -2)   
                            sb.Append(AdjustInCell((irun++).ToString(), (uint)idxwidth, true));
                        else if(index < 0)
                            sb.Append(AdjustInCell((++irun).ToString(), (uint)idxwidth, true));
                        else
                        {   sb.Append(line.Substring(0, idxwidth));
                            line = line.Substring(idxwidth);
                        }
                        sb.Append(idxsep);
                    }
                    if(line.Length <= maxtext)
                        sb.Append(line);
                    else
                        sb.Append(AdjustInCell(line, (uint)maxtext, false));
                }
                formatter.WriteLine(sb.ToString());
            }
            if(total != null)
            {   sb.Length = 0;
                if(index != 0)   
                {   sb.Append(formatter.GetLine1(), idxwidth);
                    sb.Append(formatter.GetCross2());
                }
                sb.Append(formatter.GetLine1(), maxtext);
                formatter.WriteLine(sb.ToString());
                if(total != "")
                {   if(index > 0)
                    {   formatter.WriteLine(total.Substring(0, idxwidth) +
                                 "".PadLeft(sepwidth) + total.Substring(idxwidth));
                    }
                    else                    
                        formatter.WriteLine("".PadLeft(idxwidth+sepwidth) + total);
                    sb.Length = 0;
                    sb.Append(formatter.GetLine2(), maxwidth);
                    formatter.WriteLine(sb.ToString());
                }
            }
            return maxwidth;
        }
    }
}
