//==============================================================================
// TextTool.cs implements a table formatter
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU Lesser General Public License
// This software is published under the GNU LGPL license. Please refer to the
// files COPYING and COPYING.LESSER for details. Please use the following e-mail
// address to contact me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace ZTool
{
    //==========================================================================
    // TextTool class - Routines to output Tables and Lists
    //==========================================================================

    public class TextTool
    {
        /// <summary>A value of <c>true</c> causes the <see cref="FormatterAscii" /> to be used.</summary>
        public static bool          UseAscii;
        /// <summary>Use the environment variable <c>COLUMNS</c> if set to <c>true</c>.</summary>
        public static bool          AutoWidth = true;
        /// <summary>Allows to override the maximum output width (for line wrapping or truncation).
        ///          The default value of <c>0</c> stands for an infinite width.</summary>
        public static uint          TextWidth;
        /// <summary>An optional string prefix for each output line.</summary>
        public static string        Prefix;
        /// <summary>The formatter to used, set by <see cref="GetDefaultFormatter"/>.</summary>
        public static IFormatter    Formatter;
        /// <summary>Value passed to <see cref="TextTool.Write(uint, string)"/>.</summary>
        public static uint          WriteMode;

        /// <summary>Controls the horizontal alignment of text.</summary>
        public enum Adjust
        {   /// <summary>No alignment or padding occurs.</summary>
            None,
            Left,
            Right,
            Center 
        };
        
        /// <summary>Used to build a bitmask for selecting decoration items.</summary>
        public enum Decoration  
        {   
            None   = 0,
            Column = 1,
            Single = 2,
            Double = 4
        }
        
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

        // =============================================================================
        // TextTool Callback Interface
        // =============================================================================

        public interface IFormatter
        {
            char   GetBadChar();            // like "?"
            char   GetLine1();              // like "-"
            char   GetLine2();              // like "="
            string GetCross1();             // like "-+-" (Header)
            string GetCross2();             // like "-+-" (Footer)
            string GetIndexSeparator();     // like " | "
            string GetColumnSeparator();    // like "  " (2 spaces)
            string GetEllipses();           // like "..."
            void   WriteLine(string line);
        }
        
        // Unicode Formatter (Table 37, DOS block drawing chars)
        public class FormatterUnicode : IFormatter
        {
            public virtual char   GetBadChar()             {   return '█';     }
            public virtual char   GetLine1()               {   return '─';     }
            public virtual char   GetLine2()               {   return '═';     }
            public virtual string GetCross1()              {   return "─┼─";   }
            public virtual string GetCross2()              {   return "─┴─";   }
            public virtual string GetIndexSeparator()      {   return " │ ";   }
            public virtual string GetColumnSeparator()     {   return "  ";    }
            public virtual string GetEllipses()            {   return "►";     }
            public virtual void   WriteLine(string line)   {   LineTool.Write(TextTool.WriteMode, Prefix + line);    }
        }
        
        // ASCII Formatter
        public class FormatterAscii : IFormatter
        {
            public virtual char   GetBadChar()             {   return '?';     }
            public virtual char   GetLine1()               {   return '-';     }
            public virtual char   GetLine2()               {   return '=';     }
            public virtual string GetCross1()              {   return "-+-";   }
            public virtual string GetCross2()              {   return "-+-";   }
            public virtual string GetIndexSeparator()      {   return " | ";   }
            public virtual string GetColumnSeparator()     {   return "  ";    }
            public virtual string GetEllipses()            {   return "...";   }
            public virtual void   WriteLine(string line)   {   LineTool.Write(TextTool.WriteMode, Prefix + line);    }
        }
        
        public static IFormatter GetDefaultFormatter()
        {   if(AutoWidth) TextWidth = LineTool.WindowWidth(0);
            if(TextWidth == 0) TextWidth = 80;
            uint upre = 0;
            if(Prefix != null) upre = (uint)Prefix.Length;
            if(upre < TextWidth) TextWidth -= upre;
         
            if(Formatter != null) return Formatter;
            WriteMode = LineTool.Modes.Normal; 
            Formatter = UseAscii ? (IFormatter)(new FormatterAscii()) 
                                 : (IFormatter)(new FormatterUnicode());
           return Formatter;
        }
        
        // =============================================================================
        // 
        // =============================================================================

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
                    string txt =  (c == null) ? "" : c.ToString();
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

            // Helper to format a single table row
            private string Format(StringBuilder sb, string csep, string elip, uint[] widths, string[] cells)
            {   if(cells == null) return null;
                int isep = (IndexMode > 0) ? 1 : 0;
                int iend = cells.Length - 1;
                sb.Length = 0;
                
                // Do the formatting
                for(int irun=0; irun <= iend; irun++)
                {   if(irun > isep) sb.Append(csep);
                    Adjust tadj = Adjust.Left;
                    if(Columns[irun].RigthAlign) tadj = Adjust.Right;
                    else if(irun >= iend)        tadj = Adjust.None;
                    sb.Append(TextAdjust(cells[irun], widths[irun], tadj, elip)); 
                }
                return sb.ToString();
            }
            
            /// <summary>
            /// Format a table
            /// </summary>
            /// <returns>
            /// An array of strings containing the formatted rows.
            /// </returns>
            /// <remarks>For a table row that has a value of <c>null</c> a separator line
            /// should be written to output.  This condition is indicated by a <c>null</c>
            /// string in the output array. 
            /// </remarks>
            public string[] Format(bool autoGrow)
            {   if(formatted != null)
                    return formatted;
                if(Formatter == null) GetDefaultFormatter();
                
                int icnt = (rows == null) ? 0 : rows.Count;
                formatted = new string[icnt];
                int icol = Columns.Length;
                
                // Find column widths
                uint[] widths = new uint[icol];
                                                    // decoration width
                uint usum = (uint)Formatter.GetIndexSeparator().Length;
                usum += (uint)((icol-1) * Formatter.GetColumnSeparator().Length);
                if(IndexMode < 0) usum += (uint)icnt.ToString().Length;
                                                    // default for cols width
                uint udef = (uint)((TextTool.TextWidth + icol - 1) / icol);
                bool truc = false;                  // MaxWidth truncation flag
                
                for(int irun=0; irun < icol; irun++)
                {   if(Columns[irun].MaxWidth > 0)  // fixed size column
                    {   widths[irun] = Columns[irun].MaxWidth;
                        if(Columns[irun].MinWidth > widths[irun]) truc = true;
                        usum += Columns[irun].MaxWidth;
                    }
                    else                            // variable size
                    {   widths[irun] = Columns[irun].MinWidth;
                        usum += Math.Min(udef, widths[irun]);  
                    }
                }

                // only grow if enabled and something was truncated ....
                if(autoGrow && truc && usum < TextWidth)
                {   uint udif = TextWidth - usum;   // size to grow by
                    uint ulas = udif + 1;
                    while(udif > 0 && udif < ulas)  // stop at saturation
                    {   ulas = udif;
                        for(int irun=0; irun < icol; irun++)
                            if(Columns[irun].MaxWidth == 0)
                            {   if(widths[irun] > udef)
                                {   widths[irun] += 1; udif--; 
                                }
                                if(widths[irun] > udef && udef > 0)
                                {   widths[irun] += 1; udif--; 
                                }
                            }
                            else if(Columns[irun].MinWidth > widths[irun])
                            {   widths[irun] += 1; udif--; 
                            }
                    }
                }
                
                // Format the table body
                icnt=0;
                StringBuilder sb = new StringBuilder();
                string csep = Formatter.GetColumnSeparator();
                string elip = Formatter.GetEllipses();
                if(rows != null) foreach(string[] cells in rows)
                    formatted[icnt++] = Format(sb, csep, elip, widths, cells);

                // Format header and footer
                string[] more;
                more = header as string[];
                if(more != null) header = Format(sb, csep, elip, widths, more);
                more = footer as string[];
                if(more != null) footer = Format(sb, csep, elip, widths, more);

                rows = null;
                return formatted;
            }

            public void PrintTable()
            {   PrintTable(0);
            }
            
            public void PrintTable(int maxwidth)
            {   Format(maxwidth == 0);
                string header = this.header as string;
                string footer = this.footer as string;
                this.footer = this.header = null;
                
                int index = IndexMode;
                if(IndexMode > 0) 
                {   index = (int)Columns[0].MaxWidth;
                    if(index <= 0) index = (int)Columns[0].MinWidth;
                }
                TextTool.PrintTable(index, header, formatted, maxwidth, footer);
                formatted = null;
            }
        }
        
        // =====================================================================
        // static methods
        // =====================================================================

        /// <summary>
        /// Create a left or right aligned, space padded string of fixed width.
        /// </summary>
        /// <param name="text">
        /// The text to be formatted (<c>null</c> is ok).
        /// </param>
        /// <param name="width">
        /// Size of the returned string.
        /// </param>
        /// <param name="adjust">
        /// Controls what happens to the string. All values except <c>Adjust.None</c>
        /// will expand the string to the size given by <c>width</c>.
        /// </param>
        /// <param name="ellipses">
        /// If <c>null</c> nothing happens if <c>text</c> is longer than <c>width</c>.
        /// An empty value will clip <c>text</c> and any other value will be appended to
        /// a clipped <c>text</c>.
        /// </param>
        /// <returns>
        /// The formatted string.
        /// </returns>
        public static string TextAdjust(string text, uint width, Adjust adjust, string ellipses)
        {   int ipad = 0;
            int icnt = (text == null) ? 0 : text.Length;
            int itxt = icnt;
            if(width == 0) width = TextWidth;
            if(icnt >= (int)width) icnt = (int)width;
            else                   ipad = (int)width - icnt;
            StringBuilder sb = new StringBuilder((int)width);
            
            if(ipad > 0)                        // padding for Right and Center
            {   if(adjust == Adjust.Center)
                {   int ilef = ipad / 2; ipad -= ilef;
                    if(ilef > 0) sb.Append(' ', ilef);
                }
                else if(adjust == Adjust.Right)
                    sb.Append(' ', ipad);
            }
            
            if(itxt > 0)                        // has text output ...
            {   int irun = sb.Length;
                if(itxt <= (int)width || ellipses == null)
                    sb.Append(text, 0, itxt);   // no truncation
                else if(ellipses.Length >= (int)width)
                    sb.Append(ellipses, 0, (int)width);
                else
                {   sb.Append(text, 0, icnt-ellipses.Length);
                    sb.Append(ellipses);
                }
                
                int iend = sb.Length;
                while(irun < iend)              // remove control chars
                {   if(sb[irun] < ' ') sb[irun] = GetDefaultFormatter().GetBadChar();
                    irun++;
                }
            }

            if((adjust == Adjust.Left || adjust == Adjust.Center) && ipad > 0)
                sb.Append(' ', ipad);
            return sb.ToString();
        }
        
        static public void PrintAdjust(uint indent, uint maxcol, Adjust adjust, Decoration deco, string text)
        {   if(Formatter == null) GetDefaultFormatter();
            if(maxcol == 0) maxcol = TextWidth;
            string sepc = null;
            uint   sepl = 0;
            if(indent > 0 && (deco & Decoration.Column) != 0)
            {   sepc = Formatter.GetIndexSeparator();
                sepl = (uint)sepc.Length;
            }
            if(indent+sepl >= maxcol) indent = 0;
            
            string left = null;
            if(indent > 0) 
            {   if(string.IsNullOrEmpty(text))
                {   left = "".PadRight((int)indent);  text = "";
                }
                else if(text.Length == indent)
                {   left = text;
                    text = "";
                }
                else if(text.Length <= indent)
                {   left = text.PadRight((int)indent);
                    text = "";
                }
                else
                {   left = text.Substring(0, (int)indent);
                    text = text.Substring((int)indent);
                }
                left += sepc;
            }
            
            uint maxc = maxcol - (indent+sepl);
            string line;
            if(adjust != Adjust.None || text.Length > maxc) 
                line = TextAdjust(text, maxcol-(indent+sepl), adjust, Formatter.GetEllipses());
            else
                line = text;
            if(indent > 0) line = left + line;

            Formatter.WriteLine(line);
            if(deco != Decoration.None && deco != Decoration.Column)
                Formatter.WriteLine(DecoLine(deco, indent, maxcol));
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
        public static int PrintTable(int index, 
                                     string header, string[] entries, int maxwidth, string total)
        {   // get deco ...
            if(Formatter == null) GetDefaultFormatter();
            string sepindex = (index == 0) ? null : Formatter.GetIndexSeparator();
            int    sepwidth = (sepindex == null) ? 0 : sepindex.Length;
            string cross = Formatter.GetCross1();

            // update maxwidth - find the longest entry
            if(maxwidth == 0)
            {   int longest = 0;
                foreach(string s in entries)
                    if(s != null) longest = Math.Max(longest, s.Length);
                longest += sepwidth;            
                if(longest > TextTool.TextWidth) maxwidth = (int)TextTool.TextWidth;                
            }
            
            // empty array as default
            if(entries == null) entries = new string[0];
            
            // create header text
            if(header == null) header = "{0} entries";
            header = String.Format(header, entries.Length);
            
            // find minimum width
            int idxwidth = (index >= 0) ? index : String.Format("{0}", entries.Length).Length;
            int mintext = 8;
            int hdrleng = header.Length;
            if(index > 0) hdrleng -= index;
            if(hdrleng > mintext) mintext = hdrleng;
            int minmax = idxwidth + sepwidth;
            if(maxwidth > 0 && minmax + 4 > maxwidth) maxwidth = minmax + 4;
            int maxtext = (maxwidth > 0) ? maxwidth - minmax : 0;
            
            // find max used width
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
            maxwidth = idxwidth + sepwidth + maxtext;
            
            // now print a header ...
            StringBuilder sb = new StringBuilder();
            string line; 
            if(index != 0)
            {   if(index > 0)
                {   line = header.Substring(0, idxwidth);
                    header = header.Substring(idxwidth);
                }
                else 
                    line = TextAdjust("", (uint)idxwidth, Adjust.Left, null);
                Formatter.WriteLine(string.Format("{0}{1}{2}", line, sepindex, header));
                sb.Append(Formatter.GetLine1(), idxwidth);
                sb.Append(cross);
            }
            else
                Formatter.WriteLine(header);
                
            sb.Append(Formatter.GetLine1(), maxtext);
            Formatter.WriteLine(sb.ToString());

            // and the list ...
            string ellipses = Formatter.GetEllipses();
            int irun = 0;
            foreach(string s in entries)
            {   sb.Length = 0;
                if(s == null)            
                {   if(index != 0)   
                    {   sb.Append(Formatter.GetLine1(), idxwidth);
                        sb.Append(cross);
                    }
                    sb.Append(Formatter.GetLine1(), maxtext);
                }
                else
                {   line = s;   
                    if(index != 0)   
                    {   if(index == -2)   
                            sb.Append(TextAdjust((irun++).ToString(), (uint)idxwidth, Adjust.Right, null));
                        else if(index < 0)
                            sb.Append(TextAdjust((++irun).ToString(), (uint)idxwidth, Adjust.Right, null));
                        else
                        {   sb.Append(line.Substring(0, idxwidth));
                            line = line.Substring(idxwidth);
                        }
                        sb.Append(sepindex);
                    }
                    if(line.Length <= maxtext)
                        sb.Append(line);
                    else
                        sb.Append(TextAdjust(line, (uint)maxtext, Adjust.None, ellipses));
                }
                Formatter.WriteLine(sb.ToString());
            }
            if(total != null)
            {   sb.Length = 0;
                if(index != 0)   
                {   sb.Append(Formatter.GetLine1(), idxwidth);
                    sb.Append(Formatter.GetCross2());
                }
                sb.Append(Formatter.GetLine1(), maxtext);
                Formatter.WriteLine(sb.ToString());
                if(total != "")
                {   if(index > 0)
                    {   Formatter.WriteLine(total.Substring(0, idxwidth) +
                                 "".PadLeft(sepwidth) + total.Substring(idxwidth));
                    }
                    else                    
                        Formatter.WriteLine("".PadLeft(idxwidth+sepwidth) + total);
                    sb.Length = 0;
                    sb.Append(Formatter.GetLine2(), maxwidth);
                    Formatter.WriteLine(sb.ToString());
                }
            }
            return maxwidth;
        }

        public static uint FindBreak(string text, uint start, uint maxlen)
        {   if(string.IsNullOrEmpty(text)) return 0;
            uint ulen = (uint)text.Length;
            if(ulen <= start) return 0;
            uint umax = maxlen + start;
            if(umax >= ulen) return ulen - start;            
            
            for(int irun=(int)umax; irun > start; irun--)
                if(text[irun-1] <= ' ') return (uint)irun - start;
            
            // does not fit, return required size
            for(int irun=(int)umax; irun < ulen; irun++)
                if(text[irun] <= ' ') return (uint)irun - start;
            return ulen - start;
        }
        
        public static string DecoLine(Decoration deco, uint indent, uint width)
        {   if(Formatter == null) GetDefaultFormatter();
            bool bColumns = (deco & Decoration.Column) != 0;    // column separator

            char fill = (char)0;
            if     ((deco & Decoration.Single) != 0) fill = Formatter.GetLine1();
            else if((deco & Decoration.Double) != 0) fill = Formatter.GetLine2();
            
            StringBuilder sb = new StringBuilder((int)width);
            if(bColumns)
            {   string sepa = null;
                if(fill == 0)
                {   fill = ' ';
                    sepa = Formatter.GetColumnSeparator();
                }
                else if((deco & Decoration.Single) != 0)
                    sepa = Formatter.GetCross1();
                else
                {   fill = Formatter.GetLine1();
                    sepa = Formatter.GetCross2();
                }
                if(indent > 0) 
                {   sb.Append(fill, (int)Math.Min(indent, width));
                    width = (indent >= width) ? 0 : (width - indent);
                }

                uint usep = (uint)sepa.Length;
                sb.Append(sepa, 0, (int)Math.Min(usep, width));
                width = (usep >= width) ? 0 : (width - usep);
                if(width > 0) sb.Append(fill, (int)width);
            }
            else
            {   if(fill == 0) fill = ' ';
                sb.Append(fill, (int)width);
            }
            return sb.ToString();
        }    

        public static string[] TextIndent(string text, int indent, uint width)
        {   if(text == null) text = "";
            if(width == 0) width = TextWidth;
            
            uint uind = (uint)Math.Abs(indent);             // indentation width
            if(uind >= width) uind = 0;
            uint utxt = width;                              // text width
            if(indent > 0) utxt -= uind;
            
            List<string> lines = new List<string>();
            StringBuilder sb = new StringBuilder();
            if(indent < 0) sb.Append(' ', (int)uind);
           
            uint urow = 1;
            uint umax = utxt;
            if(indent < 0) umax -= uind;
            if(indent > 0) umax  = width;
            for(uint urun=0; ; urow++, umax=utxt)
            {   uint ubrk = FindBreak(text, urun, umax);
                if(ubrk == 0) break;

                // search for a new-line ...
                for(uint ulin=0; ulin < ubrk; ulin++)
                {   if(text[(int)(urun+ulin)] != '\n') continue;
                    ubrk = ulin;
                    break;
                }
                
                sb.Append(text, (int)urun, (int)ubrk);
                
                urun += ubrk;
                while(urun < text.Length && text[(int)urun] <= ' ') urun++;
                lines.Add(sb.ToString());                
                sb.Length = 0;
                if(indent > 0) sb.Append(' ', indent);
            }
            return lines.ToArray();
        }
        

        /*
            TextTool.PrintAdjust( 9, 0,  TextTool.Adjust.Center, TextTool.Decoration.Column,
                                 "123456789Demonstration of a very cool Tool");
            TextTool.PrintIndent( 9, 0, TextTool.Decoration.Single | TextTool.Decoration.Column,
                                 "Lines    Wenn ich einmal reich bin,\ndudel diedel dudel dumm.\n" +
                                 "Eins und eins das macht zwei - die nächste Zahl ist drei!");
            TextTool.PrintIndent( 9, 0, TextTool.Decoration.Single | TextTool.Decoration.Column,
                                 "Hallo    Wenn ich einmal reich bin, dudel diedel dudel dumm." +
                                 "Eins und eins das macht zwei - die nächste Zahl ist drei!\n" +
                                 "Und der Quatsch geht immer noch weiter.");
            TextTool.PrintIndent( 9, 0, TextTool.Decoration.Single | TextTool.Decoration.Column,
                                 "Zwei     die Zahl nach eins Donaudampfschiffahrtsgesellschaft");
            TextTool.PrintIndent( 9, 0, TextTool.Decoration.Single | TextTool.Decoration.Double | TextTool.Decoration.Column,
                                 "Drei     noch eine");
        */
        public static void PrintIndent(int indent, uint width, Decoration deco, string text) 
        {   if(Formatter == null) GetDefaultFormatter();
            if(width == 0) width = TextWidth;
            
            if((deco & Decoration.Single) != 0) 
                Formatter.WriteLine(DecoLine(deco, (uint)Math.Abs(indent), width));
            if(text != null)
            {   uint uwid = width;
                uint usep = 0;
                if((deco & Decoration.Column) != 0)
                {   usep = (uint)Formatter.GetIndexSeparator().Length;
                    if(indent+usep >= uwid) usep = indent = 0;
                    else uwid -= usep;
                }
                    
                string[] lines = TextIndent(text, indent, uwid);
                foreach(string line in lines)
                {   if(usep > 0)
                        PrintAdjust((uint)Math.Abs(indent), width, Adjust.None, Decoration.Column, line);
                    else
                        Formatter.WriteLine(line);
                }
            }
            if((deco & Decoration.Double) != 0) 
                Formatter.WriteLine(DecoLine(Decoration.Double | (deco & Decoration.Column), (uint)Math.Abs(indent), width));
        }
        

        /*            
            string[] list =
            {   "Anfang " + "Wenn ich einmal reich bin,\ndudel diedel dudel dumm.\n" +
                            "Eins und eins das macht zwei - die nächste Zahl ist drei!",
                            null,
                "Hallo  " + "Wenn ich einmal reich bin, dudel diedel dudel dumm. " +
                            "Eins und eins das macht zwei - die nächste Zahl ist drei!\n" +
                            "Und der Quatsch geht immer noch weiter.",
                            null,
                "Zwei   " + "die Zahl nach eins Donaudampfschiffahrtsgesellschaft",
                "Drei   " + "noch eine"
            };
            TextTool.PrintList(7, 0, true, true, "Feine Listen ganz einfach", true, list);
        */
        
        public static void PrintList(uint indent, uint width, bool decoLines, bool decoCols, 
                                     string header, bool centerHeader, string[] entries)
        {   if(Formatter == null) GetDefaultFormatter();
            if(width == 0) width = TextWidth;

            Decoration decoLine  = decoCols ? Decoration.Column : Decoration.None;
            Decoration decoBreak = decoLine;
            Decoration decoLast  = decoLine;
            if(decoLines) decoBreak |= Decoration.Single;
            if(decoLines) decoLast  |= Decoration.Double;
            
            if(header == "")
                Formatter.WriteLine(DecoLine(decoBreak, indent, width));
            else if(header != null)
            {   if(indent > 0) header = "".PadRight((int)indent) + header;
                PrintAdjust(indent, width, centerHeader ?  Adjust.Center : Adjust.None, decoBreak, header);
            }
            uint ucnt = (entries == null) ? 0 : (uint)entries.Length;
            for(uint urun=0; urun < ucnt; urun++)
            {   string entry = entries[urun];
                if(entry == null)
                    Formatter.WriteLine(DecoLine(decoBreak, indent, width));
                else                    
                    PrintIndent((int)indent, width, (urun+1 < ucnt) ? decoLine : decoLast, entry);
            }

        }
    }
}
