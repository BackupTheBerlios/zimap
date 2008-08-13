//==============================================================================
// ZIMapFactory.cs implements the ZIMapFactory class    
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU Lesser General Public License
// This software is published under the GNU LGPL license. Please refer to the
// files COPYING and COPYING.LESSER for details. Please use the following e-mail
// address to contact me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.Text;
using System.Collections.Generic;

namespace ZIMap
{
    //==========================================================================
    // ZIMapFactory
    //==========================================================================

    /// <summary>
    /// The root of the IMap command layer that simplifies the execution of
    /// IMap commands (Command Layer).
    /// </summary>
    /// <remarks>
    /// This class manages IMap commands via the <see cref="ZIMapCommand"/> class.
    /// The important point is that this class 'owns' the commands and can control
    /// their life cycle (see the <see cref="Commands"/> property for example). For
    /// an application it appears as if commands get automatically excuted in 
    /// background. After the application has called the <see cref="ZIMapCommand.Queue"/>
    /// method, it can simply access the server response 
    /// (see <see cref="ZIMapCommand.Result"/>).  Obviously this will be blocking until
    /// the server response got received, but any number of commands can be run in
    /// parallel.  An application can easily 'batch' some commands and then try to
    /// consume the results.  This programming style greatly reduces latency times
    /// and helps to build fast IMap applications.
    /// <para />
    /// Commands are created (see <see cref="CreateGeneric"/> and friends), are
    /// queued for execution (see <see cref="QueueCommand"/>) and last but not
    /// least they are associated with the server response
    /// (see <see cref="ExecuteCommands(bool)"/> and <see cref="Completed"/>).
    /// <para />
    /// Finally commands can be automatically or manually removed from the factory
    /// (see <see cref="DisposeCommands"/> and <see cref="EnableAutoDispose"/>).
    /// <para />
    /// For each connection there should be at most one factory.  For this reason the
    /// class cannot be directly instantiated.  Use <see cref="ZIMapConnection.CommandLayer"/>
    /// to get an instance that is parented by a connection. 
    /// <para />
    /// This class belongs to the <c>Command</c> layer which is the second highest of four
    /// layers:
    /// <para />
    /// <list type="table">
    /// <listheader>
    ///   <term>Layer</term>
    ///   <description>Description</description>
    /// </listheader><item>
    ///   <term>Application</term>
    ///   <description>The application layer with the following important classes:
    ///   <see cref="ZIMapApplication"/>, <see cref="ZIMapServer"/> and <see cref="ZIMapExport"/>.
    ///   </description>
    /// </item><item>
    ///   <term>Command</term>
    ///   <description>The IMap command layer with the following important classes:
    ///   <see cref="ZIMapFactory"/> and <see cref="ZIMapCommand"/>.
    ///   </description>
    /// </item><item>
    ///   <term>Protocol</term>
    ///   <description>The IMap protocol layer with the following important classes:
    ///   <see cref="ZIMapProtocol"/> and  <see cref="ZIMapConnection"/>.
    ///   </description>
    /// </item><item>
    ///   <term>Transport</term>
    ///   <description>The IMap transport layer with the following important classes:
    ///   <see cref="ZIMapConnection"/> and  <see cref="ZIMapTransport"/>.
    ///   </description>
    /// </item></list>
    /// </remarks>
    public abstract class ZIMapFactory : ZIMapBase, IDisposable
    {
        // command are held in a single list ...
        private List<ZIMapCommand>  commands;
        // controls the DisposeCommand method ...
        private bool                autoDispose = true;

        // for the Capabilities property
        private string[]            capabilities;

        // for the HierarchyDelimiter property
        private char                hierarchyDelimiter;

        // set for ZIMapCommand.Login via QueueCommand()
        private string              username;
                
        /// <summary>The one and only constructor.</summary>
        /// <param name="parent">
        /// The owning connection.
        /// </param>
        /// <remarks>
        /// The constructor is protected, use <see cref="ZIMapConnection.CommandLayer"/>
        /// to get an instance of a factory.
        /// </remarks>
        protected ZIMapFactory(ZIMapConnection parent) : base(parent) {}

        // =====================================================================
        // Accessors for properties
        // =====================================================================

