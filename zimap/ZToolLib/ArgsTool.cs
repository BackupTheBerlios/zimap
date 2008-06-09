//==============================================================================
// ArgsTool.cs implements command line parser
//==============================================================================

#region Copyright 2008 Dr. Jürgen Pfennig  --  GNU Lesser General Public License
// This software is published under the GNU LGPL license. Please refer to the
// files COPYING and COPYING.LESSER for details. Please use the following e-mail
// address to contact me or to send bug-reports:  info at j-pfennig dot de .  
#endregion

using System;
using System.Text;
using System.Collections.Generic;

namespace ZTool
{
    //==========================================================================
    // ArgsTool class - Reading and parsing command lines
    //==========================================================================
    
    /// <summary>
    /// Class to parse command lines.
    /// </summary>
    public class ArgsTool
    {
        /// <value>Usually this will contain the application name</value>
        public static string AppName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;

        /// <summary>
        /// This enumeration provide information on the validity of an option.   
        /// </summary>
        public enum OptionStatus
        {
            /// <value>The option is valid</value>
            OK = 0,
            /// <value>Undefined, given string matched no option name</value>
            Undefined = -1,
            /// <value>Given string matches more than one option name</value>
            Ambiguous = -2,
            /// <value>Invalid use of an option (argument missing)</value>
            Invalid = -3
        }

        /// <summary>
        /// Value representing an option argument.
        /// </summary>
        /// <remarks>An <see cref="Option"/> array is returned be
        /// <see cref="Parse(string[], string[])"/>
        /// </remarks>
        public struct Option
        {   /// <summary>The option name</summary>
            public string       Name;
            public string       Value;
            public string[]     SubValues;
            public uint         Index;  // index in options[] 
            public OptionStatus Error;  // 0 := ok, -1 := undefined, -2 := ambiguous
        }
        
        /// <summary>
        /// Search for an option name.
        /// </summary>
        /// <param name="options">
        /// Option description array. 
        /// </param>
        /// <param name="name">
        /// The name to be searched (can be abbreviated). 
        /// </param>
        /// <returns>
        /// A value <c>&gt;= 0</c> on success, <c>-1</c> if the option name
        /// was not found or <c>-2</c> if the option name was not unique.
        /// </returns>
        /// <remarks>
        /// The search is case sensitive.<para />
        /// Return values <c>&lt;= 0</c>can be interpreted as <see cref="OptionStatus"/>
        /// </remarks>
        public static int Find(string[] options, string name)
        {   if(options == null || string.IsNullOrEmpty(name)) return -1;
            int offset = -1;
            for(int irun=0; irun+2 < options.Length; irun+=3)
            {   if(string.IsNullOrEmpty(options[irun])) continue;
                if(options[irun].StartsWith(name))
                {   if(offset >= 0) return -2;
                    offset = irun;
                }
            }
            return offset;
        }
        
        public static string List(string[] options, string prefix)
        {   if(options == null) return "";
            int indent = -1;
            StringBuilder sb = new StringBuilder();
            for(int irun=0; irun+2 < options.Length; irun+=3)
            {   if(irun > 0) sb.AppendLine();
                if(!string.IsNullOrEmpty(options[irun])) 
                {   if(indent < 0)
                    {   indent = prefix == null ? 0 : prefix.Length;
                        sb.Append(prefix);
                    }
                    else if(indent > 0)
                        sb.Append(' ', indent);
                    sb.Append(options[irun].PadRight(10));
                    sb.Append(' ');
                    sb.Append(options[irun+2]);
                }
            }
            return sb.ToString();            
        }
        
        /// <summary>
        /// Format option help like  [-myoption:{value}]
        /// </summary>
        public static string Param(string[] options, string name, bool optional)
        {   
            StringBuilder sb = new StringBuilder();
            if(optional) sb.Append('[');
            sb.Append('-');
            string argi = null;
            int iopt = Find(options, name);
            if(iopt >= 0)
            {   name = options[iopt];
                argi = options[iopt+1];
            }
            sb.Append(name);
            if(!string.IsNullOrEmpty(argi))               
            {   sb.Append(":{");
                sb.Append(argi);
                sb.Append('}');
            }
            if(optional) sb.Append(']');
            return sb.ToString();
        }
        
