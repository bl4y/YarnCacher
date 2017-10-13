using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace YarnCacher
{
    public class Program
    {
        public static async Task Main(string[] Options)
        {
            if (Options.Length < 3 || (Options.Length > 0 && Options[0] == "-h"))
            {
                Console.WriteLine("Usage: YarnCacher.exe <absolute-path-to-package.json> <azure-storage-access-key> <azure-blob-container-name> <OPTIONAL: absolute-path-to-yarn>");
                return;
            }

            Console.WriteLine(" *** YarnCacher for Azure @ https://github.com/bl4y/YarnCacher *** ");

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
                Console.WriteLine(" > Invalid Yarn cache directory. Make sure Yarn is added to PATH or specify Yarn path in arguments. More info: YarnCacher.exe -h");
                return;
            }

            Console.WriteLine(" > Yarn cache directory: " + YarnCachePath);

            Console.WriteLine("Generating MD5 hash for package.json...");

            string PackagePath = Options[0];
            string PackageHash = string.Empty;

            if (!File.Exists(PackagePath))
            {
                Console.WriteLine(" > Invalid package.json path. Please specify full absolute path, including package.json .");
                return;
            }

            using (MD5 MD5Instance = MD5.Create())
            {
                using (FileStream Stream = File.OpenRead(PackagePath))
                {
                    PackageHash = string.Join(string.Empty, MD5Instance.ComputeHash(Stream).Select(x => x.ToString("x2")));
                }
            }

            Console.WriteLine(" > Hash: " + PackageHash);

            string CacheArchivePath = Path.Combine(Path.GetDirectoryName(PackagePath), "yarn-pre-cache-" + PackageHash + ".zip");

            Console.WriteLine("Accessing Azure...");

            CloudBlobClient AzureBlobClient;
            CloudBlobContainer AzureBlobContainer;
            CloudBlockBlob AzureBlockBlob;

            try
            {
                AzureBlobClient = CloudStorageAccount.Parse(Options[1]).CreateCloudBlobClient();
                AzureBlobContainer = AzureBlobClient.GetContainerReference(Options[2]);
                AzureBlockBlob = AzureBlobContainer.GetBlockBlobReference(Path.GetFileName(CacheArchivePath));
            }
            catch (Exception e)
            {
                Console.WriteLine(" > Failed to access Azure. Exception message:");
                Console.WriteLine(e.Message);
                return;
            }

            Console.WriteLine(" > Connected to Azure.");

            bool AzureBlockBlobExists = await AzureBlockBlob.ExistsAsync();

            if (AzureBlockBlobExists)
            {
                Console.WriteLine("Downloading pre-cached archive from Azure...");

                await AzureBlockBlob.DownloadToFileAsync(CacheArchivePath, FileMode.CreateNew);

                Console.WriteLine("Cleaning up...");

                DirectoryInfo CacheDirectoryInfo = new DirectoryInfo(YarnCachePath);

                foreach (FileInfo File in CacheDirectoryInfo.GetFiles())
                    File.Delete();

                foreach (DirectoryInfo Directory in CacheDirectoryInfo.GetDirectories())
                    Directory.Delete(true);

                Console.WriteLine("Uncompressing pre-cached archive...");

                ZipFile.ExtractToDirectory(CacheArchivePath, YarnCachePath);

                File.Delete(CacheArchivePath);
            }

            Console.WriteLine("Installing and building Yarn packages...");

            Process YarnInstallProcess = Process.Start(new ProcessStartInfo("cmd", "/c " + (Options.Length == 4 ? Options[4] : "yarn") + " install")
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(PackagePath)
            });

            YarnInstallProcess.WaitForExit();

            Console.WriteLine(" > Process output:");
            Console.WriteLine(YarnInstallProcess.StandardOutput.ReadToEnd().TrimEnd('\r', '\n'));

            if (!AzureBlockBlobExists)
            {
                Console.WriteLine("Compressing Yarn cache...");

                ZipFile.CreateFromDirectory(YarnCachePath, CacheArchivePath);

                Console.WriteLine("Uploading pre-cached archive to Azure...");

                await AzureBlockBlob.UploadFromFileAsync(CacheArchivePath);

                Console.WriteLine("Cleaning up...");

                File.Delete(CacheArchivePath);
            }

            Console.WriteLine(" > Finished.");
        }
    }
}
