using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO.Compression;
using SystemFile = System.IO.File;

namespace Sonksuru.Controllers;

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

                await using (var fs = SystemFile.Create(path, 4096, FileOptions.Asynchronous))
                {
                    await using var sw = new StreamWriter(fs);
                    await file.CopyToAsync(sw.BaseStream, cancellationToken);
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
            var basePath = CreateDirectory("files");
            var encryptedFileOutPath = Path.Combine(basePath, "encryptedFile");
            var decryptedFileOutPath = Path.Combine(basePath, "decryptedFile");

            _logger.LogInformation("Starting encryption");
            await RunCommandAsync($"sonksuru_api.bash {filePath} {encryptedFileOutPath} {decryptedFileOutPath}", cancellationToken);
            _logger.LogInformation("Finished encryption");

            var encryptedFileTask = SystemFile.ReadAllTextAsync(encryptedFileOutPath, cancellationToken);
            var decryptedFileTask = SystemFile.ReadAllTextAsync(decryptedFileOutPath, cancellationToken);

            await Task.WhenAll(encryptedFileTask, decryptedFileTask);

            return (encryptedFileTask.Result, decryptedFileTask.Result);
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

    private async Task RunCommandAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = "/usr/bin/bash",
                Arguments = arguments,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        _logger.LogInformation(process.StartInfo.Arguments);
        process.Start();

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);

        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogError(error);
            throw new InvalidOperationException(error);
        }
    }
}