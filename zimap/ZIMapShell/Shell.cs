//==============================================================================
// Shell.cs - A simple IMap shell
//==============================================================================

using System;
using System.Text;
using System.Collections.Generic;

using ZTool;
using ZIMap;

namespace ZIMapTools
{
    // =========================================================================
    // Class to implement the ZIMapShell program 
    // =========================================================================
	
	/// <summary>
	/// A simple interactive IMAP command shell. 
	/// </summary>
	/// This program knows about IMAP data encodings (literals and mailbox names
	/// for example) and supports SSL.  It is a nice tool for learning IMAP
	/// commands by trying them out interactively.
    class ZIMapShell
    {
	    // =====================================================================
	    // Hook ZIMapLib to format Debug/Error messages 
	    // =====================================================================

	    class IMapCallback : ZIMapConnection.CallbackDummy
	    {
	        public override bool Monitor(ZIMapConnection connection, ZIMapConnection.Monitor level, 
	                                     string origin, string message)
	        {   if(origin == null || message == null) return true;
	            switch(level)
	            {   case ZIMapConnection.Monitor.Debug:    
	                        LineTool.Extra("{0}: {1}", origin, message); return true;
	                case ZIMapConnection.Monitor.Info:    
	                        LineTool.Info("{0}: {1}", origin, message); return true;
	                case ZIMapConnection.Monitor.Error:
	                        LineTool.Error("{0}: {1}", origin, message); return true;
	            }
	            return true;
	        }
	    }
    
	    // =====================================================================
        // The 'command loop' routine
	    // =====================================================================

		/// <summary>The 'command loop' routine</summary>
		/// <param name="server">A name or URL to contact the server</param>
		/// <param name="prot">A protocol name like 'imap' or 'imaps'</param>
		/// <param name="mode">SSL handling, see 
		///        <see cref="ZIMapConnection.TlsModeEnum"/></param>
		/// <param name="timeout">Timeout in [s]</param>
		/// <param name="account">The IMAP account (user) to be used</param>
		/// <param name="password">Password for the given account</param>
		/// <param name="debug">Turns debug output on</param>
		/// <returns><c>true</c> on success</returns>
		public static bool Run(string server, string prot, 
                               ZIMapConnection.TlsModeEnum mode, uint timeout,
                               string account, string password, bool debug)
        {
            ZIMapConnection connection;
            ZIMapProtocol protocol;
            ZIMapConnection.Callback = new IMapCallback();
            
            try {
                connection = ZIMapConnection.GetConnection(server, 
                                ZIMapConnection.GetIMapPort(prot), mode, timeout);
                if(connection == null) 
                {   LineTool.Error("Connect failed");
                    return false;
                }
                connection.MonitorLevel = debug ? ZIMapConnection.Monitor.Debug 
                                                : ZIMapConnection.Monitor.Error;
                protocol = connection.ProtocolLayer;
                protocol.MonitorLevel = connection.MonitorLevel;
                connection.TransportLayer.MonitorLevel = connection.MonitorLevel;
                LineTool.Info(protocol.ServerGreeting);
            }
            catch(Exception e) {
                LineTool.Error("Connect failed with exception: {0}", e.Message);
                return false;
            }
            
            if(!string.IsNullOrEmpty(account))
            {   StringBuilder sb = new StringBuilder("LOGIN ");
                if(!ZIMapConverter.QuotedString(sb, account, false) ||
                   sb.Append(' ') == null ||
                   !ZIMapConverter.QuotedString(sb, password, false))
                {   LineTool.Error("Error: Can use only 7-bit data for '-account'");
                    return false;
                }
                LineTool.Info(sb.ToString());                
                protocol.Send(sb.ToString());
                ZIMapProtocol.ReceiveData data;
                protocol.Receive(out data);
                if(!data.Succeeded)
                {   LineTool.Error("Error: {0}", data.Message);
                    return false;
                }
            }
            
            // The loop that does the real work
            string[] command;
            while((command = LineTool.Prompt("IMAP", 2)) != null) {
                try {
                    string message = command[0].ToUpper();
                    if(command.Length > 1) message += " " + command[1];
                    protocol.Send(message);
                    uint tag;
                    string status;
                    ZIMapProtocol.ReceiveState info;
                    do {
                        info = protocol.Receive(out tag, out status, out message);
                        LineTool.Message("{0} {1} {2}", tag, status, message);
                    }
                    while(info == ZIMapProtocol.ReceiveState.Info);
                    
                    if(connection.IsTransportClosed) return false;
                }
                catch(Exception e) {
                    LineTool.Error("Command failed with exception: {0}", e.Message);
                }
            }
            return true;
        }
        
