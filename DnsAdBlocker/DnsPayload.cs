using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using Windows.Networking;

namespace DnsAdBlocker
{
    public class DnsPayload
    {
        public HostName RemoteAddress;
        public String   RemotePort;
        public byte[]   Query;


        public DnsPayload(HostName RemoteAddress, String RemotePort, byte[] Query)
        {
            this.RemoteAddress = RemoteAddress;
            this.RemotePort = RemotePort;
            this.Query = Query;
        }


    }
}
