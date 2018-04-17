using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Web.Http;
using System.Threading.Tasks;
using Windows.System.Threading;
using System.Diagnostics;
using Windows.UI.Core;
using Windows.System.Threading.Core;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Text;
using System.Net;
using System.Net.Sockets;
using Windows.Networking;
using System.Collections.Concurrent;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;


namespace DnsAdBlocker
{


    [DataContract(Name = "Server")]
    public class SettingsServer
    {
        [DataMember(Name = "DNSForwarder", IsRequired = true, EmitDefaultValue = true)]
        public string DNSForwarder = "8.8.8.8";

        [DataMember(Name = "ListenPort", IsRequired = true, EmitDefaultValue = true)]
        public string ListenPort = "53";
    }


    [DataContract(Name = "General")]
    public class SettingsGeneral
    {
        [DataMember(Name = "MaxThreads", EmitDefaultValue = true)]
        public string MaxThreads = "4";

        [DataMember(Name = "RefreshTimeHours", EmitDefaultValue = true)]
        public string RefreshTimeHours = "24";
    }


    [DataContract(Name = "Settings")]
    public class SettingsAll
    {
        [DataMember(Name = "General")]
        public SettingsGeneral General;

        [DataMember(Name = "Server")]
        public SettingsServer Server;

        [IgnoreDataMember]
        public const string SettingsFile = "settings.json";

        public SettingsAll()
        {
            //Task taskA = Task.Factory.StartNew(() => CopySettingsToLocalFolder());
            //taskA.Wait();
            //Debug.WriteLine("CopySettingsToLocalFolder Task Compelte");
        }


        static public SettingsAll GetDefaultSettings()
        {
            SettingsAll settings = new SettingsAll();
            settings.Server = new SettingsServer();
            settings.General = new SettingsGeneral();

            settings.General.MaxThreads = "4";
            settings.General.RefreshTimeHours = "24";
            settings.Server.DNSForwarder = "8.8.8.8";
            settings.Server.ListenPort = "53";

            return settings; 
        }


        static public async Task CopySettingsToLocalFolder()
        {
            try
            {
                var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                Debug.WriteLine("localFolder:: {0}", localFolder.Path);

                var existingFile = await localFolder.TryGetItemAsync(SettingsFile);
                if(existingFile == null)
                {
                    Debug.WriteLine("File:: {0} doesn't exist in {1}.", SettingsFile, localFolder.Path);
                    // Copy the file from the install folder to the local folder
                    var installFolder = Windows.ApplicationModel.Package.Current.InstalledLocation;
                    var installFile = await installFolder.GetFileAsync(SettingsFile);
                    if(installFile != null)
                    {
                        Debug.WriteLine("Copy File:: {0} from {1} to {2}.", SettingsFile, installFolder.Path, localFolder.Path);
                        await installFile.CopyAsync(localFolder, SettingsFile, Windows.Storage.NameCollisionOption.ReplaceExisting);
                    }
                }
                else
                {
                    Debug.WriteLine("File:: {0} exist in {1}.", SettingsFile, localFolder.Path);
                }
            }
            catch(Exception Ex)
            {
                Debug.WriteLine("CopyHostFilesFromInstallToLocalDirectory:: ERROR::{0}.", Ex.Message);
            }
        }

    }

}