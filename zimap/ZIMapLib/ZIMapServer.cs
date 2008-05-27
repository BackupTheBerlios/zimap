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
    /// The <b>application<b> layer it the top-most of three layers. The others
    /// are the command and the protocol layers.
    /// </remarks>
    public class ZIMapServer
    {
        public class Namespace
        {
            public  string  Prefix;
            public  char    Delimiter;
            
            public override string ToString()
            {   return string.Format("Prefix: '{0}'   Delimiter: '{1}'", Prefix, Delimiter);
            }
        }

        /// <summary>Refer to the user's personal namespace</summary>
        public const uint Personal = 0;
        /// <summary>Refer to the 'other users' namespace</summary>
        public const uint Others = 1;
        /// <summary>Refer to the 'shared folders' namespace</summary>
        public const uint Shared = 2;
        
        private ZIMapFactory    factory;
        private string[]        namespaces;
        private Namespace[]     namespdata;
        
        public static ZIMapServer Create(ZIMapFactory factory)        
        {   return new ZIMapServer(factory);
        }
        
        public ZIMapServer(ZIMapFactory factory)
        {
            this.factory = factory;
        }

        //======================================================================
        // Accessors 
        //======================================================================
        
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
        {   if(item > Shared) return null;
            if(namespaces != null) return namespaces[item]; 
            ZIMapCommand.Namespace cmd = new ZIMapCommand.Namespace(factory);
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
        {   if(item > Shared) return null;
            if(namespdata == null) namespdata = new Namespace[Shared+1];
            if(namespdata[item] != null) return namespdata[item]; 

            namespdata[item] = new Namespace();
            string list = NamespaceList(item);
            if(list != null)
            {   ZIMapParser parser = new ZIMapParser(list);
                if(parser.Length > 0 && parser[0].Type == ZIMapParserData.List)
                {   ZIMapParser.Token[] toks = parser[0].List;
                    if(toks.Length > 0)
                        namespdata[item].Prefix = toks[0].Text;
                    if(toks.Length > 1) 
                    {   string del = toks[1].Text;
                        if(!string.IsNullOrEmpty(del))
                            namespdata[item].Delimiter = del[0];
                    }
                }
            }
            return namespdata[item];
        }
        
        /// <summary>
        /// Get the mailbox name of the current user 
        /// </summary>
        /// <returns>
        /// A fully qualified mailbox name
        /// </returns>
        public virtual string MyMailboxName()
        {   string name = NamespaceDataUser.Prefix + "INBOX";
            if(name == "INBOX.INBOX") name = "INBOX";
            return name;
        }

        /// <summary>
        /// Make sure that filter and qualifier can be passed to IMap LIST
        /// </summary>
        /// <param name="qualifier">
        /// Qualifier, can be <c>null</c>. The default is "".
        /// </param>
        /// Filter, can be <c>null</c>. The default is "*".
        /// </param>
        public virtual void NormalizeQualifierFilter(ref string qualifier, ref string filter)
        {   if(string.IsNullOrEmpty(qualifier)) 
                qualifier = "";
            else if(qualifier[qualifier.Length-1] == factory.HierarchyDelimiter)
                qualifier = qualifier.Substring(0, qualifier.Length-1);
            if(string.IsNullOrEmpty(filter))
                filter = "*";
            if(qualifier != "" && filter[0] != factory.HierarchyDelimiter && filter[0] != '*')
                filter = factory.HierarchyDelimiter + filter;
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

            // TODO: complete FilterMailboxes 
            if(nsIndex != Personal && nsIndex != Shared) return false;
           
            // can we remove shared and other user folders?
            string shared = (nsIndex == Personal) ? NamespaceDataShared.Prefix
                                                  : NamespaceDataUser.Prefix;
            string other  = NamespaceDataOther.Prefix;
            if(shared == null) shared = "";
            if(other  == null) other  = "";
            if(shared == "" && other == "") return false;
            
            if(nsIndex == Shared)
            {   if(shared.EndsWith(".")) shared = shared.Substring(0, shared.Length - 1);
                if( other.EndsWith(".")) other  =  other.Substring(0,  other.Length - 1);
            }
            
            // ok, remove them ...
            int ilas = 0;
            for(int irun=0; irun < mailboxes.Length; irun++)
            {   if(nsIndex == Shared)
                {   if(shared == mailboxes[irun].Name) continue;
                    if(other  == mailboxes[irun].Name) continue;
                }
                else
                {   if(shared != "" && mailboxes[irun].Name.StartsWith(shared)) continue;
                    if(other  != "" && mailboxes[irun].Name.StartsWith(other) ) continue;
                }
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
            
            string  user   = NamespaceDataUser.Prefix;   if(user   == "") user   = null;
            string  other  = NamespaceDataOther.Prefix;  if(other  == "") other  = null;
            string  shared = NamespaceDataShared.Prefix; if(shared == "") shared = null;
            
            string[] keys = new string[mailboxes.Length];
            
            for(int irun=0; irun < mailboxes.Length; irun++)
            {   string name = mailboxes[irun].Name;
                if(name == null) name = "5";
                if     (shared != null && name.StartsWith(shared))
                   name = "4" + name;
                else if(other != null && name.StartsWith(other))
                   name = "3" + name;
                else if(user != null && name.StartsWith(user))
                   name = "2" + name;
                else if(name == "INBOX")
                   name = "0" + name;
                else
                   name = "1" + name;
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
        {   if(resource == "storage") return true;
            return false;
        }
        
        public uint FindNamespaceIndex(string qualifier, bool substring)
        {   if(string.IsNullOrEmpty(qualifier)) return uint.MaxValue;
            if(!qualifier.EndsWith(".")) qualifier += ".";
            if(qualifier == NamespaceDataUser.Prefix)   return Personal;            
            if(qualifier == NamespaceDataOther.Prefix)  return Others;            
            if(qualifier == NamespaceDataShared.Prefix) return Shared;            
            return uint.MaxValue;
        }
    }
}
