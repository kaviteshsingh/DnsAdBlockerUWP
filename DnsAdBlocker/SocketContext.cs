using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;

namespace DnsAdBlocker
{
    public class SocketContext
    {
        public HostName RemoteAddress;
        public String   RemotePort;
        public byte[] Response;
        public List<DnsQuery> Queries;

        //public AutoResetEvent evtResponse;

        public SocketContext()
        {
            this.Response = null;
            //this.evtResponse = new AutoResetEvent(false);
        }

        //public SocketContext(byte[] response, AutoResetEvent evtResponse)
        //{
        //    this.Response = response;
        //    this.evtResponse = evtResponse;
        //}
    }
}
