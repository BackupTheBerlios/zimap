//==============================================================================
// Items.cs     The ZLibAdmin data cache
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU General Public License
// This software is published under the GNU GPL license.  Please refer to the
// file COPYING for details. Please use the following e-mail address to contact
// me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.Collections.Generic;
using ZIMap;
using ZTool;

// TODO: MBoxRef Children() to return a subtree

namespace ZIMapTools
{
    /// <remarks>
    /// The CacheData class defines some nested classes and structures to implement
    /// data caching: DataRef (top level), MBoxRef (sets of mailboxes) and MailRef
	/// (sets of mail headers).
    /// </remarks>
    public partial class CacheData : ZIMapBase
    {
        /// <summary>Enumerates cacheable entities.</summary>
        public enum Info
        {   All=255,
            /// <summary>Folders with current Prefix</summary>
            Folders=1,
            /// <summary>Flag: Folders have detail info</summary>
            Details=2, 
            /// <summary>Flag: Folders have quota info</summary>
            Quota=4,
            /// <summary>Flag: Folders have rights info</summary>
            Rights=8,
            /// <summary>Users from "Shared folder" Namespace</summary>
            Shared=32,
            /// <summary>Users from "Other users" Namespace</summary>
            Others=64,
            /// <summary>Mail headers of current Folder</summary>
            Headers=128,
        };

        // =============================================================================
        // DataRef     
        // =============================================================================

		/// <summary>
		/// Class to implement the top level of data caching.
		/// </summary>
        public abstract class DataRef
        {
            private MBoxRef     current = MBoxRef.Nothing;

            private MBoxRef     boxes = MBoxRef.Nothing;
            private MBoxRef     users = MBoxRef.Nothing;
            private uint        boxesTime, usersTime;

//            private ZIMapApplication.MailInfo[] headers;
            private MailRef     headers;
            private uint        headersTime;

            // list prefix (does not end with delimiter)
            private string      qualifier;
            // list filter (second argument for IMap LIST)
            private string      filter = "";
            // flags for items that are cached            
            private Info        info;

            /// <summary>Enables or disables caching, <see cref="Expire"/></summary>
            public bool         Caching = true;
            /// <summary>Expiration time for cache items in [s], <see cref="Expire"/></summary>
            public uint         Lifetime = 0;

            // =================================================================
            // Methods
            // =================================================================

            protected abstract bool LoadData(Info what);

            private void        SetInfo(Info what, bool val)
            {   if(val) info |= what;
                else    info &= ~what;
            }

            protected void UpdateCurrent(MBoxRef current)
            {   this.current = current;
            }

            protected void UpdateFolders(MBoxRef boxes, bool details)
            {   this.boxes = boxes;
                SetInfo(Info.Details, details);
                info |= Info.Folders;
                boxesTime = Second();
            }

            protected void UpdateExtras(bool rights, bool quota)
            {   SetInfo(Info.Rights, rights);
                SetInfo(Info.Quota, quota);
            }
            
            protected void UpdateUsers(MBoxRef users, bool others)
            {   this.users = users;
                SetInfo(Info.Others, others);
                SetInfo(Info.Shared, !others);
                usersTime = Second();
            }
/*
            protected void UpdateHeaders(ZIMapApplication.MailInfo[] headers)
            {   this.headers = new MailRef(headers, "-");
*/            
            protected void UpdateHeaders(MailRef headers)
            {   this.headers = headers;
                info |= Info.Headers;
                headersTime = Second();
            }
            
            // =================================================================
            // Accessors
            // =================================================================

            public MBoxRef Folders
            {   get {   return boxes; }
            }
            
            public MBoxRef Current
            {   get {   return current; }
            }

            public MBoxRef Users
            {   get {   return users; }
            }

            public MailRef Headers
            {   get {   return headers; }
            }

            public string Filter
            {   get {   return filter; }
                set {   if(value == "") value = null;   
                        if(filter == value) return;
                        filter = value;
                        Clear(Info.Folders);
                    }
            }
            
            public virtual string Qualifier
            {   get {   return qualifier; }
                set {   if(qualifier == value) return;
                        qualifier = value; filter = "";
                        Clear(Info.Folders);
                    }
            }
                
