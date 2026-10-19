using System.Text.Json;
using System.Text.RegularExpressions;

namespace PythonRunnerApp.Services;

public class PythonReleaseInfo
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string DisplayName => $"Python {Version}";
}

public class PythonVersionService
{
    private static readonly HttpClient _httpClient = new HttpClient();

    static PythonVersionService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PythonRunnerApp/1.0");
    }

    public async Task<List<PythonReleaseInfo>> GetAvailableWindowsVersionsAsync()
    {
        var versions = new List<PythonReleaseInfo>();

        var response = await _httpClient.GetStringAsync("https://api.github.com/repos/astral-sh/python-build-standalone/releases/latest");
        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        if (root.TryGetProperty("assets", out var assets))
        {
            var regex = new Regex(@"^cpython-([0-9]+\.[0-9]+\.[0-9]+(?:\w+)?)\+[0-9]+-x86_64-pc-windows-msvc-install_only\.tar\.gz$");

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var nameProp) && asset.TryGetProperty("browser_download_url", out var urlProp))
                {
                    var name = nameProp.GetString() ?? "";
                    var url = urlProp.GetString() ?? "";

                    var match = regex.Match(name);
                    if (match.Success)
                    {
                        var version = match.Groups[1].Value;
                        versions.Add(new PythonReleaseInfo
                        {
                            Version = version,
                            DownloadUrl = url
                        });
                    }
                }
            }
        }

        return versions.OrderByDescending(v => v.Version).ToList();
    }
}
