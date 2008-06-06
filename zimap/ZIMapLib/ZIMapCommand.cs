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
    /// Helper to issue IMAP commands and to collect the server responses
    /// belonging to it.
    /// </summary>
    /// <remarks>
    /// This class is public but abstract. You may for example use 
    /// <see cref="ZIMap.ZIMapFactory.CreateGeneric"/> to instantiate a concrete
    /// command object. Commands are associated with a <see cref="ZIMapFactory"/>
    /// (until <see cref="Dispose()"/> is called).  The factory is reponsible
    /// for command execution.
    /// </remarks>
    public abstract partial class ZIMapCommand : ZIMapBase, IDisposable
    {   // our parent factory
        protected readonly ZIMapFactory   factory;
        // the class name for Monitor()
        protected readonly string         name;
        // the object's state, modified at various points
        protected ZIMapCommandState state;
        /// <summary>not internally used, see UserData property</summary>
        protected object            userData;
        // prefix the command with UID
        protected bool              uidCommand;

        /// <summary>
        /// List of commands the can have the <c>UID</c> command prefix.
        /// </summary>
        /// <remarks>
        /// The RFC3501 "UID" Command is essentially a prefix for some IMap commands. This
        /// is implemented by the <see cref="ZIMapCommand.UidCommand" /> property. That
        /// property can be set <c>true</c> for the commands named in this array.
        /// </remarks>
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
        ZIMapReceiveData            data;
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
            state = ZIMapCommandState.Created;
        }
            
        // =====================================================================
        // Accessors for properties
        // =====================================================================

        public bool AutoDispose
        {   get {   return autoDispose; }
            set {   autoDispose = value; }
        }
        
        public ZIMapCommandState CommandState 
        {   get {   return state; }
        }
        
        public ZIMapReceiveData Data
        {   get {   if(tag == 0 || tag != data.Tag)
                    {   if(state == ZIMapCommandState.Created) Queue();
                        factory.ExecuteCommands(this);
                    }
                    return data;    
                }
        }
        
        public string CommandName 
        {   get {   return command; }
        }

        public bool HasLiterals 
        {   get {   return literals != null; }
        }

        public bool IsReady
        {   get {   return (state == ZIMapCommandState.Completed ||
                            state == ZIMapCommandState.Failed); }
        }
        
        public uint Tag {
            get {   return tag; }
        }

        public bool UidCommand
        {   get {   return  uidCommand; }
            set {   if(value == false)
                    {   uidCommand = false; return;
                    }
                    if(ZIMapFactory.FindInStrings(UidCommands, 0, command, false) < 0)
                        Error(ZIMapErrorCode.InvalidArgument, "Not a UID command");
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
                {   Error(ZIMapErrorCode.InvalidArgument);
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
            {   Error(ZIMapErrorCode.InvalidArgument, "null");
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
            {   Error(ZIMapErrorCode.InvalidArgument, "null");
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
        /// <see cref="Error(ZIMapErrorCode)"/> function will be called to throw
        /// an exception if a literal is required.
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
                    {   Error(ZIMapErrorCode.InvalidArgument, "has 8bit char");
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
            {   Error(ZIMapErrorCode.InvalidArgument, level.ToString());
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
        
        public bool AddList(string list)
        {   if(list == null) list = "";
            if(list.StartsWith("("))
            {   if(!list.EndsWith(")"))
                {   Error(ZIMapErrorCode.InvalidArgument, "Bad use of ()");
                    return false;
                }
                list = list.Substring(1, list.Length-2);
            }
            string[] arr = list.Split(" ".ToCharArray());
            return AddList(arr);
        }

        public bool AddList(string[] list)
        {   int iBeg = 0;
            int iEnd = (list == null) ? 0 : list.Length;
            uint level = AddBeginList();
            for(; iBeg < iEnd; iBeg++)
            {   if(list[iBeg] == "") continue;
                AddDirect(list[iBeg]);
            }
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
        /// Throws an <see cref="ZIMapErrorCode.CommandBusy"/> error if
        /// the command is currently executing.
        /// </remarks>
        public virtual bool Reset()
        {   if(state == ZIMapCommandState.Queued ||
               state == ZIMapCommandState.Running)
            {   Error(ZIMapErrorCode.CommandBusy, command);
                return false;
            }

            tag = listLevel = 0;
            literals = null;
            userData = null;
            data.Reset();
            state = ZIMapCommandState.Created;
            return true;
        }
        
        public bool Execute(bool wait)
        {   Monitor(ZIMapMonitor.Info, "Execute: " + name);
            
            if(state == ZIMapCommandState.Disposed)
            {   Error(ZIMapErrorCode.DisposedObject, name);
                return false;
            }
            if(state != ZIMapCommandState.Created &&
               state != ZIMapCommandState.Queued)
            {   Error(ZIMapErrorCode.CommandState, command);
                return false;
            }

            // we do not catch exceptions, assume error ...
            state = ZIMapCommandState.Failed;
            AddEndList(0);                          // implicitly close lists
            // now try ...
            
            ZIMapProtocol prot = ((ZIMapConnection)Parent).ProtocolLayer;
            if(prot == null)                        // should not happen!
            {   Error(ZIMapErrorCode.DisposedObject);
                return false;
            }

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
                tag = prot.Send(literals.ToArray());
                literals = null;
            }
            args = null;
            
            // did it work? We don't get here after an exception
            if(tag == 0) 
            {   Monitor(ZIMapMonitor.Error, "Excecute: send failed");
                return false;
            }
            state = ZIMapCommandState.Running;
            if(!wait) return true;
            return factory.ExecuteCommands(this);   // wait for result
        }
            
        public bool Queue()
        {   Monitor(ZIMapMonitor.Debug, "Queue");
            if(state == ZIMapCommandState.Disposed)
            {   Error(ZIMapErrorCode.DisposedObject, base.GetType());
                return false;
            }
            if(state != ZIMapCommandState.Created)
            {   Error(ZIMapErrorCode.CommandBusy, command);
                return false;
            }

            autoDispose = true;
            state = ZIMapCommandState.Queued;
            return factory.QueueCommand(this);      // make it the most recent
        }

        public bool ReceiveCompleted(ZIMapReceiveData rdata)
        {   if(state != ZIMapCommandState.Running)
            {   Error(ZIMapErrorCode.CommandState, state);
                return false;
            }
            if(tag != rdata.Tag)
            {   Error(ZIMapErrorCode.InvalidArgument, "wrong tag");
                return false;
            }
                
            state = (rdata.ReceiveState == ZIMapReceiveState.Ready)
                  ? ZIMapCommandState.Completed : ZIMapCommandState.Failed;
            Monitor(ZIMapMonitor.Info, "ReceiveCompleted: " + tag + "  state: " + state);
            data = rdata;
            if(MonitorLevel <= ZIMapMonitor.Debug)              // expensive ...
                Monitor(ZIMapMonitor.Debug, data.ToString());
            ZIMapConnection.Callback.Result(factory.Connection, this);

            return true;
        }

        /// <summary>
        /// Return an 'untagged response' parser for Data.Status == CommandName. 
        /// </summary>
        /// <returns>
        /// A parser on succes or <c>null</c> on error.
        /// </returns>
        /// <remarks>
        /// This routine will work for most simple IMap commands. If it returns <c>null</c>
        /// either the command failed with an error status or the server did not return
        /// an 'untagged response' with the expected Data.Status.
        /// </remarks>
        public ZIMapParser InfoParser()
        {   uint index = 0;
            return InfoParser("", ref index);
        }

        public ZIMapParser InfoParser(string filter, ref uint index)
        {   // return null if something went wrong ...
            ZIMapReceiveInfo[] infos;
            ZIMapReceiveData data = Data;
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
            sb.AppendFormat("Command: CommandName={0}  CommandState={1}",
                            CommandName, CommandState);
            if(IsReady) sb.AppendFormat("\n" +
                            "         ReceiveState={0}  Tag={1}\n" +
                            "         Result: {2}", data.Status, Tag, data.Message);
            if(data.Infos != null)
                foreach(ZIMapReceiveInfo info in data.Infos)
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
            
            // base has no def xtor ...
            public Generic(ZIMapFactory parent, string command) : base(parent)
            {   base.command = (command == null) ? "NOOP" : command.ToUpper();
            }
           
            // must implement, abstract in base ...
            protected override void Monitor(ZIMapMonitor level, string message)
            {   if(factory.MonitorLevel <= level)
                    ZIMapConnection.Monitor(Parent, name, level, message); 
            }

            /// <summary>
            /// Free resources and detach the command from it's factory
            /// </summary>
            /// <remarks>
            /// This method is similar to <see cref="Reset"/> but the later does
            /// not cancel a queued command nor detach it from it's factory.
            /// </remarks>
            public override void Dispose()
            {   if(state == ZIMapCommandState.Disposed) return;
                state = ZIMapCommandState.Disposed;

                // Tell the factory to cancel/remove the command ...
                factory.DetachCommand(this);

                // Call factory to handle AutoDispose
                if(factory.EnableAutoDispose && Tag > 0)
                    factory.DisposeCommands(this, false);
                
                // Free resources (must set Disposed again)
                Reset();
                state = ZIMapCommandState.Disposed;
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
            /// Throws an <see cref="ZIMapErrorCode.CommandBusy"/> error if
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
            /// Mail index
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
            /// <remarks>Use <c>Queue(0, uint.MaxValue, what)</c> to select all
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
            /// An array of mail UIDs or IDs.
            /// </param>
            public bool Queue(uint [] items, string what)
            {   AddSequence(items);
                if(command == "FETCH") AddList(what);
                else                   AddDirect(what);
                return Queue();
            }
       }
   }
}