            public bool Load(Info what)
            {   what &= ~info;
                if(what == 0) return true;
                return LoadData(what);
            }

            /// <summary>
            /// Clear cached data.
            /// </summary>
            /// <remarks>
            /// The method does not clear or close the current mailbox.
            /// </remarks>
            public bool Clear(Info what)
            {   //if((what & (Info.Quota | Info.Rights)) != 0)
                //    what |= (Info.Details | Info.Quota | Info.Rights);
                
                if((what & Info.Details) != 0) info &= ~Info.Details;
                if((what & Info.Quota)   != 0) info &= ~Info.Quota;
                if((what & Info.Rights)  != 0) info &= ~Info.Rights;
                
                if((what & Info.Folders) != 0)
                {   boxes = MBoxRef.Nothing; boxesTime = 0;
                    info &= ~(Info.Folders | Info.Details | Info.Quota | Info.Rights);
                }
                if((what & (Info.Others | Info.Shared)) != 0)
                {   users = MBoxRef.Nothing; usersTime = 0;
                    info &= ~(Info.Others | Info.Shared);
                }
                if((what & Info.Headers) != 0)
                {   headers = MailRef.Nothing; headersTime = 0;
                    info &= ~(Info.Headers);
                }
                return false;
            }

            // Helper to get the seconds since 2000-01-01
            private uint Second()
            {   DateTime dt = DateTime.Now;
                DateTime db = new DateTime(2000, 1, 1);
                return (uint)((dt - db).TotalSeconds);
            }
            
            /// <summary>
            /// Handles Cache expiration via <see cref="Enabled"/> and <see cref="Lifetime"/>
            /// </summary>
            public bool Expire()
            {   if(!Caching) return Clear(Info.All);
                if(Lifetime == 0) return false;
                uint usec = Second() - Lifetime;
                if(headersTime > 0 && usec > headersTime)   // headers expired...
                    Clear(Info.Headers);
                if(boxesTime > 0 && usec > boxesTime)       // folders expired...
                    Clear(Info.Folders);
                if(usersTime > 0 && usec > usersTime)       // users expired...
                    Clear(Info.Shared | Info.Others);
                return true;
            }
            
            /// <summary>Append a new entry to the array of cached folders.</summary>
            public bool FolderAppend(string boxname, char delimiter)
            {   if(boxes.IsNothing) return false;
                return boxes.Append(boxname, delimiter);
            }   
            
            /// <summary>Remove one entry from the array of cached folders.</summary>
            public bool FolderDelete(MBoxRef mbox)
            {   if(boxes.IsNothing) return false;
                return boxes.Delete(mbox);
            }   
        }
            
        // =============================================================================
        // MBoxRef     
        // =============================================================================

		/// <summary>
		/// A structure to simplify the handling of sets of mailboxes.
		/// </summary>
        public struct MBoxRef
        {   // index in Boxes array
            private uint    index;
            // length of the mailbox array (allow array to be null without expensive tests) 
            private uint    length;
            // The array of mailboxes
            private ZIMapApplication.MailBox[] boxes;
            
            // User for empty refernces
            private static ZIMapApplication.MailBox[] empty = new ZIMapApplication.MailBox[0];
            
            /// <summary>A static invalid reference.</summary>
            public static readonly MBoxRef Nothing = new MBoxRef(null, uint.MaxValue); 
            
            public MBoxRef(bool asEmpty)
            {   this.index  = 0;
                this.length = 0;
                this.boxes  = asEmpty ? null : empty;
            }
            
            public MBoxRef(ZIMapApplication.MailBox[] boxes)
            {   this.index  = 0;
                this.length = (boxes == null) ? 0 : (uint)boxes.Length; 
                this.boxes  = (length > 0) ? boxes : empty;
            }
            
            public MBoxRef(ZIMapApplication.MailBox[] boxes, uint index)
            {   this.index  = index;
                this.length = (boxes == null) ? 0 : (uint)boxes.Length; 
                this.boxes  = (length > 0) ? boxes : empty;
            }

