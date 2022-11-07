using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO.Compression;
using SystemFile = System.IO.File;

namespace Sonksuru.Controllers
{
    public class HomeController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            IWebHostEnvironment env,
            ILogger<HomeController> logger)
        {
            _env = env;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<FileContentResult> UploadFile(
            List<IFormFile> files,
            CancellationToken cancellationToken)
        {
            var directory = CreateDirectory("uploads");
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var file in files)
                {
                    var untrustedFileName = file.FileName;
                    var filePathExtension = Path.GetExtension(untrustedFileName);

                    //var trustedFileNameForDisplay = WebUtility.HtmlEncode(untrustedFileName);
                    var trustedFileNameForFileStorage = Path.GetRandomFileName();
                    var path = Path.Combine(directory, trustedFileNameForFileStorage);

                    await using (var stream = System.IO.File.Create(path))
                    {
                        await file.CopyToAsync(stream, cancellationToken);
                    }
                    var (encryptedFile, decryptedFile) = await ProcessSonksuruApiAsync(path, cancellationToken);

                    var originalFileEntry = archive.CreateEntry($"original{filePathExtension}");
                    await using (var streamWriter = new StreamWriter(originalFileEntry.Open()))
                    {
                        await streamWriter.WriteAsync(await SystemFile.ReadAllTextAsync(path, cancellationToken));
                    }

                    var encryptedFileEntry = archive.CreateEntry($"encryptedFile{filePathExtension}");
                    await using (var streamWriter = new StreamWriter(encryptedFileEntry.Open()))
                    {
                        await streamWriter.WriteAsync(encryptedFile);
                    }

                    var decryptedFileEntry = archive.CreateEntry($"decryptedFile{filePathExtension}");
                    await using (var streamWriter = new StreamWriter(decryptedFileEntry.Open()))
                    {
                        await streamWriter.WriteAsync(decryptedFile);
                    }
                }
            }

            return File(memoryStream.ToArray(), "application/zip", "files.zip");
        }

        private async Task<(string encryptedFile, string decryptedFile)> ProcessSonksuruApiAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                const string outputFile = "fileout.txt";
                var encryptedFileOutPath = Path.Combine(CreateDirectory("sonksuru"), outputFile);

                _logger.LogInformation("Starting encryption");
                var encryptedFile = await RunCommandAsync(filePath, encryptedFileOutPath, SonksuruAction.Encrypt, cancellationToken);
                _logger.LogInformation("Finished encryption");

                var fileName = Path.GetFileName(filePath);
                var decryptedFileOutPath = Path.Combine(CreateDirectory("sonksuru"), fileName);

                _logger.LogInformation("Starting decryption");
                var decryptedFile = await RunCommandAsync(encryptedFileOutPath, decryptedFileOutPath, SonksuruAction.Decrypt, cancellationToken);
                _logger.LogInformation("Finished decryption");
                //var decryptedFile = await DecryptAsync(filePath, fileOutPath, cancellationToken);

                return (encryptedFile, decryptedFile);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        private string CreateDirectory(string folder)
        {
            var directoryPath = Path.Combine(_env.ContentRootPath, folder);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            return directoryPath;
        }

        private async Task<string> RunCommandAsync(string filePath, string outputPath, SonksuruAction sonksuruAction, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting encryption");
            var bashScriptPath = Path.Combine(_env.ContentRootPath, "sonksuru.bash");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "/usr/bin/bash",
                    Arguments = $"{bashScriptPath} {(int)sonksuruAction} {filePath}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            process.Start();

            var standardErrorTask = process.StandardError.ReadToEndAsync();
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            await Task.WhenAll(standardErrorTask, standardOutputTask);
            var error = standardErrorTask.Result;
            var output = standardOutputTask.Result;

            await Task.WhenAll(
                process.WaitForExitAsync(cancellationToken),
                SystemFile.WriteAllTextAsync(outputPath, output, cancellationToken));

            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogError(error);
                throw new InvalidOperationException(error);
            }

            _logger.LogInformation("Finished encryption");
            return output;
        }
    }
}