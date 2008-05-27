//==============================================================================
// ZIMapProtocol.cs implements the ZIMapProtocol class    
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU Lesser General Public License
// This software is published under the GNU LGPL license. Please refer to the
// files COPYING and COPYING.LESSER for details. Please use the following e-mail
// address to contact me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.IO;
using System.Net;
using System.Collections.Generic;

namespace ZIMap
{
    //==========================================================================
    // Structures
    //==========================================================================

    /// <summary>
    /// Holds an untagged server reply.
    /// </summary>
    /// <remarks>
    /// Usually this structure is a member array of <see cref="ZIMapReceiveData"/>.
    /// </remarks>
    public struct ZIMapReceiveInfo
    {
        public string      Status;
        public string      Message;

        private ZIMapParser parser;
        
        /// <summary>
        /// Resets the structure to it's initial state
        /// </summary>
        /// <remarks>
        /// Call this method to release resources (mostly the parser). 
        /// </remarks>
        public void Reset()
        {   Status = Message = null;
            parser = null;
        }
        
        /// <value>
        /// A parser for the <see cref="Message"/> property.
        /// </value>
        /// <remarks>
        /// This property remembers the last return value (e.g. the parser is
        /// created  on the 1st call and kept in memory) see <see cref="Reset"/>.
        /// </remarks>
        public ZIMapParser Parser
        {   get {   if(parser != null) return parser;
                    parser = new ZIMapParser(Message);
                    return parser;
                }
            set {   if(value == null)
                        parser = null;
                    else
                        ZIMapException.Throw(null, ZIMapErrorCode.InvalidArgument, "must be null");
                }
        }
        
        /// <summary>
        /// Renders the structure state to a string
        /// </summary>
        /// <returns>
        /// A string for debug output
        /// </returns>
        public override string ToString()
        {   return Status + " " + Message;
        }
    }

    /// <summary>
    /// Holds the server replies to a command.
    /// </summary>
    /// <remarks>
    /// Usually created by <see cref="ZIMapProtocol.Receive(ZIMapReceiveData)"/>
    /// and consumed by <see cref="ZIMapCommand.ReceiveCompleted"/>.
    /// </remarks>
    public struct ZIMapReceiveData
    {   /// <value>The tag number is set by Send()</value>
        public uint                 Tag;
        /// <value>Returned by server (OK, BAD, FAILED)</value>
        public string               Status;
        /// <value>Returned by server, descriptive text</value>
        public string               Message;
        public ZIMapReceiveInfo[]   Infos;
        public byte[][]             Literals;
        public ZIMapReceiveState    ReceiveState;

        private ZIMapParser parser;

        /// <summary>
        /// Resets the structure to it's initial state
        /// </summary>
        /// <remarks>
        /// Call this method to release resources (mostly the parsers). 
        /// </remarks>
        public void Reset()
        {   Tag = 0;
            Status = Message = null;
            Infos = null;
            Literals = null;
            ReceiveState = ZIMapReceiveState.Exception;
        }
        
        /// <value>
        /// A parser for the <see cref="Message"/> property.
        /// </value>
        /// <remarks>
        /// This property remembers the last return value (e.g. the parser is
        /// created  on the 1st call and kept in memory) see <see cref="Reset"/>.
        /// </remarks>
        public ZIMapParser Parser
        {   get {   if(parser != null) return parser;
                    parser = new ZIMapParser(Message);
                    return parser;
                }
            set {   if(value == null)
                        parser = null;
                    else
                        ZIMapException.Throw(null, ZIMapErrorCode.InvalidArgument, "must be null");
                }
        }

        /// <value>
        /// Returns <c>true</c> if ReceiveState is <see cref="ZIMapReceiveState.Ready"/>
        /// </value>
        public bool Succeeded
        {   get {   return (ReceiveState == ZIMapReceiveState.Ready);   }
        }

