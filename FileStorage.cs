// Copyright: Thomas Burkhart 2016
// Licence: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;
using PCLStorage;

// This is a helper class to ease file IO based on PCLStorage
// Mainly it offers Object Serialization/Deserialization, ZIP-File 
// handling and some convinience functions
//
// It's implemented as singleton so easy to use from everywhere
//
// IMPORTANT!!!
//
// If not otherwise mentioned are all file paths relative to the Apps data directory
//

namespace TBInfrastructure
{
    
    public class FileStorage
    {
        private readonly IFolder appBasefolder; 
        private JsonWriter zipFileJsonWriter;
        private StreamWriter zipStreamWriter;

        private IFile zipOutFile;
        private Stream zipOutFileStream;
        private ZipOutputStream zipOutputStream;

        // If you need the full path to the Apps data directory, this is where you can get it
        public string AppBaseFolderPath { get; set; }


        private FileStorage()
        {
            appBasefolder = FileSystem.Current.LocalStorage;
            AppBaseFolderPath = appBasefolder.Path;
        }

        //Singleton
        public static FileStorage Instance { get; } = new FileStorage();



        public async Task<bool> CheckDirectoryExists(string path)
        {
            var result = await appBasefolder.CheckExistsAsync(path);
            return result == ExistenceCheckResult.FolderExists;
        }

        public async Task<bool> CheckFileExists(string path)
        {
            var result = await appBasefolder.CheckExistsAsync(path);
            return result == ExistenceCheckResult.FileExists;
        }

        public async void CreateDirectory(string path)
        {
            await appBasefolder.CreateFolderAsync(path, CreationCollisionOption.FailIfExists);
        }

        public async Task DeleteFile(string path)
        {
            if (await appBasefolder.CheckExistsAsync(path) == ExistenceCheckResult.FileExists)
            {
                var file = await appBasefolder.GetFileAsync(path);
                await file.DeleteAsync();
            }
        }


        //Deserializes a json-file into a provided object type
        public async Task<object> GetObjectFromFile(string jsonFileName, Type type)
        {
            var jsonFile = await appBasefolder.GetFileAsync(jsonFileName);
            var jsonInFileStream = await jsonFile.OpenAsync(FileAccess.Read);
            var json = new JsonSerializer();
            using (var file = new StreamReader(jsonInFileStream))
            {
                return json.Deserialize(file, type);
            }
        }



        //Serializes an object to a json-file
        public async Task SaveObjectToFile(string jsonFileName, object objectToWrite)
        {
            var jsonFile = await appBasefolder.CreateFileAsync(jsonFileName, CreationCollisionOption.ReplaceExisting);
            var jsonStream = await jsonFile.OpenAsync(FileAccess.ReadAndWrite);

            var streamWriter = new StreamWriter(jsonStream);
            var jsonWriter = new JsonTextWriter(streamWriter);

            var serializer = new JsonSerializer();

            serializer.Serialize(jsonWriter, objectToWrite);

            jsonWriter.Flush();

            jsonStream.Dispose();
        }