            // =================================================================
            // Accessors
            // =================================================================
            
            /// <summary>Gets the array of mailboxes to which this instance refers to.</summary>
            /// <returns>The result is never <c>null</c>, event for a "Nothing" reference.</returns>
            public ZIMapApplication.MailBox[] Array
            {   get {   return boxes;
                    }
            }

            public ZIMapApplication.MailBox Current
            {   get {   return (index < length) ? boxes[index] : new ZIMapApplication.MailBox();
                    }
            }
            
            /// <summary>Returns the number of mailboxes that this reference can access.</summary>
            /// <returns>On error <c>0</c> is returned.</returns>
            public uint Count
            {   get {   return length;  }
            }

            /*
            /// <summary>Check if the mailbox has detailed info.</summary>
            public bool HasDetails
            {   get {   return (index < length) ? boxes[index].HasDetails : false; 
                    }
            }
            
            /// <summary>Check if the mailbox has subscription info.</summary>
            public bool HasSubscription
            {   get {   return (index < length) ? boxes[index].HasSubscription : false;   
                    }
            }*/
            
            /// <summary>Returns the current mailbox index.</summary>
            /// <returns>On error <see cref="uint.MaxValue" /> is returned.</returns>
            public uint Index
            {   get {   return (length > 0) ? index : uint.MaxValue;   
                    }
            }

            public bool IsNothing
            {   get {   return (boxes == null);
                    }
            }

            /// <summary>Checks if the reference points to a mailbox.</summary>
            /// <returns>When not referencing a mailbox <c>false</c> is returned.</returns>
            public bool IsValid
            {   get {   return index < length;
                    }
            }
           /*
            /// <summary>Returns the read-only status of the mailbox.</summary>
            /// <returns>If a mailbox is read-only <c>true</c> is returned.</returns>
            public bool ReadOnly
            {   get {   return (index < length) ? boxes[index].ReadOnly : true;
                    }
            }*/
            
            /// <summary>Returns the name of the mailbox.</summary>
            /// <returns>On error <c>null</c> is returned.</returns>
            public string Name
            {   get {   return (index < length) ? boxes[index].Name : null;
                    }
            }
            
            /// <summary>Returns the number of messages in the mailbox.</summary>
            /// <returns>On error <see cref="uint.MaxValue" /> is returned.</returns>
            public uint Messages
            {   get {   return (index < length) ? boxes[index].Messages : uint.MaxValue;
                    }
            }

            public bool Subscribed
            {   get {   return (index < length) ? boxes[index].Subscribed : false;
                    }
            }

            // =================================================================
            // Extra Mailbox data
            // =================================================================

            // Stored as UserData in the MailBox entry
            private class UserData
            {   public string   Rights;
                public string   QuotaRoot;
                public uint     StorageLimit, StorageUsage;       
                public uint     MessageLimit, MessageUsage;       
            }

            /// <summary>Check if the reference contains extra data for the mailbox.</summary>
            /// <returns>For availlable extra data <c>true</c> is returned.</returns>
            public bool HasExtra
            {   get {   if(index >= length) return false;
                        return boxes[index].UserData != null;
                    }
            }
            
            // helper that allocates the data class
            private UserData Extra 
            {   get {   UserData udat = (UserData)(boxes[index].UserData);
                        if(udat != null) return udat;
                        boxes[index].UserData = udat = new MBoxRef.UserData();
                        return udat;
                    }
            }

            public string ExtraRights
            {   get {   if(!HasExtra) return null;
                        return Extra.Rights;
                    }
                set {   if(IsValid) Extra.Rights = value;   }
            }

            public string ExtraQuotaRoot
            {   get {   if(!HasExtra) return null;
                        return Extra.QuotaRoot;
                    }
                set {   if(IsValid) Extra.QuotaRoot = value; }
            }

            public bool ExtraSetQuota(ZIMapApplication.QuotaInfo info)
            {   if(!IsValid) return false;
                UserData data     = Extra;                      // creates instance
                data.QuotaRoot    = info.Valid ? info.QuotaRoot : "";
                data.StorageUsage = info.StorageUsage; data.StorageLimit = info.StorageLimit;
                data.MessageUsage = info.MessageUsage; data.MessageLimit = info.MessageLimit;
                return true;
            }

