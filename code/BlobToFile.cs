// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Azure.Storage.Files.Shares;
using Newtonsoft.Json.Linq;
using System.Net;
using Azure.Storage.Files.DataLake;
using Azure.Storage.Files.DataLake.Models;
using System.Linq;

namespace SecureUpload.Mover
{
    public static class BlobToFile
    {
        [FunctionName("BlobToFile")]
        public static async Task Run([EventGridTrigger] EventGridEvent eventGridEvent,
                                     ILogger log,
                                     ExecutionContext context)
        {
            var data = eventGridEvent.Data as JObject;
            var api = (string)data.GetValue("api");

            if (api.StartsWith("Delete"))
            {
                return;
            }

            log.LogInformation(eventGridEvent.Data.ToString());

            var configuration = new ConfigurationBuilder()
                                        .SetBasePath(context.FunctionAppDirectory)
                                        .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                                        .AddEnvironmentVariables()
                                        .Build();

            // var blobUri = new Uri((string)data.GetValue("blobUrl")); // ADLS
            var blobUri = new Uri((string)data.GetValue("url"));

            // https://asecpublic.blob.core.windows.net/user1/1.zip
            //         ^^^^^^^^^^ storage account
            // https://asecpublic.blob.core.windows.net/user1/1.zip
            //                                          ^^^^^ container
            // https://asecpublic.blob.core.windows.net/user1/1.zip
            //                                               ^ directory
            // https://asecpublic.blob.core.windows.net/user1/1.zip
            //                                                ^^^^^ filename
            var storageAccount = blobUri.Host.Substring(0, blobUri.Host.IndexOf('.'));
            var path = WebUtility.UrlDecode(blobUri.PathAndQuery.Substring(1));
            var container = path.Substring(0, path.IndexOf('/'));
            path = path.Substring(container.Length + 1);
            var lastIndexOfSlash = path.LastIndexOf('/');
            var directory = "/";
            var fileName = path;

            if (lastIndexOfSlash != -1)
            {
                directory = path.Substring(0, lastIndexOfSlash);
                fileName = path.Substring(lastIndexOfSlash + 1);
            }

            if (api == "PutBlob" || api == "PutBlockList")
            {
                var sourceConnection = configuration["StoragesPublic"];
                var destinationConnection = configuration["StoragesPrivate"];

                var blobService = new BlobServiceClient(sourceConnection);
                var blobContainer = blobService.GetBlobContainerClient(container);
                var blobClient = blobContainer.GetBlobClient(path);

                var fileService = new ShareServiceClient(destinationConnection);

                log.LogInformation($"get share client ({container})...");
                var shareClient = fileService.GetShareClient(container);
                await shareClient.CreateIfNotExistsAsync();

                log.LogInformation($"get file directory ({directory})...");
                var directoryClient = shareClient.GetDirectoryClient(directory);

                if (directory != "/" && !await directoryClient.ExistsAsync())
                {
                    await directoryClient.CreateAsync();

                    log.LogInformation($"get file share acl ({container})...");
                    var dataLakeService = new DataLakeServiceClient(sourceConnection);
                    var dataLakeFileSystem = dataLakeService.GetFileSystemClient(container);
                    var dataLakeDirectory = dataLakeFileSystem.GetDirectoryClient(".");
                    var dataLakeAcl = await dataLakeDirectory.GetAccessControlAsync();

                    var assignedAadUserAcls = dataLakeAcl
                                                    .Value
                                                    .AccessControlList
                                                    .Where(x => x.AccessControlType == AccessControlType.User)
                                                    .Select(x => new { AadUserId = x.EntityId, Permissions = x.Permissions });

                    foreach (var assignedAadUserAcl in assignedAadUserAcls)
                    {
                        log.LogInformation($"assign acl: user={assignedAadUserAcl.AadUserId}, permissions={assignedAadUserAcl.Permissions}");
                    }
                }

                log.LogInformation($"get file client ({fileName})...");
                var fileClient = directoryClient.GetFileClient(fileName);

                log.LogInformation($"downloading content...");
                var stream = await blobClient.OpenReadAsync();

                if (!await fileClient.ExistsAsync())
                {
                    log.LogInformation($"creating file...");
                    await fileClient.CreateAsync(stream.Length);
                }

                log.LogInformation($"uploading content...");
                await fileClient.UploadAsync(stream);

                log.LogInformation($"deleting source blob...");
                await blobClient.DeleteAsync();
            }
        }
    }
}