        // Very handy function for the first App start
        // It will download a Zipfile and extracts it to the provided folder
        public async Task PopulateDataFolderFromZipUri(string uri, string outFolder)
        {
            using (var httpClient = new HttpClient())
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get,
                    uri))
                {
                    using (var response = await httpClient.SendAsync(request))
                    {
                        Stream contentStream = await (response.Content.ReadAsStreamAsync());

                        await ExtractZipFileFromStream(contentStream, outFolder);
                        contentStream.Dispose();
                    }
                }

            }
        }

        #region ZIP Methods

        // Returns a list of all Zip-Files in a folder
        public async Task<List<string>> GetZipFilesInFolder(string folder)
        {
            var fileNames = new List<string>();
            var zipFolder = await appBasefolder.GetFolderAsync(folder);
            var files = await zipFolder.GetFilesAsync();
            foreach (var file in files)
            {
                if (file.Name.Contains(".zip"))
                {
                    fileNames.Add(file.Name);
                }
            }
            return fileNames;
        }


        public async Task  ExtractZipFile(string archiveFilenameIn, string outFolder)
        {
                var inFile = await appBasefolder.GetFileAsync(archiveFilenameIn);
                var fs = await inFile.OpenAsync(FileAccess.Read);
                await ExtractZipFileFromStream(fs, outFolder);
        }


        public async Task ExtractZipFileFromStream(Stream fs, string outFolder)
        {
            ZipFile zf = null;
            try
            {
                zf = new ZipFile(fs);
                foreach (ZipEntry zipEntry in zf)
                {
                    if (!zipEntry.IsFile)
                    {
                        continue;           // Ignore directories
                    }
                    String entryFileName = zipEntry.Name;
                    // to remove the folder from the entry:- entryFileName = Path.GetFileName(entryFileName);
                    // Optionally match entrynames against a selection list here to skip as desired.
                    // The unpacked length is available in the zipEntry.Size property.

                    byte[] buffer = new byte[4096];     // 4K is optimum
                    Stream zipStream = zf.GetInputStream(zipEntry);

                    // Manipulate the output filename here as desired.
                    String fullZipToPath = Path.Combine(outFolder, entryFileName);
                    string directoryName = Path.GetDirectoryName(fullZipToPath);
                    if (directoryName.Length > 0)
                        await appBasefolder.CreateFolderAsync(directoryName, CreationCollisionOption.OpenIfExists);

                    // Unzip file in buffered chunks. This is just as fast as unpacking to a buffer the full size
                    // of the file, but does not waste memory.
                    // The "using" will close the stream even if an exception occurs.
                    var outFile = await appBasefolder.CreateFileAsync(fullZipToPath, CreationCollisionOption.ReplaceExisting);

                    using (var streamWriter = await outFile.OpenAsync(FileAccess.ReadAndWrite))
                    {
                        StreamUtils.Copy(zipStream, streamWriter, buffer);
                    }
                }
            }
            finally
            {
                if (zf != null)
                {
                    zf.IsStreamOwner = true; // Makes close also shut the underlying stream
                    zf.Close(); // Ensure we release resources
                }
            }
        }


        // The following functions are for creating a new ZIP-File
        // You can only create one ZIP-File at a time
        // The Sequence goes like this
        // 1. Call CreateNewZipFile to initiate the creation
        // 2. Call any of the Writefunction as often you like
        // 3. Call WriteAndCloseZipFile to flush and close the ZIP-File

        public async Task CreateNewZipFile(string path)
        {
            zipOutFile = await appBasefolder.CreateFileAsync(path, CreationCollisionOption.ReplaceExisting);
            zipOutFileStream = await zipOutFile.OpenAsync(FileAccess.ReadAndWrite);

            zipOutputStream = new ZipOutputStream(zipOutFileStream);
            zipStreamWriter = new StreamWriter(zipOutputStream);
            zipFileJsonWriter = new JsonTextWriter(zipStreamWriter);
        }

        // When writing already compresse data like JPEGs it's better to leave compressed = false
        public void WriteByteArray2Zip(string fileNameInZip, Byte[] buffer, bool compressed = false)
        {
            if (compressed)
            {
                zipOutputStream.SetLevel(3);
            }
            else
            {
                zipOutputStream.SetLevel(0);
            }

            var newEntry = new ZipEntry(fileNameInZip);

            zipOutputStream.PutNextEntry(newEntry);

            zipOutputStream.Write(buffer, 0, buffer.Length);

            zipOutputStream.CloseEntry();
        }


        public void WriteStream2Zip(string fileNameInZip, Stream streamToZip, bool compressed = false)
        {
            if (compressed)
            {
                zipOutputStream.SetLevel(3);
            }
            else
            {
                zipOutputStream.SetLevel(0);
            }

            var newEntry = new ZipEntry(fileNameInZip);

            zipOutputStream.PutNextEntry(newEntry);

            StreamUtils.Copy(streamToZip, zipOutputStream, new byte[4096]);

            zipOutputStream.CloseEntry();
        }


        public void WriteObject2Zip(string fileNameInZip, object objectToWrite, bool compressed = true)
        {
            if (compressed)
            {
                zipOutputStream.SetLevel(3);
            }
            else
            {
                zipOutputStream.SetLevel(0);
            }

            var serializer = new JsonSerializer();

            var newEntry = new ZipEntry(fileNameInZip);
            zipOutputStream.PutNextEntry(newEntry);

            serializer.Serialize(zipFileJsonWriter, objectToWrite);

            zipFileJsonWriter.Flush();
        }


        // After this ZipFile has to be new Created
        public void WriteAndCloseZipFile()
        {
            zipOutputStream.Finish();

            zipOutputStream = null;

            zipOutFileStream.Dispose();
            zipOutFileStream = null;

            zipStreamWriter = null;
            zipOutFile = null;
            zipFileJsonWriter = null;
        }


        // Gets a filestream for a file inside a ZIP-Archive
        public async Task<Stream> GetFileStreamFromZip(string zipFilePath, string pathInZip)
        {
            var zipInFile = await appBasefolder.GetFileAsync(zipFilePath);
            var zipInFileStream = await zipInFile.OpenAsync(FileAccess.Read);

            var zipFile = new ZipFile(zipInFileStream);
            var entry = zipFile.GetEntry(pathInZip);
            return zipFile.GetInputStream(entry);
        }

        // Reads a byte array from a file inside a ZIP-Archive
        public async Task<Byte[]> GetByteArrayFromZip(string zipFilePath, string pathInZip)
        {
            var zipInFile = await appBasefolder.GetFileAsync(zipFilePath);
            var zipInFileStream = await zipInFile.OpenAsync(FileAccess.Read);

            var zipFile = new ZipFile(zipInFileStream);
            var entry = zipFile.GetEntry(pathInZip);
            var buffer = new Byte[entry.Size];
            var zipStream = zipFile.GetInputStream(entry);

            int bytesRead = await zipStream.ReadAsync(buffer, 0, (int)entry.Size);

            if (bytesRead != entry.Size)
            {
                throw new Exception("ZipEntry not fully read");
            }
            zipInFileStream.Dispose();

            return buffer;
        }


        // Reads an object from a file inside a ZIP-Archive
        public async Task<object> GetObjectFromZip(string zipFilePath, string pathInZip, Type type)
        {
            var inStream = await GetFileStreamFromZip(zipFilePath, pathInZip);
            var json = new JsonSerializer();
            Object deserializedObject;
            using (var file = new StreamReader(inStream))
            {
                deserializedObject = json.Deserialize(file, type);
            }
            inStream.Dispose();
            return deserializedObject;

        }




        #endregion
    }
}