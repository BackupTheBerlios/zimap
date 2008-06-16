//==============================================================================
// ZIMapConverter.cs implements the ZIMapConverter class
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU Lesser General Public License
// This software is published under the GNU LGPL license. Please refer to the
// files COPYING and COPYING.LESSER for details. Please use the following e-mail
// address to contact me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.Text;

namespace ZIMap
{
    //==========================================================================
    // ZIMapConverter
    //==========================================================================

    /// <summary>
    /// This class provides some static methods to provide string conversions
    /// that are needed to implement IMap4Rev1
    /// </summary>
    public static class ZIMapConverter 
    {
        private static byte[] base64_encode =
        {
          (byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E', (byte)'F', (byte)'G',
          (byte)'H', (byte)'I', (byte)'J', (byte)'K', (byte)'L', (byte)'M', (byte)'N',
          (byte)'O', (byte)'P', (byte)'Q', (byte)'R', (byte)'S', (byte)'T', (byte)'U', 
          (byte)'V', (byte)'W', (byte)'X', (byte)'Y', (byte)'Z',
          (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f', (byte)'g',
          (byte)'h', (byte)'i', (byte)'j', (byte)'k', (byte)'l', (byte)'m', (byte)'n',
          (byte)'o', (byte)'p', (byte)'q', (byte)'r', (byte)'s', (byte)'t', (byte)'u', 
          (byte)'v', (byte)'w', (byte)'x', (byte)'y', (byte)'z',
          (byte)'0', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', 
          (byte)'6', (byte)'7', (byte)'8', (byte)'9', (byte)'+', (byte)','
        };
        
        private static byte[] base64_decode;

        /// <summary>
        /// Convert the input to a quoted string.
        /// </summary>
        /// <param name="result">
        /// The quoted string
        /// </param>
        /// <param name="input">
        /// Text to convert
        /// </param>
        /// <param name="allow8bit">
        /// Allow control chars (&lt; 0x20) and 8-bit chars (&gt; 0xfe)
        /// </param>
        /// <returns>
        /// <c>true</c> on success. If an invalid char was detected <c>false</c> is
        /// returned.
        /// </returns>
        /// <remarks>
        /// The upper 8-bits of a character are ignored, even if <c>allow8bit</c> is set.
        /// Inplace conversion is allowed, e.g. result and input can be the same variable.
        /// </remarks>
        public static bool QuotedString(out string result, string input, bool allow8bit)
        {   if(input == null)
            {   result = null; return false;
            }
            StringBuilder sb = new StringBuilder();
            bool bok = QuotedString(sb, input, allow8bit);
            result = sb.ToString();
            return bok;
        }
        
        /// <summary>
        /// Convert the input to a quoted string.
        /// </summary>
        /// <param name="result">
        /// The quoted string
        /// </param>
        /// <param name="input">
        /// Text to convert
        /// </param>
        /// <param name="allow8bit">
        /// Allow control chars (&lt;0x20) and 8-bit chars (&gt;0xfe)
        /// </param>
        /// <returns>
        /// <c>true</c> on success. If an invalid chars were detected <c>false</c> is
        /// returned.
        /// </returns>
        /// <remarks>
        /// If <c>allow8bit</c> is not set all illegal characters are replaced by a
        /// question mark.
        /// </remarks>
        public static bool QuotedString(StringBuilder result, string input, bool allow8bit)
        {   if(input == null || result == null) return false;
            result.Append('"');
            bool isText = true;
            int irun = 0;
            
            while(irun < input.Length)
            {   char c = input[irun++];
                if(c == '"' || c == '\\')               // escape '"' and '\'
                    result.Append('\\');
                if(c < 0x20 || c > 0x7e)                // special chars
                {   isText = false;
                    if(!allow8bit) c = '?';
                }
                result.Append(c);
            }
                    
            result.Append('"');
            return allow8bit ? true : isText;
        }

        /// <summary>
        /// Check if a text is printable 7-bit (US-ASCII) data.
        /// </summary>
        /// <param name="input">
        /// The text to be checked.
        /// </param>
        /// <returns>
        /// <c>true</c> on success (e.g. if the input contains no controls chars
        /// and no 8-bit chars.
        /// </returns>
        public static bool Check7BitText(string input)
        {   if(input == null || input == "") return true;
            int irun = 0;
            
            while(irun < input.Length)
            {   char c = input[irun++];
                if(c < 0x20 || c > 0x7e)                // special chars
                    return false;
            }
            return true;
        }

        public static uint HexCharToUint(char hex)
        {   if(hex >= '0' && hex <= '9') return (uint)hex - (uint)'0';
            if(hex >= 'A' && hex <= 'F') return (uint)hex - (uint)'A' + 10;
            if(hex >= 'a' && hex <= 'f') return (uint)hex - (uint)'f' + 10;
            return 0xffff;
        }
        
        public static string ConvertToUnicode(string charset, string rawtext)
        {   if(rawtext == null || rawtext == "") return rawtext;
            StringBuilder sb = new StringBuilder();
            if(!ConvertToUnicode(charset, false, rawtext, sb))
                return rawtext;
            return sb.ToString();
        }

        // ConvertToUnicode caches to last decoder...
        private static string  last_charset;
        private static Decoder last_decoder;
        
        public static bool ConvertToUnicode(string charset, bool base64, string rawtext, StringBuilder result)
        {   if(rawtext == null || rawtext == "")
            {   return true;
            }
            try
            {   byte[] bytes;
                char[] chars;
                
                // make byte array for decoder and allocate char array
                if(base64)
                {   bytes = Convert.FromBase64String(rawtext);
                    chars = new char[bytes.Length];
                }
                else
                {   chars = rawtext.ToCharArray();
                    bytes = new byte[chars.Length];
                    for(int irun=0; irun < chars.Length; irun++)
                    {   byte c = (byte)chars[irun];
                        bytes[irun] = (c == (byte)'_') ? (byte)32 : c;
                    }
                }
                
                // normalize charset name
                if(charset == null || charset == "")
                    charset = "iso-8859-1";
                else
                    charset = charset.Trim().ToLower();
                
                // HACK: for mono
                if(System.IO.Path.DirectorySeparatorChar == '/')
                    if(charset == "windows-1252" || charset == "iso-8859-15")
                        charset = "iso-8859-1";

                // Reuse a previous decoder?
                Decoder dec;
                if(last_charset == charset)
                {   dec = last_decoder;
                    if(dec == null) return false;
                    dec.Reset();
                }

                // Get a new decoder (may throw exception) ...
                else
                {   string[] cset = charset.Split("-".ToCharArray(), 2);
                    int cp = 0;
                    if(cset.Length == 2 && cset[0].ToLower() == "windows")
                        int.TryParse(cset[1], out cp);
                    Encoding e = (cp == 0) ? Encoding.GetEncoding(charset) :
                                             Encoding.GetEncoding(cp);
                    dec = e.GetDecoder();
                    last_decoder = dec;
                    last_charset = charset;
                }
                
                // and do the conversion ...
                int ires = dec.GetChars(bytes, 0, bytes.Length, chars, 0);
                result.Append(chars, 0, ires);
            }
            catch
            {   return false;
            }
            return true;
        }

        public static string DecodeRfc2047Text(string rawtext)
        {   if(rawtext == null || rawtext == "") return rawtext;
            int start = rawtext.IndexOf("=?");
            if(start < 0) return rawtext;
            StringBuilder text = new StringBuilder(rawtext.Substring(0, start));
            StringBuilder word = new StringBuilder();
            string charset = "";
            bool base64 = false;
            int state = 1; start += 2;
            while(start < rawtext.Length)
            {   char c = rawtext[start++];
                if(state == 1)                          // getting charset
                {   if(c == '?')
                    {   charset = word.ToString(); word.Length = 0;
                        state = 2;
                    }
                    else
                        word.Append(c);
                }
                else if(state == 2)                     // getting encoding
                {   if(c == '?')
                    {   if(word.Length > 0)   
                        {   base64 = (word[0] == 'b' || word[0] == 'B');
                            word.Length = 0;
                        }
                        else
                            base64 = false;
                        state = 3;
                    }
                    else
                        word.Append(c);
                }
                else if(state == 3)                     // the text
                {   if(c == '?' && start < rawtext.Length && rawtext[start] == '=')
                    {   if(!ConvertToUnicode(charset, base64, word.ToString(), text))
                            break;
                        state = 0; start++; word.Length = 0;
                    }
                    else if(!base64 && c == '=' && start+1 < rawtext.Length)
                    {   uint code = (HexCharToUint(rawtext[start]) << 4) + HexCharToUint(rawtext[start+1]);
                        if(code > 0xff) return rawtext;
                        word.Append((char)code);
                        start += 2;
                    }
                    else
                        word.Append(c);
                }
                
                // state 0 - collect normal text
                else if(c == '=' && start < rawtext.Length && rawtext[start] == '?')
                {   start++; state = 1;
                }
                else if(c == '_')
                    text.Append(' ');
                else if(c < ' ')
                    text.Append(' ');
                else
                    text.Append(c);
            }
            
            if(state != 0)
            {
                //Console.WriteLine("**** Converion error: {0}: {2}: {1}", state, rawtext, charset); 
                return rawtext;              // bad format, ignore
            }
            return text.ToString();
        }
        
        /// <summary>
        /// Converts a string to a RFC3501 modified Base64 encoding
        /// </summary>
        /// <param name="result">
        /// The encoded string
        /// </param>
        /// <param name="input">
        /// The string to be encoded
        /// </param>
        /// <returns>
        /// <c>true</c> if a conversion took place -or-
        /// <c>false</c> if the text remains unchanged
        /// </returns>
        /// <remarks>
        /// Inplace conversion is allowed, e.g. result and input can be the same variable.
        /// </remarks>
        public static bool MailboxEncode(out string result, string input)
        {   if(input == null)
            {   result = null;  return false;
            }
            
            result = input;            
            bool converted = false;
            StringBuilder sb = null;
            int irun = 0;
            while(irun < input.Length)
            {   char c = input[irun];
                if(c < 0x20 || c == '&' || c > 0x7e)    // need conversion...
                {   converted = true;
                    sb = new StringBuilder(input.Substring(0, irun));
                    break;
                }
                irun++;
            }
            if(!converted) return false;                // return original

            // run the encoder ...
            bool modeAscii = true;
            char [] block = new char[3];
            int ncvt = 0;
            
            while(irun < input.Length)
            {   char c = input[irun++];
                if(modeAscii)
                {   if(c == '&')
                    {   sb.Append("&-");
                        continue;
                    }
                    if(c >= 0x20 && c <= 0x7e)
                    {   sb.Append(c);
                        continue;
                    }
                    sb.Append('&');
                    modeAscii = false;
                    ncvt = 1; block[0] = c;
                    block[1] = block[2] = (char)0;
                }
                
                // switch base64 back to ASCII
                else if(c >= 0x20 && c <= 0x7e)
                {   CharsToBase64(sb, block, true);
                    sb.Append(c);
                    modeAscii = true;
                }
                
                // another char fits into the block...
                else if(ncvt < 3)
                {   block[ncvt++] = c;
                }
                
                // flush the block and continue in Base64 mode
                else
                {   CharsToBase64(sb, block, false);
                    ncvt = 1; block[0] = c;
                    block[1] = block[2] = (char)0;
                }
            }
            
            // flush if still in Base64 mode ...
            if(ncvt > 0 && !modeAscii)
                CharsToBase64(sb, block, true);
            result = sb.ToString();                     // return consersion
            return true;
        }

        // Unicode chars to Base64 (3 chars end up in 8 bytes)
        private static void CharsToBase64(StringBuilder sb, char[] input, bool final)
        {   byte [] output = new byte[8];
            byte hig0, hig1, hig2, low0, low1, low2;

            hig0 = (byte)(input[0] >> 8);
            low0 = (byte)(input[0]);
            hig1 = (byte)(input[1] >> 8);
            low1 = (byte)(input[1]);
            hig2 = (byte)(input[2] >> 8);
            low2 = (byte)(input[2]);
            
            output[0] = base64_encode[ hig0 >> 2 ];
            output[1] = base64_encode[ ((hig0 &  3) << 4) + (low0 >> 4) ];
            output[2] = base64_encode[ ((low0 & 15) << 2) + (hig1 >> 6) ];
            output[3] = base64_encode[ hig1 & 63 ];
            
            output[4] = base64_encode[ low1 >> 2 ];
            output[5] = base64_encode[ ((low1 &  3) << 4) + (hig2 >> 4) ];
            output[6] = base64_encode[ ((hig2 & 15) << 2) + (low2 >> 6) ];
            output[7] = base64_encode[ low2 & 63 ];

            // remove padding bytes ...
            int ifin = 7;
            while(ifin > 0 && output[ifin] == base64_encode[0]) ifin--;
            int iout = 0;
            while(iout <= ifin) sb.Append((char)output[iout++]);
            if(final)
                sb.Append('-');
        }
        
        /// <summary>
        /// Converts a RFC3501 modified Base64 encoding to a string
        /// </summary>
        /// <param name="text">
        /// The string to be converted
        /// </param>
        /// <param name="converted">
        /// Flags if a conversion took place or if the text stays unchanged
        /// </param>
        /// <returns>
        /// The converted or original string
        /// </returns>
        public static string MailboxDecode(string text, out bool converted)
        {   converted = false;
            StringBuilder sb = null;
            int irun = 0;
            while(irun < text.Length)
            {   if(text[irun] == '&')       // shift-out flags conversion
                {   converted = true;
                    sb = new StringBuilder(text.Substring(0, irun));
                    break;
                }
                irun++;
            }
            if(!converted) return text;     // return original

            // initialize decoder look-up table ...            
            if(base64_decode == null)
            {   base64_decode = new byte[128];
                for(int iini=0; iini < 64; iini++)
                    base64_decode[ base64_encode[iini] ] = (byte)iini;
            }
            
            // run the decoder ...            
            bool modeAscii = true;
            byte [] block = new byte[8];
            int ncvt = 0;

            while(irun < text.Length)
            {   char c = text[irun++];
                if(modeAscii)                   // in copy mode
                {   if(c == '&' && irun < text.Length)
                    {   if(text[irun] == '-')   // '&-' becomes '&'
                            irun++;
                        else                    // is Base64 ...
                        {   modeAscii = false;
                            ncvt = 0; block.Initialize();
                            continue;
                        }
                    }
                    sb.Append(c);
                }
                else                            // in Base64 mode
                {   if(c== '-')
                    {   Base64ToText(sb, block, true);
                        modeAscii = true;
                    }
                    else if(ncvt < 8)
                        block[ncvt++] = base64_decode[c & 0xff];
                    else
                    {   block.Initialize();
                        Base64ToText(sb, block, false);
                        ncvt = 1;
                        block[0] = base64_decode[c & 0xff];
                    }
                }
            }
            if(ncvt > 0 && !modeAscii)              // flush Base64 buffer
                Base64ToText(sb, block, true);
            return sb.ToString();                   // return conversion
        }

        // Base64 -> Unicode (needs 8 bytes for 1 ... 3 chars)
        private static void Base64ToText(StringBuilder sb, byte[] input, bool final)
        {
            // reassemble octets ...
            byte [] output = new byte[6];
            output[0] = (byte)((input[0] << 2) + (input[1] >> 4));  // 4 more            
            output[1] = (byte)((input[1] << 4) + (input[2] >> 2));  // 2 
            output[2] = (byte)((input[2] << 6) + input[3]);            
            output[3] = (byte)((input[4] << 2) + (input[5] >> 4));  // 4 more            
            output[4] = (byte)((input[5] << 4) + (input[6] >> 2));  // 2 
            output[5] = (byte)((input[6] << 6) + input[7]);
            
            // make chars and store them until one is zero ...
            char cout;
            cout = (char)((output[0] << 8) + output[1]);
            sb.Append((char)cout);
            cout = (char)((output[2] << 8) + output[3]);
            if(cout == 0) return;
            sb.Append((char)cout);
            cout = (char)((output[4] << 8) + output[5]);
            if(cout == 0) return;
            sb.Append((char)cout);
        }

        // =====================================================================
        // Static methods for Time conversion
        // =====================================================================

        private static System.Globalization.DateTimeFormatInfo DTInfo =
            System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat;

        /// <summary>
        /// Convert an RFC 3501 formatted string to <see cref="DateTime"/>.
        /// </summary>
        /// <param name="imapTime">
        /// Date string in the format: "18-May-2008 11:20:06 +0200".
        /// </param>
        /// <param name="toUTC">
        /// When <c>true</c> the result is converted to UTC, oherwise it is
        /// returned as local time.
        /// </param>
        /// <returns>
        /// The conversion result or <see cref="DateTime.MinValue"/> on
        /// error.
        /// </returns>
        public static DateTime DecodeIMapTime(string imapTime, bool toUTC)
        {   DateTime res;
            if(!DateTime.TryParse(imapTime, out res))
                return DateTime.MinValue;
            return toUTC ? res.ToUniversalTime() : res;
        }

        /// <summary>
        /// Format a date in IMap representation (see RFC 3501)
        /// </summary>
        /// <param name="time">
        /// The <see cref="DateTime"/> to be formatted.  The values
        /// <see cref="DateTime.MinValue"/> and <see cref="DateTime.MaxValue"/>
        /// are replaced by <see cref="DateTime.Now"/>.
        /// </param>
        /// <returns>
        /// Date string in the format: "18-May-2008 11:20:06 +0200".
        /// </returns>
        /// <remarks>
        /// Asctime is a legacy UNIX format, the name is taken from the C-library
        /// routine that creates strings in this format.
        /// <para />
        /// If the <see cref="DateTime"/> argument is in UTC (e.g. is of kind
        /// <see cref="DateTimeKind.Unspecified"/>) the time is converted to local
        /// time because the RFC 3501 format uses the local time plus a timezone offset.
        /// </remarks>
        public static string EncodeIMapTime(DateTime time)
        {   if(time == DateTime.MinValue || time == DateTime.MaxValue)
                time = DateTime.Now;
            else if(time.Kind == DateTimeKind.Utc)
                time = time.ToLocalTime();
            string ts = time.ToString("dd-MMM-yyyy HH:mm:ss zzz", DTInfo);
            int col = ts.LastIndexOf(':');
            if(col > 0) ts = ts.Remove(col, 1);
            return ts;
        }

        /// <summary>
        /// Convert an asctime string to <see cref="DateTime"/>.
        /// </summary>
        /// <param name="ascTime">
        /// The string in the format "Fri Jun 13 21:00:21 2008".
        /// </param>
        /// <param name="fromUTC">
        /// For <c>true</c> the input string is assumed to be UTC whereas
        /// <c>false</c> will assume a local time string.
        /// </param>
        /// <returns>
        /// The conversion result or <see cref="DateTime.MinValue"/> on
        /// error.
        /// </returns>
        /// <remarks>
        /// Asctime is a legacy UNIX format, the name is taken from the C-library
        /// routine that creates strings in this format.
        /// </remarks>
        public static DateTime DecodeAscTime(string ascTime, bool fromUTC) 
        {   DateTime res;
            DateTime.TryParseExact(ascTime, "ddd MMM d HH:mm:ss yyyy", DTInfo, 
                fromUTC ? System.Globalization.DateTimeStyles.AssumeUniversal
                        : System.Globalization.DateTimeStyles.AssumeLocal, out res);
            return res;
        }

        /// <summary>
        /// Output <see cref="DateTime"/> as asctime string.
        /// </summary>
        /// <param name="time">
        /// The <see cref="DateTime"/> value to be converted.  The values
        /// <see cref="DateTime.MinValue"/> and <see cref="DateTime.MaxValue"/>
        /// are replaced by <see cref="DateTime.Now"/>.
        /// </param>
        /// <param name="toUTC">
        /// For <c>true</c> the output string will be returned as UTC whereas
        /// <c>false</c> will return a local time string.
        /// </param>
        /// <returns>
        /// The string representation of the time in the format 
        /// "Fri Jun 13 21:00:21 2008".
        /// </returns>
        /// <remarks>
        /// Asctime is a legacy UNIX format, the name is taken from the C-library
        /// routine that creates strings in this format.
        /// <para />
        /// An exception with code <see cref="ZIMapException.Error.InvalidArgument"/>
        /// is thrown if the <see cref="DateTime"/> argument is neither UTC nor
        /// local time (e.g. is of kind <see cref="DateTimeKind.Unspecified"/>).
        /// </remarks>
        public static string EncodeAscTime(DateTime time, bool toUTC)
        {   if(time == DateTime.MinValue || time == DateTime.MaxValue)
                time = DateTime.Now;
            else if(time.Kind == DateTimeKind.Unspecified)
            {   ZIMapException.Throw(null, ZIMapException.Error.InvalidArgument,
                                     "time kind is 'Unspecified'");
                return null;
            }
            time = toUTC ? time.ToUniversalTime() : time.ToLocalTime();
            return time.ToString("ddd MMM d HH:mm:ss yyyy", DTInfo);
        }

        // =====================================================================
        // String arrays
        // =====================================================================
 
        private static string[] emptyStringArray;
        
        public static string[] StringArray(int size)
        {   if(size == 0)
            {   if(emptyStringArray == null) emptyStringArray = new string[0];
                return emptyStringArray;
            }
            return new string[size];
        }

        private static string[] splitArray = new string[] { " " };
        
        /// <summary>
        /// Splits a string of space separated words to an array of words. 
        /// </summary>
        /// <param name="words">
        /// Any text or <c>null</c>.
        /// </param>
        /// <returns>
        /// When the input string was <c>>null</c> a value of <c>>null</c> is
        /// returned, otherwise an array.  The returned array can be empty.
        /// </returns>
        /// <remarks>
        /// Multiple spaces are ignored, this routine does not return empty
        /// strings in the result array.
        /// </remarks>
        public static string[] StringArray(string words)
        {   if(words == null) return null;
            if(words == "")   return StringArray(0);
            return words.Split(splitArray, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}