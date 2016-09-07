using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;
using PCLStorage;

namespace TBInfrastructure
{
    public class FileStorage
    {
        private readonly IFolder appBasefolder; 

        public string AppBaseFolderPath;
        private JsonWriter zipFileJsonWriter;
        private StreamWriter zipStreamWriter;

        private IFile zipOutFile;
        private Stream zipOutFileStream;
        private ZipOutputStream zipOutputStream;

        private FileStorage()
        {
            appBasefolder = FileSystem.Current.LocalStorage;
            AppBaseFolderPath = appBasefolder.Path;
        }

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



        public async Task SaveObjectToFile(string jsonFileName, object objectToWrite, Type type)
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


        public async Task DeleteFile(string path)
        {
            if (await appBasefolder.CheckExistsAsync(path) == ExistenceCheckResult.FileExists)
            {
                var file = await appBasefolder.GetFileAsync(path);
                await file.DeleteAsync();
            }
        }


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

        #region ZIP

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




        public async Task CreateNewZipFile(string path)
        {
            zipOutFile = await appBasefolder.CreateFileAsync(path, CreationCollisionOption.ReplaceExisting);
            zipOutFileStream = await zipOutFile.OpenAsync(FileAccess.ReadAndWrite);

            zipOutputStream = new ZipOutputStream(zipOutFileStream);
            zipStreamWriter = new StreamWriter(zipOutputStream);
            zipFileJsonWriter = new JsonTextWriter(zipStreamWriter);
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


        public async Task<Stream> GetFileStreamFromZip(string zipFilePath, string pathInZip)
        {
            var zipInFile = await appBasefolder.GetFileAsync(zipFilePath);
            var zipInFileStream = await zipInFile.OpenAsync(FileAccess.Read);

            var zipFile = new ZipFile(zipInFileStream);
            var entry = zipFile.GetEntry(pathInZip);
            return zipFile.GetInputStream(entry);
        }


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

        #endregion
    }
}