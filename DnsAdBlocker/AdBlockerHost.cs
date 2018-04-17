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
using System.Threading;

namespace DnsAdBlocker
{
    class AdBlockerHost
    {
        string WhiteListDataSet = @"whitelist.db";
        string WhiteListUrl = @"whitelist.urls";
        string BlackListDataSet = @"blacklist.db";
        string BlackListUrl = @"blacklist.urls";

        List<string> FileNameList = new List<string>();

        string[] HostFilePaths = new string[] {
            @"https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts",
            @"https://mirror1.malwaredomains.com/files/justdomains",
            @"http://sysctl.org/cameleon/hosts",
            @"https://zeustracker.abuse.ch/blocklist.php?download=domainblocklist",
            @"https://zeustracker.abuse.ch/blocklist.php?download=ipblocklist",
            @"https://zeustracker.abuse.ch/blocklist.php?download=squiddomain",
            @"https://s3.amazonaws.com/lists.disconnect.me/simple_tracking.txt",
            @"https://s3.amazonaws.com/lists.disconnect.me/simple_ad.txt",
            @"https://hosts-file.net/ad_servers.txt",
            @"https://adaway.org/hosts.txt",
            @"https://pgl.yoyo.org/adservers/serverlist.php?hostformat=hosts&showintro=0&mimetype=plaintext",
            @"http://someonewhocares.org/hosts/"
        };


        /*
         * We can use HashSet because here we just want to store URL strings. But there is no concurrent
         * HashSet class. We use Concurrent dictionary so that it thread safe for iteration, add, delete etc. 
         * use the value as byte just to make key-value pair happy. 
         */
        ConcurrentDictionary<string, byte> _blackList = new ConcurrentDictionary<string, byte>();
        ConcurrentDictionary<string, byte> _whiteList = new ConcurrentDictionary<string, byte>();

        SemaphoreSlim _filesemaphore = new SemaphoreSlim(1,1);
        int _refreshHours = 24;
        ThreadPoolTimer _periodicTimer = null;

        public AdBlockerHost()
        {
            Debug.WriteLine("AdBlockerHost: entry");

            FileNameList.Add(WhiteListDataSet);
            FileNameList.Add(WhiteListUrl);
            FileNameList.Add(BlackListDataSet);
            FileNameList.Add(BlackListUrl);


            /* This blocks the UI thread so be careful. Right now, i am doing this 
             * for test purpose and to just copy the files from install to local 
             * directory.
             * https://stackoverflow.com/questions/23048285/call-asynchronous-method-in-constructor
             */
            //Task.Run(() => this.CopyHostFilesFromInstallToLocalDirectory()).Wait();            

            //Debug.WriteLine("Total Url Entries in WhiteList:: {0}", _whiteList.Count);
            //Debug.WriteLine("Total Url Entries in BlackList:: {0}", _blackList.Count);

            //IAsyncAction asyncAction = Windows.System.Threading.ThreadPool.RunAsync(SetupDatabase, WorkItemPriority.High);

            //Task.Run(() => this.SetupDatabase());

            _periodicTimer = ThreadPoolTimer.CreatePeriodicTimer(RefreshTimerHandler, TimeSpan.FromMinutes(2));

            Debug.WriteLine("AdBlockerHost: Exit");
        }

        async void RefreshTimerHandler(ThreadPoolTimer timer)
        {
            Debug.WriteLine("RefreshTimerHandler: Entry");
            await SetupDatabase();
            Debug.WriteLine("RefreshTimerHandler: Exit");
        }

        public async Task SetupDatabase(/*IAsyncAction operation*/)
        {
            await CopyHostFilesFromInstallToLocalDirectory().ConfigureAwait(false);

            await ParseHostFile(WhiteListDataSet, _whiteList).ConfigureAwait(false);
            await ParseHostFile(BlackListDataSet, _blackList).ConfigureAwait(false);

            await FetchAdBlockDataFromUrlList(WhiteListUrl, _whiteList).ConfigureAwait(false);
            await FetchAdBlockDataFromUrlList(BlackListUrl, _blackList).ConfigureAwait(false);

            await WriteDataToFile(WhiteListDataSet, _whiteList).ConfigureAwait(false);
            await WriteDataToFile(BlackListDataSet, _blackList).ConfigureAwait(false);

            Debug.WriteLine("Total Url Entries in WhiteList:: {0}", _whiteList.Count);
            Debug.WriteLine("Total Url Entries in BlackList:: {0}", _blackList.Count);
        }

        public bool IsUrlBlackListed(string Url)
        {
            bool bIsMatch = false;
            byte bValue = 0;

            bIsMatch = _whiteList.TryGetValue(Url, out bValue);
            if(bIsMatch == false)
            {
                bIsMatch = _blackList.TryGetValue(Url, out bValue);
                return bIsMatch;
            }
            else
            {
                //Debug.WriteLine("WhiteListed:: {0}", Url);
                return false;
            }
        }