            public bool ExtraGetQuota(out ZIMapApplication.QuotaInfo info)
            {   info = new ZIMapApplication.QuotaInfo();
                if(!HasExtra) return false;
                UserData data = Extra;
                info.QuotaRoot = data.QuotaRoot;
                info.StorageUsage = data.StorageUsage; info.StorageLimit = data.StorageLimit;
                info.MessageUsage = data.MessageUsage; info.MessageLimit = data.MessageLimit;
                return !string.IsNullOrEmpty(data.QuotaRoot);
            }

            // =================================================================
            // Methods
            // =================================================================
            
            /// <summary>Advance the current index, can be used as iterator.</summary>
            /// <returns>On success <c>true</c> is returned.</returns>
            public bool Next()
            {   if(boxes == null) return false;
                if(index == uint.MaxValue)              // set by Reset()
                {   index = 0;  return true;
                }
                if(index >= length) return false;
                index++;  
                return index < length;
            }

            /// <summary>Sets the current index.</summary>
            /// <returns>On success <c>true</c> is returned.</returns>
            public bool Next(uint position)
            {   if (boxes == null || position >= length) return false;
                index = position; return true;
            }
            
            /// <summary>Prepary Index for a new iteration.</summary>
            public void Reset()            
            {   index = uint.MaxValue;
            }

            /// <summary>Iterator to enumerate a mailbox and it's descendents.</summary>
            /// <param name="position">
            /// Set on return, must initially be <see cref="MBoxRef.Nothing"/>.
            /// </param>
            /// <param name="server">
            /// Used for namespace support <see cref="ZIMapServer.FriendlyName"/>.
            /// </param>
            /// <returns>On success <c>true</c> is returned.</returns>
            /// <remarks>
            /// The returned <paramref name="position"/> should be used to access the
            /// data.<para/>
            /// <example><code lang="C#">
            ///   uint udel = 0;
            ///   CacheData.MBoxRef root = ...;
            ///   CacheData.MBoxRef position = CacheData.MBoxRef.Nothing;
            ///   while(umbx.Recurse(ref positioin, Server))
            ///   {    if(position.Messages > 0) udel++
            ///   }
            /// </code></example> 
            /// </remarks>
            public bool Recurse(ref MBoxRef position, ZIMapServer server)
            {   if(!IsValid) return false;
                if(!position.IsValid)                       // start the iteration
                {   position = this;
                    return true;
                }
                
                // get the friendly root name and append a hierarchie delimiter
                uint rnsi;
                string root = boxes[index].Name;
                root = server.FriendlyName(root, out rnsi);
                root += server[rnsi].Delimiter;

                // now scan the list of mailboxes ...
                while(position.Next())
                {   if(position.index == index) continue;   // Reset() called?
                    uint cnsi;
                    string name = server.FriendlyName(position.Name, out cnsi);
                    if(rnsi == cnsi && name.StartsWith(root)) return true;
                }
                position = MBoxRef.Nothing;
                return false;
            }
            
            /// <summary>Sends a EXAMINE or SELECT if neccessary.</summary>
            /// <returns>On success <c>true</c> is returned.</returns>
            public bool Open(ZIMapApplication application, bool readOnly)
            {   if(!IsValid) return false;
                if(readOnly || !application.MailboxIsReadonly)  // don't downgrade
                    readOnly = application.MailboxIsReadonly;   
                bool bok = application.MailboxOpen(Name, readOnly);
                return bok;
            }

            /// <summary>Search the array for an exact name match.</summary>
            public uint Search(string name)
            {   if(boxes == null || string.IsNullOrEmpty(name)) return uint.MaxValue;
                for(int irun=0; irun < length; irun++)
                    if(boxes[irun].Name == name) return (uint)irun;
                return uint.MaxValue;
            }

