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
using ZIMap;
using ZTool;

namespace ZIMapTools
{

    /// <summary>
    /// A command line tool to administer mailboxes on an IMap server.
    /// </summary>
    public partial class ZIMapAdmin
    {
        public enum OutLevel {  Undefined, Error, Brief, Info, All };

        // =============================================================================
        // Program call options
        // =============================================================================

        // -debug: generate debug output
        private static uint     Debug;
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
        // running from command line
        private static bool     Batch;
        // running a command
        private static bool     Executing;

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
        // user confirmed warning
        private static bool         UseIdOk;
        // progress reporting
        private static ZIMapConnection.Progress ProgressReporting;

        // =============================================================================
        // Console and Debug support
        // =============================================================================


        public static void Fatal(string message, params object[] arguments)
        {   LineTool.Error(message, arguments);
            System.Environment.Exit(1);
        }

        public static void Fatal(string message)
        {   LineTool.Error(message);
            System.Environment.Exit(1);
        }

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

        public static void DebugLevel(uint level)
        {   ZIMapConnection.Monitor lvl = ZIMapConnection.Monitor.Error;
            if(Debug > 1) App.SetMonitorLevel(lvl, true);
            Debug = Math.Min(3, level);
            if(Debug > 0) lvl = (Debug > 1) ? ZIMapConnection.Monitor.Debug
                                            : ZIMapConnection.Monitor.Info;
            App.SetMonitorLevel(lvl, Debug > 1);
        }
        
        // =============================================================================
        // TextTool Setup
        // =============================================================================

        public static TextTool.TableBuilder GetTableBuilder(uint columns)
        {   TextTool.UseAscii = Ascii;
            TextTool.AutoWidth = true;
            TextTool.Prefix = "    ";
            TextTool.GetDefaultFormatter();
            LineTool.PromptSuffix = Ascii ? "> " : "► ";
            if(columns <= 0) return null;
            return new TextTool.TableBuilder(columns);
        }

        // =============================================================================
        // ZIMapLib Callback Interface
        // =============================================================================

        class IMapCallback : ZIMapConnection.CallbackDummy
        {   // Pretty formatting of monitor messages ...
            public override bool Monitor(ZIMapConnection connection, ZIMapConnection.Monitor level,
                                         string source, string message)
            {   if(message == null || source == null) return true;
                
                // output messages ...
                string text = null;
                if(level <= ZIMapConnection.Monitor.Error)
                {   if(message.Length > 2 && message[0] == ':')
                        text = message.Substring(1);
                }
                if(text == null) text = source + ": " + message;

                switch(level)
                {   case ZIMapConnection.Monitor.Debug:     LineTool.Extra(text); 
                                                            return true;
                    case ZIMapConnection.Monitor.Info:      LineTool.Info(text);
                                                            return true;
                    case ZIMapConnection.Monitor.Error:     ZIMapAdmin.ErrorCalled = true;
                                                            LineTool.Error(text);
                                                            return true;
                    default:                                return true;
                }
            }

            public override bool Progress(ZIMapConnection connection, uint percent)
            {   if(ZIMapAdmin.Output < OutLevel.All) return true;
                if(ZIMapAdmin.Debug > 0)             return true;
                if(percent < 100) LineTool.Progress("Working [", percent, 
                                                    string.Format("] {0,2}%\r", percent));
                return true;
            }
            