        /// <summary>
        /// On success an array of capability names -or- <c>null</c> on error.
        /// </summary>
        /// <value>An array of strings where each string represents a 
        /// capability name.</value>
        /// <remarks>
        /// The factory caches the result and unless the property is explicitly
        /// set to <c>null</c> it will always return the same object.  Please
        /// note that if the capabilites depend on the server state and thus
        /// change at login.
        /// </remarks>
        public string[] Capabilities
        {   get {   if(capabilities == null)
                    {   ZIMapCommand.Capability cap = new ZIMapCommand.Capability(this);   
                        capabilities = cap.Capabilities;
                        cap.Dispose();
                    }
                    return capabilities;
                }
            set {   capabilities = null;
                    if(value != null) RaiseError(ZIMapException.Error.MustBeZero);
                }
        }

        /// <summary>
        /// Returns the parent <see cref="ZIMapConnection"/> object.
        /// </summary>
        /// <remarks>
        /// This is a fast and simple accessor. 
        /// </remarks>
        public ZIMapConnection Connection
        {   get {   return (ZIMapConnection)Parent;    }
        }

        /// <summary>
        /// Controls if <see cref="DisposeCommands"/> gets called by commands
        /// when they are disposed.
        /// </summary>
        /// <value>The default value is <c>true</c>.</value>
        /// <remarks>
        /// The <see cref="ZIMapCommand.Dispose"/> method will call 
        /// <c>DisposeCommand(this, false)</c> if this property returns <c>true</c>.
        /// <para />
        /// Disabling auto dispose completely is somewhat exotic and should be avoided.
        /// It is possible to disable auto dispose on a per command level, see
        /// <see cref="ZIMapCommand.AutoDispose"/>.  
        /// </remarks>
        public bool EnableAutoDispose
        {   get {   return autoDispose; }
            set {   autoDispose = value; }
        }
        
        /// <summary>
        /// <c>true</c> if there at least one command has an outstanding reply.
        /// </summary>
        /// <remarks>
        /// Searches the list of command for an entry with a command state 
        /// of <see cref="ZIMapCommand.CommandState.Running"/>. This is much faster
        /// than checking the array returned by <see cref="RunningCommands"/>.
        /// </remarks>
        public bool HasRunningCommands
        {   get {   if(GetCommands() == null) return false; // parent closed
                    foreach(ZIMapCommand cmd in commands)
                        if(cmd.State == ZIMapCommand.CommandState.Running) return true;
                    return false;
                }
        }
        
        /// <summary>
        /// Gets os sets the server's default Hierarchy Delimiter character.
        /// </summary>
        /// <remarks>
        /// This command requires a valid login. Zero is returned if the
        /// delimiter character cannot be determined. On success the result
        /// get cached, only on the 1st call a 'List "" ""' command is sent.
        /// <para />
        /// This property also works when Namespace support is disabled. To
        /// get the delimiter it issues an IMap <c>LIST "" ""</c> command.
        /// </remarks>
        public char HierarchyDelimiter
        {   get {   if(hierarchyDelimiter == (char)0)
                    {   ZIMapCommand.List list = new ZIMapCommand.List(this);
                        list.Queue("", "");
                        ZIMapCommand.List.Item [] items = list.Items;
                        if(items != null && items.Length > 0)
                            hierarchyDelimiter = items[0].Delimiter;
                        list.Dispose();
                    }
                    return hierarchyDelimiter;
                }
            set {   hierarchyDelimiter = (char)0;
                }
        }
        
        /// <summary>
        /// Return an array of all commands that are attached to this factory instance.
        /// </summary>
        /// <value>An array created from the internal command list.</value>
        /// <remarks>
        /// When no command is found, an empty array is returned.
        /// <para />
        /// The factory keeps all attached commands in a single list.  While 
        /// <see cref="Commands"/> returns all commands independently of their state,
        /// <see cref="CompletedCommands"/> and <see cref="RunningCommands"/> filter
        /// the the data before returning a subset.<para />
        /// <example><code lang="C#">
        ///     int irun = RunningCommands.Length;
        ///     int irdy = CompletedCommands.Length;
        ///     int iall = Commands.Length;
        ///     Console.WriteLine("AutoDispose={0}  Commands={1} (completed={2}  running={3})",
        ///                                         EnableAutoDispose, iall, irdy, irun);
        /// </code></example>
        /// </remarks>
        public ZIMapCommand[] Commands
        {   get {   if(GetCommands() == null) return null;  // parent closed
                    return commands.ToArray();  
                }
        }
        
