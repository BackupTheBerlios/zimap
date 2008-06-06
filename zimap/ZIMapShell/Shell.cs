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
    // 
    // =========================================================================
    class MainClass
    {
        public static void Message(string format, params object[] parameters)
        {
            Message(string.Format(format, parameters));
        }
        public static void Message(string message)
        {
            Console.WriteLine("***** " + message);
        }
        
        public static void Run(string server, string prot, 
                               ZIMapConnection.TlsModeEnum mode, uint timeout, bool debug)
        {
            ZIMapConnection connection;
            ZIMapProtocol protocol;
            
            try {
                connection = ZIMapConnection.GetConnection(server, ZIMapConnection.GetIMapPort(prot), 
                                                           mode, timeout);
                connection.MonitorLevel = debug ? ZIMapMonitor.Debug : ZIMapMonitor.Error;
                if(connection == null) {
                    Message("Connect failed");
                    return;
                }
                protocol = connection.ProtocolLayer;
                protocol.MonitorLevel = connection.MonitorLevel;
                connection.TransportLayer.MonitorLevel = connection.MonitorLevel;
                Message(protocol.ServerGreeting);
            }
            catch(Exception e) {
                Message("Connect failed with exception: " + e.Message);
                return;
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
                        Console.WriteLine("      {0} {1} {2}", tag, status, message);
                    }
                    while(info == ZIMapReceiveState.Info);
                    
                    if(connection.IsTransportClosed) return;
                }
                catch(Exception e) {
                    Message("Command failed with exception: " + e.Message);
                    return;
                }
            }
        }
        
        public static void Usage()
        {   Console.WriteLine("Usage:   {0} {1} {2} {3} {4}", ArgsTool.AppName,
                          ArgsTool.Param(options, "server",  false),
                          ArgsTool.Param(options, "protocol", true),
                          ArgsTool.Param(options, "timeout",  true),
                          ArgsTool.Param(options, "debug",    true));
            Console.WriteLine("         {0} -help\n\n{1}\n", ArgsTool.AppName,
                          ArgsTool.List(options, "Options: "));
        }
        
        private static string[] options = {
            "server",   "host",     "Connect to a server at {host}",
            "protocol", "name",     "Use the protocol {name}             (default: imap)",
            "timeout",  "seconds",  "Connection/Read/Write timeout         (default: 30)",
            "debug",    "",         "Output debug information",
            "help",     "",         "Print this text and quit",
        };

        public static void Main(string[] args)
        {
            string server  = null;
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
                }
            }
            opts = null;

            if(server == null)
            {   server = LineTool.Prompt("Server  ");
                if(string.IsNullOrEmpty(server)) return;
            }

            Message("Connecting {0}://{1} ...", prot, server);
            Run(server, prot, tlsmode, timeout, debug);
            Message("Quit");
        }
    }
}