        async Task CopyHostFilesFromInstallToLocalDirectory()
        {
            /*
             * https://social.msdn.microsoft.com/Forums/windowsapps/en-US/f5bf9fe1-91aa-4489-bd1f-e1483388b854/uwp-deploying-files-to-applicationdatalocalfolder?forum=wpdevelop
             */

            await _filesemaphore.WaitAsync();

            try
            {
                var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                Debug.WriteLine("localFolder:: {0}", localFolder.Path);

                foreach(var file in FileNameList)
                {
                    var existingFile = await localFolder.TryGetItemAsync(file);

                    if(existingFile == null)
                    {
                        Debug.WriteLine("File:: {0} doesn't exist in {1}.", file, localFolder.Path);
                        // Copy the file from the install folder to the local folder
                        var installFolder = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFolderAsync("HostFiles");
                        var installFile = await installFolder.GetFileAsync(file);
                        if(installFile != null)
                        {
                            Debug.WriteLine("Copy File:: {0} from {1} to {2}.", file, installFolder.Path, localFolder.Path);
                            await installFile.CopyAsync(localFolder, file, Windows.Storage.NameCollisionOption.ReplaceExisting);
                        }
                    }
                    {
                        Debug.WriteLine("File:: {0} exist in {1}.", file, localFolder.Path);
                    }
                }
            }
            catch(Exception Ex)
            {
                Debug.WriteLine("CopyHostFilesFromInstallToLocalDirectory:: ERROR::{0}.", Ex.Message);
            }

            _filesemaphore.Release();
        }

        async Task ParseHostFile(string fileName, ConcurrentDictionary<string, byte> database)
        {
            try
            {
                Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                var dataFileExist = await localFolder.TryGetItemAsync(fileName);
                if(dataFileExist == null)
                {
                    return;
                }

                StorageFile dataFile = await localFolder.GetFileAsync(fileName);
                var lines = await FileIO.ReadLinesAsync(dataFile);

                foreach(string item in lines)
                {
                    // comments
                    if(item.StartsWith("#"))
                    {
                        continue;
                    }

                    char[] delimiterChars = { ' ', '\t' };
                    string[] IPAddr = item.Split(delimiterChars, StringSplitOptions.RemoveEmptyEntries);

                    if(IPAddr.Length == 1)
                    {
                        database.TryAdd(IPAddr[0], 0);
                    }

                    if(IPAddr.Length == 2)
                    {
                        database.TryAdd(IPAddr[1], 0);
                    }
                }
            }
            catch(Exception Ex)
            {
                Debug.WriteLine("ParseHostFile:: ERROR::{0} FOR {1}", Ex.Message, fileName);
            }
        }

        async Task WriteDataToFile(string fileName, ConcurrentDictionary<string, byte> database)
        {
            await _filesemaphore.WaitAsync();
            try
            {
                Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                StorageFile dataFile = await localFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                await FileIO.WriteLinesAsync(dataFile, database.Keys);

                Debug.WriteLine("{0}::{1}:: WRITTEN.", fileName, dataFile.Path);
            }
            catch(Exception Ex)
            {
                Debug.WriteLine("WriteDataToFile:: ERROR::{0} FOR {1}", Ex.Message, fileName);
            }
            _filesemaphore.Release();
        }

        async Task<bool> DownloadFile(string urlPath, string fileName)
        {
            Uri uri = new Uri(urlPath);
            Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

            try
            {
                StorageFile destinationFile = await localFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                HttpClient client = new HttpClient();
                var buffer = await client.GetBufferAsync(uri);
                await Windows.Storage.FileIO.WriteBufferAsync(destinationFile, buffer);

            }
            catch(Exception Ex)
            {
                Debug.WriteLine("DownloadFile:: {0} FAILED.", Ex.Message);
                return false;
            }

            return true;

        }

        async Task FetchAdBlockDataFromUrlList(string UrlListFileName, ConcurrentDictionary<string, byte> adDatabase)
        {
            try
            {
                ConcurrentDictionary<string, byte> UrlList = new ConcurrentDictionary<string, byte>();

                await ParseHostFile(UrlListFileName, UrlList);

                foreach(var item in UrlList.Keys)
                {
                    string fileName = Guid.NewGuid().ToString();
                    bool result = await DownloadFile(item, fileName);

                    if(result == true)
                    {
                        await ParseHostFile(fileName, adDatabase);

                        Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                        var dataFileExist = await localFolder.TryGetItemAsync(fileName);
                        if(dataFileExist != null)
                        {
                            await dataFileExist.DeleteAsync(StorageDeleteOption.PermanentDelete);
                            Debug.WriteLine("Downloaded file {0} from {1} DELETED.", fileName, item);
                        }
                    }
                }
            }
            catch(Exception Ex)
            {
                Debug.WriteLine("FetchAdBlockDataFromUrlList {0}:: Ex.Message:{1}", UrlListFileName, Ex.Message);
            }
        }
    }
}




/*
 * https://docs.microsoft.com/en-us/uwp/api/windows.storage.storagefolder
 * 
            using Windows.Storage;
            using System.Threading.Tasks;
            using System.Diagnostics; // For writing results to Output window.

            // Get the app's local folder.
            StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

            // Create a temporary folder in the current folder.
            string folderName = "Test";
            StorageFolder testFolder = await localFolder.CreateFolderAsync(folderName);

            // Has the folder been created?
            if(await localFolder.TryGetItemAsync(folderName) != null)
                Debug.WriteLine("Folder " + folderName + " exists.");
            else
                Debug.WriteLine("Folder " + folderName + " does not exist.");

            // Delete the folder permanently.
            await testFolder.DeleteAsync(StorageDeleteOption.PermanentDelete);

            // Has the folder been deleted?
            if(await localFolder.TryGetItemAsync(folderName) != null)
                Debug.WriteLine("Folder " + folderName + " exists.");
            else
                Debug.WriteLine("Folder " + folderName + " does not exist.");

 
 */
