using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.Storage;



namespace DnsAdBlocker
{
    class DataSerializer
    {
        /*
         * http://web.archive.org/web/20130430190551/http://www.danrigsby.com/blog/index.php/2008/03/07/xmlserializer-vs-datacontractserializer-serialization-in-wcf/ 
         * 
         * XmlSerializer:
                has been around for a long time
                is "opt-out"; everything public gets serialized, unless you tell it not to ([XmlIgnore])
  
         * DataContractSerializer is:
                the new kid in town
                optimized for speed (about 10% faster than XmlSerializer, typically)
                "opt-in" - only stuff you specifically mark as [DataMember] will be serialized
                but anything marked with [DataMember] will be serialized - whether it's public or private
                doesn't support XML attributes (for speed reasons)
         */


        static public async Task SerializeXml<T>(string fileName, T data)
        {            
            StringWriter stringWriter = new StringWriter();
            XmlSerializer serializer = new XmlSerializer(typeof(T));
            serializer.Serialize(stringWriter, data);

            string content = stringWriter.ToString();
            stringWriter.Dispose();

            Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            StorageFile dataFile = await localFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(dataFile, content);
        }

        static public async Task<T> DeserializeXml<T>(string fileName)
        {
            Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

            StorageFile file = await localFolder.GetFileAsync(fileName);
            string content = await FileIO.ReadTextAsync(file);

            XmlSerializer serializer = new XmlSerializer(typeof(T));
            StringReader sr = new StringReader(content);

            var desObject = (T)serializer.Deserialize(sr);
            sr.Dispose();

            return desObject;            
        }


        static public async Task SerializeDCS<T>(string fileName, T data)
        {
            DataContractSerializer serializer = new DataContractSerializer(typeof(T));
            string content = string.Empty;
            using(var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, data);
                stream.Position = 0;
                content = new StreamReader(stream).ReadToEnd();
            }

            Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            StorageFile dataFile = await localFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(dataFile, content);
        }

        static public async Task<T> DeserializeDCS<T>(string fileName)
        {
            Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

            StorageFile file = await localFolder.GetFileAsync(fileName);

            var inputStream = await file.OpenReadAsync();
            DataContractSerializer serializer = new DataContractSerializer(typeof(T));

            return (T)serializer.ReadObject(inputStream.AsStreamForRead());
        }


        static public async Task SerializeJson<T>(string fileName, T data)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            string content = string.Empty;
            using(var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, data);
                stream.Position = 0;
                content = new StreamReader(stream).ReadToEnd();
            }

            Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            StorageFile dataFile = await localFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(dataFile, content);
        }

        static public async Task<T> DeserializeJson<T>(string fileName)
        {
            Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

            StorageFile file = await localFolder.GetFileAsync(fileName);

            var inputStream = await file.OpenReadAsync();
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));

            return (T)serializer.ReadObject(inputStream.AsStreamForRead());
        }
    }
}
