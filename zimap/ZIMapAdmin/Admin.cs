//==============================================================================
// Admin.cs     The ZLibAdmin command parser
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
    
    /// <summary>
    /// Testing
    /// </summary>
    public partial class ZIMapAdmin
    {
        public enum OutLevel {  Undefined, Error, Brief, Info, All };

        // =============================================================================
        // Program call options
        // =============================================================================
        
        // -debug: generate debug output
        private static bool     Debug;
        // -ascii: do not use line drawing chars
        private static bool     Ascii;
        // --: explicit end of options 
        private static bool     EndOption;
        // -server: name of IMAP server
        private static string   Server;
        // -protocol: protocol name
        private static string   Protocol = "imap";
        // -account: IMap account name
        public  static string   Account;
        // -password: IMap password
        private static string   Password;
        // -mailbox: initial mailbox
        private static string   MailBoxName;
        // -timeout: network/server timeout
        private static uint     Timeout;
        // -optput: console output verbosity
        private static OutLevel Output;
        // -log: optional session transscript file
        private static string   Log;
        // extra arguments
        private static string[] Commands;
        
        // =============================================================================
        // Execution State        
        // =============================================================================

        // IMap layer
        private static ZIMapApplication App;
        // Data access and Cache        
        private static CacheData    Cache;
        // set true by calls to Error()
        private static bool         ErrorCalled;
        // unparsed command string
        private static string       UnparsedCommand;
        // selected list prefix (from user command)
        public  static string       ListPrefix;

        // =============================================================================
        // Console and Debug support        
        // =============================================================================

        public static void Error(string message)
        {   ErrorCalled = true;
            WriteOutput("*** ", message);
        }

        public static void Error(string message, params object[] arguments)
        {   ErrorCalled = true;
            WriteOutput("*** ", string.Format(message, arguments));
        }

        public static void Message(string message)
        {   if(Output >= OutLevel.Brief)   
                WriteOutput("    ", message);
        }

        public static void Message(string message, params object[] arguments)
        {   if(Output >= OutLevel.Brief)   
                WriteOutput("    ", string.Format(message, arguments));
        }

        public static void Info(string message)
        {   if(Output >= OutLevel.Info)   
                WriteOutput("    ", message);
        }

        public static void Info(string message, params object[] arguments)
        {   if(Output >= OutLevel.Info)   
                WriteOutput("    ", string.Format(message, arguments));
        }
        
        public static bool Confirm(string prompt)
        {   bool result = LineTool.Confirm("*** " + prompt);
            if(result) return true;
            ErrorCalled = true;
            return false;
        }

        public static bool Confirm(string message, params object[] arguments)
        {   return Confirm(string.Format(message, arguments));
        }

        public static void WriteOutput(string prefix, string message)
        {   if(!Ascii && prefix != "    ")
            {   if     (prefix == "*** ") Console.ForegroundColor = ConsoleColor.Red;
                else if(prefix == "**  ") Console.ForegroundColor = ConsoleColor.Blue;
                else                      Console.ForegroundColor = ConsoleColor.Green;
                
                Console.Write(prefix);
                Console.ResetColor();
                Console.WriteLine(message);
            }
            else
                Console.WriteLine("{0}{1}", prefix, message);
            // TODO: WriteOutput write to Log
        }
        
        // =============================================================================
        // TextTool Callback Interface        
        // =============================================================================

        public class TabFormatterA : TextTool.DefaultFormatter
        {   public override void WriteLine(string line)
            {   Console.WriteLine("    " + line);
            }
        }

        public class TabFormatterU : TextTool.IFormatter
        {
            public char   GetLine1()               {   return '─';     }
            public char   GetLine2()               {   return '═';     }
            public string GetCross1()              {   return "─┼─";   }
            public string GetCross2()              {   return "─┴─";   }
            public string GetIndexSeparator()      {   return " │ ";   }
            public string GetColumnSeparator()     {   return "  ";    }
            public void WriteLine(string line)     {   Console.WriteLine("    " + line);    }
        }
        
        public static TextTool.TableBuilder GetTableBuilder(uint columns)
        {   TextTool.TableBuilder tb = new ZTool.TextTool.TableBuilder(columns);
            if(Ascii)
                tb.Formatter = new ZIMapAdmin.TabFormatterA();
            else
                tb.Formatter = new ZIMapAdmin.TabFormatterU();
            return tb;
        }
        
        // =============================================================================
        // ZIMapLib Callback Interface        
        // =============================================================================

        class IMapCallback : ZIMapConnection.ICallback
        {   public bool Monitor(ZIMapConnection connection, ZIMapMonitor level, string message)
            {   if(message == null) return true;   
                string prefix = null;
                if      (level == ZIMapMonitor.Debug)    prefix = "*   ";
                else if (level == ZIMapMonitor.Info)     prefix = "**  ";
                else if (level == ZIMapMonitor.Error)    prefix = "*** ";
                else if (level == ZIMapMonitor.Messages) return true;
                if(prefix == null)
                {   if(ZIMapAdmin.Output < OutLevel.All) return true;
                    if(ZIMapAdmin.Debug) return true;
                    int idx = message.LastIndexOf(' ');
                    if(idx >= message.Length - 1) return true;
                    if(idx >= 0) message = message.Substring(idx+1);
                    uint num;
                    if(!uint.TryParse(message, out num)) return true;
                    
                    System.Text.StringBuilder bar = new System.Text.StringBuilder(30);
                    if(num >= 100)
                    {   bar.Append(' ', 18 + 25);
                        bar.Append('\r');
                        Console.Write(bar.ToString());
                        return true;
                    }
                    uint mrk = (num + 3) / 4;
                    bar.Append('#', (int)mrk);
                    if(mrk < 25) bar.Append(' ', (int)(25 - mrk));
                    if(ZIMapAdmin.Ascii)
                        Console.Write("    Working [{0}] {1,2}%\r", bar, num);  // 18 chars
                    else
                    {   Console.Write("    Working [");
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write(bar);
                        Console.ResetColor();
                        Console.Write("] {0,2}%\r", num);
                    }
                }
                else
                {   int icol = message.IndexOf(' ');
                    if(icol >= 0 && icol+2 < message.Length && message[icol+1] == ':')
                    {   message = message.Substring(icol+2);
                        ZIMapAdmin.ErrorCalled = true;
                    }
                    ZIMapAdmin.WriteOutput(prefix, message);
                }
                return true;   
            }
            public bool Closed(ZIMapConnection connection)
            {   //Console.WriteLine("CLOSED");
                return true;   }
            public bool Request(ZIMapConnection connection, uint tag, string command)
            {   //Console.WriteLine("REQUEST: " + tag + ": " + command);
                return true;   }
            public bool Result(ZIMapConnection connection, object info)
            {   //if(info is ZIMapReceiveData)
                //    Console.WriteLine("RESULT: {0}: {1}: {2}", ((ZIMapReceiveData)info).Tag,
                //      ((ZIMapReceiveData)info).ReceiveState, ((ZIMapReceiveData)info).Message);
                //else
                //    Console.WriteLine("RESULT: " + info.GetType().Name);
                return true;   }
            public bool Error(ZIMapConnection connection, ZIMapException error)
            {   ZIMapAdmin.Error("Error [exception]: {0}", error.Message);
                System.Threading.Thread.CurrentThread.Abort();
                return false;   
            }
        }

        // =============================================================================
        // Command execution        
        // =============================================================================
        
        /// <summary>
        /// A simple overload for <see cref="Execute(string, string[], string[])"/>
        /// </summary>
        /// <param name="cmd">
        /// Comand string to be executed.
        /// </param>
        /// <returns>
        /// <c>true</c> on success.
        /// </returns>
        public static bool Execute(string cmd)
        {   UnparsedCommand = cmd;
            List<string> opts = new List<string>();
            List<string> args = new List<string>();
            if(!string.IsNullOrEmpty(cmd))
            {   ZIMapParser parser = new ZIMapParser(cmd);
                cmd = parser[0].ToString();

                bool bimap = cmd == "imap";
                for(int irun=1; irun < parser.Length; irun++)
                {   ZIMapParser.Token token = parser[irun];
                    if(!bimap && token.Type == ZIMapParserData.Quoted)
                        args.Add(token.Text);
                    else if(token.Type != ZIMapParserData.Text ||
                            token.Text.Length <= 1 ||
                            !(token.Text[0] == '-' || token.Text[0] == '/'))
                        args.Add(token.ToString());
                    else
                        opts.Add(token.Text.Substring(1));
                }
            }
            
            ErrorCalled = false;
            bool bok = Execute(cmd, opts.ToArray(), args.ToArray());
            UnparsedCommand = null;
            if(bok) return true;
            if(!ErrorCalled) Error("Command failed: {0}", cmd);
            return false;
        }
        
        /// <summary>
        /// Execute preparsed commands.
        /// </summary>
        /// <param name="cmd">
        /// The command name (a single word).
        /// </param>
        /// <param name="opts">
        /// List of options (<c>null</c> is permitted).
        /// </param>
        /// <param name="args">
        /// List of arguments (<c>null</c> is permitted).
        /// </param>
        /// <returns>
        /// <c>true</c> on success.
        /// </returns>
        public static bool Execute(string cmd, string[] opts, params string[] args)
        {   if(string.IsNullOrEmpty(cmd))
            {   Error("No Command given");
                return false;
            }

            // search command in list ...
            int offset = -1;
            for(int irun=0; irun+3 < commands.Length; irun+=4)
            {   if(string.IsNullOrEmpty(commands[irun])) continue;
                if(commands[irun].StartsWith(cmd))
                {   if(offset >= 0)
                    {   Error("The command name is ambiguous: " + cmd);
                        return false;
                    }
                    offset = irun;
                }
            }
            if(offset < 0)
            {   Error("The command name is unknown: " + cmd);
                return false;
            }
            cmd = commands[offset];
            
            // now check the options ...
            string oset = commands[offset+1];               // option definitions
            if(string.IsNullOrEmpty(oset)) opts = null;     // no options allowed
            else if(opts == null)                           // no option present
                opts = ZIMapConverter.StringArray(0); 
            
            if(opts != null && opts.Length > 0)
            {   string[] list = commands[offset+1].Split(" ".ToCharArray());
                for(int iopt=0; iopt < opts.Length; iopt++)
                {   int iidx = -1;
                    string opt = opts[iopt];
                    for(int ilis=0; ilis < list.Length; ilis++)
                    {   if(!list[ilis].StartsWith(opt)) continue;
                        if(iidx > 0)
                        {   Error("Option argument ambiguous: " + opt);
                            return false;
                        }
                        iidx = ilis;
                    }
                    if(iidx < 0)
                    {   Error("Option argument invalid: " + opt);
                        return false;
                    }
                    opts[iopt] = list[iidx];
                }
            }
            
            // finally check arguments ...
            string aset = commands[offset+2];               // argument definitions
            if(string.IsNullOrEmpty(aset)) args = null;     // no arguments allowed
            else if(args == null)                           // no arguments present
                args = ZIMapConverter.StringArray(0); 
            bool singleArg = false;                         // only one arg allowed
            bool multiArgs = false;
            
            string[] alis = null;
            if(!string.IsNullOrEmpty(aset))                 // have arg definition?
            {   multiArgs = aset.Contains("...");
                alis = aset.Split(" ".ToCharArray());       // arg defs array
                if(alis.Length == 1 && !multiArgs)          // single arg command
                    singleArg = true;
            }
            
            if(args != null && args.Length > 0)             // validate arg count...
            {   if(string.IsNullOrEmpty(aset))
                {   Error("No non-option arguments permitted");
                    return false;
                }
                if(!multiArgs && args.Length > alis.Length) // check maximum
                {   Error(String.Format("Invalid non-option argument count (have {0}, want {1})",
                                        args.Length, alis.Length));
                    return false;
                }
            }
            
            // special case 'help' ...
            if(cmd == "help")
            {   Command(opts, args.Length > 0 ? args[0] : null);
                return true;
            }
            
            // execute ...
            string exec = "Execute" + cmd[0].ToString().ToUpper() + cmd.Substring(1);
            if(Debug)
                Message("Command: {0} {1}", cmd, args == null ? "" : string.Join(" ", args));

            try {
                object[] arga = null;
                // Console.WriteLine("{0} {1} {2}", singleArg, opts == null, args == null);
                
                if(singleArg)
                {   string arg = (args == null || args.Length < 1) ? "" : args[0];
                    if(opts != null)                 arga = new object[] { opts, arg };
                    else                             arga = new object[] { arg };
                }
                else
                {   if(opts != null && args != null) arga = new object[] { opts, args }; 
                    else if(opts != null)            arga = new object[] { opts }; 
                    else if(args != null)            arga = new object[] { args };
                }

                object stat = typeof(ZIMapAdmin).InvokeMember(exec, 
                                     System.Reflection.BindingFlags.Static |
                                     System.Reflection.BindingFlags.InvokeMethod, 
                                     null, null, arga);
                return (bool)stat;
            }
            catch(System.MissingMethodException ex)
            {   Error("Command not implemented: {0}: {1}", cmd, ex.Message);
                return false;
            }
            catch(Exception ex)
            {   Error("Command caused exception: " + ex.Message);
                if(ex.InnerException != null)
                {   Error("-       inner  exception: " + ex.InnerException.Message);
                    if(Debug) Console.WriteLine(ex.InnerException.StackTrace);
                }
                else if(Debug)
                    Console.WriteLine(ex.StackTrace);
                return false;
            }
            finally
            {   if(!Cache.Enabled) Cache.Clear();
                Cache.CommandRunning = false;
            }
        }

        // =============================================================================
        // Usage, Options and Commands        
        // =============================================================================
        
        /// <summary>
        /// Output a usage message.
        /// </summary>
        public static void Usage()
        {   string usage = "Usage:   " + ArgsTool.AppName + "  ";
            string extra = "         " + ArgsTool.AppName + "  ";
            string space = " ".PadRight(usage.Length);
            Console.WriteLine(usage + "{0} {1} {2} {6}\n" +                 // server, protocol, timeout
                              space + "{3} {4} {5} {10}\n" +                 // account passwd mailbox
                              space + "{7} {8} {9} [commands...]\n" +   // ascii confirm debug
                              extra + "[{11} | {12}]",                   // help, commands
                              ArgsTool.Param(options, "server",  false),
                              ArgsTool.Param(options, "protocol", true),
                              ArgsTool.Param(options, "timeout",  true),
                              ArgsTool.Param(options, "account", false),
                              ArgsTool.Param(options, "password", true),
                              ArgsTool.Param(options, "mailbox",  true),
                              ArgsTool.Param(options, "ascii",    true),
                              ArgsTool.Param(options, "confirm",  true),
                              ArgsTool.Param(options, "output",   true),
                              ArgsTool.Param(options, "log",      true),
                              ArgsTool.Param(options, "debug",    true),
                              ArgsTool.Param(options, "help",    false),
                              ArgsTool.Param(options, "command", false));
            Console.WriteLine("\n{0}\n\n{1}", ArgsTool.List(options, "Options: "),
              "The program enters an interactive mode if no commands (non-option arguments) are\n" +
              "given.  Multiple commands can be used, execution stops after the 1st failure. It\n" +
              "is recommended to enclose commands in single quotes like: 'create \"My Folder\"'.\n\n" +
              "The characters '-' and ':' are used to indicate an option and a value.  Alterna-\n" +
              "tively '/' and '=' are accepted.  So '/timeout=20' is also a legal option\n"); 
        }

        /// <summary>
        /// Output the list of commands.
        /// </summary>
        public static void Command(string[] optc, string argc)
        {   bool bAll  = false;
            bool bList = false;
            string cmdh = null;
            if(optc != null) foreach(string opt in optc)
            {   if     (opt == "all")  bAll  = true;
                else if(opt == "list") bList = true;
            }
            if(bAll) bList = false;
            if(!(bAll || bList))
                cmdh = string.IsNullOrEmpty(argc) ? "help" : argc;
            if(cmdh == null)
                Console.WriteLine(ArgsTool.AppName + " implements the following commands:\n");
            
            for(int irun=0; irun+3 < commands.Length; irun+=4)
            {   if(string.IsNullOrEmpty(commands[irun]))
                {   if(cmdh != null) continue;
                    if(!Ascii) Console.ForegroundColor = System.ConsoleColor.Red;
                    Console.WriteLine("    * {0}", commands[irun+3]);
                    if(!Ascii) Console.ResetColor();
                    if(!bList) Console.WriteLine();
                    continue;
                }   

                // help for one command - skip others ...
                if(cmdh != null && !commands[irun].StartsWith(cmdh)) continue;
                
                // print options ...
                System.Text.StringBuilder args = new System.Text.StringBuilder();
                string[] opts = commands[irun+1].Split(" ".ToCharArray());
                string pref = commands[irun];
                if(!Ascii) Console.ForegroundColor = System.ConsoleColor.Blue;
                foreach(string opt in opts)
                {   if(opt == "") continue;
                    args.Append("[-");
                    args.Append(opt);
                    args.Append("] ");
                    if(args.Length > 50)
                    {   Console.WriteLine("    {0} {1}", pref.PadRight(9), args);
                        pref = " "; args.Length = 0;
                    }
                }
                Console.WriteLine("    {0} {1}{2}", 
                                  pref.PadRight(9), args, commands[irun+2]);
                if(!Ascii) Console.ResetColor();
                
                // print details ...
                if(!bList)
                {   string text = commands[irun+3].Replace("\n", "\n    ");
                    Console.WriteLine("\n    - {0}\n", text);
                }
            }
            if(bList) Console.WriteLine();
        }
        
        private static string[] options = {
            "server",   "host",     "Connect to a server at {host}",
            "protocol", "name",     "Use the protocol {name}             (default: imap)",
            "timeout",  "seconds",  "Connection/Read/Write timeout         (default: 30)",
            "",         "",         "",
            "account",  "user",     "Login using the {user} account",
            "password", "text",     "Use the login password {text}",
            "mailbox",  "name",     "Open the {name} mailbox in write mode",
            "",         "",         "",
            "ascii",    "",         "Do not use line drawing chars or colors",
            "confirm",  "mode",     "Enable/disable confirmations (on/off)",
            "output",   "level",    "Console verbosity (error brief info all)",
            "log",      "file",     "Write session transcript to a log file",
            "debug",    "",         "Output debug information",
            "",         "",         "",
            "help",     "",         "Print this text and quit",
            "command",  "",         "Print the list of commands and quit",
        };
        
        private static string[] commands = {
            "", "", "", "Commands to examine or to modify mailboxes ...",
            
            "list",     "all counts subscription rights quota", "{filter}",
                        "Lists mailboxes.  The filter can be a full mailbox name or may\n" +
                        "contain '%' to match inside one hierarchy level or '*' to match\n" +
                        "anything.  The default option is -detailed.",
            "open",     "read write", "[{mailbox}]", 
                        "Opens a mailbox.  Default option is -read (for read-only access).\n" +
                        "The mailbox name is given as unicode string.  The name can ba a sub-\n" +
                        "string and is matched against a list of mailboxes fetched from the\n" +
                        "server.",
            "subscribe","add remove", "[{filter}|{mailbox}...]",
                        "List, add (option -add) or remove (option -remove) subscriptions.\n" +
                        "Giving no option behaves like 'list -subscrition {filter}'.",
            
            "create",   "", "{mailbox}...",
                        "Create a new (child-)mailbox.  A user may create multiple children\n" +
                        "in his INBOX folder (although the server may list them as sibblings).",
            "delete",   "", "{mailbox}...",
                        "Delete a (child-)mailbox.  Continues without an error if the mailbox\n" +
                        "did not exist.  Some servers may refuse to delete non-empty mailboxes.",
            "rename",   "", "{mailbox} {newname}",
                        "Rename a mailbox.",

            "", "", "", "Commands for an open mailbox ...",

            "show",     "brief to from subject date size flags uid id", "[{mailbox}]",
                        "If a mailbox argument is given this mailbox is made current.  Then the\n" +
                        "mails in the current mailbox are listed.  The -brief option implies -to,\n" +
                        "-from and -subject.  Brief is the default if no other options is given.",
            "sort",     "revert to from subject date size flags uid id", "",      
                        "Sorts the mails of the current mailbox, -revert reverses the direction.",
            "set",      "id uid deleted seen flagged custom", "[{flag}...] {item}...|*",
                        "Set built-in or custom flags for mail items in the current mailbox.\n" +
                        "The -custom option enable the use of {flag} arguments (which cannot be\n" +
                        "numeric).  A list of item numbers or * select the affected mails.  The\n" +
                        "item numbers can also be ids or uids (with -id or -uid option).",
            "unset",    "id uid deleted seen flagged custom", "[{flag}...] {item}...|*",
                        "Like the 'set' command but clears the flags.\n",
            "expunge",  "", "",
                        "Remove mails flagged as deleted from the current mailbox",
            "copy",     "id uid", "{mailbox} {item}...|*",
                        "Copy mails to another mailbox.  The mailbox name is followed by a list\n" +
                        "of item numbers (or *), mail ids (option -id) or uids (option -uid).",
            "close",    "", "",
                        "Close any open mailbox.  The IMap server implicitly deletes all\n" +
                        "mails in the mailbox that have the \\Deleted flag set.",
                   
            "", "", "", "Administrative commands ...",
            
            "user",     "list add remove quota", "[{user}] [{storage} [{messages}]]",
                        "List users, create or remove a user root mailbox, set user quotas or\n" +
                        "(without option) make a user the default user.",
            "shared",   "list add remove quota", "[{name}] [{storage} [{messages}]]",
                        "List shared root mailboxes, create or remove a shared root mailbox.",
            "quota",    "mbyte", "{mailbox} [{storage} {messages}]]",
                        "Change the quota settings of a mailbox.  Default unit for storage is\n" +
                        "kByte (use -byte or -mbyte to override).  Use a value of 0 to clear a\n" +
                        "quota setting.  Without storage argument the current quota are shown.",
            "rights",   "all read write none custom deny", "{mailbox} [{rights} [{user}...]]",
                        "Change the rights for a mailbox.  The flags -all -read -write -none\n" +
                        "and -custom are exclusive.  By default rights are granted, the -deny\n" +
                        "flag  adds 'negative' rights.  When -custom is give a list of custom\n" +
                        "rights must follow the mailbox name.",

            "", "", "", "Miscellaneous commands ...",
            
            "search",   "header body both query", "[{query}] values...",
                        "Search the mailbox",
            "imap",     "verbatim", "command args...",
                        "Execute an IMap command.  By default the command arguments get parsed and\n" +
                        "reassembled, use -verbatim to run an unparsed command.",
            "info",     "mailbox server application", "[{mailbox}|{item}]",
                        "Output information about a mailitem (default), a mailbox, the server or\n" +
                        "the application.  For a mailitem the item number is required.",
            "export",   "append override", "{file} {id|uid} ...",
                        "Export mails to a mbox file.  The -override option will cause existing\n" +
                        "files to be overridden without a warning, -append will add data to an ex-\n" +
                        "isting file or create a new file as required.  There is no default option.",
            "import",   "", "{file}",
                        "Import mails from a mbox file into the current mailbox.",
            "cache",    "clear on off", "",
                        "Enable or disable caching of IMap data (-on and -off) or clear\n" +
                        "the current cache content.  The default option is -on.",
            "help",     "all list", "[{command}]",
                        "Prints the list of commands (-list) or information about a single\n" +
                        "command (with the {command} argument). A detailed list containing\n" +
                        "containing all commands is printed with -all."
        };

        /// <summary>
        /// Simple helper to check for an option argument.
        /// </summary>
        /// <param name="opts">
        /// The list of option arguments (<c>null</c> is ok)
        /// </param>
        /// <param name="option">
        /// The option to be searched for (<c>null</c> is ok)
        /// </param>
        /// <returns>
        /// <c>true</c> if the option was found.
        /// </returns>
        /// <remarks>The spelling must match exactly.
        /// </remarks>
        public static bool HasOption(string[] opts, string option)
        {   if(opts == null || opts.Length <= 0) return false;
            foreach(string opt in opts)
                if(opt == option) return true;
            return false;
        }
        
        // =============================================================================
        // Main        
        // =============================================================================
        public static void Main(string[] args)
        {   uint confirm = 0;

            // --- step 1: parse command line arguments            
            
            ArgsTool.Option[] opts = ArgsTool.Parse(options, args, out Commands);
            if(opts == null)
            {   Console.WriteLine("Invalid command line. Try /help to get usage info.");
                return;
            }
            foreach(ArgsTool.Option o in opts)
            {   if(o.Error == ArgsTool.OptionStatus.Ambiguous)
                {   Console.WriteLine("Ambiguous option: " + o.Name);
                    return;
                }
                if(o.Error != ArgsTool.OptionStatus.OK)
                {   Console.WriteLine("Invalid option: {0}. Try /help to get usage info", o.Name);
                    return;
                }
                switch(o.Name)
                {   case "?":   
                    case "help":    Usage();
                                    return;
                    case "command": Execute("help -all " + o.Value);
                                    return;
                    case "ascii":   Ascii = true;
                                    break;
                    case "confirm": if     (o.Value == "on")  confirm = 1;
                                    else if(o.Value == "off") confirm = 2;
                                    else                      confirm = 0;
                                    break;
                    case "output":  if     (o.Value == "error") Output = OutLevel.Error;
                                    else if(o.Value == "brief") Output = OutLevel.Brief;
                                    else if(o.Value == "all")   Output = OutLevel.All;
                                    else                        Output = OutLevel.Info;
                                    break;
                    case "log":     Log = o.Value;
                                    if(string.IsNullOrEmpty(Log))
                                    {   Console.WriteLine("Missing log file");
                                        return;
                                    }
                                    break;
                    case "debug":   Debug = true;
                                    break;
                    case "server":  Server = o.Value;
                                    break;
                    case "protocol":
                                    Protocol = o.Value;
                                    break;
                    case "timeout":
                                    if(!uint.TryParse(o.Value, out Timeout))
                                    {   Console.WriteLine("Invalid timeout: " + o.Value);
                                        return;
                                    }
                                    break;
                    case "account": Account = o.Value;
                                    break;
                    case "password":
                                    Password = o.Value;
                                    break;
                    case "mailbox": MailBoxName = o.Value;
                                    break;
                    case "--":      EndOption = true;
                                    break;
                }
            }
            opts = null;                                    // memory can be freed

            // --- step 2: prompt for missing parameters            

            bool batch;
            if(Commands != null && Commands.Length > 0)
            {   if(confirm != 1) LineTool.AutoConfirm = true;
                if(Output == OutLevel.Undefined) Output = OutLevel.Brief;
                batch = true;

                string missing = null;
                if     (Server  == null)  missing = "server";
                else if(Account == null)  missing = "account";
                else if(Password == null) missing = "password";
                if(missing != null)
                {   Error("Please add a '-{0}' option to your command line", missing);
                    return;
                }
            }
            else
            {   if(confirm == 2) LineTool.AutoConfirm = true;
                if(Output == OutLevel.Undefined) Output = OutLevel.All;
                batch = false;
                
                if(Server == null)
                {   Server = LineTool.Prompt("Server  ");
                    if(string.IsNullOrEmpty(Server)) return;
                }
                if(Account == null)
                {   Account = LineTool.Prompt("Account ");
                    if(string.IsNullOrEmpty(Account)) return;
                }
                if(Password == null)
                {   Password = LineTool.Prompt("Password");
                    if(string.IsNullOrEmpty(Password)) return;
                }
            }

            // --- step 3: Connect and configure            
            
            Message(string.Format("Connecting {0}:{1} ...", Protocol, Server));
            ZIMapConnection.Callback = new IMapCallback();
            
            App = new ZIMapApplication(Server, Protocol);
            App.EnableUidCommands = true;
            App.EnableProgressReporting = true;
            Cache = new CacheData(App);

            if(Timeout != 0) App.Timeout = Timeout;
            if(Debug) App.MonitorLevel = ZIMapMonitor.Debug;
            if(Debug) App.SetMonitorLevel(ZIMapMonitor.Debug, true);

            if(!App.Connect(Account, Password))
            {   Error("Failed to connect");
                return;
            }
            
            if(Output >= OutLevel.Info)
            {   Info("Server: " + App.Connection.ProtocolLayer.ServerGreeting);
                Info("Server: The Hierarchy Delimiter is: " + App.Factory.HierarchyDelimiter);
            }
            
            if(!App.Factory.HasCapability("IMAP4rev1"))
                Error("WARNING: This is not an IMAP4rev1 server!");
                
            // --- step 4: Open mailbox, Execute Commands            
            
            if(MailBoxName == null || Execute("open -write " + MailBoxName))
            {
                // has commands from command line...
                if(batch)
                {   if(EndOption)
                    {   string   carg = string.Join(" ", Commands);
                        Commands = carg.Split(new string[] { "--" }, StringSplitOptions.RemoveEmptyEntries);
                    }
                    foreach(string cmd in Commands)
                        if(!Execute(cmd)) break;
                }
                
                // prompt for commands...
                else
                {   Message("Entering command loop. Type 'help -list' to see the list of commands...");
                    while(true)
                    {   string cmd = LineTool.Prompt("Command ");
                        if(string.IsNullOrEmpty(cmd)) break;
                        Execute(cmd);
                    }
                }
            }

            // --- step 5: Disconnect and exit            
            
            App.Disconnect();
            return;
        }
    }
}