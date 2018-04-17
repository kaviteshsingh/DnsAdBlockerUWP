using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using Windows.Networking;
using System.Collections.Concurrent;
using Windows.Networking.Sockets;
using System.Diagnostics;
using System.IO;
using Windows.Storage.Streams;
using System.Threading;

namespace DnsAdBlocker
{
    /*
     * https://stackoverflow.com/questions/33235131/universal-windows-project-httpclient-exception
     * 
     * 
     * 
     */

    class DnsServer
    {
        Object _lock = new Object();
        AdBlockerHost _adBlockerHost = null;
        SettingsAll _settings = null;

        ConcurrentDictionary<DatagramSocket, SocketContext> _SocketContextMapping = new ConcurrentDictionary<DatagramSocket, SocketContext>();

        DatagramSocket _dnsListener =null;
        ConcurrentQueue<DatagramSocket> _forwardingSockets = new ConcurrentQueue<DatagramSocket>();
        ConcurrentDictionary<DatagramSocket, byte> _forwardingSocketsPending = new ConcurrentDictionary<DatagramSocket, byte>();
        ConcurrentQueue<DnsPayload>  _pendingDnsRequest = new ConcurrentQueue<DnsPayload>();
        AutoResetEvent _evtForwardingSocketAvailable = new AutoResetEvent(false);
        //ManualResetEvent _evtForwardingSocketAvailable = new ManualResetEvent(false);
        long nQueries = 0;

        public DnsServer()
        {
            Task.Run(() => this.SetupDnsServer());
        }

        async void SetupDnsServer()
        {
            _adBlockerHost = new AdBlockerHost();

            await ReadConfiguration();
            await _adBlockerHost.SetupDatabase();
            await SetupProxyServer();
            Debug.WriteLine("DnsServer complete");
        }


        async Task ReadConfiguration()
        {
            try
            {
                await SettingsAll.CopySettingsToLocalFolder();
                SettingsAll setting = new SettingsAll();
                _settings = await DataSerializer.DeserializeJson<SettingsAll>(SettingsAll.SettingsFile);
            }
            catch(Exception Ex)
            {
                Debug.WriteLine("ERROR READING CONFIGURATION FILE: {0}", Ex.Message.ToString());
                _settings = SettingsAll.GetDefaultSettings();
                await DataSerializer.SerializeJson<SettingsAll>(SettingsAll.SettingsFile, _settings);
            }
        }


        async Task SetupProxyServer()
        {
            Debug.WriteLine("SetupProxyServer entry");

            try
            {
                int MaxSockets = 0;

                bool result =  int.TryParse(_settings.General.MaxThreads, out MaxSockets);
                if(!result)
                {
                    MaxSockets = 128;
                    Debug.WriteLine("ERROR:: Failed to read MaxThreads {0}.", MaxSockets);
                }

                Debug.WriteLine("MaxThreads {0}.", MaxSockets);

                _dnsListener = new DatagramSocket();

                _dnsListener.Control.DontFragment = true;
                //_dnsListener.Control.MulticastOnly = true;
                _dnsListener.Control.QualityOfService = SocketQualityOfService.LowLatency;
                _dnsListener.MessageReceived += _dnsListener_MessageReceived;



                await _dnsListener.BindServiceNameAsync(_settings.Server.ListenPort);
                Debug.WriteLine("DatagramSocket:: On {0}::{1} port.", _settings.Server.ListenPort, _dnsListener.Information.LocalPort);

                for(int i = 0; i < MaxSockets; i++)
                {
                    DatagramSocket socket = new DatagramSocket();
                    socket.Control.DontFragment = true;
                    socket.Control.QualityOfService = SocketQualityOfService.LowLatency;
                    socket.MessageReceived += forwarder_MessageReceived;
                    await socket.BindServiceNameAsync("");

                    _forwardingSockets.Enqueue(socket);
                    // https://stackoverflow.com/questions/8112399/incorrect-output-from-debug-writelineput-text-here-0-mystring
                    Debug.WriteLine("Forwarding DatagramSocket:: On {0} port.", socket.Information.LocalPort, null);
                }
            }
            catch(Exception Ex)
            {
                _dnsListener = null;
                Debug.WriteLine(Ex.Message + " " + Ex.StackTrace);
                throw Ex;
            }

            Debug.WriteLine("SetupProxyServer exit");

        }



        private async void _dnsListener_MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            try
            {
                Stream streamIn = args.GetDataStream().AsStreamForRead();
                uint datalen = args.GetDataReader().UnconsumedBufferLength;
                byte[] dataBuffer = new byte[datalen];

                int bytesRead = await streamIn.ReadAsync(dataBuffer, 0, (int)datalen).ConfigureAwait(false);

                DnsPayload payload = new DnsPayload(args.RemoteAddress, args.RemotePort, dataBuffer);

                _pendingDnsRequest.Enqueue(payload);

                Debug.WriteLine("_dnsListener_MessageReceived:: Request from {0}::{1}", args.RemoteAddress, args.RemotePort, null);

                Task.Run(() => this.ProcessDnsRequest());
                Debug.WriteLine("_dnsListener_MessageReceived:: Complete");
            }
            catch(Exception Ex)
            {
                Debug.WriteLine("_dnsListener_MessageReceived:: {0}", Ex.Message);
            }