        /// <summary>
        /// Return an array of all completed commands (success or failed) that are still
        /// attached to this factory instance.
        /// </summary>
        /// <value>An array created from a subset of the internal command list.</value>
        /// <remarks>
        /// When no command with the appropriate state is found, an empty array is returned.
        /// <para />
        /// The factory keeps all attached commands in a single list.  While 
        /// <see cref="Commands"/> returns all commands independently of their state,
        /// <see cref="CompletedCommands"/> and <see cref="RunningCommands"/> filter
        /// the the data before returning a subset.
        /// </remarks>
        public ZIMapCommand[] CompletedCommands
        {   get {   return GetCommands(true);  }
        }
        
        /// <summary>Get commands that are running but not yet completed and that
        /// are attached to this factory instance.
        /// </summary>
        /// <remarks>
        /// When no command with state <see cref="ZIMapCommand.CommandState.Running"/>
        /// is found, an empty array is returned.
        /// <para />
        /// The factory keeps all attached commands in a single list.  While 
        /// <see cref="Commands"/> returns all commands independently of their state,
        /// <see cref="CompletedCommands"/> and <see cref="RunningCommands"/> filter
        /// the the data before returning a subset.
        /// </remarks>
        public ZIMapCommand[] RunningCommands
        {   get {   return GetCommands(false);  }
        }

        /// <summary>
        /// Return the User name of the last ZIMapCommand.Login queued command.
        /// </summary>
        /// <remarks>
        /// This parameter gets updated when the Login command executes Queue().
        /// </remarks>
        public string User
        {   get {   return username;  }
        }
        
        // =====================================================================
        // Methods
        // =====================================================================

        /// <summary>
        /// Release all resources, cancel commands.
        /// </summary>
        /// <remarks>
        /// This method calls <see cref="DisposeCommands"/> to cancel all commands
        /// and resets the factory instance to its initial state.
        /// </remarks>
        public void Dispose()
        {   if(commands == null)                    // nothing to do...
                return;
            DisposeCommands(null, true);
            capabilities = null;
            hierarchyDelimiter = (char)0;
            commands = null;
            username = null;
            autoDispose = true;
        }

        /// <summary>
        /// Release and cancel commands.
        /// </summary>
        /// <remarks>
        /// When a command instance is passed as 1st argument this command and all commands
        /// with a lower Tag number (e.g. that are older) get disposed unless they are still
        /// busy. The given command must have been sent (e.g. must have a non-zero Tag).
        /// This method is invoked by <see cref="ZIMapCommand.Dispose"/> method if the owning
        /// factorie's <see cref="EnableAutoDispose"/> property is set.
        /// <para />
        /// Unsually one would like that only commands having the <see cref="ZIMapCommand.AutoDispose"/>
        /// property set are affected.  A call with <paramref name="overrideAutoDispose"/> 
        /// set <c>true</c> will include all commands.
        /// </remarks>
        public bool DisposeCommands(ZIMapCommand lastToDispose, bool overrideAutoDispose)
        {
            if(lastToDispose == null && overrideAutoDispose)
                MonitorDebug( "Disposing " + commands.Count + " command(s)");
            
            uint tag = 0;
            if(lastToDispose != null)                       // get target tag
            {   tag = lastToDispose.Tag;
                MonitorDebug("Disposing (auto): " + tag);
                if(tag == 0)
                {   RaiseError(ZIMapException.Error.CommandState, "not sent, no tag");
                    return false;
                }
            }
            
            bool auto = autoDispose;
            try
            {   autoDispose = false;                        // prevent recursion   
                foreach(ZIMapCommand cmd in GetCommands(true))
                {   if(cmd.Tag > tag) continue;             // younger than target
                    if(!overrideAutoDispose && !cmd.AutoDispose)
                                                  continue; // no autoDispose
                    MonitorDebug("Disposing (auto): " + cmd.Tag);
                    cmd.Dispose();
                }
            }
            finally
            {   autoDispose = auto;
            }
            return true;
        }

