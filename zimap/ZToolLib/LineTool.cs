//==============================================================================
// LineTool.cs implements command line tools
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU Lesser General Public License
// This software is published under the GNU LGPL license. Please refer to the
// files COPYING and COPYING.LESSER for details. Please use the following e-mail
// address to contact me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ZTool
{
    //==========================================================================
    // LineTool class - Functions to do console input and output
    //==========================================================================
    
    /// <summary>
    /// The class provides functions to do console input and output and to write
    /// to a log file.
    /// </summary>
    /// <remarks>
    /// The class supports GNU readline on Linux, colored output and progress
    /// reporting.
    /// </remarks>
    public static class LineTool
    {
        /// <summary>
        /// Removes leading/trailing spaces, replaces multiple spaces by single
        /// spaces, treats controls chars as spaces.
        /// </summary>
        /// <param name="text">
        /// Input string -or- <c>null</c>
        /// </param>
        /// <returns>
        /// Converted string -or- <c>null</c> (when the input was <c>null</c>).
        /// </returns>
        /// <remarks>
        /// Behind the 1st occurence of a single or double quote the routine
        /// stops to compact multiple spaces to single spaces.
        /// </remarks>
        public static string SimplifyWhiteSpace(string text)
        {   if(text == null || text == "") return text;
            
            // get length, remove trailing spaces
            int ilen = text.Length;
            while(ilen > 0)
                if(text[ilen-1] > 32) break;
                else ilen--;
            if(ilen <= 0) return "";
            
            StringBuilder sb = new StringBuilder();
            bool skip = true;
            bool quot = false;
            for(int irun=0; irun < text.Length; irun++)
            {   char c = text[irun];
                if(c <= 32)
                {   if(skip && !quot) continue;
                    skip = true; c = ' ';
                }
                else if(c == '"' || c == '\'')
                    quot = true;
                else
                    skip = false;
                sb.Append(c);
            }
            return sb.ToString();
        }
        
        /// <summary>
        /// Breaks an input line into words.
        /// </summary>
        /// <param name="line">
        /// The input line -or- <c>null</c>
        /// </param>
        /// <param name="maxFields">
        /// Limit the size of the returned array. Extra input words are
        /// appended to the last array element.
        /// </param>
        /// <returns>
        /// A string array  -or- <c>null</c> (when the input was <c>null</c>). 
        /// </returns>
        /// <remarks>
        /// This routine internally calls <see cref="SimplifyWhiteSpace"/>
        /// </remarks>
        public static string[] Parse(string line, uint maxFields)
        {
            if(line == null) return null;
            else line = SimplifyWhiteSpace(line);
            if(line == "") return null;
            return line.Split(" ".ToCharArray(), (int)maxFields);
        }

        /// <summary>
        /// Prompt on Console for user input.
        /// </summary>
        /// <param name="prompt">
        /// The prompt string.
        /// </param>
        /// <param name="maxFields">
        /// Limit the size of the returned array. Extra input words are
        /// appended to the last array element.
        /// </param>
        /// <returns>
        /// A string array  -or- <c>null</c> (on EOF or an empty input line). 
        /// </returns>
        /// <remarks>
        /// This routine internally calls <see cref="Parse"/>
        /// </remarks>
        public static string[] Prompt(string prompt, uint maxFields)
        {   return Parse(ReadLine(prompt), maxFields);
        }

        /// <summary>
        /// Prompt on Console for user input.
        /// </summary>
        /// <param name="prompt">
        /// The prompt string.
        /// </param>
        /// <returns>
        /// A string -or- <c>null</c> (on EOF or an empty input line). 
        /// </returns>
        /// <remarks>
        /// This routine internally calls <see cref="SimplifyWhiteSpace"/>
        /// </remarks>
        public static string Prompt(string prompt)
        {   return SimplifyWhiteSpace(ReadLine(prompt));
        }

        /// <value>If set <c>true</c> all calls to <see cref="Confirm"/> will
        /// return <c>true</c> without prompting the user.</value>
        public static bool AutoConfirm;
        
        /// <summary>
        /// Prompt on Console for user confirmation.
        /// </summary>
        /// <param name="prompt">
        /// The prompt string.
        /// </param>
        /// <returns>
        /// <c>true</c> if the user typed 'yes' -or- <c>flase</c> for 'no'. 
        /// </returns>
        /// <remarks>
        /// Prompting can be disabled, <see cref="AutoConfirm"/>
        /// </remarks>
        public static bool Confirm(string prompt)
        {   if(AutoConfirm) return true;
            while(true)
            {   string reply = Prompt(prompt + "? [y/N]");
                if(string.IsNullOrEmpty(reply)) return false;
                switch(reply.ToLower())
                {   case "y":
                    case "ye":
                    case "yes": return true;
                    case "n":
                    case "no":  return false;
                }
                Error("Please answer with 'yes' or 'no'!");
            }
        }

        // =====================================================================
        // Output routines
        // =====================================================================
        
        static private void WriteWithPrefix(uint mode, string prefix, string format, object[] args)
        {   StringBuilder sb;
            if(!InitTTY) Initialize();                  // colors enabled?
            uint colf = mode & 0xff;
            bool cmod = (mode & TextAttributes.Continue) != 0;

            // case 1: no color
            if(!EnableColor || colf == 0)
            {   sb = new StringBuilder();
                if(prefix != null)   sb.Append(prefix);
                if(format != null)
                {   if(args == null) sb.Append(format);
                    else             sb.AppendFormat(format, args);
                }
                
                // append spaces to override progress text
                if(!cmod && progressLength > 0)
                {   uint ulen = (uint)sb.Length;
                    if(prefix != null) ulen -= (uint)prefix.Length;
                    if(progressLength > ulen)
                        sb.Append(' ', (int)(progressLength - ulen));
                    progressLength = 0;
                }
            }

            else
            {   // override progress text
                if(!cmod && progressLength > 0) Progress(null, null, null);
                
                // case 2: color with readline support
                if(EnableReadline)
                {   sb = new StringBuilder();
                    if(colf > 256)
                        sb.AppendFormat("\x001b[1;{0}m", colf & 255);
                    else
                        sb.AppendFormat("\x001b[{0}m", colf);
                    if(prefix != null)
                    {   sb.Append(prefix);
                        sb.Append("\x001b[0m");
                    }
                    if(format != null)
                    {   if(args == null) sb.Append(format);
                        else             sb.AppendFormat(format, args);
                    }
                    if(prefix == null)
                        sb.Append("\x001b[0m");
                }

                // case 3: color for console
                else
                {   Console.ForegroundColor = TextAttributes.ToColor(colf);
                    if(prefix != null)
                    {   Console.Write(prefix);
                        Console.ResetColor();
                    }
                    if(format != null)
                    {   if(args == null) Console.Write(format);
                        else             Console.Write(format, args);
                    }
                    if(prefix == null)
                        Console.ResetColor();
                    if((mode & TextAttributes.Continue) == 0)
                        Console.WriteLine();
                    return;
                }
            }
            
            // write (case 1 and 2)
            if(cmod)
                Console.Write(sb.ToString());
            else
                Console.WriteLine(sb.ToString());
        }

        public static void Write(uint mode, string message)
        {   WriteWithPrefix(mode, null, message, null);
        }

        public static void Write(uint mode, string message, params object[] args)
        {   WriteWithPrefix(mode, null, message, args);
        }

        public static void Write(string message)
        {   WriteWithPrefix(Modes.Normal, null, message, null);
        }

        public static void Write(string message, params object[] args)
        {   WriteWithPrefix(Modes.Normal, null, message, args);
        }
        
        public static void Message(string message)
        {   WriteWithPrefix(Modes.Normal, PrefixMessage, message, null);
        }

        public static void Message(string format, params object[] args)
        {   WriteWithPrefix(Modes.Normal, PrefixMessage, format, args);
        }
        
        public static void Error(string message)
        {   WriteWithPrefix(Modes.Alert, PrefixError, message, null);
        }
        
        public static void Error(string format, params object[] args)
        {   WriteWithPrefix(Modes.Alert, PrefixError, format, args);
        }
        
        public static void Info(string message)
        {   WriteWithPrefix(Modes.Info, PrefixInfo, message, null);
        }
        
        public static void Info(string format, params object[] args)
        {   WriteWithPrefix(Modes.Info, PrefixInfo, format, args);
        }
        
        public static void Extra(string message)
        {   WriteWithPrefix(Modes.Extra, PrefixDebug, message, null);
        }
        
        public static void Extra(string format, params object[] args)
        {   WriteWithPrefix(Modes.Extra, PrefixDebug, format, args);
        }

        // =====================================================================
        // Progress reporting
        // =====================================================================

        public static bool EnableProgress = true;
        
        public static uint ProgressBar = 25;

        // length of the current progress output
        private static uint progressLength;

        public static void Progress(string head, uint percent, string tail)
        {   if(percent > 100) percent = 100;
            uint ucnt = (percent * ProgressBar + 99) / 100;
            StringBuilder body = new StringBuilder();
            if(ucnt > 0) body.Append('#', (int)ucnt);
            if(ucnt < ProgressBar && !string.IsNullOrEmpty(tail))
                tail = tail.PadLeft((int)(tail.Length + ProgressBar - ucnt));
            Progress(head, body.ToString(), tail);
        }
            
        public static void Progress(string head, string body, string tail)
        {   uint ulen = 0;
            if(!string.IsNullOrEmpty(head)) ulen += (uint)head.Length;
            if(!string.IsNullOrEmpty(body)) ulen += (uint)body.Length;
            if(!string.IsNullOrEmpty(tail)) ulen += (uint)tail.Length;

            string fill = "";
            if(progressLength > ulen) fill = fill.PadRight((int)(progressLength-ulen));

            if(ulen > 0)
            {   Console.Write("{0}{1}", PrefixMessage, head, ulen, progressLength);
                if(!string.IsNullOrEmpty(body))
                    WriteWithPrefix(Modes.Extra+TextAttributes.Continue, null, body, null);
                Console.Write("{0}{1}\r", tail, fill);
            }
            else
                Console.Write("{0}{1}\r", PrefixMessage, fill);
            progressLength = ulen;
        }

        // =====================================================================
        // Setup and configuration
        // =====================================================================

        /// <summary>
        /// Handle color definitions and conversions between the .NET
        /// <see cref="ConsoleColor"/> type and ANSI terminal attributes.
        /// </summary>
        /// <remarks>
        /// This class is used be the text output functions of <see cref="LineTool"/>
        /// to determine the output color.
        /// </remarks>
        public class TextAttributes
        {
            /// <summary>
            /// Converts from <see cref="ConsoleColor"/> to ANSI terminal attributes.
            /// </summary>
            /// <param name="color">
            /// The .NET color value.
            /// </param>
            /// <returns>
            /// An ANSI value.  The lower 8 bits specify a color attribute and the
            /// bit 0x100 indicates 'bright' mode.
            /// </returns>
            /// <remarks>
            /// Not all <see cref="ConsoleColor"/> values are understood, on error <c>0</c> 
            /// is returned.  See also <see cref="ToColor"/>.
            /// </remarks>
            public static uint ToAnsi(ConsoleColor color)
            {   switch(color)
                {   case ConsoleColor.DarkRed:     return 31;
                    case ConsoleColor.DarkGreen:   return 32;
                    case ConsoleColor.DarkYellow:  return 33;
                    case ConsoleColor.DarkBlue:    return 34;
                    case ConsoleColor.DarkMagenta: return 35;
                    case ConsoleColor.DarkCyan:    return 36;
                    case ConsoleColor.Red:         return 256+31;
                    case ConsoleColor.Green:       return 256+32;
                    case ConsoleColor.Yellow:      return 256+33;
                    case ConsoleColor.Blue:        return 256+34;
                    case ConsoleColor.Magenta:     return 256+35;
                    case ConsoleColor.Cyan:        return 256+36;
                }
                return 0;
            }
            
            /// <summary>
            /// Convert ANSI terminal attributes back to .NET colors.
            /// </summary>
            /// <param name="ansi">
            /// A color/brightness value (see <see cref="ToAnsi"/>).
            /// </param>
            /// <returns>
            /// A .Net <see cref="ConsoleColor"/> value.
            /// </returns>
            /// <remarks>
            /// Only a few ANSI values are understood, on error
            /// <see cref="ConsoleColor.Black"/> is returned. See also <see cref="ToAnsi"/>.
            /// </remarks>
            public static ConsoleColor ToColor(uint ansi)
            {   switch(ansi)
                {   case 31:        return ConsoleColor.DarkRed;
                    case 32:        return ConsoleColor.DarkGreen;
                    case 33:        return ConsoleColor.DarkYellow;
                    case 34:        return ConsoleColor.DarkBlue;
                    case 35:        return ConsoleColor.DarkMagenta;
                    case 36:        return ConsoleColor.DarkCyan;
                    case 256+31:    return ConsoleColor.Red;
                    case 256+32:    return ConsoleColor.Green;
                    case 256+33:    return ConsoleColor.Yellow;
                    case 256+34:    return ConsoleColor.Blue;
                    case 256+35:    return ConsoleColor.Magenta;
                    case 256+36:    return ConsoleColor.Cyan;
                }
                return ConsoleColor.Black;
            }
            
            /// <summary>The color definintion used for normal text output.</summary>
            public uint         Normal = ToAnsi(ConsoleColor.Black);
            /// <summary>Color definintion used for extra (debug) text output.</summary>
            public uint         Extra  = ToAnsi(ConsoleColor.Green);
            /// <summary>Color definintion used for informational text output.</summary>
            public uint         Info   = ToAnsi(ConsoleColor.Blue);
            /// <summary>Color definintion used to output error messages.</summary>
            public uint         Alert  = ToAnsi(ConsoleColor.Red);
            
            /// <summary>Flag indicating that no CRLF should be added to the output.</summary>
            public const uint   Continue = 0x10000;
        }
        
        public static readonly TextAttributes Modes = new TextAttributes();

        /// <summary>Prefix for output with <see cref="Message"/></summary>
        public static string PrefixMessage = "    ";
        public static string PrefixDebug   = "*   ";
        public static string PrefixInfo    = "**  ";
        public static string PrefixError   = "*** ";

        /// <summary>Characters written after the prompt text</summary>
        public static string PromptSuffix  = "> ";
        
        public static bool EnableColor    = true;
        public static bool EnableReadline = true;
        public static bool EnableResize   = true;

        private static bool InitTTY;
        private static bool IsTTY;
        private static bool IsWindows;

        /// <summary>
        /// Gets the output size base on the current screen width
        /// </summary>
        /// <param name="umax">
        /// When non-zero this is the maximum returned value. Used to limit
        /// the output width.
        /// </param>
        /// <returns>
        /// On Windows a Console resize will always be detected, under Linux/Mono
        /// this works only when <see cref="EnableResize"/> is set.  This property
        /// causes a P-Invoke call to a linux ioctl that returns the current console
        /// size.
        /// </returns>
        static public uint WindowWidth(uint umax)
        {   uint uwid = WinColumns();
            if(uwid == 0) uwid = 80;
            if(umax > 0 && umax > uwid) uwid = umax;
            return uwid;
        }
        
        /// <summary>
        /// Check to values <see cref="EnableReadline"/>, <see cref="EnableColor"/> 
        /// and <see cref="EnableResize"/> for validity.
        /// </summary>
        /// <remarks>
        /// This routine checks some environment variables to see if the requested
        /// features can be enabled.  On the top level these are:
        /// <para/>
        /// <c>ZTOOL_COLOR</c> which clears <see cref="EnableColor"/> if the value
        /// is set but not "on".
        /// <para/>
        /// <c>ZTOOL_READLINE</c> which clears <see cref="EnableReadline"/> if the
        /// value is set but not "on".
        /// <para/>
        /// <c>ZTOOL_RESIZE</c> which clears <see cref="EnableResize"/> if the
        /// value is set but not "on".
        /// <para/>
        /// The varable <c>TERM</c> is used when <c>ZTOOL_COLOR</c> is not set to
        /// clear <see cref="EnableColor"/> if the value is not "linux" and not
        /// "xterm".
        /// </remarks>
        public static void Initialize()
        {   InitTTY = true;

            // stupid way to detects windos os ...
            IsWindows = System.IO.Path.DirectorySeparatorChar == '\\';
            
            // Use ZTOOL_COLOR to disable colors
            if(EnableColor)
            {   string zcol = Environment("ZTOOL_COLOR");
                if     (zcol == "off") EnableColor = false;
                else if(zcol != "on" && !IsWindows)
                {   string term = Environment("TERM");
                    if(term != "linux" && term != "xterm") EnableColor = false;
                }
            }            

            // Use ZTOOL_READLINE to disable readline
            if(EnableReadline)
            {   string zlin = Environment("ZTOOL_READLINE");
                if(zlin == "off") EnableReadline = false;
            }            

            // Use ZTOOL_RESIZE to disable resize
            if(EnableResize)
            {   string zlin = Environment("ZTOOL_RESIZE");
                if(zlin == "off") EnableResize = false;
            }            

            // Now use P-Invoke calls ...
            if(!EnableReadline && !EnableResize) return;
            InvokeInit();
            if(!IsTTY)
            {   EnableReadline = false;
                EnableResize = false;
            }
        }

        // =====================================================================
        // Low level routines
        // =====================================================================

        // Low level routine to read tty input
        private static string ReadLine(string prompt)
        {   if(prompt == null) return null;
            prompt = string.Format("{0}{1}", prompt, PromptSuffix);
            if(!InitTTY) Initialize();

            // override an enventual progress message
            uint upro = (uint)prompt.Length;
            if(upro < progressLength) Progress(null, null, null);
            
            if(EnableReadline) return InvokeReadLine(prompt);
            Console.Write(prompt);
            return Console.ReadLine();
        }

        private static uint WinColumns()
        {   if(!InitTTY) Initialize(); 
            if(!EnableResize)
            {   uint uwid = (uint)Console.WindowWidth;
                if(IsWindows) uwid--;   // prevent extra line
                return uwid;
            }
            uint rows, cols;
            InvokeWinsize(out rows, out cols);
            return cols;
        }
        
        private static string Environment(string name)
        {   string var = System.Environment.GetEnvironmentVariable(name);
            if(string.IsNullOrEmpty(var)) return "";
            var = var.Trim().ToLower();
            // Extra("Environment: {0}={1}", name, var);
            return var;
        }
        
        // =====================================================================
        // P-Invoke and dummies
        // =====================================================================
        
#if MONO_BUILD
        private const string LIB_C="libc.so.6";
        private const string LIB_HISTORY="libhistory.so.5";
        private const string LIB_READLINE="libreadline.so.5";
        
        [StructLayoutAttribute(LayoutKind.Sequential)]
        private struct Winsize
        {
            public ushort ws_row;
            public ushort ws_col;
            public ushort ws_xpixel;
            public ushort ws_ypixel;
        }
        private const uint TIOCGWINSZ=0x5413;
            
        [DllImport(LIB_C, EntryPoint="ioctl")]
        private extern static void ioctl_winsize(int fd, uint mode, out Winsize size);

        [DllImport(LIB_C)]
        private extern static int isatty(int fd);

        [DllImport(LIB_HISTORY)]
        private extern static void using_history();
        [DllImport(LIB_HISTORY)]
        private extern static void add_history(string text);

        [DllImport(LIB_READLINE)]
        private extern static string readline(string prompt);

        // do inits for P-Invoke routines
        private static void InvokeInit()
        {   if(EnableReadline) using_history();
            IsTTY = (isatty(0) > 0);
        }
        
        // invoke gnu realine() and call add_history() ...
        private static string InvokeReadLine(string prompt)
        {   string line = readline(prompt);
            if(!string.IsNullOrEmpty(line)) add_history(line);
            return line;
        }
        
        private static void InvokeWinsize(out uint rows, out uint cols)
        {   Winsize ws = new Winsize();
            ioctl_winsize(0, TIOCGWINSZ, out ws);
            cols = ws.ws_col;
            rows = ws.ws_row;
        }
#else
        private static void InvokeInit()
        {   IsTTY = false;
        }

        private static string InvokeReadLine(string prompt)
        {   return null;
        }

        private static void InvokeWinsize(out uint rows, out uint cols)
        {   rows = cols = 0;
        }
#endif
    }
}
