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
    /// Class to encapsulate server dependencies and Namepaces (Application layer).
    /// </summary>
    /// <remarks>
    /// When a class instance is constructed the IMap server greeting is parsed to
    /// determine the server type.  Depending on this the class implements slightly
    /// different behaviour.
    /// <para />
    /// This class belongs to the <c>Application</c> layer which is the top-most of four
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
    public abstract class ZIMapServer : ZIMapBase
    {
        /// <summary>
        /// Encapsulates IMap Namespace properties.
        /// </summary>
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
            /// <summary>The index of this namespace.</summary>
            public  uint    Index = ZIMapServer.Nothing;

            public override string ToString()
            {   return string.Format("{0,-18} delimiter='{1}'  status={2}valid",
                                     "'" + Qualifier + "'", Delimiter, !Valid ? "not " : "");
            }
        }

        private const string            ENVIRONMENT_ADMIN = "ZIMAP_ADMIN";

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

        /// <summary>Flavour of the IMap server (example: "cyrus").</summary>
        public readonly string          ServerType;
        /// <summary>Variant of the server flavour (example: "default").</summary>
        public readonly string          ServerSubtype;

        private ZIMapFactory    factory;

        private string[]        namespaces;
        private Namespace[]     namespdata;
        private bool            namespaceOK;

        // for IsAdmin
        private bool            adminChecked;
        private bool            adminMode;

        //======================================================================
        // Construction
        //======================================================================

        /// <summary>Creates an instance of this class.</summary>
        /// <param name="factory">
        /// The instance owner.
        /// </param>
        /// <param name="useNamespaces">
        /// Of <c>true</c> the use of namespaces is enbabled, <c>false</c> disables
        /// the use of namespaces.
        /// </param>
        /// <returns>
        /// A reference to the created instance.
        /// </returns>
        public static ZIMapServer CreateInstance(ZIMapFactory factory, bool useNamespaces)
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

        // The xtor is private, use CreateInstance()
        private ZIMapServer(ZIMapFactory factory,
                            string type, string subtype) : base(factory.Connection)
        {   this.factory = factory;
            ServerType = type;
            ServerSubtype = subtype;
            MonitorLevel = factory.MonitorLevel;
            MonitorInfo( "Server is: " + type + "   subtype is:" + subtype);
        }

        // default flavour
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

            /// <summary>Cyrus has only "storage" quota.</summary>
            public override bool HasLimit(string resource)
            {   if(string.Compare(resource, "storage", true) == 0) return true;
                return false;
            }

            /// <summary>Cyrus uses "INBOX" is personal NS name for admins.</summary>
            public override bool IsAdmin
            {   get {   if(adminChecked) return adminMode;
                        adminMode = (NamespaceDataPersonal.Qualifier == "INBOX");
                        return base.IsAdmin;
                    }
            }
        }

        //======================================================================
        // Accessors
        //======================================================================

        /// <summary>
        /// Get the Namespace instance for an index, never returning <c>null</c>.
        /// </summary>
        /// <param name="nsIndex">
        /// This is either a valid index for <see cref="NamespaceData"/> or
        /// the <see cref="Personal"/> Namespace is returned.
        /// </param>
        /// <returns>
        /// Should allways return a valid reference to a Namespace.
        /// </returns>
        public Namespace this[uint nsIndex]
        {   get {   if(!namespaceOK || nsIndex > Search) nsIndex = Personal;
                    if(namespdata != null && namespdata[nsIndex] != null)
                        return namespdata[nsIndex];
                    return NamespaceData(nsIndex);
                }
        }

        /// <summary>
        /// Returns the hierarchy delimiter from the 'personal' Namespace.
        /// </summary>
        public char DefaultDelimiter
        {   get {   return NamespaceData(Personal).Delimiter;   }
        }

        /// <summary>
        /// Get the Namespace instance for the 'personal' namespace<c>null</c>.
        /// </summary>
        public Namespace NamespaceDataPersonal
        {   get {   return NamespaceData(Personal);   }
        }

        /// <summary>
        /// Get the Namespace instance for the 'other users' namespace<c>null</c>.
        /// </summary>
        public Namespace NamespaceDataOther
        {   get {   return NamespaceData(Others);   }
        }

        /// <summary>
        /// Get the Namespace instance for the 'shared folders' namespace<c>null</c>.
        /// </summary>
        public Namespace NamespaceDataShared
        {   get {   return NamespaceData(Shared);   }
        }

        /// <summary>
        /// Get the Namespace instance for the 'search results' pseudo namespace<c>null</c>.
        /// </summary>
        public Namespace NamespaceDataSearch
        {   get {   return NamespaceData(Search);   }
        }

        /// <summary>Checks if a user is administrator.</summary>
        public virtual bool IsAdmin
        {   get {   if(!adminChecked)
                    {   adminChecked = true;
                        string val = System.Environment.GetEnvironmentVariable(ENVIRONMENT_ADMIN);
                        if     (val == "on")  adminMode = true;
                        else if(val == "off") adminMode = false;
                    }
                    return adminMode;
                }
        }

        //======================================================================
        //
        //======================================================================

        /// <summary>
        /// Get a string describing a Namespace.
        /// </summary>
        /// <param name="nsIndex">
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
        public virtual string NamespaceList(uint nsIndex)
        {   if(nsIndex >= Search)
            {   if(nsIndex > Search) return null;
                return string.Format("(\"Search Results.\" \"{0}\")",
                                     NamespaceDataPersonal.Delimiter);
            }
            if(namespaces != null) return namespaces[nsIndex];
            if(!namespaceOK) return null;
            using(ZIMapCommand.Namespace cmd = new ZIMapCommand.Namespace(factory))
            {   cmd.Queue();
                if(cmd.CheckSuccess("Namespaces will be disabled"))
                    namespaces = cmd.Namespaces;
            }
            if(namespaces != null) return namespaces[nsIndex];
            namespaceOK = false;
            return null;
        }

        /// <summary>
        /// Return information from the parsed reply of the NAMESPACE command.
        /// </summary>
        /// <param name="nsIndex">
        /// Selects the returned <see cref="Namespace"/> information<para/>
        /// <list type="table"><listheader>
        ///    <term>nsIndex Value</term>
        ///    <description>Selected Namespace</description>
        /// </listheader><item>
        ///    <term>Personal (0)</term>
        ///    <description>The current user's namepace (INBOX)</description>
        /// </item><item>
        ///    <term>Others (1)</term>
        ///    <description>Other users</description>
        /// </item><item>
        ///    <term>Shared (2)</term>
        ///    <description>Shared folders</description>
        /// </item><item>
        ///    <term>Search (3)</term>
        ///    <description>Pseudo-Namespace for search results</description>
        /// </item><item>
        ///    <term>Nothing (uint.MaxValue)</term>
        ///    <description>Invalid, returns <c>null</c></description>
        /// </item></list>
        /// </param>
        /// <returns>
        /// A <see cref="Namespace"/> item that contains parsed data.
        /// The server may have sent items that get ignored by this
        /// parser (multiple entries for a namespace and annotations).
        /// <para/>
        /// When the <paramref name="nsIndex"/> argument is invalid, the method
        /// returns <c>null</c>.
        /// </returns>
        public virtual Namespace NamespaceData(uint nsIndex)
        {   if(nsIndex > Search) return null;
            if(namespdata == null) namespdata = new Namespace[Search+1];
            if(namespdata[nsIndex] != null) return namespdata[nsIndex];

            // Preinit: namespace not valid
            namespdata[nsIndex] = new Namespace();
            namespdata[nsIndex].Prefix = "";
            namespdata[nsIndex].Qualifier = "";
            namespdata[nsIndex].Index = nsIndex;

            // Get namespace info if namespaces are enabled ...
            if(namespaceOK)
            {   string list = NamespaceList(nsIndex);
                if(list != null)
                {   ZIMapParser parser = new ZIMapParser(list);
                    if(parser.Length > 0 && parser[0].Type == ZIMapParser.TokenType.List)
                    {   ZIMapParser.Token[] toks = parser[0].List;
                        if(toks.Length > 0)
                            namespdata[nsIndex].Prefix = toks[0].Text;
                        if(toks.Length > 1)
                        {   string del = toks[1].Text;
                            if(!string.IsNullOrEmpty(del))
                            {   namespdata[nsIndex].Delimiter = del[0];
                                namespdata[nsIndex].Valid = true;
                            }
                        }
                    }
                }
            }

            // Postinit: fix-up
            if(!namespdata[nsIndex].Valid)
                namespdata[nsIndex].Delimiter = factory.HierarchyDelimiter;
            string pref = namespdata[nsIndex].Prefix;
            int plen = pref.Length;
            if(plen > 0 && pref[plen-1] == namespdata[nsIndex].Delimiter)
                namespdata[nsIndex].Qualifier = pref.Substring(0, plen - 1);
            else
                namespdata[nsIndex].Qualifier = pref;

            return namespdata[nsIndex];
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
        /// Used to select the Namespace to which the result should belong.<para/>
        /// <list type="table"><listheader>
        ///    <term>nsIndex Value</term>
        ///    <description>Selected Namespace</description>
        /// </listheader><item>
        ///    <term>Personal (0)</term>
        ///    <description>The current user's namepace (INBOX)</description>
        /// </item><item>
        ///    <term>Others (1)</term>
        ///    <description>Other users</description>
        /// </item><item>
        ///    <term>Shared (2)</term>
        ///    <description>Shared folders</description>
        /// </item><item>
        ///    <term>Search (3)</term>
        ///    <description>Pseudo-Namespace for search results</description>
        /// </item><item>
        ///    <term>Nothing (uint.MaxValue)</term>
        ///    <description>Invalid, returns <c>false</c></description>
        /// </item></list>
        /// </param>
        /// <returns>
        /// A value of <c>true</c> indicates that the array was modified.
        /// </returns>
        public virtual bool MailboxesFilter(ref ZIMapApplication.MailBox[] mailboxes, uint nsIndex)
        {   if(mailboxes == null || mailboxes.Length < 1) return false;
            if(!namespaceOK || nsIndex > Shared) return false;

            // ok, remove them ...
            int ilas = 0;
            for(int irun=0; irun < mailboxes.Length; irun++)
            {   if(FindNamespace(mailboxes[irun].Name) != nsIndex)
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
                {   uint nsi = FindNamespace(name);
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

        /// <summary>
        /// Determine the namespace index from a qualifier or a qualified name.
        /// </summary>
        /// <param name="qualifier">
        /// Depending on <paramref name="qualifedName"/> this must either be a
        /// qualifier (for a value of <c>false</c>) or a qualified name (for a
        /// value of <c>true</c>).
        /// </param>
        /// <param name="qualifiedName">
        /// </param>
        /// Makes this method call <see cref="FindNamespace(string)"/>.  See also
        /// <paramref name="qualifiedName"/>.
        /// <returns>
        /// A namespace index whith <see cref="Nothing"/> as default if no
        /// namespace was found.  Important: when checking for a qualifier the
        /// input string must excactly match the Namespace qualifier.  Only if
        /// <paramref name="qualifiedName"/> is <c>true</c> a substring match
        /// will be made.
        /// <para/>
        /// When namespaces are disabled the returned values is always <see cref="Personal"/>.
        /// </returns>
        public uint FindNamespace(string qualifier, bool qualifiedName)
        {   // INBOX is always a special case
            if(!namespaceOK || qualifier == "INBOX") return Personal;
            if(qualifiedName) return FindNamespace(qualifier);

            // match qualifier only
            if(qualifier == NamespaceDataPersonal.Qualifier)   return Personal;
            if(qualifier == NamespaceDataOther.Qualifier)  return Others;
            if(qualifier == NamespaceDataShared.Qualifier) return Shared;
            return Nothing;
        }

        /// <summary>
        /// Determine the namespace index from a qualified name.
        /// </summary>
        /// <param name="name">
        /// A mail folder name or the special name INBOX.
        /// </param>
        /// <returns>
        /// A namespace index whith <see cref="Personal"/> as default if no other
        /// namespace was found.  The routine never returns <see cref="Nothing"/>.
        /// <para/>
        /// When namespaces are disabled the returned values is always <see cref="Personal"/>.
        /// </returns>
        public uint FindNamespace(string name)
        {   // INBOX is always a special case
            if(!namespaceOK || string.IsNullOrEmpty(name) || name == "INBOX")
                return Personal;

            // try to get the ns from qualified name
            if(NamespaceDataPersonal.Prefix != "" &&
               (name.StartsWith(NamespaceDataPersonal.Prefix) ||
                name == NamespaceDataPersonal.Qualifier))  return Personal;
            if(NamespaceDataOther.Prefix != "" &&
               (name.StartsWith(NamespaceDataOther.Prefix) ||
                name == NamespaceDataOther.Qualifier)) return Others;
            if(NamespaceDataShared.Prefix != "" &&
               (name.StartsWith(NamespaceDataShared.Prefix) ||
                name == NamespaceDataShared.Qualifier))   return Shared;
            if(NamespaceDataOther.Valid &&
               NamespaceDataOther.Prefix == "")  return Others;
            if(NamespaceDataShared.Valid &&
               NamespaceDataShared.Prefix == "") return Shared;
            return Personal;
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
            nsIndex = FindNamespace(mailbox);
            if(nsIndex != Personal) return mailbox;         // not personal, no INBOX

            string qual = NamespaceData(nsIndex).Qualifier;
            string pref = factory.User;
            if(string.IsNullOrEmpty(pref)) return mailbox;  // don't know my account
            if(mailbox == "INBOX") return pref;             // this is RCF3501 clean

            if(mailbox.StartsWith("INBOX"))                 // cyrus only?
                return pref + mailbox.Substring(5);         // INBOX -> account
            if(qual == "")                                  // always pefix ...
                return pref + NamespaceData(nsIndex).Delimiter + mailbox;
            return mailbox;                                 // has onw qualifier
        }

        public string FormalName(string mailbox)
        {   string pref = factory.User;
            if(string.IsNullOrEmpty(pref)) return mailbox;  // don't know my account
            if(/*mailbox == "~" ||*/ mailbox == pref)
                return "INBOX";
            pref += DefaultDelimiter;
            if(mailbox.StartsWith(pref))
                return NamespaceDataPersonal.Qualifier + mailbox.Substring(pref.Length);
            return mailbox;
        }

        public string DelimiterTrim(string name, uint nsIndex)
        {   if(string.IsNullOrEmpty(name)) return name;
            if(nsIndex > Search) nsIndex = Personal;
            if(namespdata[nsIndex] == null) NamespaceData(nsIndex);
            char deli = namespdata[nsIndex].Delimiter;

            if(name[0] == deli) name = name.Substring(1);
            int len = name.Length;
            if(len > 0 && name[len-1] == deli) name = name.Substring(0, len-1);
            return name;
        }

        public int DelimiterIndex(string name, int startIndex, uint nsIndex)
        {   if(string.IsNullOrEmpty(name)) return -1;
            if(nsIndex > Search) nsIndex = Personal;
            if(namespdata[nsIndex] == null) NamespaceData(nsIndex);
            char deli = namespdata[nsIndex].Delimiter;
            return name.IndexOf(deli, startIndex);
        }

        /// <summary>
        /// Get a selection of rights (in IMap ACL format) from a descriptive name.
        /// </summary>
        /// <param name="name">
        /// Is a descriptive name (not a set of rights)
        /// </param>
        /// <returns>
        /// A set of rights that can be used for IMap's <c>SetACL</c> command.
        /// </returns>
        /// <remarks>
        /// The accepted names for rights are listed below. All other values cause and exception
        /// to be raised:
        /// <para />
        /// <list type="table"><listheader>
        ///   <term>Name</term>
        ///   <description>Result (for cyrus) and description</description>
        /// </listheader><item>
        ///   <term>none</term>
        ///   <description>"" no rights, the server will add some required minimum rights automatically</description>
        /// </item><item>
        ///   <term>read</term>
        ///   <description>"lrs" read access</description>
        /// </item><item>
        ///   <term>append</term>
        ///   <description>"lrsip" allow append and copy</description>
        /// </item><item>
        ///   <term>write</term>
        ///   <description>"lrswipcd" allows full write access</description>
        /// </item><item>
        ///   <term>all</term>
        ///   <description>"lrswipcda" add administrative right to 'write'</description>
        /// </item></list>
        /// </remarks>
        public virtual string RightsByName(string name)
        {   switch(name)
            {   case "none":    return "";
                case "read":    return "lrs";
                case "append":  return "lrsip";
                case "write":   return "lrswipcd";
                case "all":     return "lrswipcda";
            }
            RaiseError(ZIMapException.Error.InvalidArgument, name);
            return null;
        }

        /// <summary>
        /// Uses a descriptive name for a set of rights and return an IMap ACL string
        /// that can be used to obtain additional rights.
        /// </summary>
        /// <param name="request">
        /// A descriptive name like <c>append</c> for the requested rights, see
        /// <see cref="RightsByName"/>.
        /// </param>
        /// <param name="currentRights">
        /// The current rights in IMap ACL format (can be <c>null</c> or empty).
        /// </param>
        /// <returns>
        /// If the requested rights are not contained in the current rights the method
        /// returns a non-empty string that can be used for <c>SetACL</c>.  When sufficient
        /// rights are availlable <c>null</c> is returned.  The result is always a union
        /// of requested and current rights and can therefore not be empty.
        /// </returns>
        /// <remarks>
        /// This method can be used by a caller who wants to upgrade the current rights
        /// to a requested level.  The result is either <c>null</c> if the current rigths
        /// are sufficient or a string that can be used with <c>SetACL</c> to obtain the
        /// requested rights.  The method calls <see cref="RightsByName"/> and may throw
        /// an exception for invalid values of <paramref name="request"/>. 
        /// </remarks>
        public string RightsCheck(string request, string currentRights)
        {   string want = RightsByName(request);
            if(want == null || string.IsNullOrEmpty(currentRights)) return want;
            StringBuilder sb = new StringBuilder(currentRights);
            foreach(char cwant in want)
                if(currentRights.IndexOf(cwant) < 0) sb.Append(cwant);
            want = sb.ToString();
            return (want == currentRights) ? null : want;
        }
    }
}
