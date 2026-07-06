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
    public class TimeoutWebClient : WebClient
    {
        public int TimeoutMilliseconds = 6000;

        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            if (request != null)
            {
                request.Timeout = TimeoutMilliseconds;
                HttpWebRequest httpRequest = request as HttpWebRequest;
                if (httpRequest != null)
                {
                    httpRequest.ReadWriteTimeout = TimeoutMilliseconds;
                    httpRequest.AllowAutoRedirect = true;
                }
            }

            return request;
        }
    }
}
