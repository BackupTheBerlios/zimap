//==============================================================================
// ZIMapTools       file for enumerations and helper classes
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
    // ZIMapException class    
    //==========================================================================
    /// <summary>
    /// An exception that is thrown when problems occur that cannot be handled
    /// by this IMAP implementation (simple IMAP protocol errors do not cause
    /// exceptions).
    /// </summary>
    public class ZIMapException : ApplicationException
    {
        /// <summary>
        /// Error codes used to throw an exception, see <see cref="ZIMapException"/>
        /// </summary>
        /// <remarks>
        /// Usually some additional text to describe the problem is passed along
        /// with <see cref="ZIMapException"/>
        /// </remarks>
        public enum Error {
            /// <summary>No error has occured (used as flag)</summary>
            NoError,    // must be first
            
            /// <summary>Cannot map the protocol name to the port number</summary>
            UnknownProtocol,
            /// <summary>Cannot connect to server</summary>
            CannotConnect,
            /// <summary>The server sent an unexpected tag that we don't understand</summary>
            UnexpectedTag,
            /// <summary>Could not receive data from server</summary>
            ReceiveFailed,
            /// <summary>The server sent data with a syntax that we don't understand</summary>
            UnexpectedData,
            /// <summary>Could not send data to the server</summary>
            SendFailed,
            /// <summary>Failed to close the connenction (exception in clean-up)</summary>
            CloseFailed,
            /// <summary>Attempt to use a ZIMapCommand after Dispose()</summary>
            DisposedObject,
            /// <summary>ZIMapCommand was queued but not yet answered</summary>
            CommandBusy,
            /// <summary>Cannot perform ZIMapCommand request in this state</summary>
            CommandState,
            /// <summary>Somthing that is not implemented was requested</summary>
            NotImplemented,
            /// <summary>A method was called with an inavalid argument</summary>
            InvalidArgument,
            /// <summary>Invalid argument, value must be 0 or null</summary>
            MustBeZero,
            /// <summary>Invalid argument, value cannot be 0 or null</summary>
            MustBeNonZero,
            /// <summary>Something unexpected has happened</summary>
            Unexpected,
            
            /// <summary>Unknow error (was an invalid error code)</summary>
            Unknown     // must be last
        };

        /// <summary>
        /// Array to map ZIMapErrorCode to messages texts 
        /// </summary>
        public static readonly string [] ErrorMessages = {
            /*NoError*/ "No error",         // must be first

                        "Unknown protocol name",
                        "Cannot connect to server", 
                        "Unexpected tag mark received from server",
                        "Failed to receive from server",
                        "Unexpected data received from server",
                        "Failed to send to server",
                        "Failed to close connection",
                        "Cannot use disposed object",
                        "Command is busy",
                        "Command in wrong state",
                        "Feature not implemented",
                        "The argument is invalid",
                        "Value must be 0 or null",
                        "Value cannot be 0 or null",
                        "Internal error, unexpected data",

            /*Unknown*/ "Unknown error"     // must be last
        };

        /// <summary>A <see cref="Error"/> value set by the constructor.</summary>
        public readonly Error   ErrorCode;
        
        public static string MessageFromCode(Error code)
        {   if(code < Error.NoError || code > Error.Unknown) code = Error.Unknown;
            return ErrorMessages[(int)code];
        }
        
        public ZIMapException(Error code) : base(MessageFromCode(code))
        {   ErrorCode = code; 
        }

        public ZIMapException(Error code, string message) : base(message)
        {   ErrorCode = code; 
        }

        public ZIMapException(Error code, Exception inner) : base(MessageFromCode(code), inner)
        {   ErrorCode = code; 
        }

        /// <summary>
        /// Pass exception object to ZIMapConnection.Callback, throw as error is not handled. 
        /// </summary>
        /// <param name="conn">
        /// The parent connection or <c>null</c> if unknown.
        /// </param>
        /// <param name="code">
        /// The error code <see cref="ZIMapException.ErrorCode"/>
        /// </param>
        /// <param name="arg1">
        /// <c>null</c> for no info, an Exception object as inner exception or any other
        /// object (ToString will be called).
        /// </param>
        public static void Throw(ZIMapConnection conn, Error code, object arg1)
        {   ZIMapException err;
            if(arg1 == null)
               err = new ZIMapException(code);
            else if(arg1 is Exception)
               err = new ZIMapException(code, (Exception)arg1);
            else
               err = new ZIMapException(code, MessageFromCode(code) + ": " + arg1.ToString());
            
            if(ZIMapConnection.Callback.Error(conn, err))
                return;
            throw err;
        }
    }

    //==========================================================================
    // ZIMapBase class    
    //==========================================================================
    
    /// <summary>
    /// This class is used as base class for most ZIMap classes.
    /// </summary>
    /// <remarks>
    /// The class provides the <c>Parent</c> property and Debug support.
    /// </remarks>
    public abstract class ZIMapBase
    {
        // usually of type ZIMapConnection
        private readonly ZIMapBase      parent;
        // log level for the instance
        private          ZIMapConnection.Monitor level = ZIMapConnection.Monitor.Error;
            
        /// <summary>
        /// This constructor is the only way to set the parent field.
        /// </summary>
        /// <param name="parent">
        /// Our parent - usually of type <see cref="ZIMapConnection"/>.
        /// </param>
        /// <remarks>A parent argument of <c>null</c> parents to object to itself.
        /// This trick should only be used by <see cref="ZIMapConnection"/>
        /// </remarks>
        public ZIMapBase(ZIMapBase parent)
        {   this.parent = (parent == null) ? this : parent;
        }
        
        /// <summary>
        /// Hook for sending error and debug messages or to inform of 
        /// status changes.
        /// </summary>
        /// <param name="level">
        /// <see cref="ZIMapConnection.Monitor"/> indicates the importance of the message.
        /// </param>
        /// <param name="message">
        /// The payload.
        /// </param>
        /// <remarks>Implementing this in a derived class gives the implementor
        /// full control about the message output.  Usually the implementation
        /// will just call
        /// <see cref="ZIMapConnection.MonitorInvoke(ZIMapConnection.Monitor, string)"/>.
        /// <para />
        /// Messages will only be processed if <paramref name="level"/> is less
        /// or equal to <see cref="MonitorLevel"/>).
        /// </remarks>
        protected abstract void MonitorInvoke(ZIMapConnection.Monitor level, string message);

        [System.Diagnostics.Conditional("DEBUG")]
        protected void MonitorDebug(string message)
        {   if(level > ZIMapConnection.Monitor.Debug) return;
            MonitorInvoke(ZIMapConnection.Monitor.Debug, message);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        protected void MonitorDebug(string message, params object[] args)
        {   if(level > ZIMapConnection.Monitor.Debug) return;
            MonitorInvoke(ZIMapConnection.Monitor.Debug, string.Format(message, args));
        }
        
        protected void MonitorInfo(string message)
        {   if(level > ZIMapConnection.Monitor.Info) return;
            MonitorInvoke(ZIMapConnection.Monitor.Info, message);
        }
        
        protected void MonitorInfo(string message, params object[] args)
        {   if(level > ZIMapConnection.Monitor.Info) return;
            MonitorInvoke(ZIMapConnection.Monitor.Info, string.Format(message, args));
        }
        
        protected void MonitorError(string message)
        {   MonitorInvoke(ZIMapConnection.Monitor.Error, message);
        }
        
        protected void MonitorError(string message, params object[] args)
        {   MonitorInvoke(ZIMapConnection.Monitor.Error, string.Format(message, args));
        }
        
        /// <summary>
        /// Report an error via callback and/or <see cref="ZIMapException"/>.
        /// </summary>
        /// <param name="code">
        /// The error code.
        /// </param>
        /// <param name="info">
        /// An additional description of the problem - can be of type
        /// Exception.
        /// </param>
        /// <remarks>
        /// If the info object is derived from the Exception class it is
        /// taken as 'inner exception'. For all other object types the
        /// result of ToString() is forwarded.
        /// </remarks>
        protected void RaiseError(ZIMapException.Error code, object info)
        {   ZIMapConnection conn = parent as ZIMapConnection;
            ZIMapException.Throw(conn, code, info);
        }
        
        /// <summary>
        /// Report an error via callback and/or <see cref="ZIMapException"/>.
        /// </summary>
        /// <param name="code">
        /// The error code.
        /// </param>
        /// <remarks>
        /// Consider using another overload to pass additional error information.
        /// </remarks>
        protected void RaiseError(ZIMapException.Error code)
        {   ZIMapConnection conn = parent as ZIMapConnection;
            ZIMapException.Throw(conn, code, null);
        }
        
        /// <summary>
        /// Most instances of classes in ZIMapLib belong to a tree that has a
        /// <see cref="ZIMapConnection"/> as root.
        /// </summary>
        /// <value>
        /// Returns the owner of this object.
        /// </value>
        /// <remarks>
        /// The owner is usually a ZIMapConnection and a ZIMapConnection
        /// is self-parented (e.g. has itself as the owner). 
        /// </remarks>
        public ZIMapBase Parent
        {   get {   return parent; }
        }
        
        /// <summary>
        /// Allows to control the amount of debug output generated by
        /// <see cref="MonitorInvoke"/>
        /// </summary>
        /// <value>
        /// Set/get the level down to which a call to Monitor generates output.
        /// </value>
        /// <remarks>
        /// The default value is <see cref="ZIMapConnection.Monitor.Info"/>. The only 
        /// accepted values are ZIMapMonitor.Debug, ZIMapMonitor.Info and
        /// ZIMapMonitor.Error - other values are silently ignored.
        /// </remarks>
        public ZIMapConnection.Monitor MonitorLevel
        {   get {   return level; }
            set {   if(level >= ZIMapConnection.Monitor.Debug &&
                       level <= ZIMapConnection.Monitor.Error) level = value; 
                }
        }
    }
    
    //==========================================================================
    // ZIMapParser class    
    //==========================================================================

    /// <summary>
    /// A parser for RFC3501 (imap) server replies.
    /// </summary>
    /// <remarks>
    /// Use the parser by creating an instance - the string passed to the
    /// constructor is parsed. The result is a list of <see cref="Token"/>
    /// objects that can be accessed the <see cref="Item"/> indexer.
    /// </remarks>
    public class ZIMapParser
    {
        /// <summary>
        /// Describes the type of a RFC3501 token, see ZIMapParser.Token .
        /// </summary>
        /// <remarks>
        /// There are more token types defined by RFC3501 but for the purpose of
        /// this library the types described here are sufficient.
        /// </remarks>
        public enum TokenType
        {   /// <summary>Not valid, initial state.</summary>
            Void,
            /// <summary>The token is an <c>uint</c> number.</summary>
            Number,
            /// <summary>The token data is simple text.</summary>
            Text,
            /// <summary>The token data is quoted text.</summary>
            Quoted,
            /// <summary>This token represents a [] list or tokens</summary>
            Bracketed,
            /// <summary>Gives the size of a literal</summary>
            Literal,
            /// <summary>This token represents a () list of tokens</summary>
            List
        }
    
        /// <summary>
        /// Token usually returned by <see cref="ZIMapParser"/>.
        /// </summary>
        public class Token
        {   private object      data;   
            private TokenType   type;
            
            /// <summary>
            /// The only xtor to create a token.
            /// </summary>
            /// <param name="data">
            /// Usually a string.
            /// </param>
            /// <param name="type">
            /// The token type.
            /// </param>
            /// <remarks>
            /// This code does some (time consuming) optimizations. For numeric tokens
            /// it converts a string argument to an uint and for Text tokens it uses
            /// the <c>string.Intern</c> Method to save memory.
            /// <para />
            /// Tokens of type <c>Numeric</c> that cannot be represented as an <c>uint</c>
            /// are silently converted to <c>Text</c> tokens.
            /// </remarks>
            public Token(object data, TokenType type)
            {
                this.type = type;
                if(!(data is string))
                {   this.data = data;
                    return;
                }

                // make Text and single char token intern ...
                string text = (string)data;
                if(type == TokenType.Text || text.Length == 1)
                    this.data = string.Intern(text);
                // try to use uint for Number and Literal
                else if(type == TokenType.Number || type == TokenType.Literal)
                {   uint uval;
                    if(uint.TryParse(text, out uval))
                        this.data = uval;
                    else
                    {   this.type = TokenType.Text;
                        this.data = text;
                    }
                }
                // special cases ...
                else if(text == "READ-ONLY")
                    this.data = "READ-ONLY";
                else if(text == "READ-WRITE")
                    this.data = "READ-WRITE";
                else if(text == "HEADER")
                    this.data = "HEADER";
                // nothing to be done ...                
                else
                {   this.data = string.IsInterned(text);
                    if(this.data == null) this.data = text;
                }
            }
            
            /// <summary>Returns information on the token's type.</summary>
            /// <remarks>Each token has a type that is stored in a 
            /// seperate field.</remarks>
            /// <value>A value from the <see cref="ZIMapParser.TokenType"/> enumeration.
            /// </value>
            public TokenType Type 
            {   get { return type; } 
            }
            
            /// <summary>Directly accesses the token data.</summary>
            /// <remarks>Internally the token is stored as an object, the
            /// other accessor properties cast to the specific type.</remarks>
            /// <value>An object that represents the token.</value>
            public object Data 
            {   get { return data; } 
            }
            
            /// <summary>Converts the token to an unsigned number.</summary>
            /// <remarks>May throw a conversion exception if the token type
            /// is not <c>Numeric</c></remarks>
            /// <value>The numeric representation of the token.</value>
            public uint Number  
            {   get {   if(data is uint) return (uint)data; 
                        return uint.Parse(data.ToString()); 
                    } 
            }
            
            /// <summary>Converts the token to text.</summary>
            /// <value>A simple string representation of the token</value>
            /// <remarks> Never returns <c>null</c> and never throws an exception.  See also
            /// <see cref="ToString()"/> for a more complete but alos more expensive
            /// translation to text.</remarks>
            public string Text 
            {   get {   if(data == null) return "";
                        if(type != TokenType.List) 
                            return data.ToString();
                        StringBuilder sb = new StringBuilder();
                        foreach(Token t in (Token[])data)
                        {   if(sb.Length > 1 && t.type != TokenType.Bracketed)
                                sb.Append(' ');
                            sb.Append(t.ToString());
                        }
                        return sb.ToString();
                    }
            }
            
            /// <summary>Returns the text of a quoted string or <c>null</c></summary>
            /// <value>The unquoted string</value>
            /// <remarks> This property returns <c>null</c> if the token is not
            /// of type <see cref="TokenType.Quoted"/>.  This can be used
            /// to distinguisch <c>NIL</c> values from quoted text.</remarks>
            public string QuotedText
            {   get {   if(data == null || type != TokenType.Quoted)
                            return null;
                        return (string)data;
                    }
            }
            
            /// <summary>Returns a token array for a List token</summary>
            /// <value>The array or <c>null</c> if the token is not a List</value>
            public Token[] List 
            {   get {   if(type != TokenType.List) return null; 
                        return (Token[])data; 
                    } 
            }
            
            /// <summary>String representation of the token</summary>
            /// <value>The string</value>
            /// <remarks>This function 'unparses' a token.  It restores braces and
            /// quotes.  This is more expensive than calling the Text property.
            /// </remarks>
            public override string ToString()
            {   if(data == null) return "";
                if(type == TokenType.List)
                {   StringBuilder sb = new StringBuilder("(");
                    sb.Append(Text);
                    sb.Append(')');
                    return sb.ToString();
                }
                else if(type == TokenType.Bracketed)
                {   return string.Format("[{0}]", (string)data);
                }
                else if(type == TokenType.Literal)
                {   return string.Format("{{{0}}}", Number);
                }
                else if(type == TokenType.Quoted)
                {   string tout;
                    ZIMapConverter.QuotedString(out tout, (string)data, true);
                    return tout;
                }
                return data.ToString();
            }
        }

        private List<Token>     tokens = new List<Token>();
            
        private ZIMapParser(string message, ref int index)
        {   Parse(message, ref index);
        }
        
        /// <summary>Creates an instance and parses a string.</summary>
        /// <param name="message">The string to be parsed</param>
        /// <remarks>The parser itself is a ligth-weight object, it's not
        /// worth of being reused.</remarks>
        public ZIMapParser(string message)
        {   if(string.IsNullOrEmpty(message)) return;
            int index = 0;
            Parse(message, ref index);
        }
        
        // helper used for lists
        private void Parse(string message, ref int start)
        {   TokenType state = TokenType.Void;
            bool skip = false;
            StringBuilder sb = new StringBuilder();

            int len = message.Length;               // dummy space at the end
            int idx = start;
            for(; idx <= len; idx++)
            {   char curr = (idx >= len) ? ' ' : message[idx];
                
                switch(state) 
                {   case TokenType.Void:
                            if(curr == ' ')
                                continue;
                            skip = false; sb.Length = 0;
                            if(curr == '"')
                                state = TokenType.Quoted;
                            else if(curr >= '0' && curr <= '9')
                            {   state = TokenType.Number;
                                sb.Append(curr);
                            }
                            else if(curr == '(')
                            {   idx++;
                                List<Token> list = new ZIMapParser(message, ref idx).tokens;
                                tokens.Add(new Token(list.ToArray(), TokenType.List));
                                idx--;                          // should be ')'
                                continue;
                            }
                            else if(curr == ')' && start > 0)
                            {   len=0; break;
                            }
                            else if(curr == '{')
                                state = TokenType.Literal;
                            else if(curr == '[')
                                state = TokenType.Bracketed;
                            else
                            {   state = TokenType.Text;
                                sb.Append(curr);
                            }
                            break;

                    case  TokenType.Bracketed:
                            if(curr == ']')
                            {   tokens.Add(new Token(sb.ToString(), state));
                                state = TokenType.Void;
                                continue;
                            }
                            sb.Append(curr);
                            break;

                    case  TokenType.Literal:
                            if(curr == '}')
                            {   tokens.Add(new Token(sb.ToString(), state));
                                state = TokenType.Void;
                                continue;
                            }
                            if(curr < '0' || curr > '9')
                                state = TokenType.Text;
                            sb.Append(curr);
                            break;

                    case  TokenType.Number:
                            if(curr == ')' || curr == ']')
                            {   curr  = ' '; idx--;
                            }
                            if(curr == ' ' || curr == '[')
                            {   tokens.Add(new Token(sb.ToString(), state));
                                state = TokenType.Void;
                                if(curr == '[') idx--;
                                continue;
                            }
                            if(curr < '0' || curr > '9')
                                state = TokenType.Text;
                            sb.Append(curr);
                            break;
                    
                    case  TokenType.Quoted:
                            if(skip)
                                skip = false;
                            else if(curr == '\\')
                                skip = true; 
                            else if(curr == '"')
                            {   tokens.Add(new Token(sb.ToString(), state));
                                state = TokenType.Void;
                            }
                            else
                                sb.Append(curr);
                            break;
                    
                    default:                                // text
                            if(curr == ')' || curr == ']')
                            {   curr  = ' '; idx--;
                            }
                            if(curr == ' ' || curr == '[')
                            {   tokens.Add(new Token(sb.ToString(), state));
                                state = TokenType.Void;
                                if(curr == '[') idx--;
                                continue;
                            }
                            sb.Append(curr);
                            break;
                }
            }
            start = idx;
        }
        
        /// <summary>Number of tokens in token list.</summary>
        public int Length 
        {   get { return tokens.Count; } 
        }
        
        /// <summary>Indexer to give access to the token list.</summary>
        /// <remarks>May throw an out of bounds exception.</remarks>
        public ZIMapParser.Token this[int index] 
        {   get {   return tokens[index];  }
        }

        /// <summary>Returns the array of Tokens.</summary>
        public ZIMapParser.Token[] ToArray()
        {   return tokens.ToArray();
        }
        
        /// <summary>Formatted debug output</summary>
        public override string ToString()
        {   StringBuilder sb = new StringBuilder();
            Dump(sb, 0, tokens.ToArray());
            return sb.ToString();
        }

        private void Dump(StringBuilder sb, int level, Token[] tokens)
        {   int cnt=1;
            sb.AppendFormat("ZIMapParser: {0} tokens", tokens.Length);
            foreach(Token tok in tokens)
            {   string text;
                if(tok.Type == TokenType.List)
                    text = String.Format("List ({0} items)", tok.List.Length);
                else
                    text = tok.Text;
                sb.AppendFormat("\n         {0}.{1}  type={2}  text='{3}'",
                                level, cnt++, tok.Type, text);
                if(tok.Type == TokenType.List)
                {   sb.AppendLine();
                    Dump(sb, level+1, tok.List);
                }
            }
        }
    }
}

