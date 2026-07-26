using System.Collections.Generic;


namespace VideoRenamer
{
    public class ShotRow
    {
        public int Scene;
        public int Sequence;
        public string ShotSuffix = "";
        public List<string> MainFiles = new List<string>();
        public List<string> BackupFiles = new List<string>();
        public List<string> MainTailOverrides = new List<string>();
        public List<string> BackupTailOverrides = new List<string>();
        public int ProgressPercent;
    }
}
