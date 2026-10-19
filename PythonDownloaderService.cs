using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PythonRunnerApp.Services;

public class PythonDownloaderService
{
    private readonly HttpClient _httpClient;

    public PythonDownloaderService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PythonRunnerApp/1.0");
    }

    public async Task<bool> DownloadAndExtractPython(string targetDirectory, string? downloadUrl = null, IProgress<string>? progress = null)
    {
        try
        {
            var urlToDownload = downloadUrl;
            if (string.IsNullOrEmpty(urlToDownload))
            {
                progress?.Report("Fetching latest available Python version dynamically...");
                var versionService = new PythonVersionService();
                var versions = await versionService.GetAvailableWindowsVersionsAsync();
                urlToDownload = versions.FirstOrDefault()?.DownloadUrl;

                if (string.IsNullOrEmpty(urlToDownload))
                {
                    progress?.Report("Could not dynamically resolve any Python release.");
                    return false;
                }
            }

            progress?.Report("Starting Python download...");
            Directory.CreateDirectory(targetDirectory);

            var archivePath = Path.Combine(targetDirectory, "python-standalone.tar.gz");

            progress?.Report("Downloading portable Python (standalone with tkinter)...");
            using (var response = await _httpClient.GetAsync(urlToDownload, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(archivePath, FileMode.Create))
                {
                    await stream.CopyToAsync(fileStream);
                }
            }

            progress?.Report("Extracting Python (this may take a minute)...");

            var startInfo = new ProcessStartInfo
            {
                FileName = "tar.exe",
                Arguments = $"-xf \"{archivePath}\" -C \"{targetDirectory}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                progress?.Report("Failed to start tar extraction process.");
                return false;
            }

            await process.WaitForExitAsync();

            try { File.Delete(archivePath); } catch { }

            if (process.ExitCode != 0)
            {
                progress?.Report($"Python extraction failed with exit code: {process.ExitCode}");
                return false;
            }

            progress?.Report("Python setup completed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"Error: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}