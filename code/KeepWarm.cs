using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace azure_blob_mover
{
    public static class KeepWarm
    {
        [FunctionName("KeepWarm")]
        public static void Run([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
        }
    }
}