        /// <summary>Print the usage info message.</summary> 
        public static void Usage()
        {   Console.WriteLine(ArgsTool.Usage(ArgsTool.UsageFormat.Usage,
                          ArgsTool.Param(options, "server",  false),
                          ArgsTool.Param(options, "protocol", true),
                          ArgsTool.Param(options, "account",  true)));
            Console.WriteLine(ArgsTool.Usage(ArgsTool.UsageFormat.Cont,
                          ArgsTool.Param(options, "password", true),
                          ArgsTool.Param(options, "timeout",  true),
                          ArgsTool.Param(options, "ascii",    true),
                          ArgsTool.Param(options, "debug",    true)));
            Console.WriteLine(ArgsTool.Usage(ArgsTool.UsageFormat.More, "-help"));
            Console.WriteLine("\n{0}\n",
                          ArgsTool.Usage(ArgsTool.UsageFormat.Options, options));
        }

	    // =====================================================================
        // Parse the command line, connect to the server and run command loop
	    // =====================================================================
        
        private static string[] options = {
            "server",   "host",     "Connect to a server at {host}",
            "protocol", "name",     "Use the protocol {name}             (default: imap)",
            "account",  "user",     "Login using the {user} account",
            "password", "text",     "Use the login password {text}",
            "timeout",  "seconds",  "Connection/Read/Write timeout         (default: 30)",
            "ascii",    "",         "Do not use line drawing chars or colors",
            "debug",    "",         "Output debug information",
            "help",     "",         "Print this text and quit",
        };

		/// <summary>
		/// Parse the command line, connect to the server and run command loop.
		/// </summary>
		/// <param name="args">
		/// The command line arguments.
		/// </param>
		public static void Main(string[] args)
        {   string server  = null;
            string account = null;
            string password= null;
            string prot    = "imap";
            uint   timeout = 30;
            bool   debug   = false;

            ZIMapConnection.TlsModeEnum tlsmode = ZIMapConnection.TlsModeEnum.Automatic;

            ArgsTool.Option[] opts = ArgsTool.Parse(options, args);
            if(opts == null)
            {   Console.WriteLine("Invalid command line, try /help to get usage info.");
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
                    case "ascii":   LineTool.EnableColor = false;   
                                    break;
                    case "debug":   debug = true;
                                    break;
                    case "server":  server = o.Value;
                                    break;
                    case "protocol":
                                    prot = o.Value;
                                    if(o.SubValues != null)
                                    {   if(o.SubValues[0] == "tls"  )
                                            tlsmode = ZIMapConnection.TlsModeEnum.Required;
                                        if(o.SubValues[0] == "notls")
                                            tlsmode = ZIMapConnection.TlsModeEnum.Disabled;
                                    }
                                    break;
                    case "timeout":
                                    if(!uint.TryParse(o.Value, out timeout))
                                    {   Console.WriteLine("Invalid timeout: " + o.Value);
                                        return;
                                    }
                                    break;
                    case "account": account = o.Value;
                                    break;
                    case "password":
                                    password = o.Value;
                                    break;
               }
            }
            opts = null;

            if(server == null)
            {   server = LineTool.Prompt("Server  ");
                if(string.IsNullOrEmpty(server)) return;
            }

            if(account != null && password == null)
            {   password = System.Environment.GetEnvironmentVariable("ZIMAP_PWD");
                if(string.IsNullOrEmpty(password))
                {   password = LineTool.Prompt("Password");
                    if(string.IsNullOrEmpty(password)) return;
                }
            }
            
            LineTool.Message("Connecting {0}://{1} ...", prot, server);
            if(Run(server, prot, tlsmode, timeout, account, password, debug))
                LineTool.Info("Exit after empty input line");
        }
    }
}