        /// <summary>
        /// Renders the structure state to a string
        /// </summary>
        /// <returns>
        /// A string for debug output
        /// </returns>
        public override string ToString()
        {   System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int ninf = (Infos == null) ? 0 : Infos.Length;
            sb.AppendFormat("ZIMapReceiveData:  ReceiveState={0}  Tag={1}  Infos={2}   Literals={3}",
                            ReceiveState, Tag, ninf, (Literals == null) ? 0 : Literals.Length);
            for(int irun=0; irun < ninf; irun++)
                sb.AppendFormat("\n    Info {0}: {1}", irun+1, Infos[irun]); 
            sb.AppendFormat("\n    Message: {0}", Message); 
            return sb.ToString ();
        }
    }


    //==========================================================================
    // Classes
    //==========================================================================

    public abstract class ZIMapProtocol : ZIMapBase
    {
        private readonly ZIMapTransport  transport;
        private readonly ZIMapConnection connection;

        private string  greeting;        
        private uint    send_cnt;
        private bool    bye_received;
        private uint    exists_cnt = uint.MaxValue;

        public ZIMapProtocol(ZIMapBase parent, ZIMapTransport transport) : base(parent)
        {   this.transport = transport;
            this.connection = parent as ZIMapConnection;
        }

        /// <value>
        /// If this property is not set to <c>uint.MaxValue</c> (which is the default)
        /// the server reponses will be checked for "* nn EXISTS" messages and on
        /// reception of such a message this property will be updated and a
        /// <see cref="ZMApMonitorInfo.Message"/> callback will be made. Set this 
        /// property to <c>0</c> to enable callbacks and to <c>uint.MaxValue</c> to
        /// disable them. By default these callbacks are disabled to save time. 
        /// </value>
        public uint ExistsCount
        {   get {   return exists_cnt;  }
            set {   exists_cnt = value; }
        }

        public string ServerGreeting 
        {   get {   if(greeting != null) return greeting;
                    if(transport == null) return "";
                    Monitor(ZIMapMonitor.Debug, "ServerGreeting: fetching");
                    bool bok = false;
                    uint tag = 0;
                    string status, message;
                    ZIMapReceiveState rsta;
                    bok = transport.Poll(1000);              // should be in buffer!
                    if(bok)
                    {   rsta = Receive(out tag, out status, out greeting);
                        if(rsta == ZIMapReceiveState.Info)
                            return greeting;
                    }
                    else if(transport.Send("* NOOP"))
                    {   while(true)
                        {   rsta = Receive(out tag, out status, out message);
                            if(rsta == ZIMapReceiveState.Info)
                                greeting = message;
                            else if(rsta == ZIMapReceiveState.Ready)
                                return greeting;
                            else 
                                break;
                        }
                    }
                    Error(ZIMapErrorCode.CannotConnect, "Invalid or missing greeting");
                    return "";
                } 
        }
        
        /// <summary>
        /// Low level routine to send an IMap command to the server. Usually
        /// this routine is not called directly - use ZIMapCommand::Execute()
        /// instead. When this routine is used the server response must be
        /// collected using the Receive() method.
        /// </summary>
        /// <param name="message">
        /// The command to be sent
        /// </param>
        /// <returns>
        /// On success: the tag number of this command. On error 0 is returned. 
        /// </returns>
        /// <remarks>
        /// This method does not handle literals, see <see cref="Send(object[])"/>.
        /// </remarks>
        public uint Send(string message)
        {   if(transport == null)                           // connection is closed
            {   Error(ZIMapErrorCode.DisposedObject);
                return 0;
            }

            object info;
            if(greeting == null) info = ServerGreeting;     // must fetch greeting
                
            try
            {   uint cnt = ++send_cnt;
                ZIMapConnection.Callback.Request(connection, cnt, message);

                if(transport.Send(cnt, message))            // exception for message=null
                    return cnt;

                if(transport.IsClosed)
                {   info = "transport closed";
                    ZIMapConnection.Callback.Closed(connection);
                }                    
                else   
                    info = "transport timeout";
            }
            catch(Exception inner)
            {   info = inner;
            }
            Error(ZIMapErrorCode.SendFailed, info);
            return 0;
        }

