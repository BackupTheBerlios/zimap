//==============================================================================
// ZIMapExport.cs implements the ZIMapExport class
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU Lesser General Public License
// This software is published under the GNU LGPL license. Please refer to the
// files COPYING and COPYING.LESSER for details. Please use the following e-mail
// address to contact me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace ZIMap
{
    //==========================================================================
    // Class to import and export mail messages in mbox format
    //==========================================================================

    /// <summary>
    /// Class to import and export mail messages in mbox format (Application layer).
    /// </summary>
    /// <remarks>
    /// The <b>application</b> layer it the top-most of three layers. The others
    /// are the command and the protocol layers.
    /// </remarks>
    public class ZIMapExport : ZIMapBase, IDisposable
    {
        public struct MailFile
        {   /// <value>The unencoded (UNICODE) mailbox name</value>
            public string      MailboxName;
            /// <value>The filesystem name (encoded with version)</value>
            public string      FileName;
            /// <value>The folder for the file in FileName</value>
            public string      Folder;
            /// <value>The most recent version of the file</value>
            public ushort      Latest;
            // <value>The hierarchy nesting level</value>
            //public uint        Depth;
            /// <value><c>null</c> or array of version numbers</value>
            public ushort[]    VersionNumbers;
            /// <value><c>null</c> or array of version file names</value>
            public string[]    VersionNames;
            
            /// <summary>Return the full Path for FileName</summary>
            public string FullPath()
            {   return Path.Combine(Folder, FileName);
            }
            
            /// <summary>Status formatted for debug output</summary>
            public override string ToString()
            {   string vers = "";
                if(VersionNames != null) 
                    vers = string.Format(" ({0} versions)", VersionNames.Length);
                return string.Format("Mailbox '{0}'  Latest {1}{2}",
                                         MailboxName, Latest, vers);
            }
        }

        public static char[] BadChars = @"\/.:*?".ToCharArray();        
        
        public static bool   Allow8Bit = true;        

        public ZIMapExport(ZIMapConnection parent) : base(parent) {}

        // must implement, abstract in base ...
        protected override void Monitor(ZIMapMonitor level, string message)
        {   if(MonitorLevel <= level) 
                ZIMapConnection.Monitor(Parent, "ZIMapExport", level, message); 
        }
        
        // the folder that was passed to Open()
        private string      folder;
        // non-null if a file argument was passed to Open()
        private string      file;
        // do not override files, use new versions
        private bool        versioning;
        // Results of ParseFolder
        private MailFile[]  existing;
        // true if ParseFolder was told to get versions
        private bool        hasVersions;

        // serial number for Open()        
        private static uint serialCounter;
        private uint        serial;
        
        public string       Folder
        {   get {   return folder;  }
            set {   if(Open(value, false, false, false) == 0)
                        Error(ZIMapErrorCode.InvalidArgument);
                }
        }

        public MailFile[]   Existing
        {   get {   if(existing != null) return existing;
                    return CurrentMailFiles(false);
                }
            set {   existing = null;
                    if(value != null) Error(ZIMapErrorCode.MustBeZero);
                }
        }

        public string       Filename
        {   get {   return file;  }
            set {   if(Open(value, true, false, false) == 0)
                        Error(ZIMapErrorCode.InvalidArgument);
                }
        }

        public uint         Serial
        {   get {   return serial; }
        }
        
        public bool         Versioning
        {   get {   return versioning;  }
            set {   versioning = value; }
        }
        
        public MailFile[] ExistingWithVersions(bool wantVersions)
        {   if(!wantVersions) return Existing;
            if(existing != null && hasVersions) return existing;
            hasVersions = true;
            existing = ParseFolder(folder, true);
            return existing;
        }

        public uint Open(string path, bool allowFile, bool openWrite, bool allowCreate)
        {   if(string.IsNullOrEmpty(path)) return 0;
            Dispose();
            path = Path.GetFullPath(path);
            
            try {
                Console.WriteLine("Full  : " + path);
                bool exists = Directory.Exists(path);
                bool isfile = exists ? false : File.Exists(path);
                if(isfile)
                {   exists = true;
                    if(!allowFile)
                    {   Monitor(ZIMapMonitor.Error, "Path specifies a file: " + path);
                        return 0;
                    }
                }
                if(exists)
                {   if(isfile)
                    {   folder = Path.GetDirectoryName(path);
                        file   = Path.GetFileName(path);
                    }
                    else
                        folder = path;
                }
                else
                {   if(!allowFile || !allowCreate)
                    {   Monitor(ZIMapMonitor.Error, "Folder or file does not exist: " + path);
                        return 0;
                    }
                    folder = Path.GetDirectoryName(path);
                    file   = Path.GetFileName(path);
                    Console.WriteLine("Forder: " + folder);
                    Console.WriteLine("File  : " + file);
                    
                    if(string.IsNullOrEmpty(file))
                        allowFile = false;
                    else if(!Directory.Exists(folder))
                    {   Monitor(ZIMapMonitor.Error, "Base folder does not exist " + folder);
                        folder = file = null;
                        return 0;
                    }
                    
                    if(allowFile)
                    {   file = path;
                        File.Create(file);
                        Monitor(ZIMapMonitor.Info, "Created file: " + file);
                    }
                    else
                    {   folder = path; file = null;
                        Directory.CreateDirectory(folder);
                        Monitor(ZIMapMonitor.Info, "Created folder: " + folder);
                    }
                }         
            }
            catch(Exception ex)
            {
                Monitor(ZIMapMonitor.Info, "Exception: " + ex.Message);
                folder = file = null;
                return 0;
            }
            
            serial = ++serialCounter;
            return serial;
        }
        
        public Stream WriteMailbox(string mailbox)
        {   int index = CheckMailboxArg(ref mailbox);
            if(index < -1)
            {   Monitor(ZIMapMonitor.Error, "Cannot get mailbox data");
                return null;
            }
            
            string enam;
            string info = "Create new mbox file: ";
            ushort vers = 0;
            if(index >= 0)
            {   vers = existing[index].Latest;
                if(versioning)
                {   if(vers == ushort.MaxValue)
                    {   Error(ZIMapErrorCode.InvalidArgument, "Version number overrun");
                        return null;
                    }
                    enam = FileNameEncode(mailbox, ++vers);
                    existing[index].Latest = vers;
                    hasVersions = false;
                    info = "Create new version: ";
                }
                else
                {   enam = existing[index].FileName;
                    info = "Override mbox file: ";
                }                    
            }
            else
                enam = FileNameEncode(mailbox, 0);
            Monitor(ZIMapMonitor.Info, info + enam);
            string efil = Path.Combine(folder, enam);
            return File.Create(efil);
       }
        
        public Stream ReadMailbox(string mailbox)
        {   int index = CheckMailboxArg(ref mailbox);
            if(index < 0)
            {   Monitor(ZIMapMonitor.Error, "Found no data for mailbox");
                return null;
            }
            string file = existing[index].FileName;
            Monitor(ZIMapMonitor.Info, "Open mbox file: " + file);
            return File.OpenRead(file);
       }

        public void Dispose ()
        {   folder = null;
            file = null;
            existing = null;
            serial = 0;
        }

        private int CheckMailboxArg(ref string mailbox)
        {   if(string.IsNullOrEmpty(mailbox))
            {   mailbox = null;
                if(file == null)
                {   Monitor(ZIMapMonitor.Error, "No file specified");
                    return -3;
                }
            }
            if(folder == null)
            {   Monitor(ZIMapMonitor.Error, "No folder specified");
                return -3;
            }

            if(file != null)
            {   if(Existing.Length != 1)
                {   ZIMapException.Throw((ZIMapConnection)Parent,
                                          ZIMapErrorCode.Unexpected, "ZIMapExport.CheckMailboxArg");
                    return -3;
                }
                if(mailbox != null) existing[0].MailboxName = mailbox;
                else                mailbox = existing[0].MailboxName;
                return 0;
            }
            return FindMailbox(Existing, mailbox);
        }

        public MailFile[] CurrentMailFiles(bool versions)
        {   // parse a folder ...
            if(folder == null) return null;
            if(file == null)
            {   Monitor(ZIMapMonitor.Info, "CurrentMailFiles: Parsing folder: " + folder);
                if(existing != null && (!versions || hasVersions)) return existing;
                existing = ParseFolder(folder, versions);
                hasVersions = versions;
                if(existing == null)
                    Monitor(ZIMapMonitor.Info, "CurrentMailFiles: Bad Folder");
                return existing;  
            }
            
            // single item for a file (Mailboxname is just a guess)
            if(existing != null) return existing;
            existing = new MailFile[1];
            existing[0].FileName = file;
            existing[0].Folder = folder;
            existing[0].MailboxName = FileNameDecode(Path.GetFileNameWithoutExtension(file), 
                                                     out existing[0].Latest);
            Monitor(ZIMapMonitor.Info, "CurrentMailFiles: Single file: " + existing[0].MailboxName);
            return existing;  
        }
        
        // =============================================================================
        // Private helpers for filename encoding
        // =============================================================================

        // helper to classify filename characters
        private static bool FileNameGood(char chr)
        {   if(chr >= 'a' && chr <= 'z') return true;
            if(chr >= 'A' && chr <= 'Z') return true;
            if(chr >= '0' && chr <= '9') return true;
            if(chr <= 32 || chr == 127)  return false;
            if(chr == '_')               return false;
            if(chr >= 128 && !Allow8Bit) return false;
            for(int irun=0; irun < BadChars.Length; irun++)
                if(chr == BadChars[irun]) return false;
            return true;
        }

        // encode a 16 bit symbol (without prefix)
        private static void SymbolEncode(StringBuilder sb, uint uchr)
        {   // 1st char (6 bit)
            uint ulow = uchr & 63;
            if(ulow <= 9)       sb.Append((char)('0' + ulow));
            else if(ulow <= 35) sb.Append((char)('A' + ulow - 10));
            else if(ulow <= 61) sb.Append((char)('a' + ulow - 36));
            else if(ulow == 62) sb.Append('-');
            else                sb.Append('+');
            if(uchr <= 63)
            {   sb.Append('_'); return;
            }
            
            // 2nd char (6 bit)
            uchr >>= 6;
            ulow = uchr & 63;
            if(ulow <= 9)       sb.Append((char)('0' + ulow));
            else if(ulow <= 35) sb.Append((char)('A' + ulow - 10));
            else if(ulow <= 61) sb.Append((char)('a' + ulow - 36));
            else if(ulow == 62) sb.Append('-');
            else                sb.Append('+');
            if(uchr <= 63)
            {   sb.Append('_'); return;
            }
            
            // 3nd char (4 bit)
            uchr >>= 6;
            ulow = uchr & 63;
            if(ulow <= 9)       sb.Append((char)('0' + ulow));
            else                sb.Append((char)('A' + ulow - 10));
        }
            
        // helper to encode a single character
        private static void FileNameEncode(StringBuilder sb, char chr)
        {   sb.Append('_');
            if(chr == '_')
            {   sb.Append('_'); return;
            }
            SymbolEncode(sb, (uint)chr);
        }

        // helper to decode a 6bit code
        private static uint SymbolDecode(char chr)
        {   if(chr == '-') return 62;
            if(chr == '+') return 63;
            if(chr < '0' || chr > 'z') return uint.MaxValue;
            uint uval = (uint)chr;
            if(chr <= '9') return uval - '0';
            if(chr <  'A') return uint.MaxValue;
            if(chr <= 'Z') return uval - 'A' + 10;
            if(chr <  'a') return uint.MaxValue;
            if(chr <= 'z') return uval - 'a' + 36;
            return uint.MaxValue;
        }

        // helper to decode a 16bit symbol from a string
        private static uint SymbolDecode(string encoded, ref int index)
        {   int ilen = encoded.Length - index;
            if(ilen < 2) return uint.MaxValue;          // minimum "0_"

            // 1st char
            char chr = encoded[index++];
            uint uchr = SymbolDecode(chr);
            if(uchr == uint.MaxValue) return uchr;      // bad symbol
            chr = encoded[index++];
            if(chr == '_') return uchr;
            
            // 2nd char
            if(ilen < 3) return uint.MaxValue;
            uint umid = SymbolDecode(chr);
            if(umid == uint.MaxValue) return uchr;      // bad symbol
            uchr += (umid << 6);
            chr = encoded[index++];
            if(chr == '_') return uchr;
            
            // 3rd char
            umid = SymbolDecode(chr);
            if(umid == uint.MaxValue) return uchr;      // bad symbol
            return uchr + (umid << 12);
        }
        
        // helper to decode the file name from a string
        private static char FileNameDecode(string encoded, ref int index)
        {   int ilen = encoded.Length - index;
            char chr = encoded[index];
            if(ilen < 3 || chr != '_') return chr;      // minimum: "_0_"
            int start = index + 1;
            uint uchr = SymbolDecode(encoded, ref start);
            if(uchr != uint.MaxValue) index = start - 1; 
            return (char)uchr;
        }
        
        // helper to decode the file version from a string
        private static ushort FileVersionDecode(string encoded, ref int index)
        {   int ilen = encoded.Length - index;
            if(ilen < 4) return ushort.MaxValue;        // minimum: "_=0_"
            if(encoded[index] != '_' || encoded[index+1] != '=')
                         return ushort.MaxValue;
            int start = index + 2;
            uint vers = SymbolDecode(encoded, ref start);
            if(vers != uint.MaxValue) index = start - 1;
            return (ushort)vers;
        }

        // =============================================================================
        // Public interface for file handling
        // =============================================================================
        
        /// <summary>
        /// Encode filename and version in a portable way.
        /// </summary>
        /// <param name="mailbox">
        /// A <see cref="System.String"/>
        /// </param>
        /// <param name="version">
        /// A <see cref="System.UInt32"/>
        /// </param>
        /// <returns>
        /// A <see cref="System.String"/>
        /// </returns>
        public static string FileNameEncode(string mailbox, ushort version)
        {   if(string.IsNullOrEmpty(mailbox)) return mailbox;
            StringBuilder sb = new StringBuilder();
            for(int irun=0; irun < mailbox.Length; irun++)
            {   if(FileNameGood(mailbox[irun]))
                    sb.Append(mailbox[irun]);
                else
                    FileNameEncode(sb, mailbox[irun]);
            }
            if(version > 0)
            {   sb.Append("_=");
                SymbolEncode(sb, version);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Decode a filename and get the file version
        /// </summary>
        /// <param name="encoded">
        /// A <see cref="System.String"/>
        /// </param>
        /// <param name="version">
        /// A <see cref="System.UInt32"/>
        /// </param>
        /// <returns>
        /// A <see cref="System.String"/>
        /// </returns>
        public static string FileNameDecode(string encoded, out ushort version)
        {   version = 0;
            if(string.IsNullOrEmpty(encoded)) return encoded;
            
            StringBuilder sb = new StringBuilder();
            for(int irun=0; irun < encoded.Length; irun++)
            {   if(encoded[irun] != '_' || irun+1 >= encoded.Length)
                {   sb.Append(encoded[irun]);
                    continue;
                }
                if(encoded[irun+1] == '=')
                    version = FileVersionDecode(encoded, ref irun);
                else
                    sb.Append(FileNameDecode(encoded, ref irun));
            }
            return sb.ToString();
        }
        
        private static void SortFiles(MailFile[] infos)
        {   // get maximum name length
            string[] names = new string[infos.Length]; 
            int ilen = 0;
            for(int irun=0; irun < names.Length; irun++)
                ilen = Math.Max(ilen, infos[irun].MailboxName.Length);
            // create sort key ...
            for(int irun=0; irun < names.Length; irun++)
                names[irun] = string.Format("{0}{1,4:X}",
                    infos[irun].MailboxName.PadRight(ilen), infos[irun].Latest);
            // we must sort by mailbox name + version
            Array.Sort(names, infos);
        }
        
        public static MailFile[] ParseFolder(string folder, bool versions)
        {   if(string.IsNullOrEmpty(folder)) return null;
            if(!Directory.Exists(folder)) return null;
            folder = Path.GetFullPath(folder);
            string[] names = Directory.GetFiles(folder);
            if(names == null) return new MailFile[0];

            // get all files in the folder
            MailFile[] infos = new MailFile[names.Length];
            for(int irun=0; irun < names.Length; irun++)
            {   string name = names[irun];
                infos[irun].Folder = folder;
                infos[irun].FileName = Path.GetFileName(name);
                infos[irun].MailboxName = FileNameDecode(
                    Path.GetFileNameWithoutExtension(name),                                                         
                    out infos[irun].Latest);
            }
            
            // we must sort by mailbox name + version
            SortFiles(infos);
            
            // count versions
            uint ucnt = 0;
            uint[] uver = new uint[infos.Length];
            string mbox = "";
            for(int irun=0; irun < infos.Length; irun++)
            {   if(infos[irun].MailboxName == "") continue;
                if(mbox == infos[irun].MailboxName)
                {   uver[ucnt-1] = uver[ucnt-1] + 1;
                    continue;
                }
                mbox = infos[irun].MailboxName; ucnt++; 
            }

            // create the final MailFile array ...
            MailFile[] roots = new MailFile[ucnt];
            mbox = null;     
            ucnt = 0;
            ushort latest = 0;
            for(int irun=0; irun < infos.Length; irun++)
            {   if(infos[irun].MailboxName == "") continue;
                if(mbox == infos[irun].MailboxName)
                {   if(infos[irun].Latest <= latest) continue;
                    latest = infos[irun].Latest;
                    roots[ucnt-1].Latest = latest;
                    continue;
                }
                roots[ucnt] = infos[irun];
                latest = roots[ucnt].Latest;
                mbox = infos[irun].MailboxName;
                
                // fill the Versions array
                if(uver[ucnt] > 0 && versions)
                {   uint uarr = uver[ucnt] + 1;
                    ushort[] varr = new ushort[uarr];
                    string[] narr = new string[uarr];
                    for(uint uarc=0; uarc < uarr; uarc++)
                    {   varr[uarc] = infos[irun+(int)uarc].Latest;
                        narr[uarc] = infos[irun+(int)uarc].FileName;
                    }
                    roots[ucnt].VersionNumbers = varr;
                    roots[ucnt].VersionNames   = narr;
                }
                ucnt++;
            }
            return roots; 
        }
        
        public static int FindMailbox(MailFile[] existing, string mailbox)
        {   if(existing == null) return -2;
            if(string.IsNullOrEmpty(mailbox)) return -2;
            for(int irun=0; irun < existing.Length; irun++)
                if(existing[irun].MailboxName == mailbox) return irun;
            return -1;
        }

        public static void DumpMailFiles(MailFile[] files)
        {
            if(files == null)
            {   Console.WriteLine("No files");
                return;
            }
            foreach(MailFile f in files)
                Console.WriteLine(f.ToString());
        }
    }
}
