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
            public string       MailboxName;
            /// <value>The filesystem name (encoded with version)</value>
            public string       FileName;
            /// <value>The folder for the file in FileName</value>
            public string       Folder;
            /// <value>The most recent version of the file</value>
            public ushort       Latest;
            /// <value><c>null</c> or array of version numbers</value>
            public ushort[]     VersionNumbers;
            /// <value><c>null</c> or array of version file names</value>
            public string[]     VersionNames;

            // see FullPath preperty
            private string      fullPath;
            // see FileInfo property 
            private FileInfo    info;
            // see Valid property
            private uint        valid;

            /// <summary>Return the full Path for FileName</summary>
            public string FullPath
            {   get {   if(fullPath == null) fullPath = Path.Combine(Folder, FileName);
                        return fullPath;
                    }
            }
            
            /// <summary>Return a FileInfo for the current file</summary>
            public FileInfo FileInfo
            {   get {   // new FileInfo does not throw exceptions!
                        if(info == null) info = new FileInfo(FullPath);
                        return info;
                    }
            }
            
            public bool Valid
            {   get {   if(valid == 1) return true;
                        if(valid >  1) return false;
                        valid = 2;
                        try
                        {   string line = null;
                            StreamReader sr = new StreamReader(FullPath);
                            if(sr != null) line = sr.ReadLine();
                            if(line == null) return false;
                            if(line.Length > 10 && line.StartsWith("From "))
                            {   valid = 1;
                                return true;
                            }
                        } catch(Exception) {}
                        return false;
                    }        
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

        // --- class data ---        

        private const byte              SPACE = 32;
        private const byte              CR = (byte)'\r';
        private const byte              LF = (byte)'\n';
        private static readonly byte[]  CRLF = { CR, LF };

        /// <summary>Base folder for relative arguments of <see cref="Open"/></summary>
        public static string BaseFolder;
        /// <summary>Characters excluded from filenames</summary>
        public static char[] BadChars = @"\/.:*?".ToCharArray();        
        /// <summary>Allow 8 bit characters in filenames</summary>
        public static bool   Allow8Bit = true;
        
        // A counter for the Serial property
        private static uint serialCounter;
        
        // --- instance data ---        
        
        // hierarchy delimiter
        private char        delimiter;        
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
        private uint        serial;
        // streams from ReadMailbox or WriteMailbox
        private Stream      stream;
        // data from ReadMailbox
        private byte[]      mbdata;
        // offset to mbdata (e.g. number of bytes processed)
        private uint        mboffs;
        
        // =============================================================================
        // Xtor
        // =============================================================================

        /// <summary>
        /// This is the one and only constructor.
        /// </summary>
        /// <param name="parent">
        /// Required reference to the connection.  Must not be <c>null</c>.
        /// </param>
        public ZIMapExport(ZIMapConnection parent) : base(parent) {}

        // must implement, abstract in base ...
        protected override void MonitorInvoke(ZIMapConnection.Monitor level, string message)
        {   if(MonitorLevel <= level) 
                ZIMapConnection.MonitorInvoke(Parent, "ZIMapExport", level, message); 
        }

        /// <summary>
        /// Close open import/export file and free all resources.
        /// </summary>
        /// <remarks>
        /// Without a call to this function an output file might not be correctly
        /// written (e.g. might be empty or truncated).  An alternate way to close
        /// the file is to assing <c>null</c> to the <see cref="IOStream"/> property.
        /// </remarks>
        public void Dispose ()
        {   folder = null;
            file = null;
            existing = null;
            serial = 0;
            IOStream = null;            // property calls Close()
        }

        // =============================================================================
        // Properties
        // =============================================================================
        
        /// <summary>Gives access to the namespace delimiter</summary>
        public char Delimiter
        {   get {   return delimiter;   }
            set {   delimiter = value;  }
        }
            
        public string       Folder
        {   get {   return folder;  }
            set {   if(Open(value, delimiter, false, false, false) == 0)
                        RaiseError(ZIMapException.Error.InvalidArgument);
                }
        }

        public MailFile[]   Existing
        {   get {   if(existing != null) return existing;
                    return CurrentMailFiles(false);
                }
            set {   existing = null;
                    if(value != null) RaiseError(ZIMapException.Error.MustBeZero);
                }
        }

        public string       Filename
        {   get {   return file;  }
            set {   if(Open(value, delimiter, true, false, false) == 0)
                        RaiseError(ZIMapException.Error.InvalidArgument);
                }
        }

        public uint         Serial
        {   get {   return serial; }
        }

        public Stream       IOStream
        {   get {   return stream;
                }
            set {   if(value != null) RaiseError(ZIMapException.Error.MustBeZero);
                    mbdata = null;
                    if(stream == null) return;
                    stream.Close();
                    stream = null;
                }
        }
       
        public bool         Versioning
        {   get {   return versioning;  }
            set {   versioning = value; }
        }

        // =============================================================================
        // Public Interface
        // =============================================================================
        
        public MailFile[] ExistingWithVersions(bool wantVersions)
        {   if(!wantVersions) return Existing;
            if(existing != null && hasVersions) return existing;
            hasVersions = true;
            existing = ParseFolder(folder, delimiter, true);
            return existing;
        }

        public uint Open(string path, char delimiter, 
                         bool allowFile, bool openWrite, bool allowCreate)
        {   if(string.IsNullOrEmpty(path)) return 0;
            if(delimiter == 0) 
                delimiter = ((ZIMapConnection)Parent).CommandLayer.HierarchyDelimiter;
            this.delimiter = delimiter;
            Dispose();
            if(!string.IsNullOrEmpty(BaseFolder) && !Path.IsPathRooted(path))
                path = Path.Combine(BaseFolder, path);
            path = Path.GetFullPath(path);
            
            try {
                bool exists = Directory.Exists(path);
                bool isfile = exists ? false : File.Exists(path);
                if(isfile)
                {   exists = true;
                    if(!allowFile)
                    {   MonitorError("Path specifies a file: " + path);
                        return 0;
                    }
                }
                if(exists)
                {   if(isfile)
                    {   folder = Path.GetDirectoryName(path);
                        file   = Path.GetFileName(path);
                        MonitorError("Using existing file: " + path);
                    }
                    else
                    {   folder = path;
                        MonitorError("Using existing folder: " + path);
                    }
                }
                else
                {   if(/*!allowFile ||*/ !allowCreate)
                    {   MonitorError("Folder or file does not exist: " + path);
                        return 0;
                    }
                    folder = Path.GetDirectoryName(path);
                    file   = Path.GetFileName(path);
                    
                    if(string.IsNullOrEmpty(file))
                        allowFile = false;
                    else if(!Directory.Exists(folder))
                    {   MonitorError("Base folder does not exist " + folder);
                        folder = file = null;
                        return 0;
                    }
                    
                    if(allowFile)
                    {   file = path;
                        File.Create(file);
                        MonitorInfo( "Created file: " + file);
                    }
                    else
                    {   folder = path; file = null;
                        Directory.CreateDirectory(folder);
                        MonitorInfo( "Created folder: " + folder);
                    }
                }         
            }
            catch(Exception ex)
            {
                MonitorInfo( "Exception: " + ex.Message);
                folder = file = null;
                return 0;
            }
            
            serial = ++serialCounter;
            return serial;
        }
        
        /// <summary>
        /// Creates a file for a mailbox that can be used for writing.
        /// </summary>
        /// <param name="mailbox">
        /// A mailbox name (without namespace prefix).
        /// </param>
        /// <param name="delimiter">
        /// The hierarchy delimiter used in the mailbox name.
        /// </param>
        /// <returns>
        /// On success <c>true</c> is returned.
        /// </returns>
        public bool WriteMailbox(string mailbox)
        {   IOStream = null;                            // property calls close   
            int index = CheckMailboxArg(ref mailbox, false);
                        MonitorInfo( mailbox + " " + index);
            if(index < -1) return false;
            
            string info = "Create new mbox file: ";
            string efil, enam;
            if(this.file == null)
            {   ushort vers = 0;
                if(index >= 0)
                {   vers = existing[index].Latest;
                    // TODO: NTFS needs versioning for mailbox names that differ only in case
                    if(versioning)
                    {   if(vers == ushort.MaxValue)
                        {   RaiseError(ZIMapException.Error.InvalidArgument, 
                                       "Version number overrun");
                            return false;
                        }
                        enam = FileNameEncode(mailbox, delimiter, ++vers);
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
                    enam = FileNameEncode(mailbox, delimiter, 0);
                efil = Path.Combine(folder, enam);
            }
            else
            {   info = "Using file: ";
                enam = efil = Path.Combine(folder, file);
            }
            MonitorInfo( info + enam);

            try
            {   stream = File.Create(efil);
                return true;
            }
            catch(Exception ex)
            {   MonitorError("Exception: " + ex.Message);
                stream = null;
            }
            return false;
        }
        
        public bool ReadMailbox(string mailbox)
        {   IOStream = null;                            // property calls close   
            int index = CheckMailboxArg(ref mailbox, true);
            if(index < 0) return false;
            
            string file = existing[index].FullPath;
            MonitorInfo( "Open mbox file: " + file);
            try
            {   stream = File.OpenRead(file);
            }
            catch(Exception ex)
            {   MonitorError("Exception: " + ex.Message);
                stream = null;
                return false;
            }
            
            mboffs = 0;
            mbdata = new byte[stream.Length];
            int ilen = stream.Read(mbdata, 0, mbdata.Length);
            if(ilen != mbdata.Length) 
            {   MonitorError("Could not read mbox file: " + file);
                stream = null;
                return false;
            }
            return true;
        }

        public MailFile[] CurrentMailFiles(bool versions)
        {   // parse a folder ...
            if(folder == null) return null;
            if(file == null)
            {   MonitorInfo( "CurrentMailFiles: Parsing folder: " + folder);
                if(existing != null && (!versions || hasVersions)) return existing;
                existing = ParseFolder(folder, delimiter, versions);
                hasVersions = versions;
                if(existing == null)
                    MonitorInfo( "CurrentMailFiles: Bad Folder");
                return existing;  
            }
            
            // single item for a file (Mailboxname is just a guess)
            if(existing != null) return existing;
            existing = new MailFile[1];
            existing[0].FileName = file;
            existing[0].Folder = folder;
            existing[0].MailboxName = FileNameDecode(Path.GetFileNameWithoutExtension(file), 
                                                     delimiter, out existing[0].Latest);
            MonitorInfo( "CurrentMailFiles: Single file: " + existing[0].MailboxName);
            return existing;  
        }
        
        // =============================================================================
        // Low level IO
        // =============================================================================

        // <param name="quoted">Do not use Content-Length, and do From 
        // (un)quoting instead.</param>

        
        public bool WriteMail(string from, DateTime date, string flags,
                                byte[] header, byte[] body, bool quoted)
        {   if(string.IsNullOrEmpty(from))  return false;
            if(header == null || body == null) return false;

            from = ZIMapMessage.AddressParse(from, true);            
            string text = string.Format("From {0} {1}", from, 
                                ZIMapConverter.EncodeAscTime(date, true));
            try 
            {   // Write the "From " line
                WriteData(Encoding.ASCII.GetBytes(text), true);
                // Write private headers
                if(!quoted)
                {   text = string.Format("Content-Length: {0}", body.Length);
                    WriteData(Encoding.ASCII.GetBytes(text), true);
                    if(!string.IsNullOrEmpty(flags))
                    {   text = string.Format("X-ZIMap-Flags: {0}", flags);
                        WriteData(Encoding.ASCII.GetBytes(text), true);
                    }
                }
                
                // the mail headers
                bool blin = ZIMapMessage.EndsWithEmptyLine(header);
                WriteData(header, !blin);
                // the mail body as a block
                if(!quoted)
                {   blin = ZIMapMessage.EndsWithEmptyLine(body);
                    return WriteData(body, !blin);
                }
                
                // loop over body line to apply "From " quoting
                uint upos = 0;
                blin = false;
                bool cont;
                do  {   uint ubeg = upos;
                        cont = FindCRLF(body, ref upos);
                        uint ulin = upos - ubeg;
                        if(ulin == 0) break;
                        if(IsFromLine(body, ubeg, ulin)) stream.WriteByte((byte)'>');
                        stream.Write(body, (int)ubeg, (int)ulin);
                        blin = (ulin == 2 && cont);             // was an empty line
                    }   while(cont);
                return WriteData(null, !blin);
            }
            catch(IOException ex)
            {   MonitorError("WriteMail caught exeption: " + ex.Message);
                return false;
            }
        }
    
        private bool WriteData(byte[] data, bool appendCRLF)
        {   if(stream == null) return false;
            if(data != null) stream.Write(data, 0, data.Length);
            if(appendCRLF)   stream.Write(CRLF, 0, 2);
            return true;
        }

        // State of the parser used in ReadMail
        private enum MboxState { SearchFrom, SearchLength, ReadHeader, ReadMail }

        /// <summary>
        /// Used to read one mail message from the import data stream.
        /// </summary>
        /// <param name="mail">The message (header and body)</param>
        /// <param name="date">The date given in the mbox From line.</param>
        /// <param name="flags">IMap flags from a X-ZIMapFlags header.</param>
        /// <param name="clean">Remove all X- headers.</param>
        /// <param name="position">Position in file after reading.</param>
        /// <returns></returns>
        public bool ReadMail(out byte[] mail, out DateTime date, out string flags,
                             out uint position, bool clean)
        {   bool cont;
            bool skip = true;
            uint ucon = 0;
            uint ulen = 0;
            uint ubeg = 0;
            position = mboffs;
            byte[] data = mbdata;
            mail = null; date = DateTime.MinValue; flags = null;
            MboxState state = MboxState.SearchFrom;
            MemoryStream ms = new MemoryStream();
            do {
                if (!skip) {
                    ms.Write(data, (int)ubeg, (int)ulen);
                    skip = true;
                }
                ubeg = mboffs;
                cont = FindCRLF(data, ref mboffs);
                ulen = mboffs - ubeg;
                string line;
                switch (state) {
                    case MboxState.SearchFrom:
                        if (ulen < 5) continue;
                        if (data[ubeg] != 'F') continue;
                        if (data[ubeg + 1] != 'r') continue;
                        if (data[ubeg + 2] != 'o') continue;
                        if (data[ubeg + 3] != 'm') continue;
                        if (data[ubeg + 4] != ' ') continue;
                        state = MboxState.SearchLength;
                        ucon = 0; flags = null;
                        // skip until start to address
                        uint uskp = 5;
                        while(uskp < ulen && data[uskp] <= SPACE) uskp++;
                        // skip the address
                        while (uskp < ulen && data[uskp] > SPACE) uskp++;
                        if(uskp >= ulen) continue;              // no date found
                        line = Encoding.ASCII.GetString(data, (int)uskp, (int)(ulen-uskp));
                        date = ZIMapConverter.DecodeAscTime(line.Trim(), true);
                        continue;

                    case MboxState.SearchLength:
                        skip = false;
                        if (ulen < 16 || data[ubeg] == 'x' || data[ubeg] == 'X')
                            goto case MboxState.ReadHeader;
                        if (data[ubeg] != 'C') continue;
                        if (data[ubeg + 1] != 'o') continue;
                        line = Encoding.ASCII.GetString(data, (int)ubeg, (int)ulen - 2);
                        if (line.ToLower().StartsWith("content-length:")) {
                            line = line.Substring(15);
                            if (!uint.TryParse(line, out ucon)) continue;
                            state = MboxState.ReadHeader;
                            skip = true;
                        }
                        continue;

                    case MboxState.ReadHeader:
                        // empty line
                        if (ulen <= 2) {
                            state = MboxState.ReadMail;
                            ms.Write(CRLF, 0, 2);
                            uint uend = mboffs + ucon;
                            if (uend > data.Length)
                                Console.WriteLine("Want {0}  have {1}", uend, data.Length);
                            else
                                ms.Write(data, (int)mboffs, (int)ucon);
                            mboffs += ucon;
                        }
                        // header that begins with 'X-' or 'x-'
                        else if ((data[ubeg] == 'x' || data[ubeg] == 'X') && data[ubeg + 1] == '-') {
                            if (ulen < 14) {
                                skip = false; continue;
                            }
                            line = Encoding.ASCII.GetString(data, (int)ubeg, (int)ulen - 2);
                            if (!line.ToLower().StartsWith("x-zimap-flags:")) {
                                skip = false; continue;
                            }
                            flags = line.Substring(14).Trim();
                        } else
                            skip = false;
                        continue;

                    default:
                        mail = ms.ToArray();
                        if (ulen > 2)               // missing CRLF, undo read
                            mboffs = ubeg;
                        position = mboffs;
                        return true;
                }
            } while (cont);
            return false;
        }

        private static bool IsFromLine(byte[] buffer, uint offset, uint count)
        {   if(count < 5) return false;
            while(buffer[offset] == '>')
            {   count--; offset++;
                if(count < 5) return false;
            }
            if(buffer[offset]   != 'F') return false;
            if(buffer[offset+1] != 'r') return false;
            if(buffer[offset+2] != 'o') return false;
            if(buffer[offset+3] != 'm') return false;
            if(buffer[offset+4] != ' ') return false;
            return true;
        }
        
        private static bool FindCRLF(byte[] buffer, ref uint position)
        {   if(buffer == null) return false;
            uint uend = (uint)buffer.Length;
            if(uend == 0 || position >= uend) return false;
            uend--;
            
            for(uint irun=position; irun <= uend; irun++)
            {   // check for line break or end of buffer
                if(buffer[irun] != CR) continue;
                if(irun == uend)                        // last char is CR
                {   position = irun; return false;
                }
                if(buffer[irun+1] != LF) continue;
                position = irun + 2; return true;       // next line
            }
            position = uend + 1; return false;
        }
        
        // =============================================================================
        // Private helpers
        // =============================================================================

        private int CheckMailboxArg(ref string mailbox, bool mustExist)
        {   if(string.IsNullOrEmpty(mailbox))
            {   mailbox = null;
                if(file == null)
                {   MonitorError("No file specified");
                    return -2;
                }
            }
            if(folder == null)
            {   MonitorError("No folder specified");
                return -2;
            }

            if(file != null)
            {   if(Existing.Length != 1)
                {   RaiseError(ZIMapException.Error.Unexpected, "ZIMapExport.CheckMailboxArg");
                    return -2;
                }
                if(mailbox != null) existing[0].MailboxName = mailbox;
                else                mailbox = existing[0].MailboxName;
                return 0;
            }
            int index = FindMailbox(Existing, mailbox);
            if(index >= 0 || !mustExist) return index;
            MonitorError("Mailbox data not fould: " + mailbox);
            return -2;
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
            if(ilen < 4) return ushort.MaxValue;        // minimum: "_^0_"
            if(encoded[index] != '_' || encoded[index+1] != '^')
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
        /// Encode filename, hierarchy delimiter and version in a portable way.
        /// </summary>
        /// <param name="mailbox">
        /// A mailbox name that has to be expressed as a valid file-system name.
        /// </param>
        /// <param name="delimiter">
        /// The IMAP server specific hierarchy delimiter.
        /// </param>
        /// <param name="version">
        /// An optional file version, <c>0</c> stands for no version.
        /// </param>
        /// <returns>
        /// The encoded file-system name.
        /// </returns>
        /// <remarks>
        /// Namespace names should not be used in file names as they are IMAP server
        /// specific.  Hierarchy delimiters which are also server specific are
        /// transformed to a portable symbol. The encoding alogorithm is:
        /// <para/>
        /// With the exception of _ (underscore) all characters acceptable for the
        /// filesystem are directly copied to the encoded string.  The underscore
        /// is an escape character.  The escape sequences use 6-Bit encoding and are:
        /// <para/><list type="table">
        /// <listheader>
        ///      <term>Escape</term>
        ///      <description>Description</description>
        /// </listheader>
        /// <item>
        ///      <term>__</term>
        ///      <description>Two underscores represent the underscore itself.</description>
        /// </item><item>
        ///      <term>_~</term>
        ///      <description>Underscores+tilde represent a hierarchy delimiter (hierarchy delimiters 
        ///                   are specific to the IMAP server, no need to conserve it).</description>
        /// </item><item>
        ///      <term>_^</term>
        ///      <description>Underscore+Circonflex is followed by the version number.</description>
        /// </item><item>
        ///      <term>_X_</term>
        ///      <description>Underscore+Code+Underscore encodes a value from 0..63.</description>
        /// </item><item>
        ///      <term>_XX_</term>
        ///      <description>Underscore+Code+Code+Underscore encodes a value from 64..4159.</description>
        /// </item><item>
        ///      <term>_XXX</term>
        ///      <description>Underscore+Code+Code+Code encodes a value from 4160..65535.</description>
        /// </item></list>
        /// <para/>
        /// The codes mentioned in the table above are defined as: 
        /// <para/><list type="table">
        /// <listheader>
        ///      <term>Charaters</term>
        ///      <description>6-bit values represented by the characters</description>
        /// </listheader>
        /// <item>
        ///      <term>0 .. 9</term>
        ///      <description>0 .. 9 (e.g. the ASCII code - 48)</description>
        /// </item><item>
        ///      <term>A .. Z</term>
        ///      <description>10 .. 35</description>
        /// </item><item>
        ///      <term>a .. z</term>
        ///      <description>36 .. 61</description>
        /// </item><item>
        ///      <term>-</term>
        ///      <description>62</description>
        /// </item><item>
        ///      <term>+</term>
        ///      <description>63</description>
        /// </item></list>
        /// /// <para/>
        /// As can be seen one mailbox name can be encoded in different ways, especially
        /// because the <see cref="BadChars"/> and  <see cref="Allow8Bit"/> properties can 
        /// be used to restrict the unencoded set of characters for specific file systems.
        /// Anyhow, the <see cref="FileNameDecode"/> routine can get back to the original
        /// mailbox name from all possible encodings. 
        /// </remarks>
        public static string FileNameEncode(string mailbox, char delimiter, ushort version)
        {   if(string.IsNullOrEmpty(mailbox)) return mailbox;
            StringBuilder sb = new StringBuilder();
            for(int irun=0; irun < mailbox.Length; irun++)
            {   char curr = mailbox[irun];
                if(curr == delimiter)
                    sb.Append("_~");
                else if(FileNameGood(curr))
                    sb.Append(mailbox[irun]);
                else
                    FileNameEncode(sb, curr);
            }
            if(version > 0)
            {   sb.Append("_^");
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
        public static string FileNameDecode(string encoded, char delimiter, out ushort version)
        {   version = 0;
            if(string.IsNullOrEmpty(encoded)) return encoded;
            
            StringBuilder sb = new StringBuilder();
            for(int irun=0; irun < encoded.Length; irun++)
            {   if(encoded[irun] != '_' || irun+1 >= encoded.Length)
                {   sb.Append(encoded[irun]);
                    continue;
                }
                if(encoded[irun+1] == '^')          // version
                    version = FileVersionDecode(encoded, ref irun);
                else if(encoded[irun+1] == '~')     // hierarchy delimiter
                {   sb.Append(delimiter);
                    irun++;
                }
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
        
        public static MailFile[] ParseFolder(string folder, char delimiter, bool versions)
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
                    delimiter, out infos[irun].Latest);
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
