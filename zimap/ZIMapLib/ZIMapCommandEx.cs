//==============================================================================
// ZIMapCommandEx.cs implements classes derived from ZIMapCommand    
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
    public abstract partial class ZIMapCommand
    {

        // =====================================================================
        // Standard IMAP Commands
        // =====================================================================
        // The following standards are (partially) implemented by this code:
        //
        // IMap           RFC 3501    http://www.faqs.org/rfcs/rfc3501.html
        // Namespace      RFC 2342    http://www.faqs.org/rfcs/rfc2342.html
        // Quota          RFC 2087    http://www.faqs.org/rfcs/rfc2087.html
        // ACL            RFC 4314    http://www.faqs.org/rfcs/rfc4314.html
        // =====================================================================

        // TODO: Implement IMap STARTTLS command        
        // TODO: Implement IMap AUTHENTICATE command        

        // ---------------------------------------------------------------------
        // Append
        // ---------------------------------------------------------------------
        /*
            string msg = "From: info@j-pfennnig.de\r\n" +
                         "To: info@j-pfennig.de\r\n" +
                         "Subject: IMAP Testing\r\n" +
                         "\r\n" +
                         "This is a test.\r\n";
            ZIMapCommand.Append acmd = app.Factory.CreateAppend();
            acmd.Queue("Spam", @"(\Seen)", System.Text.ASCIIEncoding.ASCII.GetBytes(msg));
            acmd.Execute(true);
        */
        
        /// <summary>
        /// A class that handles the APPEND IMap command.
        /// </summary>
        /// <remarks>
        /// The class overloads the Query() method.
        /// </remarks>
        public class Append : Generic
        {
            public Append(ZIMapFactory parent) : base(parent, "APPEND") {}

            public bool Queue(string mailbox, string flags, byte[] message)
            {   return Queue(mailbox, flags, DateTime.MinValue, message);
            }
            
            public bool Queue(string mailbox, string flags, DateTime time, byte[] message)
            {   if(message == null) return false;
                if(string.IsNullOrEmpty(mailbox)) return false;
                
                AddMailbox(mailbox);
                if(!string.IsNullOrEmpty(flags))
                    AddList(flags);
                if(time != DateTime.MinValue)
                    AddString(ZIMapConverter.EncodeTime(time, false));
                return AddLiteral(message);
            }
        }

        // ---------------------------------------------------------------------
        // Capability
        // ---------------------------------------------------------------------

        /// <summary>
        /// A class that handles the CAPABILITY IMap command.
        /// </summary>
        /// <remarks>
        /// The class overloads the <see cref="Parse(bool)"/> method and provides 
        /// the parser result via the <see cref="Capabilities"/> property.
        /// </remarks>
        public class Capability : Generic
        {
            public Capability(ZIMapFactory parent) : base(parent, "CAPABILITY") {}

            private string[] caps;

            /// <value>
            /// Returns <c>null</c> or and array of capability names.
            /// </value>
            /// <remarks>
            /// This property remembers the last return value (e.g. keeps memory
            /// in use) see <see cref="Reset"/>. It internally calls the 
            /// <see cref="Parse(bool)"/> method and returns <c>null</c> on errors.
            /// if the command failed.
            /// </remarks>
            public string[] Capabilities
            {   get {   Parse(); return caps;     }
            }

            protected override bool Parse(bool reset)
            {
                if(reset)
                {   caps  = null;
                    return true;
                }
            
                // return false if something went wrong ...
                ZIMapParser parser = InfoParser();
                if(parser == null) return false;
            
                // create an array of capabilites ...
                caps = new string[parser.Length];
                for(int irun=0; irun < parser.Length; irun++)
                    caps[irun] = parser[irun].Text;
                return true;
            }
        }

        // ---------------------------------------------------------------------
        // Check
        // ---------------------------------------------------------------------

        /// <summary>
        /// A class that handles the CHECK IMap command.
        /// </summary>
        /// <remarks>
        /// The class adds no functionality to it's base class.
        /// </remarks>
        public class Check : Generic
        {
            public Check(ZIMapFactory parent) : base(parent, "CHECK") {}
        }
 
        // ---------------------------------------------------------------------
        // Close
        // ---------------------------------------------------------------------

        /// <summary>
        /// A class that handles the CLOSE IMap command.
        /// </summary>
        /// <remarks>
        /// The class adds no functionality to it's base class.
        /// </remarks>
        public class Close : Generic
        {
            public Close(ZIMapFactory parent) : base(parent, "CLOSE") {}
        }

        // ---------------------------------------------------------------------
        // Copy
        // ---------------------------------------------------------------------

        public class Copy : SequenceBase
        {
            public Copy(ZIMapFactory parent) : base(parent, "COPY") {}
        }

        // ---------------------------------------------------------------------
        // Create
        // ---------------------------------------------------------------------

        public class Create : MailboxBase
        {
            public Create(ZIMapFactory parent) : base(parent, "CREATE") {}
        }

        // ---------------------------------------------------------------------
        // Delete
        // ---------------------------------------------------------------------

        public class Delete : MailboxBase
        {
            public Delete(ZIMapFactory parent) : base(parent, "DELETE") {}
        }


        // ---------------------------------------------------------------------
        // Expunge
        // ---------------------------------------------------------------------

        public class Expunge : Generic
        {
            public Expunge(ZIMapFactory parent) : base(parent, "EXPUNGE") {}
            
            private uint[] expunged;
            
            public uint[] Expunged
            {   get {   Parse(); return expunged; }
            }
            
            protected override bool Parse (bool reset)
            {
                if(reset)
                {   expunged = null;
                    return true;
                }
            
                ZIMapReceiveData data = Data;
                if(data.Infos == null) return false;
                List<uint> list = new List<uint>();
                for(int irun=0; irun < data.Infos.Length; irun++)            
                {   uint sequ;   
                    if(!uint.TryParse(data.Infos[irun].Status, out sequ))
                        continue;                           // status not a number
                    if(data.Infos[irun].Message != command)
                        continue;                           // message not "EXPUNGE"
                    list.Add(sequ);
                }
                expunged = list.ToArray();
                return true;
            }
       }
        
        // ---------------------------------------------------------------------
        // Fetch
        // ---------------------------------------------------------------------

        public class Fetch : SequenceBase
        {
            public struct Item
            {   public  uint        Index;          // index in mailbox
                public  uint        UID;            // uid (needs UID part)
                public  uint        Size;           // data size (needs RFC822.SIZE)
                public  string[]    Parts;          // unparsed parts
                public  string[]    Flags;          // flags (needs FLAGS)
                public  byte[]      Literal;        // literal data
                
                /// <summary>
                /// Search for a given flag.
                /// </summary>
                /// <remarks>
                /// The <c>flag</c> argument should start with a '\' character.
                /// The compare is not case sensitive.
                /// </remarks>
                public bool HasFlag(string flag)
                {   return ZIMapFactory.FindInStrings(Flags, 0, flag, false) >= 0; 
                }
            }

            public Fetch(ZIMapFactory parent) : base(parent, "FETCH") {}

            private Item[]  items;

            public Item[]   Items
            {   get {   Parse(); return items; }
            }
            
            protected override bool Parse(bool reset)
            {
                if(reset)
                {   items  = null;
                    return true;
                }
            
                ZIMapReceiveData data = Data;
                if(data.Infos == null) return false;
                items = new Item[data.Infos.Length];
                int ilit=0;                                 // literal index
                for(int irun=0; irun < items.Length; irun++)
                {   uint sequ;
                    if(!uint.TryParse(data.Infos[irun].Status, out sequ))
                        continue;                           // status not a number
                    items[irun].Index = sequ;
                    
                    ZIMapParser parser = new ZIMapParser(data.Infos[irun].Message);
                    if(parser.Length < 2 || parser[0].Text != command)
                        continue;                           // not for "FETCH"
                    if(parser[1].Type != ZIMapParserData.List)
                        continue;                           // server error
                    
                    ZIMapParser.Token[] tokens = parser[1].List;
                    List<string> parts = null;
                    
                    for(int itok=0; itok < tokens.Length; itok++)
                    {   ZIMapParser.Token token = tokens[itok];
                        
                        if(token.Type == ZIMapParserData.Literal)
                        {   if(data.Literals == null || ilit >= data.Literals.Length)
                                Monitor(ZIMapMonitor.Error, "Fetch: literal missing: " + ilit);
                            else
                            {   if(data.Literals[ilit].Length == token.Number)
                                    items[irun].Literal = data.Literals[ilit];
                                else
                                    Monitor(ZIMapMonitor.Error, "Fetch: literal invalid: " + ilit);
                                ilit++;
                            }
                        }
                        else if(token.Text == "UID")
                        {   if(itok+1 >= tokens.Length) continue;   // server bug
                            token = tokens[itok+1];                 // should be a number
                            if(token.Type == ZIMapParserData.Number)
                            {   items[irun].UID = token.Number;
                                itok++;
                            }
                        }
                        else if(token.Text == "FLAGS")
                        {   if(itok+1 >= tokens.Length) continue;   // server bug
                            token = tokens[itok+1];                 // should be a list
                            if(token.Type == ZIMapParserData.List)
                            {   string[] flis = new string[token.List.Length];
                                items[irun].Flags = flis;
                                int isub=0;
                                foreach(ZIMapParser.Token flag in token.List)
                                    flis[isub++] = flag.Text;
                                itok++;
                            }
                        }
                        else if(token.Text == "RFC822.SIZE")
                        {   if(itok+1 >= tokens.Length) continue;   // server bug
                            token = tokens[itok+1];                 // should be a number
                            if(token.Type == ZIMapParserData.Number)
                            {   items[irun].Size = token.Number;
                                itok++;
                            }
                        }
                        else 
                        {   if(parts == null) parts = new List<string>();
                            parts.Add(token.ToString());
                        }
                    }
                    if(parts != null)
                       items[irun].Parts = parts.ToArray();
                }
                return true;
            }
        }

        // ---------------------------------------------------------------------
        // List and Lsub
        // ---------------------------------------------------------------------

        public class List : Generic
        {
            public struct Item
            {   public  string[]    Attributes;
                public  string      Name;
                public  char        Delimiter;
            }
            
            public List(ZIMapFactory parent) : base(parent, "LIST") {}
            
            private Item[]  items;

            public Item[]   Items
            {   get {   Parse(); return items; }
            }
            
            protected override bool Parse(bool reset)
            {
                if(reset)
                {   items  = null;
                    return true;
                }
                
                List<Item> list = new List<Item>();
                ZIMapParser parser;
                uint index = 0;
                while((parser = InfoParser("", ref index)) != null)
                {   if(parser.Length < 3) continue;                 // server error
                    
                    Item item = new ZIMapCommand.List.Item();
                    ZIMapParser.Token token = parser[0];
                    string[] atts;
                    
                    if(token.Type == ZIMapParserData.List)
                    {   atts = new string[token.List.Length];
                        for(int itok=0; itok < token.List.Length; itok++)
                            atts[itok] = token.List[itok].Text;
                    }
                    else
                        atts = ZIMapConverter.StringArray(0);

                    item.Attributes = atts;
                    string sdmy = parser[1].Text;
                    item.Delimiter = (sdmy == null) ? (char)0 : sdmy[0];
                    bool bdmy;
                    item.Name = ZIMapConverter.MailboxDecode(parser[2].Text, out bdmy);
                    list.Add(item);
                }
                if(index != 0) return false;                        // error, no data
                items = list.ToArray();
                return true;
            }
            
            public bool Queue(string reference, string mailbox)
            {   AddString(reference);
                AddMailbox(mailbox);
                return Queue();
            }
        }
        
        public class Lsub : List
        {
            public Lsub(ZIMapFactory parent) : base(parent) {  command = "LSUB"; }
        }
        
        // ---------------------------------------------------------------------
        // Login and Logout
        // ---------------------------------------------------------------------
        
        public class Login : Generic
        {   
            private string username;
            
            public Login(ZIMapFactory parent) : base(parent, "LOGIN") {}
       
            public string User
            {   get {   return username;  }
            }
            
            protected override bool Parse(bool reset)
            {   if(reset) username = null;
                return true;
            }
            
            public bool Queue(string user, string password)
            {   username = user;
                AddString(user, true);
                AddString(password, true);
                factory.Capabilities = null;
                return Queue();
            }
        }
        
        public class Logout : Generic
        {
            public Logout(ZIMapFactory parent) : base(parent, "LOGOUT") {}
        }

        // ---------------------------------------------------------------------
        // Rename
        // ---------------------------------------------------------------------

        public class Rename : Generic
        {
            public Rename(ZIMapFactory parent) : base(parent, "RENAME") {}

            public bool Queue(string oldMailboxName, string newMailboxName)
            {   AddString(oldMailboxName);
                AddString(newMailboxName);
                return Queue();
            }
        }

        // ---------------------------------------------------------------------
        // Search
        // ---------------------------------------------------------------------

        public class Search : Generic
        {
            public Search(ZIMapFactory parent) : base(parent, "SEARCH") {}

            private uint[] matches;            
            
            public uint[] Matches
            {   get {   Parse(); return matches; }
            }
            
            protected override bool Parse(bool reset)
            {
                if(reset)
                {    matches = null;
                    return true;
                }

                // return false if something went wrong ...
                ZIMapParser parser = InfoParser();
                if(parser == null) return false;

                // parse the info message ...
                List<uint> list = new List<uint>();
                for(int isub=0; isub < parser.Length; isub++)
                    if(parser[isub].Type == ZIMapParserData.Number)
                        list.Add(parser[isub].Number);
                matches = list.ToArray();
                return true;
            }

            /// <summary>
            /// Prepare a simple search request without any (literal) arguments.
            /// </summary>
            /// <para name="keys">
            /// An IMap search expression like 'or text green text red'.
            /// </para>
            /// <returns>
            /// <c>true</c> on success.
            /// </returns>
            public bool Queue(string keys)
            {   return Queue(null, keys, null);
            }

            /// <summary>
            /// Prepare a search request with (literal) arguments.
            /// </summary>
            /// <para name="charset">
            /// An optional charater set that is supported by the server. Use
            /// <c>null</c> if you do not want to specify a character set. An
            /// empty string will select 'utf-8' any other value is passed to
            /// the server as the character set name.
            /// </para>
            /// <para name="keys">
            /// An IMap search expression like 'or text ? text ?'. The question
            /// marks are place holders. Each question mark corresponds to an
            /// entry in the params argument array.
            /// </para>
            /// <para name="args">
            /// An array of current values to replace the question marks in the
            /// keys argument.
            /// </para>
            /// <returns>
            /// <c>true</c> on success.
            /// </returns>
            /// <remarks>
            /// This routine automatically sends data as literals if required.
            /// </remarks>
            public bool Queue(string charset, string keys, params string[] args)
            {   if(charset == "") charset = "utf-8";   
                if(charset != null && charset.Length > 0)
                {   AddDirect("CHARSET");
                    AddDirect(charset);
                }
                if(keys == null || keys.Length < 0)
                {   Error(ZIMapErrorCode.InvalidArgument, "Search needs a non-empty key");
                    return false;
                }

                keys = keys.Trim();
                int idx = keys.IndexOf('?');
                if(idx < 0)
                {   if(args != null && args.Length > 0)
                    {   Error(ZIMapErrorCode.InvalidArgument, "Search args but key without ?");
                        return false;
                    }
                    AddDirect(keys);
                    return Queue();
                }

                for(int iarg=0; ; iarg++)
                {   if((idx == 0 && iarg == 0) ||
                       (idx >= 0 && (args == null || args.Length <= iarg)))
                    {   Error(ZIMapErrorCode.InvalidArgument, "Search key with invalid argument: " + iarg);
                        return false;
                    }
                    if(idx > 0) AddDirect(keys.Substring(0, idx).Trim());
                    AddString(args[iarg], true);
                    if(idx + 1 >= keys.Length) break;
                    if(keys[idx+1] == ' ') 
                    {   idx++;
                        if(idx + 1 >= keys.Length) break;
                    }
                    keys = keys.Substring(idx + 1).Trim();
                    idx = keys.IndexOf('?');
                    if(idx < 0)
                    {   AddDirect(keys);
                        break;
                    }
                }
                return Queue();
            }
        }

        // ---------------------------------------------------------------------
        // Select and Examine 
        // ---------------------------------------------------------------------

        public class Examine : Select
        {
            public Examine(ZIMapFactory parent) : base(parent) 
            {   command = "EXAMINE";
                readOnly = true;
            }
        }

        public class Select : MailboxBase
        {
            public Select(ZIMapFactory parent) : base(parent, "SELECT") {}

            protected bool  readOnly;
            private uint    messages;
            private uint    recent;
            private uint    unseen;
            private string  flags;

            public string[] Flags
            {   get {   Parse();
                        if(flags == null) return null;
                        ZIMapParser parser = new ZIMapParser(flags);
                        if(parser.Length < 1 || parser[0].Type != ZIMapParserData.List)
                            return null;
                        ZIMapParser.Token[] list = parser[0].List;
                        string[] rval = new string[list.Length];
                        for(int irun=0; irun < list.Length; irun++)
                            rval[irun] = list[irun].Text;
                        return rval;
                    }
            }
            
            public uint Messages
            {   get {   Parse(); return messages; }
            }

            public uint Recent
            {   get {   Parse(); return recent; }
            }
            
            public uint Unseen
            {   get {   Parse(); return unseen; }
            }
            
            public bool IsReadOnly
            {   get {   Parse(); return readOnly; }
            }
            
            protected override bool Parse(bool reset)
            {   base.Parse(reset);
                if(reset)
                {   messages = recent = unseen = 0;
                    args = null;
                    return true;
                }
                
                ZIMapReceiveData data = Data;
                if(data.Infos == null) return false;
                if(!IsReady) return false;

                // get flags. exists and recent are special...
                foreach(ZIMapReceiveInfo i in data.Infos)            
                {   if(i.Status == "FLAGS")
                        flags = i.Message; 
                    else if(i.Message == "EXISTS")
                        uint.TryParse(i.Status, out messages);
                    else if(i.Message == "RECENT")
                        uint.TryParse(i.Status, out recent);
                }
                
                // search for unseen ...
                foreach(ZIMapReceiveInfo i in data.Infos)            
                {   if(i.Status != "OK") continue;
                    ZIMapParser parser = new ZIMapParser(i.Message);
                    if(parser.Length < 1) continue;
                    if(parser[0].Type != ZIMapParserData.Bracketed) continue;
                    
                    parser = new ZIMapParser(parser[0].Text);
                    if(parser.Length < 2) continue;
                    if(parser[0].Text == "UNSEEN")
                    {   unseen = parser[1].Number;
                        break;
                    }
                }
                
                // get the readonly flag
                ZIMapParser.Token access = data.Parser[0];
                if(access.Type == ZIMapParserData.Bracketed)
                {   if(access.Text == "READ-ONLY")
                        readOnly = true;
                    else if(access.Text == "READ-WRITE")
                        readOnly = false;
                }
                data.Parser = null;
                return true;
            }
            
            protected override void ToString (StringBuilder sb)
            {   base.ToString(sb);
                sb.AppendFormat(
                    "\n         ReadOnly={0}  Messages={1}  Recent={2}  Unseen={3}",
                    IsReadOnly, Messages, Recent, Unseen);
            }
        }

        // ---------------------------------------------------------------------
        // Status
        // ---------------------------------------------------------------------

        public class Status : MailboxBase
        {
            public Status(ZIMapFactory parent) : base(parent, "STATUS") {}

            private uint cntMessages, cntRecent, cntUnseen;
            
            public uint Messages
            {   get {   Parse(); return cntMessages; }
            }

            public uint Recent
            {   get {   Parse(); return cntRecent; }
            }

            public uint Unseen
            {   get {   Parse(); return cntUnseen; }
            }

            public override bool Queue (string mailboxName)
            {   return Queue(mailboxName, null);
            }

            public bool Queue (string mailboxName, string items)
            {   if(string.IsNullOrEmpty(items)) items = "MESSAGES RECENT UNSEEN";
                if(string.IsNullOrEmpty(mailboxName)) return false;
                mboxname = mailboxName;
                AddMailbox(mailboxName);
                AddList(items);
                return Queue();
            }
            
            protected override bool Parse (bool reset)
            {
                if(reset)
                {   cntMessages = cntRecent = cntUnseen = 0;
                    return base.Parse(reset);
                }

                // return false if something went wrong ...
                ZIMapParser parser = InfoParser();
                if(parser == null) return false;
                
                // parsing ...
                if(parser.Length > 1 && parser[1].Type == ZIMapParserData.List)
                {   UpdateMailboxName(parser[0].Text);              // canonicalized name   
                    ZIMapParser.Token[] tokens = parser[1].List;    
                    for(int itok=1; itok < tokens.Length; itok++)
                    {   if(tokens[itok-1].Type != ZIMapParserData.Text) continue;
                        if(tokens[itok].Type != ZIMapParserData.Number) continue;
                        switch(tokens[itok-1].Text)
                        {   case "MESSAGES":    
                                cntMessages = tokens[itok].Number; break;
                            case "RECENT":
                                cntRecent = tokens[itok].Number; break;
                            case "UNSEEN":
                                cntUnseen = tokens[itok].Number; break;
                        }
                    }
                }
                return true;
            }
             
            protected override void ToString (StringBuilder sb)
            {   base.ToString(sb);
                sb.AppendFormat("\n         Messages={0}  Recent={1}  Unseen={2}",
                                Messages, Recent, Unseen);
            }
       }

        // ---------------------------------------------------------------------
        // Store
        // ---------------------------------------------------------------------

        public class Store : SequenceBase
        {
            public struct Item
            {   public  uint        Index;          // index in mailbox
                public  uint        UID;            // id (needs UID part)
                public  string[]    Parts;          // unparsed parts
                public  string[]    Flags;          // flags (needs FLAGS)
            }
                
            public Store(ZIMapFactory parent) : base(parent, "STORE") {}

            private Item[]  items;

            public Item[]   Items
            {   get {   Parse(); return items; }
            }
            
            protected override bool Parse(bool reset)
            {
                if(reset)
                {   items  = null;
                    return true;
                }
            
                ZIMapReceiveData data = Data;
                if(data.Infos == null) return false;
                items = new Item[data.Infos.Length];
                for(int irun=0; irun < items.Length; irun++)            
                {   uint sequ;
                    if(!uint.TryParse(data.Infos[irun].Status, out sequ))
                        continue;                           // status not a number
                    ZIMapParser parser = new ZIMapParser(data.Infos[irun].Message);
                    if(parser.Length < 2 || parser[0].Text != "FETCH")
                        continue;                           // not for "FETCH"
                    if(parser[1].Type != ZIMapParserData.List)
                        continue;                           // server error
                    
                    ZIMapParser.Token[] tokens = parser[1].List;
                    List<string> parts = null;
                    
                    for(int itok=0; itok < tokens.Length; itok++)
                    {   ZIMapParser.Token token = tokens[itok];
                        
                        if(token.Text == "UID")
                        {   if(itok+1 >= tokens.Length) continue;   // server bug
                            token = tokens[itok+1];                 // should be a number
                            if(token.Type == ZIMapParserData.Number)
                            {   items[irun].UID = token.Number;
                                itok++;
                            }
                        }
                        else if(token.Text == "FLAGS")
                        {   if(itok+1 >= tokens.Length) continue;   // server bug
                            token = tokens[itok+1];                 // should be a list
                            if(token.Type == ZIMapParserData.List)
                            {   string[] flis = new string[token.List.Length];
                                items[irun].Flags = flis;
                                int isub=0;
                                foreach(ZIMapParser.Token flag in token.List)
                                    flis[isub++] = flag.Text;
                                itok++;
                            }
                        }
                        else 
                        {   if(parts == null) parts = new List<string>();
                            parts.Add(token.ToString());
                        }
                    }
                    if(parts != null)
                       items[irun].Parts = parts.ToArray();
                }
                return true;
            }
        }
        
        // ---------------------------------------------------------------------
        // Subscribe
        // ---------------------------------------------------------------------

        public class Subscribe : MailboxBase
        {
            public Subscribe(ZIMapFactory parent) : base(parent, "SUBSCRIBE") {}
        }

        // ---------------------------------------------------------------------
        // Unsubscribe
        // ---------------------------------------------------------------------

        public class Unsubscribe : MailboxBase
        {
            public Unsubscribe(ZIMapFactory parent) : base(parent, "UNSUBSCRIBE") {}
        }

        // =====================================================================
        // Namespace Command
        // =====================================================================

        public class Namespace : Generic
        {
            public Namespace(ZIMapFactory parent) : base(parent, "Namespace") {}

            private string[] namespaces;

            /// <summary>
            /// Returns the three namespace definitions (partially parsed)
            /// </summary>
            /// <remarks>
            /// The namespaces are: (0) personal  (1) other users  (2) shared
            /// </remarks>
            public string[] Namespaces
            {   get {   Parse(); return namespaces;   }
            }

            protected override bool Parse(bool reset)
            {
                if(reset)
                {   namespaces = null;
                    return true;
                }
            
                // return false if something went wrong ...
                ZIMapParser parser = InfoParser();
                if(parser == null) return false;

                // we should get 3 lists ...
                namespaces = new string[3];
                for(int irun=0; irun < Math.Min(3, parser.Length); irun++)
                {   if(parser[irun].Type != ZIMapParserData.List) continue;
                    namespaces[irun]  = parser[irun].Text;
                }
                return true;
            }
        }

        // =====================================================================
        // ACL IMAP Commands
        // =====================================================================
        
        // ---------------------------------------------------------------------
        // DeleteACL
        // ---------------------------------------------------------------------

        public class DeleteACL : MailboxBase
        {
            public DeleteACL(ZIMapFactory parent) : base(parent, "DELETEACL") {}

            public override bool Queue (string mailboxName)
            {   return Queue (mailboxName, null);
            }

            public bool Queue (string mailboxName, string user)
            {   if(string.IsNullOrEmpty(user)) user = factory.User;
                if(string.IsNullOrEmpty(mailboxName)) return false;
                if(string.IsNullOrEmpty(user))
                {   Error(ZIMapErrorCode.InvalidArgument, "Have no user name");
                    return false;
                }
                mboxname = mailboxName;
                AddMailbox(mailboxName);
                AddString(user);
                return Queue();
            }
       }
        
        // ---------------------------------------------------------------------
        // GetACL
        // ---------------------------------------------------------------------

        public class GetACL : MailboxBase
        {
            public struct Item
            {   public bool    Negative;
                public string  Name;
                public string  Rights;
            }
            
            public GetACL(ZIMapFactory parent) : base(parent, "GETACL") {}
            
            private Item[]  items;

            public Item[]   Items
            {   get {   Parse(); return items; }
            }
            
            protected override bool Parse (bool reset)
            {   if(reset)
                {   items = null;
                    return base.Parse(reset);
                }

                List<Item> list = new List<Item>();
                ZIMapParser parser;
                uint index = 0;
                while((parser = InfoParser("ACL", ref index)) != null)
                {   UpdateMailboxName(parser[0].Text);              // canonicalized name   
                    for(int irun=1; irun+1 < parser.Length; irun += 2)
                    {   Item item = new Item();
                        item.Name = parser[irun].Text;
                        item.Rights = parser[irun+1].Text;
                        if(item.Name.Length > 1 && item.Name[0] == '-')
                        {   item.Negative = true;
                            item.Name = item.Name.Remove(0, 1);
                        }
                        list.Add(item);
                    }
                }
                if(index != 0) return false;                        // error, no data
                items = list.ToArray();
                return true;
            }

        }
        
        // ---------------------------------------------------------------------
        // ListRights
        // ---------------------------------------------------------------------

        public class ListRights : MailboxBase
        {
            public ListRights(ZIMapFactory parent) : base(parent, "LISTRIGHTS") {}

            private string[]  groups;

            public string[]   Groups
            {   get {   Parse(); return groups; }
            }
            
            protected override bool Parse (bool reset)
            {   if(reset)
                {   groups = null;
                    return base.Parse(reset);
                }

                // return false if something went wrong ...
                ZIMapParser parser = InfoParser();
                if(parser == null) return false;
                
                // parse (need: mbox user req) ...
                if(parser.Length >= 3)
                {   uint ngrp = (uint)parser.Length - 2;
                    UpdateMailboxName(parser[0].Text);              // canonicalized name   
                    groups = new string[ngrp];
                    for(int irun=2; irun < parser.Length; irun++)
                        groups[irun-2] = parser[irun].Text;
                }
                return true;
            }

            public override bool Queue (string mailboxName)
            {   return Queue (mailboxName, null);
            }

            public bool Queue (string mailboxName, string user)
            {   if(string.IsNullOrEmpty(user)) user = factory.User;
                if(string.IsNullOrEmpty(mailboxName)) return false;
                if(string.IsNullOrEmpty(user))
                {   Error(ZIMapErrorCode.InvalidArgument, "Have no user name");
                    return false;
                }
                mboxname = mailboxName;
                AddMailbox(mailboxName);
                AddString(user);
                return Queue();
            }
        }
        
        // ---------------------------------------------------------------------
        // MyRights
        // ---------------------------------------------------------------------

        public class MyRights : MailboxBase
        {
            public MyRights(ZIMapFactory parent) : base(parent, "MYRIGHTS") {}
            
            private string rights;
            
            public string Rights
            {   get {   Parse(); return rights; }
            }
            
            protected override bool Parse (bool reset)
            {   if(reset)
                {   rights = null;
                    return base.Parse(reset);
                }

                // return false if something went wrong ...
                ZIMapParser parser = InfoParser();
                if(parser == null) return false;
                
                // parse (need: mbox rights) ...
                if(parser.Length >= 2)
                {   UpdateMailboxName(parser[0].Text);              // canonicalized name   
                    rights = parser[1].Text;
                }
                return true;
            }
       }
        
        // ---------------------------------------------------------------------
        // SetACL
        // ---------------------------------------------------------------------

        public class SetACL : MailboxBase
        {
            public SetACL(ZIMapFactory parent) : base(parent, "SETACL") {}

            public override bool Queue (string mailboxName)
            {   return Queue (mailboxName, null, null);
            }

            public bool Queue (string mailboxName, string user, string rights)
            {   if(string.IsNullOrEmpty(user)) user = factory.User;
                if(string.IsNullOrEmpty(mailboxName)) return false;
                if(string.IsNullOrEmpty(rights)) rights = "rwl";
                if(string.IsNullOrEmpty(user))
                {   Error(ZIMapErrorCode.InvalidArgument, "Have no user name");
                    return false;
                }
                mboxname = mailboxName;
                AddMailbox(mailboxName);
                AddString(user);
                AddString(rights);
                return Queue();
            }
        }
 
        // =====================================================================
        // Quota IMAP Commands
        // =====================================================================
        
        // ---------------------------------------------------------------------
        // GetQuota
        // ---------------------------------------------------------------------

        public class GetQuota : Generic
        {
            public GetQuota(ZIMapFactory parent) : base(parent, "GETQUOTA") {}

            public bool Queue (string root)
            {   if(string.IsNullOrEmpty(root)) root = "";
                if(!AddString(root)) return false;
                return Queue();
            }
        }
        
        // ---------------------------------------------------------------------
        // GetQuotaRoot
        // ---------------------------------------------------------------------

        public class GetQuotaRoot : MailboxBase
        {
            public GetQuotaRoot(ZIMapFactory parent) : base(parent, "GETQUOTAROOT") {}
         
            public struct Item
            {   public string   RootName;
                public string   Resource;
                public uint     Usage, Limit;
            }
            
            private string[]    roots;
            private Item[]      quota;

            public string[] Roots
            {   get {   Parse(); return roots; }
            }
            
            public Item[] Quota
            {   get {   Parse(); return quota; }
            }
            
            protected override bool Parse (bool reset)
            {   if(reset)
                {   roots = null;
                    return base.Parse(reset);
                }

                ZIMapReceiveData data = Data;
                if(data.Infos == null) return false;

                List<Item> list = new List<Item>();
                foreach(ZIMapReceiveInfo info in data.Infos)            
                {   ZIMapParser parser = new ZIMapParser(info.Message);
                    if(parser.Length < 1) continue;         // server bug
                        
                    if(info.Status == "QUOTAROOT")          // not for us
                    {   int icnt = parser.Length;
                        roots = new string[icnt];
                        for(int irun=0; irun < icnt; irun++)
                            roots[irun] = parser[irun].Text;
                    }
                    if(info.Status != "QUOTA") continue;    // not for us...
                    
                    for(int irun=1; irun < parser.Length; irun++)
                    {   ZIMapParser.Token token = parser[irun];
                        if(token.Type != ZIMapParserData.List)
                        {   Monitor(ZIMapMonitor.Error, "Strange data: " + info);
                            break;
                        }
                        ZIMapParser.Token[] triplet = token.List;
                        if(triplet.Length == 0) continue;
                        if(triplet.Length != 3 ||
                           triplet[1].Type != ZIMapParserData.Number ||
                           triplet[2].Type != ZIMapParserData.Number) 
                        {   Monitor(ZIMapMonitor.Error, "Invalid data: " + info);
                            break;
                        }
                        
                        Item item = new Item();
                        item.RootName = parser[0].Text;
                        item.Resource = triplet[0].Text;
                        item.Usage = triplet[1].Number;
                        item.Limit = triplet[2].Number;
                        list.Add(item);
                    }
                }
                if(roots == null) return false;             // got no QUOTAROOT
                quota = list.ToArray();
                return true;
            }            
             
            protected override void ToString (StringBuilder sb)
            {   base.ToString(sb);
                if(Roots == null) return;
                sb.AppendFormat("\n         Quota Root Count={0}", roots.Length);
                foreach(Item item in Quota)
                    sb.AppendFormat("\n         RootName={0} ({1} {2} {3})",
                                 item.RootName, item.Resource, item.Usage, item.Limit);
            }
        }
        
        // ---------------------------------------------------------------------
        // SetQuota
        // ---------------------------------------------------------------------

        public class SetQuota : Generic
        {
            public SetQuota(ZIMapFactory parent) : base(parent, "SETQUOTA") {}
 
            public bool Queue (string root, string limits)
            {   if(string.IsNullOrEmpty(root)) root = "";
                if(!AddString(root)) return false;
                AddList(limits);          
                return Queue();
            }
       }
    }
}