using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VideoMaterialRenamer
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
