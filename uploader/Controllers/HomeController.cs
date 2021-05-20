using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SecureUpload.Web.Models;

namespace SecureUpload.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<HomeController> _logger;

        public HomeController(BlobServiceClient blobServiceClient,
                              ILogger<HomeController> logger)
        {
            _blobServiceClient = blobServiceClient;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> Upload()
        {
            var claimsIdentity = HttpContext.User.Identity as ClaimsIdentity;

            using var hashAlgo = MD5.Create(); //DevSkim: ignore DS126858 
            using var nameStream = new MemoryStream(Encoding.ASCII.GetBytes(claimsIdentity.Name));
            var hashBytes = await hashAlgo.ComputeHashAsync(nameStream);
            var hash = string.Join("", hashBytes.Select(x => x.ToString("x2")));

            var alias = Regex.Replace(Regex.Match(claimsIdentity.Name, "^([^@]*)").Value, @"[@\.\+\-]", "");

            var containerName = $"{alias}-{hash}";

            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            await container.CreateIfNotExistsAsync();

            var results = new List<(string time, string message)>();

            foreach (var file in Request.Form.Files)
            {
                var blobClient = container.GetBlobClient($"{Path.GetFileName(file.FileName)}");
                using var stream = file.OpenReadStream();
                await blobClient.UploadAsync(stream, overwrite: true);

                results.Add((DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), file.FileName));
            }

            return Json(results.Select(x => new { x.time, x.message }));
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
