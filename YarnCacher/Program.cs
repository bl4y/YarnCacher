using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace YarnCacher
{
    class Program
    {
        static void Main(string[] Options)
        {
            if (Options.Length < 3 || (Options.Length > 0 && Options[0] == "-h"))
            {
                Console.WriteLine("Usage: YarnCacher.exe <path-to-package.json> <azure-storage-key> <azure-block-blob> <OPTIONAL: yarn-path>");
                return;
            }

            Console.WriteLine(" *** YarnCacher for Azure by bl4y *** ");

            Console.WriteLine("Fetching Yarn cache directory...");

            string YarnCachePath = string.Empty;

            Process YarnCacheProcess = Process.Start(new ProcessStartInfo("cmd", "/c " + (Options.Length == 4 ? Options[4] : "yarn") + " cache dir")
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            YarnCacheProcess.WaitForExit();

            YarnCachePath = YarnCacheProcess.StandardOutput.ReadToEnd().TrimEnd('\r', '\n');
            try
            {
                Path.GetFullPath(YarnCachePath);
            }
            catch
            {
                Console.WriteLine("Invalid Yarn cache directory. Make sure Yarn is added to PATH or specify Yarn path in arguments. More info: YarnCacher.exe -h");
                Console.ReadKey();
                return;
            }

            Console.WriteLine(" > Yarn cache directory: " + YarnCachePath);

            Console.WriteLine("Generating MD5 hash for package.json...");

            string PackagePath = Options[0];
            string PackageHash = string.Empty;

            using (MD5 MD5Instance = MD5.Create())
            {
                using (FileStream Stream = File.OpenRead(PackagePath))
                {
                    PackageHash = string.Join(string.Empty, MD5Instance.ComputeHash(Stream).Select(x => x.ToString("x2")));
                }
            }

            Console.WriteLine(" > Hash: " + PackageHash);

            string CacheArchiveFileName = "yarn-pre-cache-" + PackageHash + ".zip";

            Console.WriteLine("Accessing Azure...");

            CloudBlobClient AzureBlobClient;
            CloudBlobContainer AzureBlobContainer;
            CloudBlockBlob AzureBlockBlob;

            try
            {
                AzureBlobClient = CloudStorageAccount.Parse(Options[1]).CreateCloudBlobClient();
                AzureBlobContainer = AzureBlobClient.GetContainerReference(Options[2]);
                AzureBlockBlob = AzureBlobContainer.GetBlockBlobReference(CacheArchiveFileName);
            }
            catch (Exception e)
            {
                Console.WriteLine(" > Failed to access Azure. Exception message:");
                Console.WriteLine(e.Message);
                Console.ReadKey();
                return;
            }

            Console.WriteLine(" > Connected to Azure.");

            if (AzureBlockBlob.ExistsAsync().GetAwaiter().GetResult())
            {
                Console.WriteLine("Downloading pre-cached archive from Azure...");

                AzureBlockBlob.DownloadToFileAsync(CacheArchiveFileName, FileMode.CreateNew).GetAwaiter().GetResult();

                Console.WriteLine("Cleaning up...");

                DirectoryInfo CacheDirectoryInfo = new DirectoryInfo(YarnCachePath);

                foreach (FileInfo File in CacheDirectoryInfo.GetFiles())
                    File.Delete();

                foreach (DirectoryInfo Directory in CacheDirectoryInfo.GetDirectories())
                    Directory.Delete(true);

                Console.WriteLine("Uncompressing pre-cached archive...");

                ZipFile.ExtractToDirectory(CacheArchiveFileName, YarnCachePath);

                File.Delete(CacheArchiveFileName);
            }

            Console.WriteLine("Installing and building Yarn packages...");

            Process YarnInstallProcess = Process.Start(new ProcessStartInfo("cmd", "/c " + (Options.Length == 4 ? Options[4] : "yarn") + " install")
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });

            YarnInstallProcess.WaitForExit();

            Console.WriteLine(" > Process output:");
            Console.WriteLine(YarnInstallProcess.StandardOutput.ReadToEnd().TrimEnd('\r', '\n'));

            if (!AzureBlockBlob.ExistsAsync().GetAwaiter().GetResult())
            {
                Console.WriteLine("Compressing Yarn cache...");

                ZipFile.CreateFromDirectory(YarnCachePath, CacheArchiveFileName);

                Console.WriteLine("Uploading pre-cached archive to Azure...");

                AzureBlockBlob.UploadFromFileAsync(CacheArchiveFileName).GetAwaiter().GetResult();

                Console.WriteLine("Cleaning up...");

                File.Delete(CacheArchiveFileName);
            }

            Console.WriteLine(" > Finished.");

            Console.ReadLine();
        }
    }
}