            // throw new NotImplementedException();
        }

        async void ProcessDnsRequest()
        {
            DnsPayload payload = null;
            DatagramSocket forwardingSocket = null;
            bool bResult = _forwardingSockets.TryDequeue(out forwardingSocket);

            if(bResult)
            {
                bResult = _pendingDnsRequest.TryDequeue(out payload);
                DnsQueryPacket dqp = new DnsQueryPacket(payload);

                byte[] Temp = new byte[2];
                Temp[0] = payload.Query[1];
                Temp[1] = payload.Query[0];
                UInt16 TxId = BitConverter.ToUInt16(Temp, 0);

                Debug.WriteLine("ProcessDnsRequest:: Request from {0}::{1}", payload.RemoteAddress, payload.RemotePort, null);

                foreach(var item in dqp.Queries)
                {
                    bool IsMatch = _adBlockerHost.IsUrlBlackListed(item.Url);
                    if(IsMatch)
                    {
                        await SendNoNameDnsResponse(payload, item.Url);
                        _forwardingSockets.Enqueue(forwardingSocket);
                        return;
                    }
                }

                // send to dns forwarder
                SocketContext sc = new SocketContext();
                sc.RemoteAddress = payload.RemoteAddress;
                sc.RemotePort = payload.RemotePort;
                sc.Queries = dqp.Queries;

                _SocketContextMapping[forwardingSocket] = sc;

                HostName dnsForwardServer = new HostName(_settings.Server.DNSForwarder);
                bool bSent = await SendDataToRemoteEnd(forwardingSocket, dnsForwardServer, _settings.Server.ListenPort, payload.Query);

                _forwardingSocketsPending[forwardingSocket] = 0x01;

                Debug.WriteLine("URL::{0} Request from {1}::{2} forwarded to DNSForwarder:: {3}::{4} using forwardingSocket ::{5}. TxId::{6} bSent {7}",
                    dqp.Queries[0], payload.RemoteAddress, payload.RemotePort,
                    _settings.Server.DNSForwarder, _settings.Server.ListenPort,
                    forwardingSocket.Information.LocalPort,
                    TxId, bSent);

            }
            else
            {
                if(_evtForwardingSocketAvailable.WaitOne(1 * 1000))
                {
                    Debug.WriteLine("<<<---------- _evtForwardingSocketAvailable WaitOne set ---------->>>. _forwardingSockets.Count = {0}, _pendingDnsRequest.Count = {1}",
                     _forwardingSockets.Count, _pendingDnsRequest.Count);
                }
                else
                {
                    Debug.WriteLine("<<<---------- _evtForwardingSocketAvailable WaitOne TIMED OUT ---------->>>");
                    List <DatagramSocket> pendingSockets = null;
                    /*
                     * this can happen in multiple threads and can cause issues in the loop so 
                     * lock and empty. We will hit this only when we run out of all the available
                     * sockets and there are pending requests. We call directly the Dispose because
                     * CancelIO can raise issue because of async message received. We would not 
                     * use the socket again since it is out of both_forwardingSockets and 
                     * _forwardingSocketsPending. 
                    */

                    lock(_lock)
                    {
                        pendingSockets = _forwardingSocketsPending.Keys.ToList();
                        foreach(var item in pendingSockets)
                        {
                            byte val = 0;
                            _forwardingSocketsPending.TryRemove(item, out val);
                        }
                    }

                    if(pendingSockets != null || pendingSockets.Count > 0)
                    {
                        foreach(var item in pendingSockets)
                        {
                            Debug.WriteLine("ProcessDnsRequest:: Close non-responding socket::{0} port.", item.Information.LocalPort, null);

                            SocketContext sc = null;
                            _SocketContextMapping.TryRemove(item, out sc);
                            item.Dispose();

                            DatagramSocket socket = new DatagramSocket();
                            socket.Control.DontFragment = true;
                            socket.Control.QualityOfService = SocketQualityOfService.LowLatency;
                            socket.MessageReceived += forwarder_MessageReceived;
                            await socket.BindServiceNameAsync("");

                            _forwardingSockets.Enqueue(socket);
                            // https://stackoverflow.com/questions/8112399/incorrect-output-from-debug-writelineput-text-here-0-mystring
                            Debug.WriteLine("New DatagramSocket created:: on {0} port.", socket.Information.LocalPort, null);
                        }
                    }
                }

                Task.Run(() => this.ProcessDnsRequest());
                Debug.WriteLine("ProcessDnsRequest:: Calling ProcessDnsRequest.");
            }
        }