        // return all commands, never null (unless exception raised)
        private List<ZIMapCommand> GetCommands()
        {   if(commands != null) return commands;           // ok, on stock
            if( ((ZIMapConnection)Parent).CommandLayer != this)
            {   RaiseError(ZIMapException.Error.DisposedObject);
                return null;                                // parent closed
            }
            commands = new List<ZIMapCommand>();
            return commands;                                // new list
        }
        
        // return running or completed command, never null (unless exception)
        private ZIMapCommand[] GetCommands(bool bCompleted)
        {   if(GetCommands() == null) return null;          // parent closed
            int cntCompleted = 0;  int cntRunning = 0;
            // pass1: count the commands
            foreach(ZIMapCommand cmd in commands)
                switch(cmd.State)
                {   case ZIMapCommand.CommandState.Running:
                            cntRunning++; break;
                    case ZIMapCommand.CommandState.Completed:
                    case ZIMapCommand.CommandState.Failed:
                            cntCompleted++; break;
                }
            
            // pass2: copy to array
            int cnt = bCompleted ? cntCompleted : cntRunning;
            ZIMapCommand[] arr = new ZIMapCommand[cnt];
            if(cnt <= 0) return arr; 
            int run = 0;
            foreach(ZIMapCommand cmd in commands)
            {   switch(cmd.State)
                {   case ZIMapCommand.CommandState.Running:
                            if(bCompleted) continue; break;
                    case ZIMapCommand.CommandState.Completed:
                    case ZIMapCommand.CommandState.Failed:
                            if(bCompleted) break; continue;
                    default:
                         continue;
                }
                arr[run++] = cmd; if(run >= cnt) break;
            }
            return arr;               
        }

        /// <summary>
        /// Attach a command to the factory, send request to server. 
        /// </summary>
        /// <param name="command">
        /// The command that is to be attached and/or sent.
        /// </param>
        /// <returns>
        /// Returns <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// Normally this method is not invoked directly, but it get's called
        /// automatically from <see cref="ZIMapCommand.Queue"/>.  This is also
        /// the case for commands that were explicitly detached from the 
        /// factory.
        /// <para />
        /// If the command is already attached to the factory it will first be
        /// detached.  Then the command will be attached to the factory and will
        /// be placed at the beginning of the commands list.  If the command
        /// is not in the state <see cref="ZIMapCommand.CommandState.Queued"/>
        /// it will be queued (in other words: the request will be sent to
        /// the server).
        /// <para />
        /// Usually commands get never explicitly detached from the factory that
        /// owns them until they are disposed, see <see cref="DetachCommand"/>.
        /// An error of type <see cref="ZIMapException.Error.InvalidArgument"/>
        /// is raised if the command was created by another factory instance.
        /// </remarks>
        public bool QueueCommand(ZIMapCommand command)
        {   if(GetCommands() == null) return false;         // parent closed
            if(command.Parent != Parent)                    // my command?
            {   RaiseError(ZIMapException.Error.InvalidArgument, "wrong owner");
                return false;
            }
            commands.Remove(command);                       // may fail: ok
            commands.Add(command);                          // move to start
            if(command.State == ZIMapCommand.CommandState.Queued)
                return true;                                // no change
            return command.Queue();                         // tell the command
        }

        /// <summary>
        /// Remove a command from the commands list of the factory.
        /// </summary>
        /// <param name="command">
        /// The command to be removed.
        /// </param>
        /// <returns>
        /// Returns <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// An error of type <see cref="ZIMapException.Error.InvalidArgument"/>
        /// is raised if the command was created by another factory instance.
        /// <para />
        /// Usually commands get never explicitly detached from the factory that
        /// owns them until they are disposed.  But an application that wants to keep
        /// a command alife for a long time (to access the returned data or to reuse
        /// it later) should eventually detach the command from the factory.
        /// The <see cref="QueueCommand"/> method will (re-)attach it automatically 
        /// when the command is reused.  This also happens when 
        /// <see cref="ZIMapCommand.Queue"/> is called.
        /// </remarks>
        public bool DetachCommand(ZIMapCommand command)
        {   if(GetCommands() == null) return false;         // parent closed
            if(command.Parent != Parent)                    // my command?
            {   RaiseError(ZIMapException.Error.InvalidArgument, "wrong owner");
                return false;
            }
            if(!commands.Remove(command))
                                      return false;         // not in list
            if(command.State != ZIMapCommand.CommandState.Disposed)
                command.Reset();                            // tell the command
            return true;
        }

