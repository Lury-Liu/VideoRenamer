using System;
using System.Net;

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
