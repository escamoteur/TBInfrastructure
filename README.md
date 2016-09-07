# TBInfrastructure
Helper Class for FileIO and Zipfiles

It's based on PCLStorage, SharpZipLib and json.net

*Important* PCLStorage must be added to all platform projects in your solution

Here an overview of the methods

```c#

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

        // If you need the full path to the Apps data directory, this is where you can get it
        public string AppBaseFolderPath { get; set; }

        //Singleton
        public static FileStorage Instance { get; } = new FileStorage();


        public async Task<bool> CheckDirectoryExists(string path)

        public async Task<bool> CheckFileExists(string path)

        public async void CreateDirectory(string path)

        public async Task DeleteFile(string path)


        //Deserializes a json-file into a provided object type
        public async Task<object> GetObjectFromFile(string jsonFileName, Type type)


        //Serializes an object to a json-file
        public async Task SaveObjectToFile(string jsonFileName, object objectToWrite)


        // Very handy function for the first App start
        // It will download a Zipfile and extracts it to the provided folder
        public async Task PopulateDataFolderFromZipUri(string uri, string outFolder)

        #region ZIP Methods

        // Returns a list of all Zip-Files in a folder
        public async Task<List<string>> GetZipFilesInFolder(string folder)


        public async Task  ExtractZipFile(string archiveFilenameIn, string outFolder)


        public async Task ExtractZipFileFromStream(Stream fs, string outFolder)


        // The following functions are for creating a new ZIP-File
        // You can only create one ZIP-File at a time
        // The Sequence goes like this
        // 1. Call CreateNewZipFile to initiate the creation
        // 2. Call any of the Writefunction as often you like
        // 3. Call WriteAndCloseZipFile to flush and close the ZIP-File

        public async Task CreateNewZipFile(string path)

        // When writing already compresse data like JPEGs it's better to leave compressed = false
        public void WriteByteArray2Zip(string fileNameInZip, Byte[] buffer, bool compressed = false)


        public void WriteStream2Zip(string fileNameInZip, Stream streamToZip, bool compressed = false)


        public void WriteObject2Zip(string fileNameInZip, object objectToWrite, bool compressed = true)


        // After this ZipFile has to be new Created
        public void WriteAndCloseZipFile()


        // Gets a filestream for a file inside a ZIP-Archive
        public async Task<Stream> GetFileStreamFromZip(string zipFilePath, string pathInZip)

        // Reads a byte array from a file inside a ZIP-Archive
        public async Task<Byte[]> GetByteArrayFromZip(string zipFilePath, string pathInZip)


        // Reads an object from a file inside a ZIP-Archive
        public async Task<object> GetObjectFromZip(string zipFilePath, string pathInZip, Type type)


        #endregion
    }
}
```