            // kill program on exception ...
            public override bool Error(ZIMapConnection connection, ZIMapException error)
            {   if(Thread.CurrentThread.ThreadState == ThreadState.AbortRequested)
                    return true;
                ZIMapAdmin.Error("Error [exception]: {0}", error.Message);
                if(!Executing)
                {   if(LineTool.LogWriter != null) LineTool.LogWriter.Close();
                    ZIMapAdmin.Error("Terminating the program");
                    System.Environment.Exit(2);
                }
                if(Debug == 0) LineTool.Info(
                    "Ignoring the error. Use the 'debug 2' command to enable debug output.");
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
            
            // Debug stuff
#if DEBUG            
            if(cmd == "xxx")
            {    ZIMapFactory.Bulk bulk = new ZIMapFactory.Bulk(App.Factory, "Examine", 8, false);
                uint urub = 0;
                uint urdy = 0;
                uint umax = 5000;
                ZIMapCommand.Generic cmdb = null;
                while(urdy < umax)
                {   if(bulk.NextCommand(ref cmdb))
                    {   cmdb.CheckSuccess(); urdy++;
                        ProgressReporting.Update(urdy, umax);
                        cmdb.Reset();
                    }
                    if(urub++ < umax)
                    {   //cmdb.AddLiteral("unfug");
                        cmdb.AddString("unfug");
                        cmdb.AddString("unfugxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
                        cmdb.Queue();
                    }
                }
                bulk.Dispose();
                return true;
            }    
#endif            
            List<string> opts = new List<string>();
            List<string> args = new List<string>();
            if(!string.IsNullOrEmpty(cmd))
            {   ZIMapParser parser = new ZIMapParser(cmd);
                cmd = parser[0].ToString();

                bool bimap = cmd == "imap";
                for(int irun=1; irun < parser.Length; irun++)
                {   ZIMapParser.Token token = parser[irun];
                    if(!bimap && token.Type == ZIMapParser.TokenType.Quoted)
                        args.Add(token.Text);
                    else if(token.Type != ZIMapParser.TokenType.Text ||
                            token.Text.Length <= 1 ||
                            !(token.Text[0] == '-' || token.Text[0] == '/'))
                        args.Add(token.ToString());
                    else
                        opts.Add(token.Text.Substring(1));
                }
            }

            ErrorCalled = false;
            bool bok = Execute(ref cmd, opts.ToArray(), args.ToArray());
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
        public static bool Execute(ref string cmd, string[] opts, params string[] args)
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
            {   string[] list = ZIMapConverter.StringArray(commands[offset+1]);
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
                alis = ZIMapConverter.StringArray(aset);    // arg defs array
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
            if(Debug > 0 || Batch)
                Message("Command: {0} {1}", cmd, args == null ? "" : string.Join(" ", args));
            ProgressReporting.Reset();
            Executing = true;

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
                    if(Debug > 0) LineTool.Write(ex.InnerException.StackTrace);
                }
                else if(Debug > 0)
                    LineTool.Write(ex.StackTrace);
                return false;
            }
            finally
            {   if(!Cache.Data.Caching) Cache.Data.Clear(CacheData.Info.All);
                Executing = false;
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
                string[] opts = ZIMapConverter.StringArray(commands[irun+1]);
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
            "debug",    "level",    "Output debug information (level 0...3)",
            "",         "",         "",
            "help",     "",         "Print this text and quit",
            "command",  "",         "Print the list of commands and quit",
        };

        private static string[] commands = {
            "", "", "", "Commands to examine or to modify mailboxes",

            "list",     "all counts subscription rights quota", "{filter}",
                        "Lists mailboxes.  The filter can be a full mailbox name or may " +
                        "contain '%' to match inside one hierarchy level or '*' to match " +
                        "anything.",
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
            "delete",   "recurse", "{mailbox}...",
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
            "expunge",  "id uid", "[{item}...|*]",
                        "Remove mails flagged as deleted from the current mailbox (otherwise " +
                        "this happens automatically when the mailbox is explicitly closed or " +
                        "if another mailbox is opened).  If a list of items or * is given " +
                        "this first flags the items as deleted and then executes expunge.",
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
            "rights",   "recurse all read write none custom deny", "{mailbox} [{rights} [{user}...]]",
                        "Change the rights for a mailbox.  The flags -all -read -write -none " +
                        "and -custom are exclusive.  By default rights are granted, the -deny " +
                        "flag  adds 'negative' rights.  When -custom is give a list of custom " +
                        "rights must follow the mailbox name.",

            "", "", "", "Special purpose commands",

            "imap",     "verbatim", "command [{argument}|?|#]...",
                        "Execute an IMap command.  By default the command arguments get parsed and " +
                        "reassembled, use -verbatim to run an unparsed command.  You can use ? chars " +
                        "as placeholders for literal data and # chars as placeholders for mailbox names.",
            "info",     "mailbox server application id uid headers body", "[{mailbox}|{item}]",
                        "Output information about a mailitem (default), a mailbox, the server or " +
                        "the application.  For a mailitem the item number is required.",
            "cache",    "clear on off", "",
                        "Enable or disable caching of IMap data (-on and -off) or clear " +
                        "the current cache content.  The default option is -on.",
            "debug",    "", "[{level]]",
                        "Enable debug output.  Valid values for level are:  0 for no debug output, " +
                        "1 normal, 2 more and 3 all debug output.  When no level is specified the " +
                        "current level is show.  Level 3 works only with debug builds.",
            
            "", "", "", "Miscellaneous commands",

            "search",   "header body or query", "[{query}] values...",
                        "Search the current mailbox for messages.  If neither -header nor -body are " +
                        "header and body are searched.  The default for a simple query is to match " +
                        "messages that contain all given values (and).  This can be changed to match " +
                        "messages that contain any of the give values using -or.  With -query you can " +
                        "specify more complex queries with ? characters as placeholders for values.",
            "export",   "list recurse override quoted id uid", "{file|path} [{mailbox}] [{id|uid}...]]",
                        "Export mails to mbox files.  The -override option will cause existing " +
                        "files to be overridden without a warning.  The -recurse option writes to a " +
                        "folder and recurses mailboxes.  The default mbox format uses 'Content-Length' " +
                        "instead of 'From quoting' and stores IMap flags in the header.  Use -quoted " +
                        "for a 'From quoted' mbox format without IMap flags.",
            "import",   "list recurse noflags clean", "{file|path} [{mailbox}]",
                        "Import mails from mbox files into the given mailbox.  The -recurse option " +
                        "takes an input folder and creates child mailboxes as needed.  Usually IMap " +
                        "flags are restored unless -noflags is used.  With -clean 'X-' headers are " +
                        "removed from input.",
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
        public static uint CheckExclusive(string[] opts, params string[] names)
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
        
        private static bool CheckArgCount(string[] args, uint umin, uint umax)
        {   uint ucnt = (uint)((args == null) ? 0 : args.Length);
            if(umax == 0) umax = uint.MaxValue;
            // no arguments
            if(umin == 0)
            {   if(ucnt == 0) return true;
                Error("No argument allowed, got {0}", ucnt);
                return false;
            }
            // one argument
            if(umin == 1 && umax == 1)
            {   if(ucnt == 1) return true;
                if(ucnt == 0) Error("Required argument missing");
                else          Error("Only one argument allowed, got {0}", ucnt);
                return false;
            }
            // range
            if(ucnt < umin)
            {   Error("Missing arguments (minimum {0}) got {1}", umin, ucnt);
                return false;
            }
            if(ucnt > umax)
            {   Error("To many arguments (maximum {0}) got {1}", umax, ucnt);
                return false;
            }
            return true;
        }
        
        private static bool CheckIdAndUidUse(string[] opts, out bool id, out bool uid)
        {   id  = HasOption(opts, "id");
            uid = HasOption(opts, "uid");
            if(!id && !uid) return true;
            
            if(CheckExclusive(opts, "id", "uid") > 1) return false;
            if(!UseIdOk &&
               !Confirm("Are you sure that you really understand how to use -id or -uid"))
                return false;
            UseIdOk = true;
            return true;
        }

        private static bool CheckNumber(string[] args, uint index, out uint number)
        {   number = 0;
            if(args == null || index >= args.Length)
            {   Error("Missing numeric argument");
                return false;
            }
            return CheckNumber(args[index], out number);
        }

        private static bool CheckNumber(string arg, out uint number)
        {   number = 0;
            if(!string.IsNullOrEmpty(arg) && uint.TryParse(arg, out number)) return true;
            Error("Argument is not a number: {0}", arg);
            return false;
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
                Fatal("Invalid command line. Try /help to get usage info.");

            foreach(ArgsTool.Option o in opts)
            {   if(o.Error == ArgsTool.OptionStatus.Ambiguous)
                    Fatal("Ambiguous option: {0}", o.Name);
                if(o.Error != ArgsTool.OptionStatus.OK)
                    Fatal("Invalid option: {0}. Try /help to get usage info", o.Name);

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
                                        Fatal("No log file specified");
                                    break;
                    case "debug":   Debug = 1;
                                    if(o.Value != null && !uint.TryParse(o.Value, out Debug))
                                        Fatal("Invalid debug level: {0}", o.Value);
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
                                        Fatal("Invalid timeout: {0}", o.Value);
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

            if(Log != null)
            {   try 
                {   LineTool.LogWriter = new System.IO.StreamWriter(Log);
                }
                catch(Exception ex)
                {   Fatal("Failed to open logfile: " + ex.Message);
                }
            }
            
            // --- step 2: prompt for missing parameters

            if(Commands != null && Commands.Length > 0)
            {   if(confirm != 1) LineTool.AutoConfirm = true;
                if(Output == OutLevel.Undefined) Output = OutLevel.Brief;
                Batch = true;

                string missing = null;
                if     (Server  == null)  missing = "server";
                else if(Account == null)  missing = "account";
                if(missing != null)
                    Fatal("Please add a '-{0}' option to your command line", missing);
            }
            else
            {   if(confirm == 2) LineTool.AutoConfirm = true;
                if(Output == OutLevel.Undefined) Output = OutLevel.All;
                Batch = false;

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
            if(!Batch)
                Message(string.Format("Connecting {0}://{1}@{2} ...", Protocol, Account, Server));
            ZIMapConnection.Callback = new IMapCallback();

            App = new ZIMapApplication(Server, port);

            if(Timeout != 0) App.Timeout = Timeout;
            DebugLevel(Debug);

            if(!App.Connect(Account, Password, tlsmode))
                Fatal("Failed to connect");
            ProgressReporting = App.Connection.ProgressReporting;
            Cache = new CacheData(App);
            
            if(Output >= OutLevel.Info)
                Info("Server: " + App.Connection.ProtocolLayer.ServerGreeting);
            if(!App.Factory.HasCapability("IMAP4rev1"))
                Error("WARNING: This is not an IMAP4rev1 server!");
            
            // --- step 4: Open mailbox, Execute Commands

            if(MailBoxName == null || Execute("open -write " + MailBoxName))
            {
                // has commands from command line...
                if(Batch)
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
                    if(App.Server.IsAdmin && App.EnableNamespaces)
                    {   Message("You are logged-in as an administrator - changing default namespace...");
                        Execute("user *");
                    }
                    while(true)
                    {   System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        string qual = Cache.Data.Qualifier;
                        if(qual == null) qual = "[no qualifier]";
                        else
                        {   uint nsid = App.Server.FindNamespace(qual, false);
                            if(nsid == ZIMapServer.Personal)    qual = "[personal]";
                            else if(nsid == ZIMapServer.Others) qual = "[other users]";
                            else if(nsid == ZIMapServer.Shared) qual = "[shared folders]";
                            else if(nsid == ZIMapServer.Search) qual = "[search results]";
                        }
                        sb.Append(qual);
                        sb.Append(Ascii ? ':' : '■');
                        string cmd = Cache.Data.Current.Name;
                        if(string.IsNullOrEmpty(cmd)) sb.Append("[no mailbox]");
                        else                          sb.Append(cmd);
                        cmd = LineTool.Prompt(sb.ToString());
                        if(string.IsNullOrEmpty(cmd)) break;
                        Execute(cmd);
                    }
                }
            }

            // --- step 5: Disconnect and exit

            App.Disconnect();
            if(LineTool.LogWriter != null) LineTool.LogWriter.Close();
            return;
        }
    }
}