            /// <summary>Append a new entry to the array.</summary>
            public bool Append(string boxname, char delimiter)
            {   if(string.IsNullOrEmpty(boxname)) return false;
                if(length == 0) boxes = new ZIMapApplication.MailBox[1];
                else            System.Array.Resize(ref boxes, (int)(length+1));
                boxes[length].Name = boxname;
                boxes[length].Delimiter = delimiter;
                boxes[length].Attributes = ZIMapConverter.StringArray(0);
                length++;
                return true;
            }

            /// <summary>Remove one entry from the array.</summary>
            public bool Delete(MBoxRef mbox)
            {   if(boxes == null || !mbox.IsValid) return false;
                uint index = mbox.Index;
                if(!object.ReferenceEquals(boxes, mbox.boxes)) index = Search(mbox.Name);
                if(length == 0 || index >= length) return false;
                ZIMapApplication.MailBox[] dest = new ZIMapApplication.MailBox[--length];
                if(index > 0) System.Array.Copy(boxes, dest, index);
                int tail = (int)(length - index);
                if(tail > 0) System.Array.Copy(boxes, index+1, dest, index, tail);
                boxes = dest;
                return true;
            }
        }

        // =============================================================================
        // MailRef     
        // =============================================================================

        public struct MailRef
        {
            public static readonly MailRef Nothing;
            
            private class MRefSegment
            {   //public uint[]       ids;
                //public bool         hasUIDs;
                public ZIMapApplication.MailInfo[]  array;
                public string       mailbox;
                public MRefSegment  next;
            }
            
            //
            private uint        index;
            //
            private MRefSegment segment;

            public MailRef(ZIMapApplication.MailInfo[] items, /*bool hasUIDs,*/ string mailbox)
            {   segment = new MailRef.MRefSegment();
                segment.array   = items;
                //segment.hasUIDs = hasUIDs;
                segment.mailbox = mailbox;
                index = 0;
            }
            
            public uint Index
            {   get {   return index;   }
                set {   index = (value >= Count) ? uint.MaxValue : value; }   
            }
            
            public ZIMapApplication.MailInfo[] Array(uint segment)
            {   MRefSegment seg = this.segment;
                if(seg == null) return null;
                while(segment > 0)
                {   if(seg.next == null) return null;
                    segment--;
                }
                return seg.array;
            }
/*            
            public uint Id
            {   get {   uint idx = index;
                        MRefSegment seg = Segment(ref idx);
                        return (seg == null) ? uint.MaxValue : seg.ids[idx];
                    }
            }
*/            
            public uint Count
            {   get {   uint ucnt = 0;
                        for(MRefSegment seg = segment; seg != null; seg = seg.next)
                            ucnt += (uint)seg.array.Length;
                        return ucnt;
                    }
            }
/*
            public bool HasUIDs
            {   get {   return HasUIDs; }
            }
*/            
            public bool IsNothing
            {   get {   return segment.array == null; }
            }

            public string Mailbox
            {   get {   uint idx = index;
                        MRefSegment seg = Segment(ref idx);
                        return (seg == null) ? null : seg.mailbox;
                    }
            }
            
            public bool Valid
            {   get {   return index < Count;   }
            }
            
            private MRefSegment Segment(ref uint idx)
            {   uint uidx = 0;
                uint uend;
                for(MRefSegment seg = segment; seg != null; seg = seg.next)
                {   uend = uidx + (uint)seg.array.Length;
                    if(idx < uend)
                    {   idx -= uidx; return seg;
                    }
                    uidx = uend;
                }
                return null;
            }
            
            public bool Append(ZIMapApplication.MailInfo[] items, string mailbox)
            {   if(items == null) return false;
                uint unew = (uint)items.Length;
                if(unew == 0) return true;
                
                MRefSegment seg = segment;
                while(seg.next != null) seg = seg.next;     // get last segment
                if(seg.mailbox == mailbox)                  // merge...
                {   uint uold = (uint)seg.array.Length;
                    System.Array.Resize(ref seg.array, (int)(uold+unew));
                    System.Array.Copy(items, 0, seg.array, uold, unew);
                    return true;
                }
                seg.next = new MailRef.MRefSegment();
                seg.next.array   = items;
                //seg.next.hasUIDs = seg.hasUIDs;
                seg.next.mailbox = mailbox;
                return true;
            }
            
        }
    }
}
