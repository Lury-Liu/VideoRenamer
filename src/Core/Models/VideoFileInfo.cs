using System.Collections.Generic;


namespace VideoRenamer
{
    public class VideoFileInfo
    {
        public string Path;
        public string FileName;
        public string SizeText;
        public string ResolutionText;
        public string DurationText;
        public string ModifiedText;
        public bool Exists;

        public string ListSummary
        {
            get
            {
                if (!Exists)
                {
                    return "文件不存在";
                }

                List<string> parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(ResolutionText) && ResolutionText != "未知")
                {
                    parts.Add(ResolutionText);
                }
                if (!string.IsNullOrWhiteSpace(DurationText))
                {
                    parts.Add(DurationText);
                }
                if (!string.IsNullOrWhiteSpace(SizeText))
                {
                    parts.Add(SizeText);
                }

                return parts.Count == 0 ? "已读取" : string.Join(" | ", parts.ToArray());
            }
        }
    }
}