        /// <summary>
        /// Send all commands that are queued and optionally wait for the
        /// completion of all commands.
        /// </summary>
        /// <param name="wait">
        /// Wait for all commands to complete if <c>true</c>. 
        /// </param>
        /// <returns>
        /// <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// Before sending any command that contains literals this routine will
        /// wait for all running commads to finish.
        /// </remarks>
        public bool ExecuteCommands(bool wait)
        {   if(GetCommands() == null) return false;         // parent closed

            // Send queued commands to server ...
            foreach(ZIMapCommand cmd in commands)
            {   if(cmd.State != ZIMapCommand.CommandState.Queued) 
                    continue;
                if(!cmd.Execute(false))                     // send command
                    return false;
            }
            
            // Wait for results            
            return wait ? ExecuteRunning(null) : true;
        }

        /// <summary>
        /// Send all commands that are queued and optionally wait for the
        /// completion of all commands or of a specific command.
        /// </summary>
        /// <param name="waitfor">
        /// Waits for all commands to complete if <c>null</c> -or- for a
        /// single cammand if a non-null object reference is passed.
        /// </param>
        /// <returns>
        /// <c>true</c> on success.
        /// </returns>
        /// <remarks>((ZIMapConnection)factory.Parent)
        /// Even if the server returns an error status for one or more commands
        /// this routine signals success. Only network level or protocal error
        /// will cause <c>false</c> to be returned.
        /// </remarks>
        public bool ExecuteCommands(ZIMapCommand waitfor)
        {
            if(!ExecuteCommands(false))                     // send queued commands
                return false;
            return ExecuteRunning(waitfor);                 // Wait for results
        }

        /// <summary>
        /// Wait for running commands to complete.
        /// </summary>
        /// <param name="waitfor">
        /// Stop waiting after the completion of the given command, can be <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// This routine just waits for the results being receiced from the server.
        /// If does not send queued commands.  For a <c>null</c> argument the
        /// routine waits for all running commands to complete.
        /// </remarks>
        public bool ExecuteRunning(ZIMapCommand waitfor)
        {   while(HasRunningCommands)
            {   ZIMapProtocol.ReceiveData rs;
                MonitorDebug("ExecuteRunning: waiting");
                if( ((ZIMapConnection)Parent).ProtocolLayer.Receive(out rs) )
                {    Completed(rs);
                     if(waitfor != null && waitfor.Tag == rs.Tag)
                        break;
                }
                else
                {   MonitorError("ExecuteRunning: receive failed");
                    return false;
                }
            }
            return true;
        }
        
        public bool Completed(ZIMapProtocol.ReceiveData rdata)
        {   if(GetCommands() == null) return false;         // parent closed
            foreach(ZIMapCommand cmd in commands)
            {   if(cmd.Tag != rdata.Tag) continue;
                if(cmd is ZIMapCommand.Login && rdata.Succeeded)
                    username = ((ZIMapCommand.Login)cmd).User;
                return cmd.Completed(rdata);
            }
            return false;
        }
        
        public ZIMapCommand.Generic CreateGeneric(string command)
        {   if(GetCommands() == null) return null;          // parent closed
            ZIMapCommand.Generic cmd = new ZIMapCommand.Generic(this, command);
            commands.Add(cmd);
            return cmd;
        }

