//==============================================================================
// ZIMapServer.cs implements the ZIMapServer class
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
    // Class to encapsulate server dependencies
    //==========================================================================

    /// <summary>
    /// Class to encapsulate server dependencies (Application layer).
    /// </summary>
    /// <remarks>
    /// The <b>application</b> layer it the top-most of three layers. The others
    /// are the command and the protocol layers.
    /// </remarks>
    public abstract class ZIMapServer : ZIMapBase
    {
        public class Namespace
        {
            /// <summary>The Namespace prefix including a trailing delimiter.</summary>
            public  string  Prefix;
            /// <summary>The Namespace prefix excluding a trailing delimiter.</summary>
            public  string  Qualifier;
            /// <summary>Hierarchy delimiter for this namespace.</summary>
            public  char    Delimiter;
            /// <summary><c>true</c> if this namespace is supported.</summary>
            public  bool    Valid;    
            
            public override string ToString()
            {   return string.Format("{0,-18} delimiter='{1}'  status={2}valid",
                                     "'" + Qualifier + "'", Delimiter, !Valid ? "not " : "");
            }
        }

        /// <summary>Refer to the user's personal namespace</summary>
        public const uint Personal = 0;
        /// <summary>Refer to the 'other users' namespace</summary>
        public const uint Others = 1;
        /// <summary>Refer to the 'shared folders' namespace</summary>
        public const uint Shared = 2;
        /// <summary>Refer to the 'search results' pseudo namespace</summary>
        public const uint Search = 3;
        /// <summary>Indicates a non-existing namespace</summary>
        public const uint Nothing = uint.MaxValue;
        
        public readonly string          ServerType;
        public readonly string          ServerSubtype;
        public readonly ZIMapFactory    Factory;

        private string[]        namespaces;
        private Namespace[]     namespdata;
        private bool            namespaceOK;
        
        //======================================================================
        // Construction 
        //======================================================================

        public static ZIMapServer Create(ZIMapFactory factory, bool useNamespaces)        
        {   if(factory == null || factory.Connection.IsTransportClosed)
                return null;
            string greeting = factory.Connection.ProtocolLayer.ServerGreeting;
            if(greeting == null)  return null;
            greeting = greeting.ToLower();
            ZIMapServer serv;
            if(greeting.Contains(" cyrus "))
                serv = new ZIMapServer.Cyrus(factory, "default");
            else
                serv = new ZIMapServer.Default(factory, "default");
            serv.namespaceOK = useNamespaces;
            return serv;
        }    

        private ZIMapServer(ZIMapFactory factory, 
                            string type, string subtype) : base(factory.Connection) 
        {   Factory = factory;
            ServerType = type;
            ServerSubtype = subtype;
            MonitorLevel = factory.MonitorLevel;
            MonitorInfo( "Server is: " + type + "   subtype is:" + subtype);
        }
            
        private class Default : ZIMapServer
        {   public Default(ZIMapFactory factory,
                           string subtype) : base(factory, "default", subtype) {}
        }
            
        // must implement, abstract in base ...
        protected override void MonitorInvoke(ZIMapConnection.Monitor level, string message)
        {   if(MonitorLevel <= level) 
                ZIMapConnection.MonitorInvoke(Parent, "ZIMapServer", level, message); 
        }
            
        //======================================================================
        // Cyrus Server 
        //======================================================================

        private class Cyrus : ZIMapServer
        {   public Cyrus(ZIMapFactory factory,
                         string subtype) : base(factory, "cyrus", subtype) {}

            /// <summary>Cyrus has only "storage" quota.
            /// </summary>
            public override bool HasLimit(string resource)
            {   if(string.Compare(resource, "storage", true) == 0) return true;
                return false;
            }
        }

        //======================================================================
        // Accessors 
        //======================================================================

        public char DefaultDelimiter
        {   get {   return NamespaceData(Personal).Delimiter;   }
        }
        
        public string NamespaceListUser
        {   get {   return NamespaceList(Personal);  }
        }
        
        public string NamespaceListOther
        {   get {   return NamespaceList(Others);  }
        }
        
        public string NamespaceListShared
        {   get {   return NamespaceList(Shared);  }
        }
        
        public Namespace NamespaceDataUser
        {   get {   return NamespaceData(Personal);   }
        }

        public Namespace NamespaceDataOther
        {   get {   return NamespaceData(Others);   }
        }

        public Namespace NamespaceDataShared
        {   get {   return NamespaceData(Shared);   }
        }

        public Namespace NamespaceDataSearch
        {   get {   return NamespaceData(Search);   }
        }

        //======================================================================
        // 
        //======================================================================
        
        /// <summary>
        /// Get a string describing a Namespace. 
        /// </summary>
        /// <param name="item">
        /// <c>0</c> for personal (user), <c>1</c> for other user and
        /// <c>2</c> for shared folders.
        /// </param>
        /// <returns>
        /// The string (containing an IMap response list node) that describes
        /// the selected namespace.
        /// </returns>
        /// <remarks>
        /// On the 1st call this routine executes an IMap NAMSPACE command. The
        /// data returned from this method is parsed by <see cref="NamespaceData"/>.
        /// </remarks>
        public virtual string NamespaceList(uint item)
        {   if(item >= Search)
            {   if(item > Search) return null;
                return string.Format("(\"Search Results.\" \"{0}\")",
                                     NamespaceDataUser.Delimiter);
            }
            if(namespaces != null) return namespaces[item]; 
            ZIMapCommand.Namespace cmd = new ZIMapCommand.Namespace(Factory);
            cmd.Queue();
            namespaces = cmd.Namespaces;
            cmd.Dispose();
            if(namespaces == null) return null;
            return namespaces[item];
        }

        /// <summary>
        /// Partial parsing of the NAMESPACE reply lists
        /// </summary>
        /// <param name="item">
        /// <c>0</c> for personal (user), <c>1</c> for other user and
        /// <c>2</c> for shared folders.
        /// </param>
        /// <returns>
        /// A <see cref="Namespace"/> item that contains parsed data.
        /// The server may hav sent items that get ignored by this
        /// parser (multiple entries for a namespace and annotations).
        /// </returns>
        public virtual Namespace NamespaceData(uint item)
        {   if(item > Search) return null;
            if(namespdata == null) namespdata = new Namespace[Search+1];
            if(namespdata[item] != null) return namespdata[item]; 

            // Preinit: namespace not valid
            namespdata[item] = new Namespace();
            namespdata[item].Prefix = "";
            namespdata[item].Qualifier = "";

            // Get namespace info if namespaces are enabled ...
            if(namespaceOK)
            {   string list = NamespaceList(item);
                if(list != null)
                {   ZIMapParser parser = new ZIMapParser(list);
                    if(parser.Length > 0 && parser[0].Type == ZIMapParser.TokenType.List)
                    {   ZIMapParser.Token[] toks = parser[0].List;
                        if(toks.Length > 0)
                            namespdata[item].Prefix = toks[0].Text;
                        if(toks.Length > 1) 
                        {   string del = toks[1].Text;
                            if(!string.IsNullOrEmpty(del))
                            {   namespdata[item].Delimiter = del[0];
                                namespdata[item].Valid = true;
                            }
                        }
                    }
                }
            }
            
            // Postinit: fix-up
            if(!namespdata[item].Valid)
                namespdata[item].Delimiter = Factory.HierarchyDelimiter;
            string pref = namespdata[item].Prefix;
            int plen = pref.Length;
            if(plen > 0 && pref[plen-1] == namespdata[item].Delimiter)
                namespdata[item].Qualifier = pref.Substring(0, plen - 1);
            else
                namespdata[item].Qualifier = pref;

            return namespdata[item];
        }
        
        /// <summary>
        /// Make sure that filter and qualifier can be passed to IMap LIST
        /// </summary>
        /// <param name="qualifier">
        /// Qualifier, can be <c>null</c>. The default is "".
        /// </param>
        /// <param name="filter">
        /// Filter, can be <c>null</c>. The default is "*".
        /// </param>
        public virtual void NormalizeQualifierFilter(ref string qualifier, ref string filter)
        {   if(string.IsNullOrEmpty(qualifier)) 
                qualifier = "";
            else if(qualifier[qualifier.Length-1] == DefaultDelimiter)
                qualifier = qualifier.Substring(0, qualifier.Length-1);
            if(string.IsNullOrEmpty(filter))
                filter = "*";
            if(qualifier != "" && filter != "*" && filter[0] != DefaultDelimiter)
                filter = DefaultDelimiter + filter;
        }
        
        /// <summary>
        /// Make sure that a MailBox array contains only entries from one Namespace. 
        /// </summary>
        /// <param name="mailboxes">
        /// An array of MailBox structures.
        /// </param>
        /// <param name="nsIndex">
        /// Can be <c>Personal</c> (0), <c>Other</c> (1) or <c>Shared</c> (2) to select
        /// a Namespace.
        /// </param>
        /// <returns>
        /// A value of <c>true</c> indicates that the array was modified.
        /// </returns>
        public virtual bool MailboxesFilter(ref ZIMapApplication.MailBox[] mailboxes, uint nsIndex)
        {   if(mailboxes == null || mailboxes.Length < 1) return false;
            if(nsIndex > Shared) return false;

            // ok, remove them ...
            int ilas = 0;
            for(int irun=0; irun < mailboxes.Length; irun++)
            {   if(FindNamespaceIndex(mailboxes[irun].Name, true) != nsIndex)
                    continue;
                if(ilas != irun) mailboxes[ilas] = mailboxes[irun];
                ilas++;
            }
            if(ilas == mailboxes.Length) return false;
            Array.Resize(ref mailboxes, ilas);
            return true;
        }
        
        /// <summary>
        /// Sort mailboxes using namespace information.
        /// </summary>
        /// <param name="mailboxes">
        /// An array of MailBox structures.
        /// </param>
        /// <returns>
        /// A value of <c>true</c> indicates that the array was sorted.
        /// </returns>
        public virtual bool MailboxSort(ZIMapApplication.MailBox[] mailboxes)
        {   if(mailboxes == null) return false;
            if(mailboxes.Length <= 1) return true;
            
            string[] keys = new string[mailboxes.Length];
            
            for(int irun=0; irun < mailboxes.Length; irun++)
            {   string name = mailboxes[irun].Name;
                if(name == null)
                    name = "9";
                else if(name.StartsWith("INBOX"))
                    name = "0" + name;
                else
                {   uint nsi = FindNamespaceIndex(name, true);
                    if(nsi == Nothing) 
                        name = "8" + name;
                    else
                        name = (nsi+1).ToString() + name;
                }
                keys[irun] = name;
            }
            Array.Sort(keys, mailboxes);
            return true;
        }
        
        /// <summary>
        /// Check support for a quota resource.
        /// </summary>
        /// <param name="resource">
        /// Could be "storage" or "message".
        /// </param>
        /// <returns>
        /// A value of <c>>true</c> indicates that the resource is supported.
        /// </returns>
        /// <remarks>
        /// The cyrus 2.x server does only support "storage". 
        /// </remarks>
        public virtual bool HasLimit(string resource)
        {   if(string.Compare(resource, "storage", true) == 0) return true;
            if(string.Compare(resource, "message", true) == 0) return true;
            return false;
        }
        
        public uint FindNamespaceIndex(string qualifier, bool substring)
        {   if(qualifier == null) return Nothing;

            if(qualifier == "INBOX") return Personal;
            if(substring)
            {   if(NamespaceDataUser.Prefix != "" &&
                   qualifier.StartsWith(NamespaceDataUser.Prefix))   return Personal;            
                if(NamespaceDataOther.Prefix != "" &&
                   qualifier.StartsWith(NamespaceDataOther.Prefix))  return Others;            
                if(NamespaceDataShared.Prefix != "" &&
                   qualifier.StartsWith(NamespaceDataShared.Prefix)) return Shared;
                if(NamespaceDataOther.Valid &&
                   NamespaceDataOther.Prefix == "")  return Others;            
                if(NamespaceDataShared.Valid &&
                   NamespaceDataShared.Prefix == "") return Shared;
                return Personal;
            }
            else
            {   if(qualifier == NamespaceDataUser.Qualifier)   return Personal;            
                if(qualifier == NamespaceDataOther.Qualifier)  return Others;            
                if(qualifier == NamespaceDataShared.Qualifier) return Shared;
            }
            return Nothing;
        }
        
        /// <summary>
        /// Get rid of INBOX and prefix personal mailboxes with the account name. 
        /// </summary>
        /// <returns>
        /// A friendly mailbox name for display purposes.  The IMap server will not
        /// allways understand this name.
        /// <para />
        /// The returned namespace index is always valid and can be passed safely
        /// to <see cref="NamespaceData"/> for exmple to get the namspace prefix.
        /// </returns>
        public string FriendlyName(string mailbox, out uint nsIndex)
        {   // the returned index is always valid
            nsIndex = FindNamespaceIndex(mailbox, true);
            
            string pref = NamespaceData(nsIndex).Qualifier;
            if(pref == "") 
            {   pref = Factory.User;
                if(string.IsNullOrEmpty(pref)) return mailbox;
            }

            if(mailbox == "INBOX") return pref;
            return pref + NamespaceData(nsIndex).Delimiter + mailbox;
        }
    }
}
