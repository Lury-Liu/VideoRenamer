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
    public class RenamePlan
    {
        public ShotRow Row;
        public int RowIndex;
        public string ColumnName;
        public bool IsMain;
        public int FileIndex;
        public int Scene;
        public int Shot;
        public int Take;
        public string TailSegment;
        public string CustomTailText;
        public bool HasCustomTail;
        public string OldPath;
        public string TargetPath;
        public string OldName;
        public string NewName;
        public string Status;
    }
}