        /// <summary>
        /// Overload of Send() that can be used for requests with literal data.
        /// </summary>
        /// <param name="fragments">
        /// An array containing strings and byte arrays. The first array element
        /// must be a string. Multiple strings will be sent with a separating
        /// space, byte arrays will be sent as literal data.
        /// </param>
        /// <returns>
        /// On success: the tag number of this command. On error 0 is returned. 
        /// </returns>
        /// <remarks>
        /// Make sure that any queued command gets sent and that the server reply
        /// is processed before issuing a command containing literal data. This
        /// layer cannot handle pending command output.
        /// </remarks>
        public uint Send(object[] fragments)
        {   if(transport == null)                           // connection is closed
            {   Error(ZIMapErrorCode.DisposedObject);
                return 0;
            }

            // --- need a string to start ---
            
            if(fragments == null || fragments.Length <= 0 || !(fragments[0] is string))
            {   Error(ZIMapErrorCode.InvalidArgument);
                return 0;
            }
            
            object info;
            if(greeting == null) info = ServerGreeting;     // must fetch greeting

            try
            {   uint cnt = ++send_cnt;                      // tag number to send
                int  idx = 0;
                bool bok  = true;
                bool blit = false;                          // last frag was literal
                bool btag = false;                          // sent tag flag           
                string message; byte[] literal;

                ZIMapConnection.Callback.Request(connection, cnt, "[]");
                                                
                // --- loop over arguments ---
                
                while(bok)
                {   if(idx >= fragments.Length)             // array done
                    {   if(blit)                            // literal needs CR/LF
                        {   bok = transport.Send("");
                            if(!bok) break;
                        }
                        return cnt;
                    }

                    info = fragments[idx++];                // next argument

                    message = info as string;
                    if(message != null)                     // string fragment
                    {   if(message.Length <= 0)
                        {   Error(ZIMapErrorCode.InvalidArgument, "String fragment must not be empty");
                            return 0;
                        }
                                                            // concatenate strings...
                        while(idx < fragments.Length && fragments[idx] is string)
                            message = string.Format("{0} {1}", message, fragments[idx++]);
                                                            // append literal size...
                        if(idx < fragments.Length && fragments[idx] is byte[])
                            message = string.Format("{0} {{{1}}}", 
                                                message, ((byte[])fragments[idx]).Length);

                        if(!btag)                           // initial arg with tag
                            transport.Send(cnt, message);
                        else if(message[0] == ')' || message[0] == ']')
                            bok = transport.Send(message);
                        else
                            bok = transport.Send(" " + message);
                        blit = false; btag = true;
                        continue;
                    }

                    literal = info as byte[];
                    if(literal != null)                     // literal
                    {   string tag, status;
                        if(blit)                            // needs " {{xxx}}}"
                        {   message = string.Format(" {{{0}}}", literal.Length);
                            bok = transport.Send(message);
                            if(!bok) break;
                        }
                        blit = true;
                        while(true)
                        {   bok = transport.Receive(out tag, out status, out message);
                            if(!bok || tag == "+") break;
                            if(tag == cnt.ToString())       // oops! Server says error
                                Error(ZIMapErrorCode.SendFailed, "abort: " + message);
                            else
                                Error(ZIMapErrorCode.UnexpectedTag, "Want + but got " + tag);
                            return 0;
                        }
                        if(bok)
                        {   bok = transport.Send(literal);
                            continue;
                        }
                    }
                    
                    if(info == null)
                    {   Error(ZIMapErrorCode.InvalidArgument);
                        return 0;
                    }
                }
                if(transport.IsClosed)
                {   info = "transport closed";
                    ZIMapConnection.Callback.Closed(connection);
                }                    
                else   
                    info = "transport timeout";
                
            }
            catch(Exception inner)
            {   info = inner;
            }
            Error(ZIMapErrorCode.SendFailed, info);
            return 0;
        }
            
