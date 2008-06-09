//==============================================================================
// Admin.cs     The ZLibAdmin command parser
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU General Public License
// This software is published under the GNU GPL license.  Please refer to the
// file COPYING for details. Please use the following e-mail address to contact
// me or to send bug-reports:  info at j-pfennig dot de .
#endregion

using System;
using System.Threading;
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

        // =============================================================================
        // Console and Debug support
        // =============================================================================

        public static void Error(string message)
        {   ErrorCalled = true;
            LineTool.Error(message);
        }

        public static void Error(string message, params object[] arguments)
        {   ErrorCalled = true;
            LineTool.Error(message, arguments);
        }

        public static void Message(string message)
        {   if(Output >= OutLevel.Brief)
                LineTool.Message(message);
        }

        public static void Message(string message, params object[] arguments)
        {   if(Output >= OutLevel.Brief)
                LineTool.Message(message, arguments);
        }

        public static void Info(string message)
        {   if(Output >= OutLevel.Info)
                LineTool.Info(message);
        }

        public static void Info(string message, params object[] arguments)
        {   if(Output >= OutLevel.Info)
                LineTool.Info(message, arguments);
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

        public static void WriteIndented(uint extra, string prefix, params string[] args)
        {   WriteIndented(extra, prefix, string.Join(" ", args));
        }
        
        public static void WriteIndented(uint extra, string prefix, string text)
        {
            TextTool.GetDefaultFormatter();
            uint umax = TextTool.TextWidth;
            if(umax > 100) umax = 100;                      // limit output width

            int indent = -3;
            if(prefix != null) indent = prefix.Length;
            if(indent > 0) text = prefix + text;

            if((extra & 1) != 0) TextTool.Formatter.WriteLine("");
            TextTool.PrintIndent(indent, umax, TextTool.Decoration.None, text);
            if((extra & 2) != 0) TextTool.Formatter.WriteLine("");
        }

        // =============================================================================
        // TextTool Callback Interface
        // =============================================================================

        public static TextTool.TableBuilder GetTableBuilder(uint columns)
        {   TextTool.UseAscii = Ascii;
            TextTool.AutoWidth = true;
            TextTool.Prefix = "    ";
            TextTool.GetDefaultFormatter();
            if(columns <= 0) return null;
            return new TextTool.TableBuilder(columns);
        }

        // =============================================================================
        // ZIMapLib Callback Interface
        // =============================================================================

        class IMapCallback : ZIMapConnection.ICallback
        {   // Pretty formatting of monitor messages ...
            public bool Monitor(ZIMapConnection connection, ZIMapMonitor level, string message)
            {   if(message == null) return true;
                
                // output messages ...
                if(level <= ZIMapMonitor.Error)
                {   int icol = message.IndexOf(' ');
                    if(icol >= 0 && icol+2 < message.Length && message[icol+1] == ':')
                        message = message.Substring(icol+2);
                }
                switch(level)
                {   case ZIMapMonitor.Debug:    LineTool.Extra(message); return true;
                    case ZIMapMonitor.Info:     LineTool.Info(message);  return true;
                    case ZIMapMonitor.Error:    ZIMapAdmin.ErrorCalled = true;
                                                LineTool.Error(message); return true;
                    case ZIMapMonitor.Progress: break;
                    default:                    return true;
                }
                
                // output progress ...
                if(ZIMapAdmin.Output < OutLevel.All) return true;
                if(ZIMapAdmin.Debug) return true;
                int idx = message.LastIndexOf(' ');
                if(idx >= message.Length - 1) return true;
                if(idx >= 0) message = message.Substring(idx+1);
                uint num;
                if(!uint.TryParse(message, out num)) return true;

                System.Text.StringBuilder bar = new System.Text.StringBuilder();
                if(num >= 100)                          // completed, fill with spaces
                {   bar.Append(' ', 18 + 25);
                    bar.Append('\r');
                    LineTool.Write(LineTool.TextAttributes.Continue, bar.ToString());
                    return true;
                }
                uint mrk = (num + 3) / 4;
                bar.Append('#', (int)mrk);
                if(mrk < 25) bar.Append(' ', (int)(25 - mrk));
                LineTool.Write(LineTool.TextAttributes.Continue, "    Working [");
                LineTool.Write(LineTool.TextAttributes.Continue + LineTool.Modes.Extra,
                               bar.ToString());
                LineTool.Write(LineTool.TextAttributes.Continue, "] {0,2}%\r", num);
                return true;
            }

            // don't care about these ...
            public bool Closed(ZIMapConnection connection)
            {   return true;   }
            public bool Request(ZIMapConnection connection, uint tag, string command)
            {   return true;   }
            public bool Result(ZIMapConnection connection, object info)
            {   return true;   }
            
            // kill program on exception ...
            public bool Error(ZIMapConnection connection, ZIMapException error)
            {   if(Thread.CurrentThread.ThreadState == ThreadState.AbortRequested)
                    return true;
                ZIMapAdmin.Error("Error [exception]: {0}", error.Message);
                System.Environment.Exit(2);
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
                                     System.Reflection.BindingFlags.InvokeMethod,
                                     null, null, arga);
                return (bool)stat;
            }
            catch(System.MissingMethodException ex)
            {   Error("Command not implemented: {0}: {1}", cmd, ex.Message);
                return false;
            }
            catch(Exception ex)
            {   if(Thread.CurrentThread.ThreadState == ThreadState.AbortRequested)
                    return false;
                Error("Command caused exception: " + ex.Message);
                if(ex.InnerException != null)
                {   Error("-       inner  exception: " + ex.InnerException.Message);
                    if(Debug) LineTool.Write(ex.InnerException.StackTrace);
                }
                else if(Debug)
                    LineTool.Write(ex.StackTrace);
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
        {   WriteIndented(0, ArgsTool.Usage(ArgsTool.UsageFormat.Usage),
                          ArgsTool.Param(options, "server",  false),
                          ArgsTool.Param(options, "protocol", true),
                          ArgsTool.Param(options, "timeout",  true), "\n",
                          ArgsTool.Param(options, "account", false),
                          ArgsTool.Param(options, "password", true),
                          ArgsTool.Param(options, "mailbox",  true), "\n",
                          ArgsTool.Param(options, "confirm",  true),
                          ArgsTool.Param(options, "output",   true),
                          ArgsTool.Param(options, "log",      true), "\n",
                          ArgsTool.Param(options, "ascii",    true),
                          ArgsTool.Param(options, "debug",    true), "[argument...]");
            WriteIndented(2, ArgsTool.Usage(ArgsTool.UsageFormat.More),
                          ArgsTool.Param(options, "help",    false),
                          ArgsTool.Param(options, "command", false));
            TextTool.Formatter.WriteLine(ArgsTool.Usage(ArgsTool.UsageFormat.Options, options));

            WriteIndented(3, null,
              "The program enters an interactive mode if no commands (non-option arguments) are " +
              "specified.  Otherwise multiple commands can be given and the execution stops after " +
              "the 1st failure.");
            WriteIndented(2, null,
              "It is recommended to enclose commands in single quotes.  Example: " +
              "'create \"My Folder\"'.  An alternative command syntax exists - just prepend each " +
              "command with '--'.  Example: ' -- delete scratch -- create \"something new\" -- list'." +
              "  The two forms cannot be mixed.  Please note that parameters for IMAP may still need " +
              "quoting if they contain spaces.");
            WriteIndented(2, null,
              "The characters '-' and ':' are used to denote options and values.  Alternatively " +
              "'/' and '=' are accepted.  So '/timeout=20' is also recognized as on option.  Option " +
              "values can be split into sub-values using commas.  Example: '-protocol:imap,tls'.");
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
                LineTool.Write(ArgsTool.AppName + " implements the following commands:\n");

            // configure output ...
            string preGroup = Ascii ? "=== " : "► ";
            string sufGroup = Ascii ? " ===" : " ◄";
            string preText  = Ascii ? "* " : "■ ";

            // loop over commands ...
            for(int irun=0; irun+3 < commands.Length; irun+=4)
            {   if(string.IsNullOrEmpty(commands[irun]))
                {   if(cmdh != null) continue;
                    LineTool.Write(LineTool.Modes.Alert, "    {0}{1}{2}", 
                                   preGroup, commands[irun+3], sufGroup);
                    if(!bList) LineTool.Write(null);
                    continue;
                }

                // help for one command - skip others ...
                if(cmdh != null && !commands[irun].StartsWith(cmdh)) continue;

                // print options ...
                System.Text.StringBuilder args = new System.Text.StringBuilder();
                string[] opts = commands[irun+1].Split(" ".ToCharArray());
                string pref = commands[irun];
                TextTool.WriteMode = LineTool.Modes.Info;
                foreach(string opt in opts)
                {   if(opt == "") continue;
                    args.Append("[-");
                    args.Append(opt);
                    args.Append("] ");
                }
                WriteIndented(0, pref.PadRight(10), args + commands[irun+2]);
                TextTool.WriteMode = LineTool.Modes.Normal;

                // print details ...
                if(!bList) WriteIndented(3, preText, commands[irun+3]);
            }
            if(bList) LineTool.Write(null);
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
            "", "", "", "Commands to examine or to modify mailboxes",

            "list",     "all counts subscription rights quota", "{filter}",
                        "Lists mailboxes.  The filter can be a full mailbox name or may " +
                        "contain '%' to match inside one hierarchy level or '*' to match " +
                        "anything.  The default option is -detailed.",
            "open",     "read write", "[{mailbox}]",
                        "Opens a mailbox.  Default option is -read (for read-only access). " +
                        "The mailbox name is given as unicode string.  The name can ba a substring " +
                        "and is matched against a list of mailboxes fetched from the " +
                        "server.",
            "subscribe","add remove", "[{filter}|{mailbox}...]",
                        "List, add (option -add) or remove (option -remove) subscriptions. " +
                        "Giving no option behaves like 'list -subscrition {filter}'.",

            "create",   "", "{mailbox}...",
                        "Create a new (child-)mailbox.  A user may create multiple children " +
                        "in his INBOX folder (although the server may list them as sibblings). ",
            "delete",   "", "{mailbox}...",
                        "Delete a (child-)mailbox.  Continues without an error if the mailbox " +
                        "did not exist.  Some servers may refuse to delete non-empty mailboxes.",
            "rename",   "", "{mailbox} {newname}",
                        "Rename a mailbox.",

            "", "", "", "Commands for an open mailbox",

            "show",     "brief to from subject date size flags uid id", "[{mailbox}]",
                        "If a mailbox argument is given this mailbox is made current.  Then the " +
                        "mails in the current mailbox are listed.  The -brief option implies -to, " +
                        "-from and -subject.  Brief is the default if no other options is given.",
            "sort",     "revert to from subject date size flags uid id", "",
                        "Sorts the mails of the current mailbox, -revert reverses the direction.",
            "set",      "id uid deleted seen flagged custom", "[{flag}...] {item}...|*",
                        "Set built-in or custom flags for mail items in the current mailbox. " +
                        "The -custom option enable the use of {flag} arguments (which cannot be " +
                        "numeric).  A list of item numbers or * select the affected mails.  The " +
                        "item numbers can also be ids or uids (with -id or -uid option).",
            "unset",    "id uid deleted seen flagged custom", "[{flag}...] {item}...|*",
                        "Like the 'set' command but clears the flags. ",
            "expunge",  "", "",
                        "Remove mails flagged as deleted from the current mailbox.  This will " +
                        "also happens automatically when the mailbox is explicitly closed or " +
                        "if another mailbox is opened.",
            "copy",     "id uid", "{mailbox} {item}...|*",
                        "Copy mails to another mailbox.  The mailbox name is followed by a list " +
                        "of item numbers (or *), mail ids (option -id) or uids (option -uid).",
            "close",    "", "",
                        "Close any open mailbox.  The IMap server implicitly deletes all " +
                        "mails in the mailbox that have the \\Deleted flag set.",

            "", "", "", "Administrative commands",

            "user",     "list add remove quota", "[{user}] [{storage} [{messages}]]",
                        "List users, create or remove a user root mailbox, set user quotas or " +
                        "(without option) make a user the default user.",
            "shared",   "list add remove quota", "[{name}] [{storage} [{messages}]]",
                        "List shared root mailboxes, create or remove a shared root mailbox.",
            "quota",    "mbyte", "{mailbox} [{storage} {messages}]]",
                        "Change the quota settings of a mailbox.  Default unit for storage is " +
                        "kByte (use -byte or -mbyte to override).  Use a value of 0 to clear a " +
                        "quota setting.  Without storage argument the current quota are shown.",
            "rights",   "all read write none custom deny", "{mailbox} [{rights} [{user}...]]",
                        "Change the rights for a mailbox.  The flags -all -read -write -none " +
                        "and -custom are exclusive.  By default rights are granted, the -deny " +
                        "flag  adds 'negative' rights.  When -custom is give a list of custom " +
                        "rights must follow the mailbox name.",

            "", "", "", "Miscellaneous commands",

            "search",   "header body or query", "[{query}] values...",
                        "Search the current mailbox for messages.  If neither -header nor -body are " +
                        "header and body are searched.  The default for a simple query is to match " +
                        "messages that contain all given values (and).  This can be changed to match " +
                        "messages that contain any of the give values using -or.  With -query you can " +
                        "specify more complex queries with ? characters as placeholders for values.",
            "imap",     "verbatim", "command [{argument}|?|#]...",
                        "Execute an IMap command.  By default the command arguments get parsed and " +
                        "reassembled, use -verbatim to run an unparsed command.  You can use ? chars " +
                        "as placeholders for literal data and # chars as placeholders for mailbox names.",
            "info",     "mailbox server application id uid headers body", "[{mailbox}|{item}]",
                        "Output information about a mailitem (default), a mailbox, the server or " +
                        "the application.  For a mailitem the item number is required.",
            "export",   "list recurse id uid", "{file|path} [{mailbox}] [{id|uid}...]",
                        "Export mails to a mbox file.  The -override option will cause existing " +
                        "files to be overridden without a warning, -append will add data to an " +
                        "existing file or create a new file as required.  There is no default option.",
            "import",   "list", "{file|path} [{mailbox}]",
                        "Import mails from a mbox file into the current mailbox.",
            "cache",    "clear on off", "",
                        "Enable or disable caching of IMap data (-on and -off) or clear " +
                        "the current cache content.  The default option is -on.",
            "help",     "all list", "[{command}]",
                        "Prints the list of commands (-list) or information about a single " +
                        "command (with the {command} argument). A detailed list containing " +
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

        /// <summary>
        /// Check for a conflict between exclusive options
        /// </summary>
        /// <param name="opts">
        /// The options that were passed to the calling function
        /// </param>
        /// <param name="names">
        /// A list of exclusive options.
        /// </param>
        /// <returns>
        /// The number of matches (0 := none of options in 'names' found,
        /// 1 := ok, >1 conflict.
        /// </returns>
        /// <remarks>
        /// In the case of a conflict an error messages is sent to output.
        /// </remarks>
        public static uint ExclusiveOption(string[] opts, params string[] names)
        {   if(opts == null || opts.Length < 1) return 0;
            if(names == null || names.Length < 1) return 0;
            uint ucnt = 0;
            string first = null;
            foreach(string opt in names)
            {   if(!HasOption(opts, opt)) continue;
                if(ucnt == 0) first = opt;
                if(ucnt > 0) Error("Option '-{0}' conflicts with '-{1}'", first, opt);
                ucnt++;
            }
            return ucnt;
        }

        // =============================================================================
        // Main
        // =============================================================================
        public static void Main(string[] args)
        {   uint confirm = 0;
            ZIMapConnection.TlsModeEnum tlsmode = ZIMapConnection.TlsModeEnum.Automatic;

            // --- step 1: parse command line arguments
            ArgsTool.Option[] opts = ArgsTool.Parse(options, args, out Commands);
            if(opts == null)
            {   LineTool.Write("Invalid command line. Try /help to get usage info.");
                return;
            }
            foreach(ArgsTool.Option o in opts)
            {   if(o.Error == ArgsTool.OptionStatus.Ambiguous)
                {   LineTool.Write("Ambiguous option: {0}", o.Name);
                    return;
                }
                if(o.Error != ArgsTool.OptionStatus.OK)
                {   LineTool.Write("Invalid option: {0}. Try /help to get usage info", o.Name);
                    return;
                }
                switch(o.Name)
                {   case "?":
                    case "help":    Usage();
                                    return;
                    case "command": GetTableBuilder(0);         // init TextTool
                                    Execute("help -all " + o.Value);
                                    return;
                    case "ascii":   Ascii = true;
                                    TextTool.UseAscii = true;
                                    LineTool.EnableColor = false;
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
                                    {   LineTool.Write("No log file specified");
                                        return;
                                    }
                                    break;
                    case "debug":   Debug = true;
                                    break;
                    case "server":  Server = o.Value;
                                    break;
                    case "protocol":
                                    Protocol = o.Value;
                                    if(o.SubValues != null)
                                    {   if(o.SubValues[0] == "tls"  )
                                            tlsmode = ZIMapConnection.TlsModeEnum.Required;
                                        if(o.SubValues[0] == "notls")
                                            tlsmode = ZIMapConnection.TlsModeEnum.Disabled;
                                    }
                                    break;
                    case "timeout":
                                    if(!uint.TryParse(o.Value, out Timeout))
                                    {   LineTool.Write("Invalid timeout: {0}", o.Value);
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
            GetTableBuilder(0);                             // init TextTool

            // --- step 2: prompt for missing parameters

            bool batch;
            if(Commands != null && Commands.Length > 0)
            {   if(confirm != 1) LineTool.AutoConfirm = true;
                if(Output == OutLevel.Undefined) Output = OutLevel.Brief;
                batch = true;

                string missing = null;
                if     (Server  == null)  missing = "server";
                else if(Account == null)  missing = "account";
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
            }

            if(Password == null)
                Password = System.Environment.GetEnvironmentVariable("ZIMAP_PWD");
            if(string.IsNullOrEmpty(Password))
            {   Password = LineTool.Prompt("Password");
                if(string.IsNullOrEmpty(Password)) return;
            }

            // --- step 3: Connect and configure

            uint port = ZIMapConnection.GetIMapPort(Protocol);
            if(port == ZIMapConnection.GetIMapPort()) Protocol = "imap";
            if(!batch)
                Message(string.Format("Connecting {0}://{1}@{2} ...", Protocol, Account, Server));
            ZIMapConnection.Callback = new IMapCallback();

            App = new ZIMapApplication(Server, port);
            App.EnableProgressReporting = true;
            Cache = new CacheData(App);

            if(Timeout != 0) App.Timeout = Timeout;
            if(Debug) App.MonitorLevel = ZIMapMonitor.Debug;
            if(Debug) App.SetMonitorLevel(ZIMapMonitor.Debug, true);

            if(!App.Connect(Account, Password, tlsmode))
            {   Error("Failed to connect");
                return;
            }

            if(Output >= OutLevel.Info)
                Info("Server: " + App.Connection.ProtocolLayer.ServerGreeting);
            if(!App.Factory.HasCapability("IMAP4rev1"))
                Error("WARNING: This is not an IMAP4rev1 server!");

            // --- step 4: Open mailbox, Execute Commands

            if(MailBoxName == null || Execute("open -write " + MailBoxName))
            {
                // has commands from command line...
                if(batch)
                {   // If the "--" syntax is used ...
                    if(EndOption)
                    {   // Arguments that contain spaces must be quoted
                        for(uint urun=0; urun < Commands.Length; urun++)
                            if(Commands[urun].Contains(" "))
                                ZIMapConverter.QuotedString(out Commands[urun], Commands[urun], true);
                        // build command string and split by "--" into single commands
                        string   carg = string.Join(" ", Commands);
                        Commands = carg.Split(new string[] { "-- " }, StringSplitOptions.RemoveEmptyEntries);
                    }

                    // Execute the array of commands until failure ...
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