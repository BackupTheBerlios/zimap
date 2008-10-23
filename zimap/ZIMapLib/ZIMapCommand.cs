//==============================================================================
// ZIMapCommand.cs implements the ZIMapCommand and derived classes    
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
    // ZIMapCommand
    //==========================================================================

    /// <summary>
    /// Class to issue IMAP commands and to collect the server responses
    /// belonging to it (Command Layer).
    /// </summary>
    /// <remarks>
    /// This class is public but abstract. You may for example use 
    /// <see cref="ZIMap.ZIMapFactory.CreateGeneric"/> to instantiate a concrete
    /// command object. Commands are associated with a <see cref="ZIMapFactory"/>
    /// (until <see cref="Dispose()"/> is called).  The factory is reponsible
    /// for command execution.
    /// </remarks>
    public abstract partial class ZIMapCommand : ZIMapBase, IDisposable
    {
        //==========================================================================
        // Enumerations    
        //==========================================================================
        /// <summary>
        /// Processing status of a  <see cref="ZIMapCommand"/> object.
        /// </summary>
        /// <remarks>
        /// A value of this type gets returned by the property 
        /// <see cref="ZIMapCommand.State"/>
        /// </remarks>
        public enum CommandState
        {   /// <summary>Command was just created or did a Reset().</summary>
            Created,
            /// <summary>Command was queued but is not yet sent.</summary>
            Queued,
            /// <summary>Command was sent but is not yet completed.</summary>
            Running,
            /// <summary>Command completed but failed.</summary>
            Failed,
            /// <summary>Command successfully completed.</summary>
            Completed,
            /// <summary>The command's Dispose method has been called.</summary>
            Disposed
        }
        
        // our parent factory
        protected readonly ZIMapFactory factory;
        // the class name for Monitor()
        protected readonly string       name;
        // the object's state, modified at various points
        protected CommandState          state;
        /// <summary>not internally used, see UserData property</summary>
        protected object                userData;
        // prefix the command with UID
        protected bool                  uidCommand;

        /// <summary>
        /// List of commands the can have the <c>UID</c> command prefix.
        /// </summary>
        /// <remarks>
        /// The RFC3501 "UID" Command is essentially a prefix for some IMap commands. This
        /// is implemented by setting the <see cref="ZIMapCommand.UidCommand" /> property.  
        /// That property can be set <c>true</c> only for the commands named in this array:
        /// <para/><list type="bullet">
        ///  <item><term>COPY - <see cref="ZIMapCommand.Copy" /></term></item>
        ///  <item><term>FETCH - <see cref="ZIMapCommand.Fetch" /></term></item>
        ///  <item><term>STORE - <see cref="ZIMapCommand.Store" /></term></item>
        ///  <item><term>SEARCH - <see cref="ZIMapCommand.Search" /></term></item>
        ///</list></remarks>
        public static readonly string[] UidCommands = { "COPY", "FETCH", "STORE", "SEARCH" };        
        
        // --- Request arguments ---
        
        // the command name, set in xtor
        protected string            command;
        // hold arguments for simple commands, cleared in Execute()
        protected string            args;
        // holds literal args, see AddLiteral(), cleared in Execute()
        protected List<object>      literals;
        // list nesting level, see AddBeginList
        protected uint              listLevel;

        // --- Response results ---

        // the tag is set when the command is sent
        protected uint              tag;
        // data is set by ReceiveCompleted
        ZIMapProtocol.ReceiveData   data;
        // set in Queue() routine        
        protected bool              autoDispose;
        
        // base has no def xtor ...
        /// <summary>
        /// The one and only constructor.
        /// </summary>
        /// <param name="parent">
        /// Each command must have a <see cref="ZIMapFactory"/> parent.
        /// </param>
        protected ZIMapCommand(ZIMapFactory parent) : 
                  base((ZIMapConnection)parent.Parent)
        {   name = "ZIMapCommand." + GetType().Name;
            factory = parent;
            MonitorLevel = factory.MonitorLevel;
            state = CommandState.Created;
        }
            
        // =====================================================================
        // Accessors for properties
        // =====================================================================

        /// <summary>
        /// Controls if this instance will be automatically disposed. See
        /// <see cref="ZIMapFactory.DisposeCommands"/> for details.
        /// </summary>
        public bool AutoDispose
        {   get {   return autoDispose; }
            set {   autoDispose = value; }
        }
        
        /// <summary>Can be used to get the current state of the command</summary>
        public CommandState State 
        {   get {   return state; }
        }
        
        /// <summary>
        /// This property returns the raw results of the command.
        /// </summary>
        /// <remarks>
        /// A call to this property will implicitly execute the command and
        /// wait for the results.  In other words: it can be blocking.  After
        /// the result became ready this property will instantly return the
        /// cached value.
        /// <para />
        /// An error may be raised if the command cannot be executed or if
        /// the send or receive operations failed. 
        /// </remarks>
        public ZIMapProtocol.ReceiveData Result
        {   get {   if(tag == 0 || tag != data.Tag)
                    {   if(state == CommandState.Created) Queue();
                        factory.ExecuteCommands(this);
                    }
                    return data;    
                }
        }
        
        /// <summary>Returns the IMap command name.</summary>
        public string Command 
        {   get {   return command; }
        }

        /// <summary>Returns <c>true</c> if some of the command arguments are literals.</summary>
        /// <remarks>The <see cref="ZIMapProtocol.Send(string)"/> method uses this property
        /// and automatically processes all server responses for running commands before
        /// it attempts to send a literal.</remarks>
        public bool HasLiterals 
        {   get {   return literals != null; }
        }

        /// <summary>Returns <c>true</c> if the command has completed.</summary>
        /// <remarks>
        /// A command is completed after a response was received from the server (or if
        /// the command was aborted because it could not be sent).  A call to this 
        /// property does not change the command state and will never block.
        /// </remarks>
        public bool IsReady
        {   get {   return (state == CommandState.Completed ||
                            state == CommandState.Failed); }
        }
        
        /// <summary>Returns <c>true</c> if the command is queued but not yet completed.</summary>
        /// <remarks>
        /// A command is completed after a response was received from the server (or if
        /// the command was aborted because it could not be sent).  A call to this 
        /// property does not change the command state and will never block.
        /// </remarks>
        public bool IsPending
        {   get {   return (state == CommandState.Queued ||
                            state == CommandState.Running); }
        }
        
        /// <summary>Returns the IMap tag to was assigned by <see cref="ZIMapProtocol.Send(string)"/>
        /// to this command.</summary>
        public uint Tag {
            get {   return tag; }
        }

        /// <summary>
        /// When enabled some commands will be sent with a <c>UID</c> prefix.
        /// </summary>
        /// <remarks>Not all commands can user the <c>UID</c> prefix, an exception
        /// is thrown on an attempt to set this property for the wron command. See
        /// <see cref="UidCommands"/> for details.</remarks>
        public bool UidCommand
        {   get {   return  uidCommand; }
            set {   if(value == false)
                    {   uidCommand = false; return;
                    }
                    if(ZIMapFactory.FindInStrings(UidCommands, 0, command, false) < 0)
                        RaiseError(ZIMapException.Error.InvalidArgument, "Not a UID command");
                    else
                        uidCommand = value; 
                }
        }
        
        /// <summary>
        /// This value is not used internally and can be used by the user to
        /// store therein whatever might be usefull.
        /// </summary>
        /// <value>
        /// Any kind of data
        /// </value>
        /// <remarks>
        /// The <see cref="Reset"/> method sets the value to <c>null</c>.
        /// </remarks>
        public object UserData
        {   get {   return userData; }
            set {   userData = value; }
        }

        // =====================================================================
        // Command arguments
        // =====================================================================
        
        /// <summary>
        /// Adds an atom-string or NIL or "". The string will not be quoted.
        /// </summary>
        /// <param name="arg">
        /// (1) a non-empty atom or atom-string,
        /// (2) null to output "NIL" or
        /// (3) an empty string to output "" (two double quotes)
        /// </param>
        /// <returns>
        /// <c>true</c> on success
        /// </returns>
        /// <remarks>
        /// The argument is only partially
        /// checked for validity and no quoting occurs. It is not possible
        /// to add arguments that contain spaces (or control chars) by this
        /// function - use AddDirect for this purpose. To add a numeric
        /// values AddDirect should be preferred for speed.
        /// </remarks>
        public bool AddName(object arg)
        {   string str = (arg == null) ? "NIL" : arg.ToString();
            if(str == "")
               str = "\"\"";
            else foreach(char chr in str)
                if((uint)chr <= 20 || (uint)chr >= 127 || chr == '"' || chr == '\\')
                {   RaiseError(ZIMapException.Error.InvalidArgument, "Has invalid char");
                    return false;
                }
            if(args == null || args == "")  args  = str;
            else if(args.EndsWith("("))     args += str;
            else                            args += " " + str;
            return true;
        }

        /// <summary>
        /// Adds the string representation of the argument to the command
        /// without doing any check (except for a null argument which throws
        /// an exception).
        /// </summary>
        /// <param name="arg">
        /// The string representation of this argument gets added
        /// </param>
        /// <returns>
        /// true on success
        /// </returns>
        /// <remarks>
        /// Please note that this function is the fastest way
        /// to pass a numeric argument to a command.
        /// </remarks>
        public bool AddDirect(object arg)
        {   if(arg == null)
            {   RaiseError(ZIMapException.Error.MustBeNonZero);
                return false;
            }
            string argv = arg.ToString();
            if(args == null || args == "")  args  = argv; 
            else if(args.EndsWith("("))     args += argv;
            else if(argv.StartsWith(")"))   args += argv;
            else                            args += " " + argv;
            return true;
        }
        
        public bool AddSequence(uint[] items)
        {   if(items == null)
            {   RaiseError(ZIMapException.Error.MustBeNonZero);
                return false;
            }
            if(items.Length == 1)
                return AddDirect(items[0]);

            StringBuilder sb;
            if(args == null || args == "")
                sb = new StringBuilder();
            else
            {   sb = new StringBuilder(args);
                if(sb[sb.Length-1] != '(') sb.Append(' ');
            }

            sb.Append(items[0].ToString());
            for(int irun=1; irun < items.Length; irun++)
            {   int igrp = irun;
                uint uval = items[irun-1];
                while(igrp < items.Length)
                    if(++uval != items[igrp]) break;
                    else igrp++;
                igrp--;
                if(igrp > irun)
                {   irun = igrp;
                    sb.Append(':');
                }
                else    
                    sb.Append(',');
                sb.Append(items[irun].ToString());
            }
            return AddDirect(sb.ToString());
        }

        /// <summary>
        /// Adds the string representation of the argument to the command
        /// as a quoted string.
        /// </summary>
        /// <param name="arg">
        /// Data to be added as a quoted string (or NIL if arg is null)
        /// </param>
        /// <returns>
        /// true on success
        /// </returns>
        /// <remarks>
        /// Special characters (" and \) are automatically quoted, control
        /// characters are not allowed and cause an exception. This overload
        /// simply calls <see cref="AddString(object, bool)"/> and never
        /// creates a literal.
        /// <para />
        /// The argument may be <c>null</c> which will add an unquoted NIL to the
        /// command. 
        /// </remarks>
        public bool AddString(object arg)
        {   return AddString(arg, false);
        }
        
        /// <summary>
        /// Adds the string representation of the argument to the command
        /// as a quoted string or optionally as a literal.
        /// </summary>
        /// <param name="argument">
        /// Data to be added as a quoted string or literal (or NIL if arg is null)
        /// </param>
        /// <param name="allowLiteral">
        /// Must be <c>true</c> to allow a literal. Otherwise the 
        /// <see cref="RaiseError(ZIMapException.Error)"/> function will be called
        /// to throw an exception if a literal is required due to 8-bit data.
        /// </param>
        /// <returns>
        /// true on success
        /// </returns>
        /// <remarks>
        /// If the text to be send contains control characters or 8-bit data
        /// (e.g. is Unicode data) this routine either throws an exception or
        /// generates a literal - depending on the <b>allowLiteral</b> argument.
        /// Special characters (" and \) are automatically quoted if quoted 
        /// text is send.
        /// <para />
        /// The argument may be null which will add an unquoted NIL to the command.
        /// </remarks>
        public bool AddString(object argument, bool allowLiteral)
        {   string arg = null;
            
            // handle 8-bit data ...
            if(argument != null)
            {   arg = argument.ToString();
                if(!ZIMapConverter.Check7BitText(arg))
                {   if(!allowLiteral)
                    {   RaiseError(ZIMapException.Error.InvalidArgument, "Has 8bit char");
                        return false;
                    }
                    return AddLiteral(arg);
                }
            }
            
            // or quoted string ...
            StringBuilder sb;
            if(args == null || args == "")
                sb = new StringBuilder();
            else
            {   sb = new StringBuilder(args);
                if(sb[sb.Length-1] != '(') sb.Append(' ');
            }
            
            if(arg == null)
                sb.Append("NIL");
            else
                ZIMapConverter.QuotedString(sb, arg, false);
            
            args = sb.ToString();            
            return true;
        }
        
        /// <summary>
        /// Opens a List. Lists can be nested. Use AddEndList() to close
        /// a list (and all nested lists). AddEndList(0) will be automatically
        /// called before a command is sent.
        /// </summary>
        /// <returns>
        /// The nesting level which can be passed to AddEndList() to close
        /// this list and all contained lists.
        /// </returns>
        public uint AddBeginList()
        {   if(args == null || args == "")  args  = "(";
            else if(args.EndsWith("("))     args += "(";
            else                            args += " (";
            return ++listLevel;
        }
        
        /// <summary>
        /// Close all lists up to a given nesting level. A level of 0 would
        /// close all open lists and would never throw an exeption. An attempt
        /// to close up to a level that is not open throws an exception.
        /// </summary>
        /// <param name="level">
        /// Lowest nesting level to be closed (0 never throws an exception)
        /// </param>
        /// <returns>
        /// <c>true</c> on success
        /// </returns>
        public bool AddEndList(uint level)
        {   if(listLevel == 0) return true;
            if(level > listLevel)
            {   RaiseError(ZIMapException.Error.InvalidArgument, level.ToString());
                return false;
            }
            if(level > 0) level--;
            uint cnt = listLevel - level; listLevel = level;
            StringBuilder app = new StringBuilder(args);
            app.Append(')', (int)cnt); args = app.ToString();
            return true;
        }

        /// <summary>
        /// Send a text as UTF-8 encoded literal.
        /// </summary>
        /// <param name="text">
        /// The text to send.
        /// </param>
        /// <returns>
        /// <c>true</c> on success
        /// </returns>
        public bool AddLiteral(string text)
        {   if(text == null || text.Length == 0)
                return AddLiteral((byte[])null);
            byte[] data = ASCIIEncoding.UTF8.GetBytes(text);
            return AddLiteral(data);
        }

        /// <summary>
        /// Send a byte array as literal.
        /// </summary>
        /// <param name="data">
        /// The array to be sent.
        /// </param>
        /// <returns>
        /// <c>true</c> on success
        /// </returns>
        /// <remarks>
        /// This is a low level routine used to send binary data.
        /// </remarks>
        public bool AddLiteral(byte[] data)
        {
            if(literals == null)
            {   literals = new List<object>();
                literals.Add(command);
            }
            if(args != null && args != "")
            {   literals.Add(args); args = "";
            }
            if(data == null) data = new byte[0];   
            literals.Add(data);
            return true;
        }

        /// <summary>
        /// Adds a Mailbox argument
        /// </summary>
        /// <param name="mailboxName">
        /// An unencoded Mailbox name
        /// </param>
        /// <returns>
        /// The method encodes the Mailbox name following the rules
        /// given by RFC3501.
        /// </returns>
        public bool AddMailbox(string mailboxName)
        {   string encodedName = mailboxName;
            ZIMapConverter.MailboxEncode(out encodedName, mailboxName);
            return AddString(encodedName);
        }
        
        /// <summary>
        /// Adds a List argument to a command.
        /// </summary>
        /// <param name="list">
        /// A string containing words that become the list content.
        /// Braces are not required and will be removed.
        /// </param>
        /// <returns>
        /// Returns <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// For a <c>null</c> argument an empty list is added.  The argument
        /// is split into an array using the space character as delimiter.
        /// The string array is passed to <see cref="AddList(string[])"/>.
        /// This routine does not know about quoting and can be used only
        /// for simple cases.
        /// </remarks>        
        public bool AddList(string list)
        {   if(string.IsNullOrEmpty(list))          // empty list
                return AddDirect("()");
            if(list[0] == '(')                      // remove brackets
            {   if(list[list.Length-1] != ')')
                {   RaiseError(ZIMapException.Error.InvalidArgument, "Bad use of ()");
                    return false;
                }
                list = list.Substring(1, list.Length-2);
            }
            return AddList(ZIMapConverter.StringArray(list));
        }

        /// <summary>
        /// Adds a List argument to a command.
        /// </summary>
        /// <param name="list">
        /// Contains an array of list elements - can be <c>null</c> or empty.
        /// </param>
        /// <returns>
        /// Returns <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// Elements of the input array that are <c>null</c> or empty are 
        /// ignored.  The routine generates an empty list when the input array is
        /// <c>null</c> or contains no non-empty elements.  The array elements
        /// themselves are not parsed,  they are passed to <see cref="AddDirect"/>. 
        /// </remarks>        
        public bool AddList(string[] list)
        {   int iBeg = 0;
            int iEnd = (list == null) ? 0 : list.Length;
            uint level = AddBeginList();
            for(; iBeg < iEnd; iBeg++)
                if(!string.IsNullOrEmpty(list[iBeg])) AddDirect(list[iBeg]);
            return AddEndList(level);
        }
        
        // =====================================================================
        // Other
        // =====================================================================
       
        public abstract void Dispose();

        /// <summary>
        /// Reset the command to it's initial state
        /// </summary>
        /// <returns>
        /// <c>true</c> on success
        /// </returns>
        /// <remarks>
        /// Call this method to release resources. 
        /// Throws an <see cref="ZIMapException.Error.CommandBusy"/> error if
        /// the command is currently executing.
        /// </remarks>
        public virtual bool Reset()
        {   if(state == CommandState.Queued || state == CommandState.Running)
            {   RaiseError(ZIMapException.Error.CommandBusy, command);
                return false;
            }

            tag = listLevel = 0;
            literals = null;
            userData = null;
            data.Reset();
            state = CommandState.Created;
            return true;
        }
        
        /// <summary>
        /// Send the command to the server and optionally wait for the response.
        /// </summary>
        /// <param name="wait">
        /// When <c>false</c> the function will only sent the command but will not
        /// wait for the response.  <c>true</c> will sent the command and will wait.
        /// </param>
        /// <returns>
        /// Returns <c>false</c> if send or receive failed.  The status does not depend on
        /// the command's success or failure, see <see cref="ZIMapProtocol.ReceiveState"/>.
        /// </returns>
        /// <remarks>
        /// It does no harm to call this function on a command that has already been sent.
        /// But it the command has been completed or if <see cref="Queue"/> has not been
        /// call a <see cref="ZIMapException.Error.CommandState"/> error will be raised.
        /// </remarks>
        public bool Execute(bool wait)
        {   // get protocol layer
            ZIMapProtocol prot = ((ZIMapConnection)Parent).ProtocolLayer;
            if (prot == null)                        // should not happen!
            {   RaiseError(ZIMapException.Error.DisposedObject);
                return false;
            }

            // the command has already been sent ...
            if(state == CommandState.Running)
            {   MonitorDebug("Execute: {0:x} is running", tag);
                if(wait) factory.ExecuteCommands(this);
                return true;
            }

            // will send command or raise an error ...
            if(literals == null)
                MonitorDebug("Execute: {0:x}", prot.SendCount+1);
            else
                MonitorDebug("Execute: {0:x}, {1} literals", 
                             prot.SendCount+1, literals.Count);
            
            if(state == CommandState.Disposed)
            {   RaiseError(ZIMapException.Error.DisposedObject, name);
                return false;
            }
            if(state != CommandState.Created &&
               state != CommandState.Queued)
            {   RaiseError(ZIMapException.Error.CommandState, command);
                return false;
            }

            // we do not catch exceptions, assume error ...
            state = CommandState.Failed;
            AddEndList(0);                          // implicitly close lists

            // now try ...
            string error = "send failed";
            if(literals == null)
            {   StringBuilder sb = new StringBuilder();
                if(uidCommand) sb.Append("UID ");
                sb.Append(command);
                if(args != null)
                {   sb.Append(' ');
                    sb.Append(args);
                }
                tag = prot.Send(sb.ToString());
            }
            else
            {   if(uidCommand) literals.Insert(0, "UID");   
                if(args != "") literals.Add(args);   
                tag = prot.Send(literals.ToArray(), out error);
                literals = null;
            }
            args = null;
            
            // did it work? We don't get here after an exception
            if(tag == 0) 
            {   MonitorError("Excecute: error: " + error);
                data.Message = error;
                return false;
            }
            state = CommandState.Running;
            if(!wait) return true;
            return factory.ExecuteCommands(this);
        }
            
        public bool Queue()
        {   MonitorDebug("Queue");
            if(state == CommandState.Disposed)
            {   RaiseError(ZIMapException.Error.DisposedObject, base.GetType());
                return false;
            }
            if(state != CommandState.Created)
            {   RaiseError(ZIMapException.Error.CommandBusy, command);
                return false;
            }

            autoDispose = true;
            state = CommandState.Queued;
            return factory.QueueCommand(this);      // make it the most recent
        }

        /// <summary>
        /// Marks a command as completed and stores the server response.
        /// </summary>
        /// <param name="rdata">
        /// The <see cref="ZIMapProtocol.ReceiveData"/> value returned from 
        /// <see cref="ZIMapProtocol.Receive"/>
        /// </param>
        /// <returns>
        /// A <see cref="System.Boolean"/>
        /// </returns>
        public bool Completed(ZIMapProtocol.ReceiveData rdata)
        {   if(state != CommandState.Running)
            {   RaiseError(ZIMapException.Error.CommandState, state);
                return false;
            }
            if(tag != rdata.Tag)
            {   RaiseError(ZIMapException.Error.InvalidArgument, "Unexpected tag");
                return false;
            }
                
            data = rdata;
            state = (data.State == ZIMapProtocol.ReceiveState.Ready)
                  ? CommandState.Completed : CommandState.Failed;
            MonitorDebug( "Completed: Tag: {0:x}  Status: {1} ({2})", tag, data.Status, state);
#if DEBUG            
            if(MonitorLevel <= ZIMapConnection.Monitor.Debug)   // expensive ...
                foreach(string line in data.ToString().Split("\n".ToCharArray()))               
                    MonitorDebug("           {0}", line);
#endif
            ZIMapConnection.Callback.Result(factory.Connection, this);
            return true;
        }

        /// <summary>
        /// Conveniency routine to check if a command succeeded.
        /// </summary>
        /// <returns>
        /// When the command succeeded <c>true</c> is returned.
        /// </returns>
        /// <remarks>
        /// The function will execute the command as required and wait for the
        /// result. The function internally calls <see cref="CheckSuccess(string)"/>.
        /// </remarks>
        public bool CheckSuccess()
        {   return CheckSuccess(null);
        }

        /// <summary>
        /// Conveniency routine to check if a command succeeded.
        /// </summary>
        /// <param name="errorMessage">
        /// An optional error message or <c>null</c>. An empty string or
        /// just ":" output a default message.
        /// </param>
        /// <returns>
        /// When the command succeeded <c>true</c> is returned.
        /// </returns>
        /// <remarks>
        /// The function will execute the command as required and wait for
        /// the result.  When the command status indicates an error and
        /// on error message was passed this message will be output via
        /// <see cref="MonitorError(string)"/>.  The function internally
        /// calls <see cref="Result"/> and 
        /// <see cref="ZIMapProtocol.ReceiveData.Succeeded"/>.
        /// </remarks>
        public bool CheckSuccess(string errorMessage)
        {
            if(Result.Succeeded)     return true;           // ok, it worked
            if(errorMessage == null) return false;          // be silent
            if(errorMessage == "" || errorMessage == ":")
                MonitorError("{0}{1} command failed: {2}", 
                             errorMessage, command, Result.Message);
            else
                MonitorError(errorMessage, command, Result.Message);
            return false;
        }

        /// <summary>
        /// Returns a parser for 'untagged response' data with the filter
        /// <c>Result.Status == Command</c>. 
        /// </summary>
        /// <returns>
        /// A parser on succes or <c>null</c> on error.
        /// </returns>
        /// <remarks>
        /// This routine will work for most simple IMap commands. If it returns <c>null</c>
        /// either the command failed with an error status or the server did not return
        /// an 'untagged response' that matches the filter condition.
        /// <para />
        /// This method simply forwards the call to <see cref="InfoParser(string, uint)"/>
        /// with an empty filter string and only uses the 1st 'untagged response'.
        /// </remarks>
        public ZIMapParser InfoParser()
        {   uint index = 0;
            return InfoParser("", ref index);
        }

        /// <summary>
        /// Returns a parser for 'untagged response' data with a given filter and a given index.
        /// </summary>
        /// <param name="filter">
        /// A filter for the command status or <c>null</c> to match all. An empty string matches
        /// the command name.
        /// </param>
        /// <param name="index">
        /// On call this is the start index, on return the value is set to the succeeding entry. 
        /// </param>
        /// <returns>Returns <c>null</c> if either the command failed with an error status or the
        /// server did not return an 'untagged response' that matches the filter condition.      
        /// </returns>
        /// <remarks>
        /// Unsually IMap command return the command name as status, but <c>OK</c> or a number
        /// are also common.
        /// </remarks>
        public ZIMapParser InfoParser(string filter, ref uint index)
        {   // return null if something went wrong ...
            ZIMapProtocol.ReceiveInfo[] infos;
            ZIMapProtocol.ReceiveData data = Result;
            if(!data.Succeeded || data.Infos == null)
            {   index = uint.MaxValue;                          // set error indicator
                return null;
            }
            infos = data.Infos;

            uint ulen = (uint)infos.Length;
            if(index >= ulen)
            {   index = 0;                                      // clear error indicator
                return null;
            }

            // handle status filter condition ...
            if(filter != null)
            {   if(filter == "") filter = command;
                while(infos[index].Status != filter)
                {   if(++index < ulen) continue;
                    index = 0; return null;
                }
            }
            
            // return the current parser ...
            return new ZIMapParser(infos[index++].Message);
        }

        // =====================================================================
        // Debug support
        // =====================================================================

        /// <summary>
        /// Formatted text representing the state of the command object.
        /// </summary>
        /// <returns>
        /// Formatted state info for Debugging purposes. This routine does  not
        /// cause the command to be queued or executed.
        /// </returns>
        public override string ToString ()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Command: CommandName={0}  State={1}", command, State);
            if(IsReady) sb.AppendFormat("\n" +
                            "         Status={0}  Tag={1}\n" +
                            "         Result: {2}", data.Status, Tag, data.Message);
            if(data.Infos != null)
                foreach(ZIMapProtocol.ReceiveInfo info in data.Infos)
                    sb.AppendFormat("\n         Info: {0}", info);
            if(IsReady) ToString(sb);
            return sb.ToString();
        }

        /// <summary>
        /// Produce additional debug output 
        /// </summary>
        /// <param name="sb">
        /// A StringBuilder that should be used to create output.
        /// </param>        
        /// <remarks>
        /// Implementors may override this routine. The base ToString() calls
        /// this overload only whe the command has already been executed.
        /// </remarks>
        protected virtual void ToString(StringBuilder sb)
        {   
        }
            
        // =====================================================================
        // The Command helper class
        // =====================================================================

        /// <summary>
        /// A concrete (base-)class that implements some infrastructure for
        /// parsing server responses.  
        /// </summary>
        /// <remarks>
        /// This class is designed as a base class for more specific IMap Command
        /// classes.  It should be rarely be used directly, for most IMap Commands
        /// exist specialized classes that are derived from Generic.
        /// </remarks>
        public class Generic : ZIMapCommand
        {
            private bool    parsed;         // set by Parse()
            
            /// <summary>Construct an instance of a generic command</summary>
            /// <param name="parent">The owning factory (must not be <c>null</c>).</param>
            /// <param name="command">The IMap command name (<c>null</c> 
            /// will generate a NOOP command).</param>
            /// <remarks>It is not very common to use this class directly.</remarks>
            public Generic(ZIMapFactory parent, string command) : base(parent)
            {   if(parent == null) ZIMapException.Throw(null,
                     ZIMapException.Error.MustBeNonZero, "ZIMapCommand xtor");
                base.command = (command == null) ? "NOOP" : command.ToUpper();
            }
           
            // must implement, abstract in base ...
            protected override void MonitorInvoke(ZIMapConnection.Monitor level, string message)
            {   if(factory.MonitorLevel <= level)
                    ZIMapConnection.MonitorInvoke(Parent, name, level, message); 
            }

            /// <summary>
            /// Free resources and detach the command (and eventually its ancestors)
            /// from the owning factory.
            /// </summary>
            /// <remarks>
            /// When the factory has set the <see cref="ZIMapFactory.EnableAutoDispose"/>
            /// property, this method calls <see cref="ZIMapFactory.DisposeCommands"/>
            /// to automatically dispose commands with a lower Tag number (e.g. that are
            /// older than the caller).
            /// <para />
            /// The effect ot this method is similar to <see cref="Reset"/> but the later does
            /// not cancel a queued command nor detach it from it's factory.
            /// </remarks>
            public override void Dispose()
            {   if(state == CommandState.Disposed) return;
                state = CommandState.Disposed;

                // Tell the factory to cancel/remove the command ...
                factory.DetachCommand(this);

                // Call factory to handle AutoDispose
                if(factory.EnableAutoDispose && Tag > 0)
                    factory.DisposeCommands(this, false);
                
                // Free resources (must set Disposed again)
                Reset();
                state = CommandState.Disposed;
            }
        
            /// <summary>
            /// Helper for derived classed to implement Parse()/Reset().
            /// </summary>
            /// <param name="reset">
            /// When <c>true</c> the call was caused by <see cref="Reset"/>.
            /// </param>
            /// <returns>
            /// <c>true</c> on success
            /// </returns>
            /// <remarks>
            /// In ZIMapCommand.Generic this routine does nothing, but implementors
            /// should override it to handle Parse() and Reset().
            /// </remarks> 
            protected virtual bool Parse(bool reset)
            {
                return true;
            }

            /// <summary>
            /// Does further parsing of the command results.
            /// </summary>
            /// <returns>
            /// <c>true</c> on success
            /// </returns>
            /// <remarks>
            /// Usually this routine is called implicitly by properties that return
            /// the results of this parsing action. Note for implementors: this 
            /// routine calls Parse(false), a derived class should not override
            /// Parse() but do the work in <see cref="Parse(bool)"/>.
            /// </remarks>
            public virtual bool Parse()
            {
                if(parsed)   return true;
                parsed = true;
                return Parse(false);
            }
            
            /// <summary>
            /// Reset the command to it's initial state
            /// </summary>
            /// <returns>
            /// <c>true</c> on success
            /// </returns>
            /// <remarks>
            /// Call this method to release resources. 
            /// Throws an <see cref="ZIMapException.Error.CommandBusy"/> error if
            /// the command is currently executing.
            /// <para />
            /// For implementors: this routine calls Parse(true), a derived class
            /// should not override <see cref="Reset()"/> but release it's resources 
            /// in <see cref="Parse(bool)"/>.
            /// </remarks>
            public override bool Reset ()
            {
                if(!base.Reset()) return false;
                parsed = false;
                Parse(true);
                return true;
            }
        }

        // =====================================================================
        // Base class for Commands that are based on a mailbox
        // =====================================================================

        /// <summary>
        /// A base class for commands the are based on a mailbox argument.
        /// </summary>
        /// <remarks>
        /// The command classes Examine/Select, Create, Delete, Subscribe
        /// and Unsubcribe are derived from this class.
        /// </remarks>
        public abstract class MailboxBase : Generic
        {
            protected string  mboxname;
            
            // base has no def xtor ...
            public MailboxBase(ZIMapFactory parent, string command) : base(parent, command) {}

            /// <summary>
            /// Returns any mailbox name passed to <see cref="Queue()"/>.
            /// </summary>
            /// <remarks>
            /// This is a simple accessor property and should not have any side effects.
            /// Mailbox names are always stored as Unicode names.
            /// </remarks>
            public string MailboxName
            {   get {   return mboxname; }
            }

            /// <summary>
            /// Handles additional parsing of server responses.
            /// </summary>
            /// <returns>
            /// <c>true</c> on success
            /// </returns>
            /// <remarks>
            /// This override just clears the MailboxName.
            /// </remarks>
            protected override bool Parse(bool reset)
            {   if(reset) mboxname = null;
                return true;
            }
            
            /// <summary>
            /// Queue a command with a mailbox name argument.
            /// </summary>
            /// <param name="mailboxName">
            /// The (unicode) mailbox name.
            /// </param>
            /// <remarks>
            /// Internally mailbox names are stored as Unicode. The 
            /// <see cref="AddMailbox"/> is used to encode the actual value.
            /// </remarks>
            public virtual bool Queue(string mailboxName)
            {   mboxname = mailboxName;
                AddMailbox(mailboxName);
                return Queue();
            }

            /// <summary>
            /// Helper that can be used to store canonicalized mbox names returned 
            /// by the server.
            /// </summary>
            /// <returns>
            /// <c>true</c> on success or <c>false</c> if the name could not be
            /// decoded.
            /// </returns>
            protected bool UpdateMailboxName(string encodedName)
            {   bool bok;
                string mbox = ZIMapConverter.MailboxDecode(encodedName, out bok);
                if(!bok) return false;
                mboxname = mbox;
                return true;
            }
            
            protected override void ToString (StringBuilder sb)
            {   sb.AppendFormat("\n         mbox: {0}", mboxname);
            }
       }

        // =====================================================================
        // Base class for Commands that take a sequence argument
        // =====================================================================

        /// <summary>
        /// A base class for commands the are tale a sequence argument.
        /// </summary>
        /// <remarks>
        /// The command classes Fetch, Store and Copy are derived from this class.
        /// </remarks>
        public abstract class SequenceBase : Generic
        {
            // base has no def xtor ...
            public SequenceBase(ZIMapFactory parent, string command) : base(parent, command) {}
            
            /// <summary>
            /// Operate on a single mail
            /// </summary>
            /// <param name="index">
            /// Mail item ID (or UID see the <see cref="UidCommand"/> property).
            /// </param>
            /// <param name="what">
            /// Command parameters in IMap syntax.
            /// </param>
            /// <remarks>This functions just calls <c>Queue(index, uint.MaxValue, what)</c>.
            /// </remarks>
            public bool Queue(uint index, string what)
            {   return Queue(index, 0, what);
            }

            /// <summary>
            /// Operate on a sequence of mails
            /// </summary>
            /// <param name="firstIndex">
            /// Start index, can be 0.
            /// </param>
            /// <param name="lastIndex">
            /// Final index, can be <c>uint.MaxValue</c> for no limit.
            /// </param>
            /// <param name="what">
            /// Command parameters in IMap syntax.
            /// </param>
            /// <remarks>Use <c>Queue(1, uint.MaxValue, what)</c> to select all
            /// items.
            /// </remarks>
            public bool Queue(uint firstIndex, uint lastIndex, string what)
            {   string format;
                if     (lastIndex == firstIndex)    format = "{0}";
                else if(lastIndex == 0)             format = "{0}";
                else if(lastIndex == uint.MaxValue) format = "{0}:*";
                else                                format = "{0}:{1}";
                string range = string.Format(format, firstIndex, lastIndex);   
                AddDirect(range);
                if(command == "FETCH") AddList(what);
                else                   AddDirect(what);
                return Queue();
            }

            /// <summary>
            /// Operate on a list of mails
            /// </summary>
            /// <param name="items">
            /// An array of mail UIDs or IDs (see the <see cref="UidCommand"/> property).
            /// </param>
            /// <param name="what">
            /// Command parameters in IMap syntax, where the COPY commands treats the
            /// value as a mailbox name and FETCH as a list. 
            /// </param>
            public bool Queue(uint [] items, string what)
            {   AddSequence(items);
                if     (command == "COPY")  AddMailbox(what);
                else if(command == "FETCH") AddList(what);
                else                        AddDirect(what);
                return Queue();
            }
       }
   }
}
