using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking.Sockets;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace DnsAdBlocker
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        DnsServer dab = null;
        public MainPage()
        {
            this.InitializeComponent();

            dab = new DnsServer();

        }

        DatagramSocket _dnsListener = null;
        private async void button_Click(object sender, RoutedEventArgs e)
        {
            _dnsListener = new DatagramSocket();
            _dnsListener.Control.DontFragment = true;
            //_dnsListener.Control.MulticastOnly = true;
            _dnsListener.Control.QualityOfService = SocketQualityOfService.LowLatency;
            _dnsListener.MessageReceived += _dnsListener_MessageReceived;
            await _dnsListener.BindServiceNameAsync("50000");
            Debug.WriteLine("Port::{0}", _dnsListener.Information.LocalPort, null);
        }


        private async void _dnsListener_MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            Stream streamIn = args.GetDataStream().AsStreamForRead();
            uint datalen = args.GetDataReader().UnconsumedBufferLength;
            byte[] dataBuffer = new byte[datalen];

            int bytesRead = await streamIn.ReadAsync(dataBuffer, 0, (int)datalen).ConfigureAwait(false);

            Debug.WriteLine("{0}", Encoding.UTF8.GetString(dataBuffer), null);

        }


    }
}
