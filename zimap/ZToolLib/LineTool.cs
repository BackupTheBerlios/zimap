//==============================================================================
// LineTool.cs implements command line tools
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
    // LineTool class - Reading and parsing command lines
    //==========================================================================
    
    public static class LineTool
    {
        /// <summary>
        /// Removes leading/trailing spaces, replaces multiple spaces by single
        /// spaces, treats controls chars as spaces.
        /// </summary>
        /// <param name="text">
        /// Input string -or- <c>null</c>
        /// </param>
        /// <returns>
        /// Converted string -or- <c>null</c> (when the input was <c>null</c>).
        /// </returns>
        public static string SimplifyWhiteSpace(string text)
        {   if(text == null || text == "") return text;
            
            StringBuilder sb = new StringBuilder();
            bool skip = true;
            int  last = 0;
            for(int irun=0; irun < text.Length; irun++)
            {   char c = text[irun];
                if(c <= 32)
                {   if(skip) continue;
                    skip = true;
                }
                else
                {   skip = false;
                    last = sb.Length + 1;
                }
                sb.Append(c);
            }
            sb.Length = last;
            return sb.ToString();
        }
        
        /// <summary>
        /// Breaks an input line into words.
        /// </summary>
        /// <param name="line">
        /// The input line -or- <c>null</c>
        /// </param>
        /// <param name="maxFields">
        /// Limit the size of the returned array. Extra input words are
        /// appended to the last array element.
        /// </param>
        /// <returns>
        /// A string array  -or- <c>null</c> (when the input was <c>null</c>). 
        /// </returns>
        /// <remarks>
        /// This routine internally calls <see cref="SimplifyWhiteSpace"/>
        /// </remarks>
        public static string[] Parse(string line, uint maxFields)
        {
            if(line == null) return null;
            else line = SimplifyWhiteSpace(line);
            if(line == "") return null;
            return line.Split(" ".ToCharArray(), (int)maxFields);
        }

        /// <summary>
        /// Prompt on Console for user input.
        /// </summary>
        /// <param name="prompt">
        /// The prompt string.
        /// </param>
        /// <param name="maxFields">
        /// Limit the size of the returned array. Extra input words are
        /// appended to the last array element.
        /// </param>
        /// <returns>
        /// A string array  -or- <c>null</c> (on EOF or an empty input line). 
        /// </returns>
        /// <remarks>
        /// This routine internally calls <see cref="Parse"/>
        /// </remarks>
        public static string[] Prompt(string prompt, uint maxFields)
        {
            Console.Write("{0}> ", prompt);
            return Parse(Console.ReadLine(), maxFields);
        }

        /// <summary>
        /// Prompt on Console for user input.
        /// </summary>
        /// <param name="prompt">
        /// The prompt string.
        /// </param>
        /// <returns>
        /// A string -or- <c>null</c> (on EOF or an empty input line). 
        /// </returns>
        /// <remarks>
        /// This routine internally calls <see cref="SimplifyWhiteSpace"/>
        /// </remarks>
        public static string Prompt(string prompt)
        {
            Console.Write("{0}> ", prompt);
            return SimplifyWhiteSpace(Console.ReadLine());
        }

        /// <value>If set <c>true</c> all calls to <see cref="Confirm"/> will
        /// return <c>true</c> without prompting the user.</value>
        public static bool AutoConfirm;
        
        /// <summary>
        /// Prompt on Console for user confirmation.
        /// </summary>
        /// <param name="prompt">
        /// The prompt string.
        /// </param>
        /// <returns>
        /// <c>true</c> if the user typed 'yes' -or- <c>flase</c> for 'no'. 
        /// </returns>
        /// <remarks>
        /// Prompting can be disabled, <see cref="AutoConfirm"/>
        /// </remarks>
        public static bool Confirm(string prompt)
        {   if(AutoConfirm) return true;
            while(true)
            {   string reply = Prompt(prompt + "? [y/N]");
                if(string.IsNullOrEmpty(reply)) return false;
                switch(reply.ToLower())
                {   case "y":
                    case "ye":
                    case "yes": return true;
                    case "n":
                    case "no":  return false;
                }
                Console.WriteLine("Please answer with 'yes' or 'no'!");
            }
        }
    }
}
