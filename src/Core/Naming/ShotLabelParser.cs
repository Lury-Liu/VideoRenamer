using System;
using System.Text;
using System.Text.RegularExpressions;

namespace VideoRenamer
{
    // 镜号标签的解析与格式化（如 "28A"）。Format 与 TryParse 互为逆运算，
    // 集中在一个文件里以避免两侧规则漂移；由测试用例锁定。
    public static class ShotLabelParser
    {
        // 正整数 + 至多两个字母后缀（允许数字与字母间有空白）。
        internal const string Pattern = @"^(?<num>\d+)\s*(?<suf>[A-Za-z]{0,2})$";

        public static bool TryParse(string text, out int shot, out string suffix)
        {
            shot = 0;
            suffix = "";
            string trimmed = (text ?? "").Trim();
            Match match = Regex.Match(trimmed, Pattern);
            int parsed;
            if (match.Success && int.TryParse(match.Groups["num"].Value, out parsed) && parsed > 0)
            {
                shot = parsed;
                suffix = NormalizeSuffix(match.Groups["suf"].Value);
                return true;
            }

            return false;
        }

        public static string NormalizeSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            foreach (char ch in value.Trim())
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
                {
                    builder.Append(char.ToUpperInvariant(ch));
                }
                if (builder.Length >= 2)
                {
                    break;
                }
            }
            return builder.ToString();
        }

        public static string Format(int shot, string suffix)
        {
            return Math.Max(1, shot).ToString() + NormalizeSuffix(suffix);
        }
    }
}
