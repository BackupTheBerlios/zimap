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

    // TODO: Delegate callback on completion
    // TODO: Factory.AsyncExecute
 
    /// <summary>
    /// The root of the IMap command layer.
    /// </summary>
    /// <remarks>
    /// This class handles IMap command via the <see cref="ZIMapCommand"/> class.
    /// Commands are created (see <see cref="CreateGeneric"/> and friends), get
    /// queued for execution (see <see cref="QueueCommand"/>) and last but not
    /// least they contain the server response (see <see cref="ExecuteCommands(bool)"/>
    /// and <see cref="ReceiveCompleted"/>). Finally command can be removed
    /// (see <see cref="DisposeCommands"/> and <see cref="EnableAutoDispose"/>).
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
                
        // base has no def xtor ...
        protected ZIMapFactory(ZIMapConnection parent) : base(parent) {}

        // =====================================================================
        // Accessors for properties
        // =====================================================================

        /// <value>
        /// On success an array of capability names -or- <c>null</c> on error.
        /// </value>
        /// <remarks>
        /// The factory caches the result, unless the proterty is manually
        /// set to <c>null</c> it will always return the same object. Please
        /// note that if the capabilites depend on the server state and thus
        /// change at login.
        /// </remarks>
        public string[] Capabilities
        {   get {   if(capabilities == null)
                    {   ZIMapCommand.Capability cap = CreateCapability();   
                        capabilities = cap.Capabilities;
                        cap.Dispose();
                    }
                    return capabilities;
                }
            set {   if(value != null)
                        Error(ZIMapErrorCode.InvalidArgument, "must by null");
                    else
                        capabilities = null;
                }
        }

        /// <value>
        /// Returns the parent <see cref="ZIMapConnection"/> object.
        /// </value>
        /// <remarks>
        /// This is a fast and simple accessor. 
        /// </remarks>
        public ZIMapConnection Connection
        {   get {   return (ZIMapConnection)Parent;    }
        }

        /// <value>
        /// Controls if <see cref="DisposeCommands"/> is called when a command
        /// is disposed.
        /// </value>
        /// <remarks>
        /// The <see cref="ZIMapCommand.Dispose"/> method calls 
        /// <c>DisposeCommand(this, false)</c> if this property returns <c>true</c>.
        /// </remarks>
        public bool EnableAutoDispose
        {   get {   return autoDispose; }
            set {   autoDispose = value; }
        }
        
        /// <value>
        /// <c>true</c> if there at least one command has an outstanding reply.
        /// </value>
        /// <remarks>
        /// Searches the list of command for an entry with a command state 
        /// of <see cref="ZIMapCommandState.Running"/>. This is much faster
        /// than checking the array returned by <see cref="RunningCommands"/>.
        /// </remarks>
        public bool HasRunningCommands
        {   get {   if(GetCommands() == null) return false; // parent closed
                    foreach(ZIMapCommand cmd in commands)
                        if(cmd.CommandState == ZIMapCommandState.Running)
                            return true;
                    return false;
                }
        }
        
        /// <value>
        /// Returns the server's Hierarchy Delimiter character.
        /// </value>
        /// <remarks>
        /// This command requires a valid login. Zero is returned if the
        /// delimiter character cannot be determined. On success the result
        /// get cached, only on the 1st call a 'List "" ""' command is sent.
        /// </remarks>
        public char HierarchyDelimiter
        {   get {   if(hierarchyDelimiter == (char)0)
                    {   ZIMapCommand.List list = CreateList();
                        list.Queue("", "");
                        list.Execute(true);
                        ZIMapCommand.List.Item [] items = list.Items;
                        if(items != null && items.Length > 0)
                            hierarchyDelimiter = items[0].Delimiter;
                        list.Dispose();
                    }
                    return hierarchyDelimiter;
                }
            set {   if(value != (char)0)
                        Error(ZIMapErrorCode.InvalidArgument, "must by zero");
                    else
                        hierarchyDelimiter = (char)0;
                }
        }
        
        /// <value>
        /// Return an array of all commands
        /// </value>
        public ZIMapCommand[] Commands
        {   get {   if(GetCommands() == null) return null;  // parent closed
                    return commands.ToArray();  
                }
        }
        
        /// <value>
        /// Return an array of all completed commands (success or failed)
        /// </value>
        public ZIMapCommand[] CompletedCommands
        {   get {   return GetCommands(true);  }
        }
        
        /// <value>
        /// Return an array of all running commands
        /// </value>
        public ZIMapCommand[] RunningCommands
        {   get {   return GetCommands(false);  }
        }

        /// <value>
        /// Return the User name of the last ZIMapCommand.Login queued command.
        /// </value>
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
        /// Does little more than a call to <see cref="DisposeCommands"/>.
        /// </remarks>
        public void Dispose()
        {   if(commands == null)                    // nothing to do...
                return;
            DisposeCommands(null, true);
            capabilities = null;
            hierarchyDelimiter = (char)0;
            commands = null;
            autoDispose = true;
        }

        /// <summary>
        /// Release and cancel commands.
        /// </summary>
        /// <remarks>
        /// When a command is given as 1st argument this command and all commands
        /// that a older get disposed unless they are still busy. The given 
        /// command must have been sent (e.g. must have a non-zero Tag).
        /// <para />
        /// Unsually only command the have <see cref="ZIMapCommand.AutoDispose"/>
        /// set are affected. A call with <c>overrideAutoDispose</c> overrides
        /// this check.
        /// </remarks>
        public bool DisposeCommands(ZIMapCommand lastToDispose, bool overrideAutoDispose)
        {
            if(lastToDispose == null && overrideAutoDispose)
                Monitor(ZIMapMonitor.Info, "Disposing " + commands.Count + " command(s)");
            
            uint tag = 0;
            if(lastToDispose != null)                       // get target tag
            {   tag = lastToDispose.Tag;
                Monitor(ZIMapMonitor.Debug, "Disposing (auto): " + tag);
                if(tag == 0)
                {   Error(ZIMapErrorCode.CommandState, "not sent, no tag");
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
                   Monitor(ZIMapMonitor.Debug, "Disposing (auto): " + cmd.Tag);
                   cmd.Dispose();
                }
            }
            finally
            {   autoDispose = auto;
            }
            return true;
        }

        private List<ZIMapCommand> GetCommands()
        {   if(commands != null) return commands;           // ok, on stock
            if( ((ZIMapConnection)Parent).CommandLayer != this)
            {   Error(ZIMapErrorCode.DisposedObject);
                return null;                                // parent closed
            }
            commands = new List<ZIMapCommand>();
            return commands;                                // new list
        }
        
        private ZIMapCommand[] GetCommands(bool bCompleted)
        {   if(GetCommands() == null) return null;          // parent closed
            int cntCompleted = 0;  int cntRunning = 0;
            // pass1: count the commands
            foreach(ZIMapCommand cmd in commands)
                switch(cmd.CommandState)
                {   case ZIMapCommandState.Running:
                            cntRunning++; break;
                    case ZIMapCommandState.Completed:
                    case ZIMapCommandState.Failed:
                            cntCompleted++; break;
                }
            
            // pass2: copy to array
            int cnt = bCompleted ? cntCompleted : cntRunning;
            ZIMapCommand[] arr = new ZIMapCommand[cnt];
            if(cnt <= 0) return arr; 
            int run = 0;
            foreach(ZIMapCommand cmd in commands)
            {   switch(cmd.CommandState)
                {   case ZIMapCommandState.Running:
                            if(bCompleted) continue; break;
                    case ZIMapCommandState.Completed:
                    case ZIMapCommandState.Failed:
                            if(bCompleted) break; continue;
                    default:
                         continue;
                }
                arr[run++] = cmd; if(run >= cnt) break;
            }
            return arr;               
        }

        public bool QueueCommand(ZIMapCommand command)
        {   if(GetCommands() == null) return false;         // parent closed
            commands.Remove(command);                       // may fail: ok
            commands.Add(command);                          // move to start
            if(command.CommandState == ZIMapCommandState.Queued)
                return true;                                // no change
            if(command is ZIMapCommand.Login)
                username = ((ZIMapCommand.Login)command).User;
            return command.Queue();                         // tell the command
        }

        public bool DetachCommand(ZIMapCommand command)
        {   if(GetCommands() == null) return false;         // parent closed
            if(!commands.Remove(command))
                                      return false;         // not in list
            if(command.CommandState != ZIMapCommandState.Disposed)
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
            {   if(cmd.CommandState != ZIMapCommandState.Queued) continue;
                if(cmd.HasLiterals)                         // clear queue
                {   Monitor(ZIMapMonitor.Debug, "ExcecCommands: literal causing flush");
                    ExecuteWait(null);
                }
                if(!cmd.Execute(false))                     // send command
                    return false;
            }
            
            // Wait for results            
            return wait ? ExecuteWait(null) : true;
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
            return ExecuteWait(waitfor);                    // Wait for results
        }

        // This is a helper that waits for all running commands.
        private bool ExecuteWait(ZIMapCommand waitfor)
        {   while(HasRunningCommands)
            {   ZIMapReceiveData rs;
                Monitor(ZIMapMonitor.Debug, "ExcecCommands: waiting");
                if( ((ZIMapConnection)Parent).ProtocolLayer.Receive(out rs) )
                {    ReceiveCompleted(rs);
                     if(waitfor != null && waitfor.Tag == rs.Tag)
                        break;
                }
                else
                {   Monitor(ZIMapMonitor.Error, "ExcecCommands: receive failed");
                    return false;
                }
            }
            return true;
        }
        
        public bool ReceiveCompleted(ZIMapReceiveData rdata)
        {   if(GetCommands() == null) return false;         // parent closed
            foreach(ZIMapCommand cmd in commands)
            {   if(cmd.Tag != rdata.Tag) continue;
                return cmd.ReceiveCompleted(rdata);
            }
            return false;
        }
        
        public ZIMapCommand.Generic CreateGeneric(string command)
        {   if(GetCommands() == null) return null;          // parent closed
            ZIMapCommand.Generic cmd = new ZIMapCommand.Generic(this, command);
            commands.Add(cmd);
            return cmd;
        }

        private ZIMapCommand.Generic CreateByName(string name)
        {   if(GetCommands() == null) return null;          // parent closed
            //string name = "Command" + command;
            object[] args = { this };
            string full =  typeof(ZIMapCommand).FullName + "+" + name; 
            object inst = System.Reflection.Assembly.GetExecutingAssembly().CreateInstance(full,
                   false, System.Reflection.BindingFlags.CreateInstance, null, args, null, null);
            if(inst == null)
            {   Error(ZIMapErrorCode.NotImplemented, full);
                return null;
            }
            ZIMapCommand.Generic cmd = (ZIMapCommand.Generic)inst;
            commands.Add(cmd);
            return cmd;
        }
            
        public bool HasCapability(string capname)
        {   return FindInStrings(Capabilities, 0, capname, false) >= 0;
        }
        
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
        
        // =====================================================================
        // Create routines
        // =====================================================================

        public ZIMapCommand.Append      CreateAppend()     { return (ZIMapCommand.Append) CreateByName("Append"); } 
        public ZIMapCommand.Capability  CreateCapability() { return (ZIMapCommand.Capability) CreateByName("Capability"); } 
        public ZIMapCommand.Check       CreateCheck()      { return (ZIMapCommand.Check)  CreateByName("Check");  } 
        public ZIMapCommand.Close       CreateClose()      { return (ZIMapCommand.Close)  CreateByName("Close");  } 
        public ZIMapCommand.Copy        CreateCopy()       { return (ZIMapCommand.Copy)   CreateByName("Copy");   } 
        public ZIMapCommand.Create      CreateCreate()     { return (ZIMapCommand.Create) CreateByName("Create"); } 
        public ZIMapCommand.Delete      CreateDelete()     { return (ZIMapCommand.Delete) CreateByName("Delete"); } 
        public ZIMapCommand.Examine     CreateExamine()    { return (ZIMapCommand.Examine)CreateByName("Examine");} 
        public ZIMapCommand.Expunge     CreateExpunge()    { return (ZIMapCommand.Expunge)CreateByName("Expunge");} 
        public ZIMapCommand.Fetch       CreateFetch()      { return new ZIMapCommand.Fetch(this);  } 
        public ZIMapCommand.List        CreateList()       { return new ZIMapCommand.List(this);   } 
        public ZIMapCommand.Login       CreateLogin()      { return (ZIMapCommand.Login)  CreateByName("Login");  } 
        public ZIMapCommand.Logout      CreateLogout()     { return (ZIMapCommand.Logout) CreateByName("Logout"); } 
        public ZIMapCommand.Lsub        CreateLsub()       { return (ZIMapCommand.Lsub)   CreateByName("Lsub");   } 
        public ZIMapCommand.Rename      CreateRename()     { return (ZIMapCommand.Rename) CreateByName("Rename"); } 
        public ZIMapCommand.Select      CreateSelect()     { return (ZIMapCommand.Select) CreateByName("Select"); }
        public ZIMapCommand.Search      CreateSearch()     { return (ZIMapCommand.Search) CreateByName("Search"); }
        public ZIMapCommand.Status      CreateStatus()     { return (ZIMapCommand.Status) CreateByName("Status"); }
        public ZIMapCommand.Store       CreateStore()      { return (ZIMapCommand.Store)  CreateByName("Store");  }
        public ZIMapCommand.Subscribe   CreateSubscribe()  { return (ZIMapCommand.Subscribe)  CreateByName("Subscribe");  }
        public ZIMapCommand.Unsubscribe CreateUnsubscribe(){ return (ZIMapCommand.Unsubscribe)CreateByName("Unsubscribe");}

        // Namespace command (www.faqs.org/rfcs/rfc2342.html) ...

        public ZIMapCommand.Namespace   CreateNamespace()       { return new ZIMapCommand.Namespace(this);  }
        
        // ACL commands (see www.faqs.org/rfcs/rfc4314.html) ...

        public ZIMapCommand.DeleteACL    CreateDeleteACL()      { return new ZIMapCommand.DeleteACL(this);  }
        public ZIMapCommand.GetACL       CreateGetACL()         { return new ZIMapCommand.GetACL(this);     }
        public ZIMapCommand.ListRights   CreateListRights()     { return new ZIMapCommand.ListRights(this); }
        public ZIMapCommand.MyRights     CreateMyRights()       { return new ZIMapCommand.MyRights(this);   }
        public ZIMapCommand.SetACL       CreateSetACL()         { return new ZIMapCommand.SetACL(this);     }
        
        // Quota commands (see www.faqs.org/rfcs/rfc2087.html)...

        public ZIMapCommand.GetQuota     CreateGetQuota()       { return new ZIMapCommand.GetQuota(this);    }
        public ZIMapCommand.GetQuotaRoot CreateGetQuotaRoot()   { return new ZIMapCommand.GetQuotaRoot(this);}
        public ZIMapCommand.SetQuota     CreateSetQuota()       { return new ZIMapCommand.SetQuota(this);    }
        
    }
}
