using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.DataMovement;
using SceneSkope.Utilities.CommandLineApplications;
using Serilog;

namespace UploadFiles
{
    internal class Program : ApplicationBase<Arguments>
    {
        private static void Main(string[] args) => new Program().ApplicationMain(args);
        protected override async Task RunAsync(Arguments arguments, CancellationToken ct)
        {
            var container = GetContainer(arguments);
            var originalLog = Log.Logger;
            var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
            var modifiedLogger = originalLog
                .ForContext("Version", version)
                .ForContext("StorageAccount", arguments.AccountName)
                .ForContext("StorageContainer", arguments.ContainerName);
            
            Log.Logger = modifiedLogger;
            await container.CreateIfNotExistsAsync(BlobContainerPublicAccessType.Off, null, null, ct).ConfigureAwait(false);
            Log.Information("Looking for matching files");

            var allFiles = GetFilesToTransfer(arguments);
            Log.Information("Found {Count} files that match", allFiles.Count);
            var transferTasks = CreateTransferTasks(container, allFiles, ct);
            await WaitForAllTransfersToFinishAsync(allFiles, transferTasks, ct).ConfigureAwait(false);
            Log.Logger = originalLog;
            Log.CloseAndFlush();
        }

        private static async Task WaitForAllTransfersToFinishAsync(List<(FileInfo file, Match match)> allFiles, List<Task> transferTasks,
            CancellationToken ct)
        {
            var totalCount = allFiles.Count;
            var finishedCount = 0;
            var skippedCount = 0;
            var successCount = 0;
            var errorCount = 0;
            while (transferTasks.Count > 0)
            {
                var finished = await Task.WhenAny(transferTasks).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                finishedCount++;
                var index = transferTasks.IndexOf(finished);
                var (file, match) = allFiles[index];
                allFiles.RemoveAt(index);
                transferTasks.RemoveAt(index);
                if (finished.IsFaulted)
                {
                    switch (finished.Exception.InnerException)
                    {
                        case TransferSkippedException tse:
                            Log.Debug("Skipped {File}", file.Name);
                            skippedCount++;
                            break;
                        default:
                            Log.Warning(finished.Exception, "Error in {File}: {Exception}", file.Name, finished.Exception.Message);
                            errorCount++;
                            break;
                    }
                }
                else
                {
                    Log.Information("Transferred {File} ok", file.Name);
                    successCount++;
                }
            }
            Log.Information("Transferred {SuccessCount} ok, skipped {SkippedCount}, had {ErrorsCount} errors",
                successCount, skippedCount, errorCount);
        }

        private static List<Task> CreateTransferTasks(CloudBlobContainer container, List<(FileInfo file, Match match)> allFiles, CancellationToken ct)
        {
            TransferManager.Configurations.ParallelOperations = 64;
            var transferTasks = new List<Task>(allFiles.Count);

            foreach (var (file, match) in allFiles)
            {
                var context = new SingleTransferContext
                {
                    ShouldOverwriteCallback = ShouldOverWriteCallback
                };
                var uploadOptions = new UploadOptions();
                var targetName = file.Name;
                var targetBlob = container.GetBlockBlobReference(targetName);
                var task = TransferManager.UploadAsync(file.FullName, targetBlob, uploadOptions, context, ct);
                transferTasks.Add(task);
            }

            return transferTasks;
        }

        private static List<(FileInfo file, Match match)> GetFilesToTransfer(Arguments arguments)
        {
            var inputDirectory = new DirectoryInfo(arguments.InputDirectory);
            var matchingRegex = new Regex(arguments.MatchingRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var allFiles = inputDirectory
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Select(file =>
                {
                    var match = matchingRegex.Match(file.Name);
                    return (file, match);
                })
                .Where(fm => fm.match.Success)
                .OrderBy(fm => fm.match.Groups["sorter"].Value)
                .ToList();
            return allFiles;
        }

        private static CloudBlobContainer GetContainer(Arguments arguments)
        {
            var credentials = new StorageCredentials(arguments.AccountName, arguments.KeyValue);
            var account = new CloudStorageAccount(credentials, true);
            var client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(arguments.ContainerName);
            return container;
        }

        private static bool ShouldOverWriteCallback(object source, object destination)
        {
            var sourceFileName = (string)source;
            var destinationBlob = (CloudBlockBlob)destination;
            var sourceFile = new FileInfo(sourceFileName);
            if (sourceFile.Length == 0)
            {
                Log.Information("Not transferring zero length {File}", sourceFileName);
                return false;
            }
            else
            {
                var sourceMd5 = Task.Factory.StartNew(() => CalculateMD5Async(sourceFileName, default)).Unwrap().GetAwaiter().GetResult();
                return sourceMd5 != destinationBlob.Properties.ContentMD5;
            }
        }

        public static async Task<string> CalculateMD5Async(string fullFileName, CancellationToken ct)
        {
            var block = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                using (var md5 = MD5.Create())
                {
                    using (var stream = new FileStream(fullFileName, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
                    {
                        int length;
                        while ((length = await stream.ReadAsync(block, 0, block.Length).ConfigureAwait(false)) > 0)
                        {
                            md5.TransformBlock(block, 0, length, null, 0);
                            ct.ThrowIfCancellationRequested();
                        }
                        md5.TransformFinalBlock(block, 0, 0);
                    }
                    var hash = md5.Hash;
                    return Convert.ToBase64String(hash);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(block);
            }
        }
    }
}
