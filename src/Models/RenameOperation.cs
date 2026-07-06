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
    public class RenameOperation
    {
        public ShotRow Row;
        public int RowIndex;
        public bool IsMain;
        public int FileIndex;
        public string OriginalPath;
        public string RenamedPath;
    }
}
