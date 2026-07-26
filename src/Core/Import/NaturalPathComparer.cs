using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;


namespace VideoMaterialRenamer
{
    public class NaturalPathComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            return StringComparer.CurrentCultureIgnoreCase.Compare(ToNaturalKey(Path.GetFileName(x)), ToNaturalKey(Path.GetFileName(y)));
        }

        private static string ToNaturalKey(string value)
        {
            if (value == null)
            {
                return "";
            }

            return Regex.Replace(value, "\\d+", delegate(Match match)
            {
                return match.Value.PadLeft(12, '0');
            });
        }
    }
}