        private async void forwarder_MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            try
            {
                SocketContext sc = null;
                bool bPresent = _SocketContextMapping.TryGetValue(sender, out sc);

                if(bPresent)
                {
                    Stream streamIn = args.GetDataStream().AsStreamForRead();
                    uint datalen = args.GetDataReader().UnconsumedBufferLength;
                    byte[] dataBuffer = new byte[datalen];

                    int bytesRead = await streamIn.ReadAsync(dataBuffer, 0, (int)datalen).ConfigureAwait(false);

                    bool bSent = await SendDataToRemoteEnd(_dnsListener, sc.RemoteAddress, sc.RemotePort, dataBuffer).ConfigureAwait(true);

                    Debug.WriteLine("[{0:D20}]:: URL::{1} --> Response from {2}::{3} sent to:: {4}::{5} using forwardingSocket {6}::{7}. bSent {8}. _forwardingSockets.Count = {9}, _pendingDnsRequest.Count = {10}",
                        Interlocked.Increment(ref nQueries), sc.Queries[0].Url,
                        _settings.Server.DNSForwarder, _settings.Server.ListenPort,
                        sc.RemoteAddress, sc.RemotePort,
                        args.LocalAddress, sender.Information.LocalPort,
                        bSent, _forwardingSockets.Count, _pendingDnsRequest.Count);
                }
            }
            catch(Exception Ex)
            {
                Debug.WriteLine("forwarder_MessageReceived:: {0}", Ex.Message);
            }

            byte dummy = 0;
            _forwardingSocketsPending.TryRemove(sender, out dummy);
            _forwardingSockets.Enqueue(sender);
            _evtForwardingSocketAvailable.Set();
            Debug.WriteLine("<<<---------- _evtForwardingSocketAvailable set ---------->>>. _forwardingSockets.Count = {0}, _pendingDnsRequest.Count = {1}",
                    _forwardingSockets.Count, _pendingDnsRequest.Count);
        }


        async Task<bool> SendDataToRemoteEnd(DatagramSocket socket, HostName RemoteAddress, String RemotePort, byte[] data)
        {
            bool bResult = false;
            try
            {
                IOutputStream outputStream = await socket.GetOutputStreamAsync(RemoteAddress, RemotePort);

                DataWriter writer = new DataWriter(outputStream);
                writer.WriteBytes(data);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
                writer.Dispose();
                bResult = true;
            }
            catch(Exception Ex)
            {
                // https://stackoverflow.com/questions/33235131/universal-windows-project-httpclient-exception
                Debug.WriteLine(Ex.Message);
                bResult = false;
            }

            return bResult;
        }


        async Task SendNoNameDnsResponse(DnsPayload payload, string Url)
        {
            byte[] Temp = new byte[2];

            // big endian thing
            Temp[0] = payload.Query[1];
            Temp[1] = payload.Query[0];
            UInt16 TxId = BitConverter.ToUInt16(Temp, 0);

            Temp[0] = payload.Query[3];
            Temp[1] = payload.Query[2];
            UInt16 flags = BitConverter.ToUInt16(Temp, 0);

            //Console.WriteLine("TxId 0x{0:x}, flags 0x{1:x}", TxId, flags);

            /*
             * wireshark shows in little endian format
                Flags: 0x8583 Standard query response, No such name
                        1... .... .... .... = Response: Message is a response
                        .000 0... .... .... = Opcode: Standard query (0)
                        .... .1.. .... .... = Authoritative: Server is an authority for domain
                        .... ..0. .... .... = Truncated: Message is not truncated
                        .... ...1 .... .... = Recursion desired: Do query recursively
                        .... .... 1... .... = Recursion available: Server can do recursive queries
                        .... .... .0.. .... = Z: reserved (0)
                        .... .... ..0. .... = Answer authenticated: Answer/authority portion was not authenticated by the server
                        .... .... ...0 .... = Non-authenticated data: Unacceptable
                        .... .... .... 0011 = Reply code: No such name (3)

             * */

            payload.Query[2] = 0x85;
            payload.Query[3] = 0x83;
            //payload.Query[3] = 0x85;

            UInt16 newFlags = BitConverter.ToUInt16(payload.Query, 2);

            bool bSent = await SendDataToRemoteEnd(_dnsListener, payload.RemoteAddress, payload.RemotePort, payload.Query);

            Debug.WriteLine("[{0:D20}]:: URL:: {1} BLOCKED:: TxId 0x{2:x4}, flags 0x{3:x4} -> 0x{4:x4}.Bytes sent to {5}: {6:D3}. bSent {7}.",
                Interlocked.Increment(ref nQueries), Url, TxId, flags, newFlags, payload.RemoteAddress.ToString(), payload.Query.Length, bSent.ToString());

            payload = null;

        }


    }
}
