//==============================================================================
// ZIMapMessage.cs implements the ZIMapMessage class
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
    // Class to handle RFC2822 encoded mail messages
    //==========================================================================

    /// <summary>
    /// Class to handle RFC2822 encoded mail messages (Application layer).
    /// </summary>
    /// <remarks>
    /// The <b>application</b> layer it the top-most of three layers. The others
    /// are the command and the protocol layers.
    /// </remarks>
    public class ZIMapMessage
    {
        byte[][]        headerVal;
        byte[][]        headerKey;
        byte[][]        bodyLines;
        uint            posSubject, posDate, posFrom, posTo;

        private const byte              SPACE = 32;
        private const byte              CR = (byte)'\r';
        private const byte              LF = (byte)'\n';
        private static readonly byte[]  CRLF = { CR, LF };
            
        /// <summary>
        /// Reset to object to it's initial state.
        /// </summary>
        /// <remarks>
        /// This method releases all memory allocated by the object.  The resulting
        /// state is the same as if a new instance had been created.
        /// </remarks>
        public void Reset()
        {   headerKey = null;
            headerVal = null;
            bodyLines = null;
            posSubject =  posDate = posFrom = posTo = uint.MaxValue;
        }
        
        /// <summary>
        /// Parses the body part of a message.
        /// </summary>
        /// <param name="data">
        /// The data to be parsed.
        /// </param>
        /// <param name="offset">
        /// An offset to the data (to skip the header for example).
        /// </param>
        /// <returns>
        /// <c>true</c> on success.
        /// </returns>
        /// <remarks>
        /// The data is not converted in any way.
        /// </remarks>
        public bool ParseBody(byte[] data, uint offset)
        {   bodyLines = null;
            if(data == null) return false;
            if(offset >= data.Length) return false;
            
            List<byte[]> blin = new List<byte[]>();
            byte[] line;
            while((line = LineOfBytes(data, ref offset, false)) != null)
            {
                if(line.Length == 0)                // empty line or EOF
                {   if(offset >= data.Length) break;
                    blin.Add(null);                 // empty line
                }
                blin.Add(line);
            }
            
            bodyLines = blin.ToArray();
            return true;
        }
        
        public bool Parse(byte[] data, bool ignoreXKeys)
        {   Reset();                                // re-init
            if(data == null) return false;
            uint position = 0;
            byte[] line;

            // --- Parse the header ---
            
            byte[] key = null;
            byte[] val = null;
            uint ulen;
            uint uhdr = 0;
            List<byte[]> keys = new List<byte[]>();
            List<byte[]> vals = new List<byte[]>();
            while((line = LineOfBytes(data, ref position, ignoreXKeys)) != null)
            {
                if(line.Length == 0) break;             // end of header
                // Handle continuation lines ...
                if(line[0] <= SPACE)
                {   if(val == null) continue;           // no val, oops!
                    uint ucnt = 1;
                    while(ucnt < line.Length)
                        if(line[ucnt] > SPACE) break;
                        else ucnt++;
                    if(ucnt >= line.Length) continue;   // empty, oops!
                    ulen = (uint)line.Length - ucnt;
                    uint uold = (uint)val.Length;
                    Array.Resize(ref val, (int)(uold + ulen + 1));
                    Array.Copy(line, ucnt, val, uold+1, ulen);
                    val[uold] = SPACE;
                    vals[vals.Count - 1] = val;
                    continue;
                }
                val = null;
                
                // Separate key and value ...
                for(uint urun=0; urun < line.Length; urun++)
                {   if(line[urun] != ':') continue;
                    
                    key = new byte[urun];               // save key
                    if(urun > 0) Array.Copy(line, key, urun);

                    urun++;                             // skip spaces
                    while(urun < line.Length)
                        if(line[urun] <= SPACE) urun++;
                        else break;
                    if(urun >= line.Length) break;
                    
                    ulen = (uint)line.Length - urun;    // save value
                    val = new byte[ulen];
                    if(ulen > 0) Array.Copy(line, urun, val, 0, ulen);
                    break;
                }
                
                if(key == null || key.Length < 1)       // this is invalid!
                    continue;
                
                // check special keys ...
                if(key.Length == 2)
                {   if(posTo == uint.MaxValue && KeyCompare(key, "To")) posTo = uhdr;
                }
                else if(key.Length == 4)
                {   if     (posFrom == uint.MaxValue && KeyCompare(key, "From")) posFrom = uhdr;
                    else if(posDate == uint.MaxValue && KeyCompare(key, "Date")) posDate = uhdr;
                }
                else if(key.Length == 7)
                {   if(posSubject == uint.MaxValue && KeyCompare(key, "Subject")) posSubject = uhdr;
                }
                
                // save results ...
                keys.Add(key); vals.Add(val);
                uhdr++; key = null;
            }
            headerKey = keys.ToArray();
            headerVal = vals.ToArray();
            
            // --- Parse the body ---
            if(position >= data.Length) return true;
            return ParseBody(data, position);
        }

        // =====================================================================
        // Data retrieval
        // =====================================================================
        
        /// <summary>
        /// Get an undecoded Unicode value of a header key.
        /// </summary>
        /// <param name="index">
        /// The line index (ranging from <c>0</c> to <c>HeaderCount - 1</c>).
        /// </param>
        /// <returns>
        /// A unicode string -or- <c>null</c> if the index was out of range.
        /// </returns>
        /// <remarks>
        /// This routine only handles 7-bit ASCII data (which should be OK for
        /// header keys).
        /// </remarks>
        public string FieldKey(int index)
        {   if(headerKey == null) return null;
            if(index < 0 || index >= headerKey.Length) return null;
            return Encoding.ASCII.GetString(headerKey[index]);
        }
        
        /// <summary>
        /// Get an undecoded Unicode value of a header field.
        /// </summary>
        /// <param name="index">
        /// The line index (ranging from <c>0</c> to <c>HeaderCount - 1</c>).
        /// </param>
        /// <returns>
        /// A unicode string -or- <c>null</c> if the index was out of range.
        /// </returns>
        /// <remarks>
        /// Header fields like To, From and Subject are RFC 2047 encoded.  Do not
        /// use this routine to get such values, see <see cref="FieldText"/>.
        /// This routine only handles 7-bit ASCII data.
        /// </remarks>
        public string FieldValue(int index)
        {   if(headerVal == null) return null;
            if(index < 0 || index >= headerVal.Length) return null;
            byte[] data = headerVal[index];
            if(data == null) return "";
            return Encoding.ASCII.GetString(data);
        }
        
        /// <summary>
        /// Returns the RFC 2047 decoded value of a header field.
        /// </summary>
        /// <param name="index">
        /// The line index (ranging from <c>0</c> to <c>HeaderCount - 1</c>).
        /// </param>
        /// <returns>
        /// A unicode string -or- <c>null</c> if the index was out of range.
        /// </returns>
        /// <remarks>
        /// Header fields like To, From and Subject are RFC 2047 encoded.  Use this
        /// routine to get such values.
        /// </remarks>
        public string FieldText(int index)
        {   if(headerVal == null) return null;
            if(index < 0 || index >= headerVal.Length) return null;
            byte[] data = headerVal[index];
            if(data == null) return "";
            string text = Encoding.ASCII.GetString(data);
            if(string.IsNullOrEmpty(text)) return text;
            return ZIMapConverter.DecodeRfc2047Text(text);
        }
        
        /// <summary>
        /// Returns the decoded string version of a body line.
        /// </summary>
        /// <param name="index">
        /// The line index (ranging from <c>0</c> to <c>BodyCount - 1</c>).
        /// </param>
        /// <param name="encoding">
        /// An Encoding object -or- <c>null</c> to use <see cref="System.Text.Encoding.ASCII"/>.
        /// </param>
        /// <returns>
        /// A unicode string -or- <c>null</c> if the index was out of range.
        /// </returns>
        /// <remarks>
        /// This routine does not handle Base64 decoding or binary data.
        /// </remarks>
        public string BodyLine(int index, Encoding encoding)
        {   if(bodyLines == null) return null;
            if(index < 0 || index >= bodyLines.Length) return null;
            byte[] data = bodyLines[index];
            if(data == null) return "";
            if(encoding == null) encoding = Encoding.ASCII;
            return encoding.GetString(data);
        }

        /// <summary>
        /// Return raw body lines.
        /// </summary>
        /// <param name="index">
        /// The line index (ranging from <c>0</c> to <c>BodyCount - 1</c>).
        /// </param>
        /// <returns>
        /// A byte array if the line contained data.  A return value of <c>null</c>
        /// either indicates an empty body line or an invalid index.
        /// </returns>
        /// <remarks>
        /// This routine simply returns the original data without modification.
        /// </remarks>
        public byte[] BodyLine(int index)
        {   if(bodyLines == null) return null;
            if(index < 0 || index >= bodyLines.Length) return null;
            return bodyLines[index];
        }

        /// <summary>
        /// Case insensitive search of the headers for a given key.
        /// </summary>
        /// <param name="index">
        /// The start index (usually <c>0</c>).  Updated on return.
        /// </param>
        /// <param name="key">
        /// The key to be searched.
        /// </param>
        /// <returns>
        /// <c>true</c> on success.  The index argument is updated on each
        /// return and may be meaningless when the return values is <c>false</c>.
        /// </returns>
        /// <remarks>
        /// The search stops at the first match.  To find ambiguities repeat the
        /// search with an updated start index (n+1).  The value of key must not
        /// contain 16-bit characters, e.g. only ISO8859 is allowed (8-bit).
        /// </remarks>
        public bool SearchKey(ref int index, string key)
        {   if(string.IsNullOrEmpty(key)) return false;
            if(index < 0 || index >= headerKey.Length) return false;
            do  {   if(KeyCompare(headerKey[index], key)) return true;
                    index++;
                } while(index < headerKey.Length);
            return false;
        }
        
        // =====================================================================
        // Accessor functions
        // =====================================================================
        
        /// <value>Returns the number of header fields</value>
        public uint HeaderCount        
        {   get {   if(headerKey == null) return 0;
                    return (uint)headerKey.Length;
                }
        }

        /// <value>Returns the number of body lines</value>
        public uint BodyCount        
        {   get {   if(bodyLines == null) return 0;
                    return (uint)bodyLines.Length;
                }
        }
        
        /// <value>Returns the RCF 2047 decoded "From" header field value</value>
        public string From
        {   get {   if(posFrom == uint.MaxValue) return "";
                    return FieldText((int)posFrom);
                }
        }
        
        /// <value>Returns the RCF 2047 decoded "To" header field value</value>
        public string To
        {   get {   if(posTo == uint.MaxValue) return "";
                    return FieldText((int)posTo);
                }
        }
        
        /// <value>Returns the "Date" header field value in IMAP format</value>
        public string DateIMap
        {   get {   if(posDate == uint.MaxValue) return "";
                    return FieldValue((int)posDate);
                }
        }
        
        /// <value>Returns the "Date" header field value as <see cref="DateTime"/></value>
        public DateTime DateBinary
        {   get {   return DecodeTime(DateIMap, false);
                }
        }

        /// <value>Returns the "Date" header field value in ISO format</value>
        public string DateISO
        {   get {   DateTime dt = DateBinary;
                    if(dt == DateTime.MinValue) return "";
                    return dt.ToString("yyyy/MM/dd HH:mm:ss");
                }
        }

        /// <value>Returns the RCF 2047 decoded "Subject" header field value</value>
        public string Subject
        {   get {   if(posSubject == uint.MaxValue) return "";
                    return FieldText((int)posSubject);
                }
        }

        // =====================================================================
        // Static helpers
        // =====================================================================
        
        /// <summary>
        /// Get a &lt;CR&gt;&lt;LF&gt; delimited line from a byte buffer.
        /// </summary>
        /// <param name="buffer">
        /// The input buffer.
        /// </param>
        /// <param name="position">
        /// Start index, gets updated before return (then points to next line).
        /// </param>
        /// <param name="ignoreX">
        /// Ignore lines starting with "x-" or "X-" if set.
        /// </param>
        /// <returns>
        /// An array containing the line (without the &lt;CR&gt;&lt;LF&gt;).
        /// </returns>
        /// <remarks>
        /// This routine might be faster than a TextStream based code.  If the
        /// last line has no &lt;CR&gt;&lt;LF&gt; it is still accepted and added
        /// to the output.
        /// </remarks>        
        public static byte[] LineOfBytes(byte[] buffer, ref uint position, bool ignoreX)
        {   if(buffer == null) return null;
            uint uend = (uint)buffer.Length;
            if(uend == 0 || position >= uend) return null;
            uend--;
            
            for(uint irun=position; irun <= uend; irun++)
            {   // check for line break or end of buffer
                if(buffer[irun] != CR) continue;
                if(irun < uend && buffer[irun+1] != LF) continue;
                
                // ok irun points to <cr> of <cr><lf>
                uint ulen = irun - position;            // size of line
                if(ignoreX && ulen >= 2 && buffer[position+1] == '-' &&
                   (buffer[position] == 'x' || buffer[position] == 'X'))
                {   position = irun + 2;                // ignore this
                    while(position < uend)              // find continuations
                    {   if(buffer[position] == CR || buffer[position] > SPACE) break;
                        if(LineOfBytes(buffer, ref position, false) == null) break;
                        irun = position - 1;                       
                    }
                    continue;
                }
                
                // return a line
                byte[] data = new byte[ulen];
                if(ulen > 0) Array.Copy(buffer, position, data, 0, ulen);
                position = irun + 2;                    // next line
                return data;                
            }
            return null;
        }

        /// <summary>
        /// Do a case insensitive ASCII comparison
        /// </summary>
        /// <param name="key">
        /// Key to be compared against a string.
        /// </param>
        /// <param name="val">
        /// The string to be matched
        /// </param>
        /// <returns>
        /// <c>true</c> is key and val are equal.
        /// </returns>
        /// <remarks>
        /// This only works if val contains only 8bit characters. The
        /// upper 8bit of unicode chars get truncated and will cause
        /// wrong results!
        /// </remarks>
        public static bool KeyCompare(byte[] key, string val)
        {   if(key == null || val == null) return false;
            if(key.Length != val.Length) return false;
            for(int irun=0; irun < val.Length; irun++)
            {   byte bval = (byte)val[irun];
                byte kval = key[irun];
                if(bval == kval) continue;
                if(bval >= 'a' && bval <= 'z') bval -= SPACE;
                if(kval >= 'a' && kval <= 'z') kval -= SPACE;
                if(bval != kval) return false;
            }
            return true;
        }
        
        
        /// <summary>
        /// Checks if a buffer ends with an empty line 
        /// </summary>
        /// <param name="data">
        /// Buffer to be checked, <c>null</c> is ok.
        /// </param>
        /// <returns>
        /// Returns <c>true</c> if two consecutive CR/LF sequence were found.
        /// </returns>
        public static bool EndsWithEmptyLine(byte[] data)
        {   if(data == null || data.Length < 4) return false;
            uint upos = (uint)data.Length - 4;
            return (data[upos]   == CRLF[0] && data[upos+1] == CRLF[1] &&
                    data[upos+2] == CRLF[0] && data[upos+3] == CRLF[1]);
        }
        
        /// <summary>
        /// Extract the URL or textual part of an E-Mail address.
        /// </summary>
        /// <param name="address">
        /// A <see cref="System.String"/>
        /// </param>
        /// <param name="url">
        /// A <see cref="System.Boolean"/>
        /// </param>
        /// <returns>
        /// A <see cref="System.String"/>
        /// </returns>
        public static string AddressParse(string address, bool url)
        {
            if(!url)
            {   ZIMapException.Throw(null, ZIMapException.Error.NotImplemented, "AddressParse for non-url");
                return null;
            }
            
            // blabla <x@x>
            // x@y (blabla)
            if(address == null || address.Length < 3) return address;
            int ilas = address.Length - 1;
            if(address[ilas] == ')')
            {   int isep = address.LastIndexOf('(');
                if(isep < 2) return address;
                if(address[isep-1] == ' ') isep--;
                address = address.Substring(0, isep);
            }
            else if(address[ilas] == '>')
            {   int isep = address.LastIndexOf('<');
                if(isep < 0) return address;
                address = address.Substring(isep+1, ilas-(isep+1));
            }
            else 
                return address;
            return address.Replace(' ', '§');       // must not contain spaces
        }
        
        // =====================================================================
        // Static methods for Time conversion
        // =====================================================================
        /* Testing DateTime conversions ...
         *  
        DateTime loc = DateTime.Now;            
        DateTime utc = DateTime.UtcNow;
        Console.WriteLine(ZIMapRfc822.EncodeTime(loc, false) + " " + loc.Kind + " " + loc.Hour);
        Console.WriteLine(ZIMapRfc822.EncodeTime(utc, true) + " " + utc.Kind + " " + utc.Hour);
        loc = ZIMapRfc822.DecodeTime("Sun, 18 May 2008 11:20:06 +0200", false);
        utc = ZIMapRfc822.DecodeTime("Sun, 18 May 2008 11:20:06 +0200", true);
        Console.WriteLine(ZIMapRfc822.EncodeTime(loc, false) + " " + loc.Kind + " " + loc.Hour);
        Console.WriteLine(ZIMapRfc822.EncodeTime(utc, true) + " " + utc.Kind + " " + utc.Hour);
        */
        
        /// <summary>
        /// Converts an RFC 2822 formatted time string to <see cref="DateTime"/>.
        /// </summary>
        /// <param name="rfc822time">
        /// A string in the format: "Sun, 18 May 2008 11:20:06 +0200 (XXX)"
        /// </param>
        /// <param name="toUTC">
        /// If <c>true</c>  set the DateTime structure to UTC.
        /// </param>
        /// <returns>
        /// A <see cref="DateTime"/> value.
        /// </returns>
        /// <remarks> 
        /// The DateTime structure internally keeps track of UTC versus local time.
        /// The RFC time is always local time.
        /// </remarks>
        public static DateTime DecodeTime(string rfc822time, bool toUTC)
        {   if(string.IsNullOrEmpty(rfc822time)) return DateTime.MinValue; 
            DateTime res;
            int idx = rfc822time.IndexOf('(');
            if(idx > 0) rfc822time = rfc822time.Substring(0, idx);
            if(!DateTime.TryParse(rfc822time, out res))
                return DateTime.MinValue;
            return toUTC ? res.ToUniversalTime() : res;
        }
        
        /// <summary>
        /// Convert a <see cref="DateTime"/> value to a RFC 2822 time string.
        /// </summary>
        /// <param name="time">
        /// A <see cref="DateTime"/> value.
        /// </param>
        /// <param name="fromUTC">
        /// If <c>true</c> the DataTime value is forcibly interpreted as UTC.
        /// </param>
        /// <returns>
        /// A string in the format:  "Sun, 18 May 2008 11:20:06 +0200"
        /// </returns>
        /// <remarks> 
        /// The DateTime structure internally keeps track of UTC versus local time.
        /// The RFC time is always local time.
        /// </remarks>
        public static string EncodeTime(DateTime time, bool fromUTC)
        {   if(fromUTC) time = time.ToLocalTime();
            string ts = time.ToString("ddd, d MMM yyyy hh:mm:ss zzz",
                System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat);
            int col = ts.LastIndexOf(':');
            if(col > 0) ts = ts.Remove(col, 1);
            return ts;
        }

        // =====================================================================
        // BodyInfo
        // =====================================================================
        
        /// <summary>
        /// Information about a constituent part of a message body, see <see cref="BodyInfo"/>.
        /// </summary>
        public class BodyPart
        {   public string       Part;
            public uint         Level;
            public bool         Alternative;
            public bool         Related;
            public uint         Size;
            public string       Type;
            public string       Subtype;
            public string       Charset;
            public string       Filename;
            public string       ID;
            public string       Description;
            public string       Encoding;
            
            public override string ToString()
            {   StringBuilder sb = new StringBuilder();
                sb.AppendFormat("Level {0} Part: '{1}/{2}'  ", Level, Type, Subtype);
                if(Charset     != null) sb.AppendFormat("Charset '{0}'  ", Charset);
                if(Filename    != null) sb.AppendFormat("Filename '{0}'  ", Filename);
                if(ID          != null) sb.AppendFormat("ID '{0}'  ", ID);
                if(Description != null) sb.AppendFormat("Descr. '{0}'  ", Description);
                sb.AppendFormat("Size '{0}/{1}'  ", Size, Encoding);
                if(Alternative) sb.Append("ALTERNATIVE ");
                if(Related)     sb.Append("RELATED ");
                return sb.ToString();
            }
        }

        /// <summary>
        /// Information about a message body, returned from <see cref="ParseBodyInfo"/>
        /// </summary>
        public class BodyInfo
        {   public string       Info;
            public BodyPart[]   Parts;
            public string[]     Other;
            
            public bool         IsValid()   {   return Parts != null;  }
        }

        public static BodyInfo ParseBodyInfo(string partList)
        {   if(string.IsNullOrEmpty(partList)) return null;
            ZIMapParser parser = new ZIMapParser(partList);
            if(parser.Length <= 0 || parser[0].Type != ZIMapParser.TokenType.List) return null;
            
            BodyInfo info = new BodyInfo();
            info.Info = partList;
            List<BodyPart> plis = new List<BodyPart>();
            List<string> pother = new List<string>();
            ParseBodyInfo(plis, pother, parser[0], 0);
            if(plis.Count   > 0) info.Parts = plis.ToArray();
            if(pother.Count > 0) info.Other = pother.ToArray();
            return info;
        }
        
        private static void ParseBodyInfo(List<BodyPart> plis, List<string> pother,
                                          ZIMapParser.Token list, uint level)
        {   if(list.Type != ZIMapParser.TokenType.List)
            {   string text = list.Text.ToUpper();
                if(text == "ALTERNATIVE")
                {   for(int irun=0; irun < plis.Count; irun++)
                        if(plis[irun].Level != level) continue;
                        else plis[irun].Alternative = true;
                }
                if(text == "RELATED")
                {   for(int irun=0; irun < plis.Count; irun++)
                        if(plis[irun].Level != level) continue;
                        else plis[irun].Related = true;
                }
                else 
                {   //Console.WriteLine("{0} Final: {1}", level, list.Text);
                    pother.Add(list.Text);
                }
                return;
            }

            uint ulen = (uint)list.List.Length;
            if(ulen >= 7 && list.List[2].Type == ZIMapParser.TokenType.List)
            {   plis.Add(ParseBodyInfo(list, level));
                return;
            }
            
            foreach(ZIMapParser.Token tok in list.List)
                ParseBodyInfo(plis, pother, tok, level+1);
        }

        /// <summary>
        /// Parse body part information
        /// </summary>
        /// <param name="partInfo">
        /// A list that contains at least 7 tokens (not checked - DANGER)
        /// </param>
        /// <param name="level">
        /// The nesting level.
        /// </param>
        private static BodyPart ParseBodyInfo(ZIMapParser.Token partInfo, uint level)
        {   ZIMapParser.Token[] list = partInfo.List;
            BodyPart part = new BodyPart();
            part.Part    = partInfo.Text;
            part.Level   = level;
            
            // 0 := Type
            part.Type    = list[0].QuotedText;
            // 1 := Subtype
            part.Subtype = list[1].QuotedText;
            
            // 2 := body parameter list (value pairs) ...
            if(list[2].Type == ZIMapParser.TokenType.List)
            {   ZIMapParser.Token[] pars = list[2].List;
                for(int ipar=0; ipar+1 < pars.Length; ipar+=2)
                    if(pars[ipar].Text.ToUpper() == "CHARSET")  
                        part.Charset = pars[ipar+1].Text;
                    else
                        part.Filename = pars[ipar+1].Text;
            }
            // 3 := id
            part.ID = list[3].QuotedText;
            // 4 := desciption
            part.Description = list[4].QuotedText;
            // 5 := encoding
            part.Encoding = list[5].QuotedText;
            
            // 6 := size
            if(list[6].Type == ZIMapParser.TokenType.Number)
                part.Size = list[6].Number;
            return part;
        }
    }
}