        /// <summary>
        /// Create a typed command object by it's name.
        /// </summary>
        /// <param name="name">
        /// An IMap command name like FETCH or SEARCH.
        /// </param>
        /// <returns>
        /// The create command object casted to <see cref="ZIMapCommand.Generic"/>.
        /// </returns>
        /// <remarks>
        /// Despite the return type this function for example creates for example a
        /// <see cref="ZIMapCommand.Fetch"/> object when the argument is FETCH.
        /// <para />
        /// The argument case can be igored in some cases and must either be lower or
        /// upper (then the function capitalizes the 1st character) or the correct mixed
        /// case (for example: <c>GetQuotaRoot</c>).
        /// <para/>
        /// For invalid comman names a <see cref="ZIMapException.Error.NotImplemented"/>
        /// error is raised.  
        /// </remarks>
        public ZIMapCommand.Generic CreateByName(string name)
        {   if(name == null || name.Length < 3) return null;
            if(GetCommands() == null) return null;          // parent closed
                
            object[] args = { this };
            if(name == name.ToUpper() || name == name.ToLower())
                name = char.ToUpper(name[0]) + name.Substring(1).ToLower();
            string full =  typeof(ZIMapCommand).FullName + "+" + name; 
            object inst = System.Reflection.Assembly.GetExecutingAssembly().CreateInstance(full,
                   false, System.Reflection.BindingFlags.CreateInstance, null, args, null, null);
            if(inst == null)
            {   RaiseError(ZIMapException.Error.NotImplemented, full);
                return null;
            }
            ZIMapCommand.Generic cmd = (ZIMapCommand.Generic)inst;
            commands.Add(cmd);
            return cmd;
        }

        /// <summary>
        /// Checks for a named IMap server capability
        /// </summary>
        /// <param name="capname">
        /// A capability name (example: <c>ACL</c>).
        /// </param>
        /// <returns>
        /// When the server has the capability <c>true</c> is returned.
        /// </returns>
        /// <remarks>The first time this method is called the library issues a
        /// <c>CAPABILITY</c> command the result stays cached unless a <c>null</c>
        /// value is assigned to the <see cref="Capabilities"/> property.
        /// </remarks>
        public bool HasCapability(string capname)
        {   return FindInStrings(Capabilities, 0, capname, false) >= 0;
        }
        
        /// <summary>
        /// Routine for searching a string array for a given value.
        /// </summary>
        /// <param name="strings">
        /// The array to be searched, <c>null</c> or an empty array are OK.
        /// </param>
        /// <param name="start">
        /// Start index.
        /// </param>
        /// <param name="what">
        /// A key that is to be searched, <c>null</c> is OK.
        /// </param>
        /// <param name="substring">
        /// Do a substring search if <c>true</c>.
        /// </param>
        /// <returns>
        /// An index value on success or <c>-1</c> on error.
        /// </returns>
        /// <remarks>
        /// The routine should not throw any errors, all object reference arguments can
        /// safely be <c>null</c>.
        /// <para/>
        /// The search is always case insensitive and stops with the 1st match.  To check
        /// for the uniqueness of a match the function can be called a second time:<para />
        /// <example><code lang="C#">
        ///     string[] array = ...;
        ///     int idx = ZIMapFactory.FindInStrings(array, 0, partialName, true);
        ///     if(idx &lt; 0) return -1;                                       // not found at all
        ///     if(partialName == array[idx]) return idx;  // exact match, work done.
        ///     // 2nd pass...
        ///     int amb = ZIMapFactory.FindInStrings(array, idx+1, partialName, true);
        ///     if(amb &lt; 0) return idx;                                         // ok, no ambiguity
        ///     if(partialName == array[amb]) return amb;  // exact match
        ///     return -2;                                                               // ambigious
        /// </code></example>
        /// </remarks>
        public static int FindInStrings(string[] strings, int start, string what, bool substring)
        {
            if(strings == null || strings.Length < 1 || what == null) return -1;
            int irun;
            if(substring) what = what.ToLower();
            for(irun=start; irun < strings.Length; irun++)
            {   if(string.IsNullOrEmpty(strings[irun])) continue;
                if(substring)
                {   string cmp = strings[irun].ToLower();
                    if(cmp.Contains(what)) return irun;
                }
                else
                    if(string.Compare(strings[irun], what, true) == 0) return irun;
            }
            return -1;
        }
        
