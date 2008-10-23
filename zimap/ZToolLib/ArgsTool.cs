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
    /// Class to parse command lines and to format help/usage output.
    /// </summary>
    /// <remarks>
    /// The class implements some often used functionality.  Options can be given in
    /// Windows style like <c>/myvalue=7</c> or in Linux style like <c>-myvalue:7</c>.
    /// </remarks>
    public static class ArgsTool
    {
        /// <summary>Usually this will contain the application name</summary>
        public static string AppName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;

        /// <summary>
        /// Provides information on the validity of an option.   
        /// </summary>
        /// <remarks>
        /// This enumeration is used by <see cref="Option"/>.  Negative return values
        /// from <see cref="Find"/> match the values of this enumeration.
        /// </remarks>
        public enum OptionStatus
        {
            /// <summary>The option is valid</summary>
            OK = 0,
            /// <summary>Undefined, the given string matched no option name</summary>
            Undefined = -1,
            /// <summary>The given string matches more than one option name</summary>
            Ambiguous = -2,
            /// <summary>Invalid use of an option (the value argument is missing)</summary>
            Invalid = -3
        }

        /// <summary>
        /// A structure that represents option arguments with value and sub-value.
        /// </summary>
        /// <remarks>An <see cref="Option"/> array is returned be
        /// <see cref="Parse(string[], string[])"/>
        /// </remarks>
        public struct Option
        {   /// <summary>The option name</summary>
            public string       Name;
            /// <summary>The option value or <c>null</c> for no value</summary>
            public string       Value;
            /// <summary>Array of option sub-values or <c>null</c> for no sub-values</summary>
            public string[]     SubValues;
            /// <summary>Index in the options[] array returned from
            /// <see cref="Parse(string[], string[])"/>.</summary>
            public uint         Index;
            /// <summary>Status potentially indicating an error.</summary>
            public OptionStatus Error;
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
        /// Return values <c>&lt;= 0</c> can be interpreted as <see cref="OptionStatus"/>
        /// </remarks>
        public static int Find(string[] options, string name)
        {   int offset = (int)OptionStatus.Undefined;
            if(options != null && !string.IsNullOrEmpty(name)) 
                for(int irun=0; irun+2 < options.Length; irun+=3)
                {   if(string.IsNullOrEmpty(options[irun])) continue;
                    if(options[irun].StartsWith(name))
                    {   if(offset >= 0) return (int)OptionStatus.Ambiguous;
                        offset = irun;
                    }
                }
            return offset;
        }
        
        /// <summary>
        /// Creates a list of options for a program's help/usage output.
        /// </summary>
        /// <param name="options">
        /// An array of three strings per option line.
        /// </param>
        /// <param name="prefix">
        /// An optional prefix for the first line (can be <c>null</c> or empty).
        /// All following lines are right indented by the prefix length. 
        /// </param>
        /// <returns>
        /// A string that can contain multiple lines.  The last (or only) line
        /// does not end with a line break.
        /// </returns>
        /// <remarks>This function works together with <see cref="Param"/> in order
        /// to simplify the implementation of a help or usage function in applications.
        /// <para />
        /// The three entries per option in <paramref name="options"/> are:
        /// <list type="table">
        /// <listheader>
        /// <term>index</term><description>content</description>
        /// </listheader>
        /// <item><term>0</term><description>The name of the option.  If this field is
        ///       empty or <c>null</c> the <see cref="List"/> function skips this entry.
        ///       </description></item>
        /// <item><term>1</term><description>If not empty or <c>null</c> this is the name
        ///       of the argument value (used by <see cref="Param"/></description>).</item>
        /// <item><term>2</term><description>Descriptive text for <see cref="List"/>.
        ///       </description></item>
        /// <item><term>...</term><description>Next option name and so on.</description>
        ///       </item>
        /// </list>
        /// </remarks>
        public static string List(string[] options, string prefix)
        {   if(options == null) return "";
            int indent = -1;
            StringBuilder sb = new StringBuilder();
            for(int irun=0; irun+2 < options.Length; irun+=3)
            {   if(irun > 0) sb.AppendLine();
                if(!string.IsNullOrEmpty(options[irun])) 
                {   if(indent < 0)
                    {   indent = prefix == null ? 0 : prefix.Length;
                        if(indent > 0) sb.Append(prefix);
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
        /// Formats a single option help like: <c>[-myoption:{value}]</c>
        /// </summary>
        /// <param name="options">
        /// An array of three strings per option, see <see cref="Usage"/>.
        /// </param>
        /// <param name="name">
        /// The name to be searched in <paramref name="options"/>.
        /// </param>
        /// <param name="optional">
        /// Enclose the output in <c>[]</c> brackets.
        /// </param>
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

		/// <summary>
		/// Parse the command line arguments of an application.
		/// </summary>
		/// <param name="options">
        /// An array of three strings per option, see <see cref="Usage"/>.
		/// </param>
		/// <param name="argv">
		/// The argument array of the Main function for example.
		/// </param>
		/// <param name="extra">
		/// Returns an array of non-option arguments (can be <c>null</c>).
		/// </param>
		/// <param name="allowExtra">
		/// Allow extra arguments.  If <c>false</c> extra arguments will cause
		/// the parse function to return in error.
		/// </param>
		/// <returns>
		/// An array of <see cref="Option"/> items on success or <c>null</c>
		/// on error (see <paramref name="allowExtra"/>).
		/// </returns>
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

		/// <summary>
		/// Parse the command line arguments of an application.
		/// </summary>
		/// <param name="options">
        /// An array of three strings per option, see <see cref="Usage"/>.
		/// </param>
		/// <param name="argv">
		/// The argument array of the Main function for example.
		/// </param>
		/// <param name="extraArgs">
		/// Returns an array of non-option arguments.
		/// </param>
		/// <returns>
		/// An array of <see cref="Option"/> items on success or <c>null</c> on error.
		/// </returns>
        public static Option[] Parse(string[] options, string[] argv, out string[] extraArgs)
        {   return Parse(options, argv, out extraArgs, true);
        }
        
		/// <summary>
		/// Parse the command line arguments of an application.
		/// </summary>
		/// <param name="options">
        /// An array of three strings per option, see <see cref="Usage"/>.
		/// </param>
		/// <param name="argv">
		/// The argument array of the Main function for example.
		/// </param>
		/// <returns>
		/// An array of <see cref="Option"/> items on success or <c>null</c> on error.
		/// </returns>
		/// This overload does not allow extra arguments.  Any non-option argument
		/// will make the function fail (e.g. returning <c>null</c>).
        public static Option[] Parse(string[] options, string[] argv)
        {   string[] extra;
            Option[] opts = Parse(options, argv, out extra, false);
            return (extra == null) ? opts : null;
        }

        /// <summary>
        /// Controls how <see cref="Usage"/> will format the output. 
        /// </summary>
        public enum UsageFormat
        {   /// <summary>Prefix with 'Usage:  ' and program name</summary>
            Usage,
            /// <summary>Prefix only with spaces (continuation line)</summary>
            Cont,
            /// <summary>Prefix with spaces and program name</summary>
            More,
            /// <summary>Output an option list, see <see cref="List"/></summary>
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
        /// <b>Example:</b>
		/// <code>
        /// private static string[] options = {
        ///     "server",   "host",     "Connect to a server at {host}",
        ///     "protocol", "name",     "Use the protocol {name}        (default: imap)",
        ///     "account",  "user",     "Login using the {user} account",
        ///     "password", "text",     "Use the login password {text}",
        ///     "timeout",  "seconds",  "Connection/Read/Write timeout  (default: 30)",
        ///     "ascii",    "",         "Do not use line drawing chars or colors",
        ///     "debug",    "",         "Output debug information",
        ///     "help",     "",         "Print this text and quit",
        /// };
        /// 
        /// public static void Usage()
        /// {   Console.WriteLine(ArgsTool.Usage(ArgsTool.UsageFormat.Usage,
        ///                   ArgsTool.Param(options, "server",  false),
        ///                   ArgsTool.Param(options, "protocol", true),
        ///                   ArgsTool.Param(options, "account",  true)));
        ///     Console.WriteLine(ArgsTool.Usage(ArgsTool.UsageFormat.Cont,
        ///                   ArgsTool.Param(options, "password", true),
        ///                   ArgsTool.Param(options, "timeout",  true),
        ///                   ArgsTool.Param(options, "ascii",    true),
        ///                   ArgsTool.Param(options, "debug",    true)));
        ///     Console.WriteLine(ArgsTool.Usage(ArgsTool.UsageFormat.More, "-help"));
        ///     Console.WriteLine("\n{0}\n",
        ///                   ArgsTool.Usage(ArgsTool.UsageFormat.Options, options));
        /// }
		/// </code>
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
                return List((string[])pars, "Options: ");
            
            sb.Append(' ');
            foreach(object o in pars) sb.Append(o);
            return sb.ToString();
        }
    }
}