        private ZIMapReceiveState Receive(ref ZIMapReceiveData data, out byte[][] literals)
        {   literals = null;
            if(transport == null)                        // connection is closed
            {   Error(ZIMapErrorCode.DisposedObject);
                return ZIMapReceiveState.Exception;
            }

            object info;
            try
            {   string tags;
                if(transport.Receive(out tags, out data.Status,
                                     out data.Message, out literals))
                {
                    // --- check the tag ---

                    if(tags == "*" || tags == "0")
                    {   if(data.Status == "BYE")
                        {   bye_received = true;
                            Monitor(ZIMapMonitor.Info, "Receive: 'BYE' will close transport");
                        }
                        return ZIMapReceiveState.Info;
                    }
                    if(tags == "+")
                        return ZIMapReceiveState.Continue;
                    if(!uint.TryParse(tags, System.Globalization.NumberStyles.AllowHexSpecifier, 
                                      null, out data.Tag))
                    {   Error(ZIMapErrorCode.UnexpectedTag, "Tag: " + tags);
                        return ZIMapReceiveState.Exception;
                    }
                    
                    // --- check the status ---
                    
                    data.ReceiveState = ZIMapReceiveState.Error;
                    if(data.Status == "OK")
                    {   if(bye_received)                    // server sent untagged BYE
                        {   transport.Close();
                            ZIMapConnection.Callback.Closed(connection);
                        }
                        data.ReceiveState = ZIMapReceiveState.Ready;
                    }
                    else if(data.Status == "NO")
                        data.ReceiveState = ZIMapReceiveState.Failure;
                    else if(data.Status != "BAD")
                    {   Error(ZIMapErrorCode.UnexpectedData, "Status: " + data.Status);
                        return ZIMapReceiveState.Exception;
                    }
                    
                    ZIMapConnection.Callback.Result(connection, data);
                    return data.ReceiveState;
                }
                else if(transport.IsClosed)
                {   ZIMapConnection.Callback.Closed(connection);
                    return ZIMapReceiveState.Closed;
                }
                info = "transport timeout";
            }
            catch(Exception inner)
            {   info = inner;
            }
            Error(ZIMapErrorCode.ReceiveFailed, info);
            return ZIMapReceiveState.Exception;
        }
        
        public ZIMapReceiveState Receive(out uint tag, out string status, out string message)
        {   ZIMapReceiveData data = new ZIMapReceiveData();
            ZIMapReceiveState stat = Receive(ref data, out data.Literals);
            tag = data.Tag;
            status = data.Status;
            message = data.Message;
            if(data.Literals != null)
                Monitor(ZIMapMonitor.Error, "Receive: literal data ignored");
            return stat;
        }

        public bool Receive(out ZIMapReceiveData data)
        {   data = new ZIMapReceiveData();
            data.ReceiveState = Receive(ref data, out data.Literals);
            
            if(data.ReceiveState == ZIMapReceiveState.Info)
            {   List<string>    infos = new List<string>();
                List<byte[]>    multi = null;
                do {
                    infos.Add(data.Status);
                    infos.Add(data.Message);
                    byte[][] literals;
                    data.ReceiveState = Receive(ref data, out literals);
                    if(literals != null)
                    {   if(data.Literals == null)
                            data.Literals = literals;
                        else 
                        {   if(multi == null)
                            {   multi = new List<byte[]>();
                                foreach(byte[] elt in data.Literals) multi.Add(elt);
                            }
                            foreach(byte[] elt in literals) multi.Add(elt);
                        }
                    }
                } while(data.ReceiveState == ZIMapReceiveState.Info);

                int icnt = infos.Count / 2;
                data.Infos = new ZIMapReceiveInfo[icnt];
                for(int irun=0; irun < icnt; irun++)
                {   data.Infos[irun].Status  = infos[irun*2];
                    string message           = infos[irun*2+1];
                    data.Infos[irun].Message = message;
                    
                    // check for "* nn EXISTS" messages ...
                    if(exists_cnt != uint.MaxValue && message == "EXISTS")
                    {   uint ecnt = 0;
                        if(uint.TryParse(infos[irun*2], out ecnt)) exists_cnt = ecnt;
                    }
                }
                if(multi != null)
                    data.Literals = multi.ToArray();
            }

            // check for errors
            switch(data.ReceiveState)
            {   case ZIMapReceiveState.Info:
                case ZIMapReceiveState.Error:
                case ZIMapReceiveState.Failure:
                case ZIMapReceiveState.Ready:   return true;
                default:                        return false;
            }
        }
    }
}
