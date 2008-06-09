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
    // LineTool class - Reading and parsing command lines
    //==========================================================================
    
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
        public static string SimplifyWhiteSpace(string text)
        {   if(text == null || text == "") return text;
            
            StringBuilder sb = new StringBuilder();
            bool skip = true;
            int  last = 0;
            for(int irun=0; irun < text.Length; irun++)
            {   char c = text[irun];
                if(c <= 32)
                {   if(skip) continue;
                    skip = true;
                }
                else
                {   skip = false;
                    last = sb.Length + 1;
                }
                sb.Append(c);
            }
            sb.Length = last;
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
            
            // case 1: no color
            if(!EnableColor || colf == 0)
            {   sb = new StringBuilder();
                if(prefix != null)   sb.Append(prefix);
                if(format != null)
                {   if(args == null) sb.Append(format);
                    else             sb.AppendFormat(format, args);
                }
            }
            
            // case 2: color with readline support
            else if(EnableReadline)
            {   sb = new StringBuilder();
                if(colf > 100)
                    sb.AppendFormat("\x001b[1;{0}m", colf-100);
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
            
            // write (case 1 and 2)
            if((mode & TextAttributes.Continue) != 0)
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
        // Setup and configuration
        // =====================================================================

        public class TextAttributes
        {
            public static uint ToAnsi(ConsoleColor color)
            {   switch(color)
                {   case ConsoleColor.DarkRed:     return 31;
                    case ConsoleColor.DarkGreen:   return 32;
                    case ConsoleColor.DarkYellow:  return 33;
                    case ConsoleColor.DarkBlue:    return 34;
                    case ConsoleColor.DarkMagenta: return 35;
                    case ConsoleColor.DarkCyan:    return 36;
                    case ConsoleColor.Red:         return 131;
                    case ConsoleColor.Green:       return 132;
                    case ConsoleColor.Yellow:      return 133;
                    case ConsoleColor.Blue:        return 134;
                    case ConsoleColor.Magenta:     return 135;
                    case ConsoleColor.Cyan:        return 136;
                }
                return 0;
            }
            
            public static ConsoleColor ToColor(uint ansi)
            {   switch(ansi)
                {   case 31:    return ConsoleColor.DarkRed;
                    case 32:    return ConsoleColor.DarkGreen;
                    case 33:    return ConsoleColor.DarkYellow;
                    case 34:    return ConsoleColor.DarkBlue;
                    case 35:    return ConsoleColor.DarkMagenta;
                    case 36:    return ConsoleColor.DarkCyan;
                    case 131:   return ConsoleColor.Red;
                    case 132:   return ConsoleColor.Green;
                    case 133:   return ConsoleColor.Yellow;
                    case 134:   return ConsoleColor.Blue;
                    case 135:   return ConsoleColor.Magenta;
                    case 136:   return ConsoleColor.Cyan;
                }
                return ConsoleColor.Black;
            }
            
            public uint         Normal = ToAnsi(ConsoleColor.Black);
            public uint         Extra  = ToAnsi(ConsoleColor.Green);
            public uint         Info   = ToAnsi(ConsoleColor.Blue);
            public uint         Alert  = ToAnsi(ConsoleColor.Red);
            
            public const uint   Continue = 0x10000;
        }
        
        public static readonly TextAttributes Modes = new TextAttributes();
        
        public static string PrefixMessage = "    ";
        public static string PrefixDebug   = "*   ";
        public static string PrefixInfo    = "**  ";
        public static string PrefixError   = "*** ";
        
        public static bool EnableColor    = true;
        public static bool EnableReadline = true;
        public static bool EnableResize   = true;

        private static bool InitTTY;
        private static bool IsTTY;

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
            
            // Use ZTOOL_COLOR to disable colors
            if(EnableColor)
            {   string zcol = Environment("ZTOOL_COLOR");
                if     (zcol == "off") EnableColor = false;
                else if(zcol != "on")
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
            prompt = string.Format("{0}> ", prompt);
            if(!InitTTY) Initialize(); 
            if(EnableReadline) return InvokeReadLine(prompt);
            Console.Write(prompt);
            return Console.ReadLine();
        }

        private static uint WinColumns()
        {   if(!InitTTY) Initialize(); 
            if(!EnableResize) return (uint)Console.WindowWidth;
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