        /// <summary>
        /// Search a string array of partially parserd data for an item.
        /// </summary>
        /// <param name="parts">
        /// The string array to be searched.
        /// </param>
        /// <param name="item">
        /// The item to be searched for.
        /// </param>
        /// <param name="text">
        /// Returns the text from array element that follows the item on success.
        /// Will return an empty string on error. Should never return <c>null</c>.
        /// </param>
        /// <returns>
        /// On success a value of <c>true</c> is returned.
        /// </returns>
        /// <remarks>
        /// The commands FETCH and STORE can return data that is only partially parsed in
        /// their Item arrays.  This function is provided to help searching such data.
        /// The following example tries to retrieve 'INTERNALDATE' from a FETCH:<para />
        /// <example><code lang="C#">
        ///     DateTime date;
        ///     string sdat;
        ///     if (ZIMapFactory.FindInParts(item.Parts, "INTERNALDATE", out sdat))
        ///         date = ZIMapConverter.DecodeIMapTime(sdat, true);
        ///     else
        ///         date = msg.DateBinary;
        /// </code></example>
        /// </remarks>
        public static bool FindInParts(string[] parts, string item, out string text)
        {   text = "";
            int idx = FindInStrings(parts, 0, item, false);
            if(idx < 0) return false;                           // item not found
            if(++idx >= parts.Length) return false;             // no value
            text = parts[idx];
            if(text.Length > 0 && text[0] == '"')               // must unquote
            {   ZIMapParser parser = new ZIMapParser(text);
                text = parser[0].Text;
            }
            return true;
        }
        
        /// <summary>
        /// A string describing the factory for debug output.
        /// </summary>
        /// <returns>
        /// A string like: 'AutoDispose=true  Commands=4 (completed=1  running=1)'
        /// </returns>
        /// <remarks>
        /// This function is intended for debugging only and is not optimized for
        /// performance. 
        /// </remarks>
        public override string ToString ()
        {   int irun = RunningCommands.Length;
            int irdy = CompletedCommands.Length;
            int iall = commands.Count;
            return string.Format("AutoDispose={0}  Commands={1} (completed={2}  running={3})",
                                 EnableAutoDispose, iall, irdy, irun);
        }

        
        // =====================================================================
        // The Bulk command class
        // =====================================================================
        
        /// <summary>
        /// Conveniency function to create a Bulk command instance.
        /// </summary>
        /// <param name="command">
        /// An IMAP command name like FETCH or SEARCH.
        /// </param>
        /// <param name="size">
        /// The number of aggregated commands.
        /// </param>
        /// <param name="uidCommand">
        /// Value for the <see cref="ZIMapCommand.UidCommand"/> property.
        /// </param>
        /// <returns>
        /// The <see cref="Bulk"/> instance.
        /// </returns>
        public Bulk CreateBulk(string command, uint size, bool uidCommand)
        {   return new Bulk(this, command, size, uidCommand);
        }
        
        /// <summary>
        /// A class to simplify the use of IMap parallel command execution.
        /// </summary>
        /// <remarks>
        /// The class aggregates an array of commands of the same type and provides
        /// methods to use these commands in a circular pattern.
        /// <para/>
        /// For the commands the <see cref="ZIMapCommand.AutoDispose"/> property
        /// is set to <c>false</c>.  So it is very important to call <see cref="Dispose"/>
        /// on the Bulk class instance in order to remove the commands from the factory.
        /// </remarks>
        public class Bulk : IDisposable
        {
            private ZIMapFactory            parent;
            private ZIMapCommand.Generic[]  commands;
            
            /// <summary>
            /// Construct a Bulk instance and allocate the command array
            /// </summary>
            /// <param name="parent">
            /// The owner of the allocated commands.
            /// </param>
            /// <param name="command">
            /// An IMAP command name like FETCH or SEARCH.
            /// </param>
            /// <param name="uidCommand">
            /// Value for the <see cref="ZIMapCommand.UidCommand"/> property.
            /// </param>
            /// <param name="size">
            /// The number of aggregated commands.
            /// </param>
            public Bulk(ZIMapFactory parent, string command, uint size, bool uidCommand)
            {   this.parent = parent;
                commands = new ZIMapCommand.Generic[size];
                for(uint urun=0; urun < size; urun++)
                {   commands[urun] = parent.CreateByName(command);
                    if(commands[urun] == null)      // aborting ...
                    {   commands = null; return;
                    }
                    commands[urun].AutoDispose = false;
                    commands[urun].UidCommand = uidCommand;
                }
            }
            
            /// <summary>
            /// The destructor calls <see cref="Dispose"/>.
            /// </summary>
            /// <remarks>
            /// Please do not rely on this destructor - call <see cref="Dispose"/>
            /// explicitly.  Not calling Dispose should be considered as a bug.
            /// </remarks>
            ~Bulk()
            {   Dispose();
            }
            
