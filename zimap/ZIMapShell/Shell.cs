//==============================================================================
// Shell.cs - A simple IMap shell
//==============================================================================

using System;
using System.Text;
using System.Collections.Generic;

using ZTool;

namespace ZIMap
{
    // =========================================================================
    // Hook ZIMapLib to format Debug/Error messages 
    // =========================================================================
    class IMapCallback : ZIMapConnection.CallbackDummy
    {
        public override bool Monitor (ZIMapConnection connection, ZIMapMonitor level, string message)
        {   if(message == null) return true;
            switch(level)
            {   case ZIMapMonitor.Debug:    LineTool.Extra(message); return true;
                case ZIMapMonitor.Info:     LineTool.Info(message);  return true;
                case ZIMapMonitor.Error:    LineTool.Error(message); return true;
            }
            return base.Monitor(connection, level, message);
        }
    }
    
    // =========================================================================
    // Class to implement the ZIMapShell program 
    // =========================================================================
    class ZIMapShell
    {
        // The loop that does the real work
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
                connection.MonitorLevel = debug ? ZIMapMonitor.Debug : ZIMapMonitor.Error;
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
                ZIMapReceiveData data;
                protocol.Receive(out data);
                if(!data.Succeeded)
                {   LineTool.Error("Error: {0}", data.Message);
                    return false;
                }
            }
            
            string[] command;
            while((command = LineTool.Prompt("IMAP", 2)) != null) {
                try {
                    string message = command[0].ToUpper();
                    if(command.Length > 1) message += " " + command[1];
                    protocol.Send(message);
                    uint tag;
                    string status;
                    ZIMapReceiveState info;
                    do {
                        info = protocol.Receive(out tag, out status, out message);
                        LineTool.Message("{0} {1} {2}", tag, status, message);
                    }
                    while(info == ZIMapReceiveState.Info);
                    
                    if(connection.IsTransportClosed) return false;
                }
                catch(Exception e) {
                    LineTool.Error("Command failed with exception: {0}", e.Message);
                }
            }
            return true;
        }
        
        // Print usage info
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

        // Parse the command line, connect to the server and run command loop
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