        private static Option[] Parse(string[] options, string[] argv, out string[] extra, bool allowExtra)
        {   extra = null;
            if(argv == null) return null;
            
            List<Option> optl = new List<Option>();
            List<string> extl = allowExtra ? new List<string>() : null;
            bool optEnd = false;
            foreach(string arg in argv)
            {   if(arg == null || arg == "") continue;
                char c = arg[0];
                
                // end of options ...
                if(!optEnd && arg == "--")
                {   optEnd = true;
                    Option o = new Option();
                    o.Name = "--";
                    optl.Add(o);
                }
                
                // add an option ...
                else if(!optEnd && arg.Length > 1 && (c == '-' || c == '/'))
                {   string varg = arg.Substring(1);
                    int sepi = varg.IndexOfAny(":=".ToCharArray());
                    Option o = new Option();
                    if(sepi > 0)
                    {   o.Name = varg.Substring(0, sepi);
                        o.Value = varg.Substring(sepi+1);
                        
                        // do we have subvalues?
                        int isub = o.Value.IndexOf(",");
                        if(isub >= 0)
                        {   string subs = o.Value.Substring(isub+1);
                            o.Value = o.Value.Substring(0, isub);
                            if(!string.IsNullOrEmpty(subs))
                                o.SubValues = subs.Split(",".ToCharArray()); 
                        }
                    }
                    else
                        o.Name = varg;
                    if(options != null)
                    {   int index = Find(options, o.Name);
                        if(index < 0)               // error!
                            o.Error = (OptionStatus)index;
                        else    
                        {   o.Name  = options[index];
                            o.Index = (uint)index;
                        }
                    }
                    optl.Add(o);
                }
                
                // handle extra arguments ...
                else if(extl != null)
                    extl.Add(arg);
                else
                    return null;
            }
         
            if(extl != null) extra = extl.ToArray();
            return optl.ToArray();
        }

        public static Option[] Parse(string[] options, string[] argv, out string[] extraArgs)
        {   return Parse(options, argv, out extraArgs, true);
        }
        
        public static Option[] Parse(string[] options, string[] argv)
        {   string[] extra;
            Option[] opts = Parse(options, argv, out extra, false);
            return (extra == null) ? opts : null;
        }

        /// <summary>
        /// Controls how <see cref="Usage"/> will format the output. 
        /// </summary>
        public enum UsageFormat
        {   /// <value>Prefix with 'Usage:  ' and program name</value>
            Usage,
            /// <value>Prefix only with spaces (continuation line)</value>
            Cont,
            /// <value>Prefix with spaces and program name</value>
            More,
            /// <value>Output an option list, see <see cref="List"/></value>
            Options
        }
        
        /// <summary>
        /// Helper to format usage messages for a program.
        /// </summary>
        /// <param name="mode">
        /// Controls what is to be formatted.
        /// </param>
        /// <param name="pars">
        /// Usually an array of strings, see remarks.
        /// </param>
        /// <returns>
        /// The formatted result.
        /// </returns>
        /// <remarks>
        /// The parameter array must be an array of strings when using
        /// <see cref="UsageFormat.Options"/>.  For all other formats
        /// the <see cref="object.ToString"/> gets called.
        /// </remarks>
        public static string Usage(UsageFormat mode, params object[] pars)
        {
            StringBuilder sb = new StringBuilder();
            if(mode == UsageFormat.Usage)
                sb.Append("Usage:   " + ArgsTool.AppName);
            else if(mode == UsageFormat.More)
                sb.Append("         " + ArgsTool.AppName);
            else if(mode == UsageFormat.Cont)
                sb.Append(' ', ArgsTool.AppName.Length + 9);
            else
            {   sb.Append(List((string[])pars, "Options: "));
                return sb.ToString();
            }
            
            sb.Append(' ');
            foreach(object o in pars) sb.Append(o);
            return sb.ToString();
        }
    }
}