            /// <summary>
            /// Remove the commands from the parent factory.
            /// </summary>
            /// <remarks>
            /// For the commands the <see cref="ZIMapCommand.AutoDispose"/> property
            /// is set to <c>false</c>.  So it is very important to call <see cref="Dispose"/>
            /// on the Bulk class instance in order to remove the commands from the factory.
            /// </remarks>
            public void Dispose()
            {   if(commands == null) return;
                GC.SuppressFinalize(this);
                for(uint uidx=0; uidx < commands.Length; uidx++)
                    commands[uidx].Dispose();
                commands = null;
            }
            
            private uint FindIndex(ZIMapCommand.Generic cmd)
            {   if(commands == null) return uint.MaxValue;   
                if(cmd == null) return 0;   
                for(uint uidx=0; uidx < commands.Length; uidx++)
                    if(object.ReferenceEquals(commands[uidx], cmd)) return uidx; 
                return uint.MaxValue;
            }
            
            /// <summary>
            /// An iterator to get the next command for executing a request.
            /// </summary>
            /// <param name="current">
            /// Returns the next command. On the initial call the value should 
            /// be <c>null</c>.  
            /// </param>
            /// <returns>
            /// The result of <see cref="ZIMapCommand.IsPending"/>.
            /// </returns>
            /// <remarks>
            /// If the return value is <c>true</c> the caller must fetch and read the result
            /// and call  <see cref="ZIMapCommand.Reset"/> before the command can be reused.
            /// <para/>
            /// If the value of <paramref name="current"/> references a command whose state
            /// is <see cref="ZIMapCommand.CommandState.Queued"/> the queued command will
            /// be sent automatically.
            /// </remarks>
            /// <para />
            /// Here a simple usage expample:
            /// <para /><example><code lang="C#">
            /// uint ucnt = NNN;                        // number of messages
            /// uint usnd = 0;                          // send counter
            /// uint urcv = 0;                          // receive counter
            /// ZIMapCommand.Generic current = null;
            /// ZIMapFactory.Bulk bulk = app.Factory.CreateBulk("XXXX", 4, false);
            /// 
            /// while(urcv &lt; ucnt)
            /// {   // step 1: queue request and check for response ...
            ///     bool done = bulk.NextCommand(ref current);
            /// 
            ///     // step 2: check server reply for error ...
            ///     if(done) 
            ///     {   urcv++;
            ///         if(!current.CheckSuccess("Command failed")) done = false; 
            ///     }
            /// 
            ///     // step 3: process data sent by server ...
            ///     if(done)
            ///     {          
            ///     }
            /// 
            ///     // step 4: create a new request
            ///     if(usnd &lt; ucnt)
            ///     {   current.Reset();
            ///         current.Queue();
            ///         usnd++;
            ///     }
            /// }
            /// bulk.Dispose();
            /// </code></example>            
            public bool NextCommand(ref ZIMapCommand.Generic current)
            {   if(current != null && current.State == ZIMapCommand.CommandState.Queued)
                    current.Execute(false);
                uint uidx = FindIndex(current);
                if(uidx == uint.MaxValue)
                    parent.RaiseError(ZIMapException.Error.InvalidArgument);
                if(current != null) uidx++;
                if(uidx >= commands.Length) uidx = 0;
                current = commands[uidx];
                return current.State != ZIMapCommand.CommandState.Created; 
            }

            /// <summary>
            /// An iterator to get the next command for with a pending request.
            /// </summary>
            /// <param name="current">
            /// Returns the next command.
            /// </param>
            /// <returns>
            /// A value of <c>true</c> if the command is pending.
            /// </returns>
            /// <remarks>
            /// This iterator is typically used after all commands where submitted
            /// to fetch the results that are still pending.  It should be called in
            /// a loop as long as it returns <c>true</c>.
            /// </remarks>
            public bool NextPending(ref ZIMapCommand.Generic current)
            {   uint uidx = FindIndex(current);
                if(uidx == uint.MaxValue)
                    parent.RaiseError(ZIMapException.Error.InvalidArgument);
                uint ucnt = (uint)commands.Length;
                while(ucnt > 0)
                {   ucnt--; 
                    if(current != null) uidx++;
                    if(uidx >= commands.Length) uidx = 0;
                    current = commands[uidx];
                    if(current.IsPending) return true;
                }
                return false;
            }
        }
    }
}
