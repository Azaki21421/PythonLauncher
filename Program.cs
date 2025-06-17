using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Collections.Generic;

class Program
{
    private const string PythonBaseDownloadUrl = "https://www.python.org/ftp/python/";
    private const string EmbeddedPythonFolderName = "EmbeddedPython";

    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting application setup...");

        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string embeddedPythonPath = Path.Combine(baseDirectory, EmbeddedPythonFolderName);
        string pythonExecutablePath = Path.Combine(embeddedPythonPath, "python.exe");

        (string pythonDownloadUrl, string pythonArchiveName, string pythonVersion) = await GetLatestPythonEmbedDownloadUrlAndName();

        if (string.IsNullOrEmpty(pythonDownloadUrl))
        {
            Console.WriteLine("Error: Could not determine the latest Python embed download URL. Exiting.");
            Console.ReadKey();
            return;
        }
        Console.WriteLine($"Determined latest Python embed version: {pythonVersion} ({pythonArchiveName})");

        bool pythonInstalledAndMatchesVersion = false;
        if (File.Exists(pythonExecutablePath))
        {
            string majorMinorVersion = Regex.Match(pythonVersion, @"(\d+\.\d+)").Groups[1].Value;
            string expectedPthPrefix = $"python{majorMinorVersion.Replace(".", "")}";

            string existingPthFile = Directory.EnumerateFiles(embeddedPythonPath, $"{expectedPthPrefix}*._pth").FirstOrDefault();

            if (existingPthFile != null)
            {
                Console.WriteLine($"Python interpreter found matching {majorMinorVersion} series. Skipping download.");
                pythonInstalledAndMatchesVersion = true;
            }
            else
            {
                Console.WriteLine("Existing Python interpreter found, but its version does not match the latest determined version or _pth file is missing. Forcing re-download.");
            }
        }

        if (!pythonInstalledAndMatchesVersion)
        {
            Console.WriteLine($"Python interpreter not found or mismatch at '{pythonExecutablePath}'. Attempting to download and set up Python...");
            await DownloadAndExtractPython(baseDirectory, embeddedPythonPath, pythonDownloadUrl, pythonArchiveName);
            if (!File.Exists(pythonExecutablePath))
            {
                Console.WriteLine("Error: Failed to set up Python. Exiting.");
                Console.ReadKey();
                return;
            }
        }
        ConfigurePythonPthFile(embeddedPythonPath);

        string sourceDepsScriptPath = Path.Combine(baseDirectory, "install_deps.py");
        string targetDepsScriptPath = Path.Combine(embeddedPythonPath, "install_deps.py");

        if (File.Exists(sourceDepsScriptPath))
        {
            try
            {
                File.Copy(sourceDepsScriptPath, targetDepsScriptPath, true);
                Console.WriteLine($"Copied 'install_deps.py' to '{targetDepsScriptPath}'");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error copying 'install_deps.py': {ex.Message}");
                Console.ReadKey();
                return;
            }
        }
        else
        {
            Console.WriteLine($"Error: 'install_deps.py' not found in the application's base directory ('{sourceDepsScriptPath}').");
            Console.WriteLine("Please ensure 'install_deps.py' is placed alongside your C# executable.");
            Console.ReadKey();
            return;
        }

        Console.Write("Enter the Python script filename (e.g., script.py): ");
        string scriptFileName = Console.ReadLine();
        string scriptPath = scriptFileName;

        if (!File.Exists(scriptPath))
        {
            Console.WriteLine($"Error: Python script '{scriptPath}' not found!");
            Console.WriteLine("Please ensure the script path is correct.");
            Console.ReadKey();
            return;
        }

        Console.WriteLine("\n--- Installing/Updating Python Dependencies ---");
        RunPythonScriptInNewWindow(pythonExecutablePath, targetDepsScriptPath, scriptPath);
        Console.WriteLine("--- Dependency setup complete ---\n");

        Console.WriteLine($"--- Running Python script: {scriptFileName} ---");
        RunPythonScriptInNewWindow(pythonExecutablePath, scriptPath);
        Console.WriteLine("--- Python script execution finished ---");

        Console.WriteLine("\nPress any key to exit.");
        Console.ReadKey();
    }

    private static async Task DownloadAndExtractPython(string baseDir, string targetDir, string pythonDownloadUrl, string pythonArchiveName)
    {
        Console.WriteLine($"Attempting to download Python from: {pythonDownloadUrl}");
        string downloadPath = Path.Combine(baseDir, pythonArchiveName);

        try
        {
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5);

                var response = await client.GetAsync(pythonDownloadUrl);
                response.EnsureSuccessStatusCode();

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await contentStream.CopyToAsync(fileStream);
                }
            }
            Console.WriteLine($"Python downloaded successfully to: {downloadPath}");

            Console.WriteLine($"Extracting Python to: {targetDir}");
            if (Directory.Exists(targetDir))
            {
                Console.WriteLine($"Deleting existing directory: {targetDir}");
                Directory.Delete(targetDir, true);
            }
            ZipFile.ExtractToDirectory(downloadPath, targetDir);
            Console.WriteLine("Python extracted successfully.");

            File.Delete(downloadPath);
            Console.WriteLine("Downloaded Python archive deleted.");
        }
        catch (HttpRequestException e)
        {
            Console.Error.WriteLine($"Download error: {e.Message}");
            if (e.InnerException != null)
                Console.Error.WriteLine($"Inner exception: {e.InnerException.Message}");
        }
        catch (IOException e)
        {
            Console.Error.WriteLine($"File I/O error during download or extraction: {e.Message}");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"An unexpected error occurred during Python download/extraction: {e.Message}");
        }
    }

    private static void ConfigurePythonPthFile(string embeddedPythonPath)
    {
        try
        {
            string pthFile = Directory.EnumerateFiles(embeddedPythonPath, "python*._pth").FirstOrDefault();

            if (pthFile == null)
            {
                Console.Error.WriteLine("Warning: Python ._pth file not found in embedded directory. Pip may not work correctly.");
                return;
            }

            Console.WriteLine($"Configuring ._pth file: {pthFile}");
            string[] lines = File.ReadAllLines(pthFile);
            bool changed = false;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "#import site")
                {
                    lines[i] = "import site";
                    changed = true;
                    Console.WriteLine("Uncommented 'import site' in ._pth file.");
                    break;
                }
            }

            if (!changed)
            {
                if (!lines.Any(line => line.Trim() == "import site"))
                {
                    Array.Resize(ref lines, lines.Length + 1);
                    lines[lines.Length - 1] = "import site";
                    changed = true;
                    Console.WriteLine("Added 'import site' to ._pth file.");
                }
                else
                {
                    Console.WriteLine("'import site' already enabled in ._pth file.");
                }
            }

            if (changed)
            {
                File.WriteAllLines(pthFile, lines);
                Console.WriteLine("._pth file updated successfully.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error configuring Python ._pth file: {ex.Message}");
        }
    }

    private static void RunPythonScriptInNewWindow(string pythonPath, string scriptPath, string scriptArgs = "")
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"\"{scriptPath}\" {scriptArgs}",
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = true,
            CreateNoWindow = false
        };

        using (Process process = new Process { StartInfo = psi })
        {
            try
            {
                process.Start();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Console.Error.WriteLine($"Python process for '{Path.GetFileName(scriptPath)}' exited with code: {process.ExitCode}");
                    Console.Error.WriteLine("Check the new console window for Python errors/output.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"An error occurred while launching Python script '{Path.GetFileName(scriptPath)}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Represents a found Python version folder on the FTP server.
    /// </summary>
    private class PythonVersionFolder
    {
        public string FullVersionString { get; set; } // e.g., "3.14.0b2", "3.13.1"
        public Version ParsedBaseVersion { get; set; } // System.Version object for numeric comparison (e.g., 3.14.0)
        public string PreReleaseSuffix { get; set; } // e.g., "b2", "a7", "rc1", or empty
        public bool IsPreRelease { get; set; }
        public DateTime ModificationDate { get; set; } // Date of the folder itself, if available from listing, or parsed from file dates
        public string FolderUrl { get; set; }
    }

    /// <summary>
    /// Finds the latest Python embeddable zip for Windows x64.
    /// Steps:
    /// 1. Scans the main Python FTP page for all version *folders* (e.g., 3.14.0/, 3.13.1/).
    /// 2. Selects the single best (latest) version folder based on version number and pre-release status.
    /// 3. Navigates into that specific best folder and searches for the embeddable/embed AMD64 .zip file.
    /// </summary>
    /// <returns>A tuple of (download URL, archive filename, extracted version string).</returns>
    private static async Task<(string, string, string)> GetLatestPythonEmbedDownloadUrlAndName()
    {
        using (HttpClient client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(60);

            try
            {
                Console.WriteLine($"Searching for Python version folders in {PythonBaseDownloadUrl}");
                string mainFtpContent = await client.GetStringAsync(PythonBaseDownloadUrl);

                var folderEntryRegex = new Regex(
                    @"<a\s+href=[""'](\d+\.\d+\.\d+(?:a|b|rc)?\d*)\/[""']>.*?<\/a>\s*(?:[\d]{2}-[A-Za-z]{3}-[\d]{4}\s+[\d]{2}:[\d]{2})?", // Optional date
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                );

                var possibleVersionFolders = new List<PythonVersionFolder>();

                foreach (Match match in folderEntryRegex.Matches(mainFtpContent))
                {
                    string currentFullVersionString = match.Groups[1].Value;
                    string dateString = match.Groups[2].Success ? match.Groups[2].Value : null;

                    string baseVersionString = currentFullVersionString;
                    string preReleaseSuffix = "";
                    bool isPreRelease = false;

                    Match preReleaseMatch = Regex.Match(currentFullVersionString, @"(\d+\.\d+\.\d+)([ab]|rc)(\d*)");
                    if (preReleaseMatch.Success)
                    {
                        baseVersionString = preReleaseMatch.Groups[1].Value;
                        preReleaseSuffix = preReleaseMatch.Groups[2].Value + preReleaseMatch.Groups[3].Value;
                        isPreRelease = true;
                    }

                    Version parsedBaseVersion;
                    try
                    {
                        parsedBaseVersion = new Version(baseVersionString);
                    }
                    catch (FormatException)
                    {
                        Console.WriteLine($"Warning: Could not parse base version string '{baseVersionString}' from '{currentFullVersionString}'. Skipping.");
                        continue;
                    }

                    DateTime folderModDate = DateTime.MinValue;
                    if (dateString != null)
                    {
                        try
                        {
                            folderModDate = DateTime.ParseExact(dateString, "dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture);
                        }
                        catch (FormatException)
                        {
                            Console.WriteLine($"Warning: Could not parse date '{dateString}' for folder '{currentFullVersionString}/'. Using MinValue.");
                        }
                    }

                    possibleVersionFolders.Add(new PythonVersionFolder
                    {
                        FullVersionString = currentFullVersionString,
                        ParsedBaseVersion = parsedBaseVersion,
                        PreReleaseSuffix = preReleaseSuffix,
                        IsPreRelease = isPreRelease,
                        ModificationDate = folderModDate,
                        FolderUrl = $"{PythonBaseDownloadUrl}{currentFullVersionString}/"
                    });
                }

                var bestVersionFolder = possibleVersionFolders
                    .OrderByDescending(f => f.ParsedBaseVersion)
                    .ThenBy(f => f.IsPreRelease)
                    .ThenByDescending(f => f.PreReleaseSuffix)
                    .FirstOrDefault();

                if (bestVersionFolder == null)
                {
                    Console.Error.WriteLine("No suitable Python version folders found on the main FTP page.");
                    return (null, null, null);
                }

                Console.WriteLine($"Found latest Python version folder: {bestVersionFolder.FullVersionString}. Checking its directory: {bestVersionFolder.FolderUrl}");

                string versionDirContent = await client.GetStringAsync(bestVersionFolder.FolderUrl);

                // The regex must now allow for pre-release suffixes (like 'b2') in the file name,
                // even if the folder name is just the base version (e.g., '3.14.0/').
                // It now captures the actual version string from the filename,
                // which might include a pre-release suffix that was not in the folder name.
                var embedZipFileRegex = new Regex(
                    $@"<a\s+href=[""'](python-(\d+\.\d+\.\d+(?:a|b|rc)?\d*)-embed(?:dable)?-amd64\.zip)[""']>.*?<\/a>\s+([\d]{2}-[A-Za-z]{3}-[\d]{4}\s+[\d]{2}:[\d]{2})",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                );

                string bestEmbedZipFileName = null;
                string bestEmbedZipFullVersionString = null;
                DateTime latestEmbedZipDate = DateTime.MinValue;

                foreach (Match fileMatch in embedZipFileRegex.Matches(versionDirContent))
                {
                    string currentEmbedZipFileName = fileMatch.Groups[1].Value;
                    string currentFileFullVersionString = fileMatch.Groups[2].Value;
                    string dateString = fileMatch.Groups[3].Value;

                    DateTime currentFileModDate;
                    try
                    {
                        currentFileModDate = DateTime.ParseExact(dateString, "dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        Console.WriteLine($"Warning: Could not parse date '{dateString}' for file '{currentEmbedZipFileName}'. Skipping.");
                        continue;
                    }

                    if (bestEmbedZipFileName == null)
                    {
                        bestEmbedZipFileName = currentEmbedZipFileName;
                        bestEmbedZipFullVersionString = currentFileFullVersionString;
                        latestEmbedZipDate = currentFileModDate;
                    }
                    else if (currentEmbedZipFileName.Contains("-embeddable-") && !bestEmbedZipFileName.Contains("-embeddable-"))
                    {
                        bestEmbedZipFileName = currentEmbedZipFileName;
                        bestEmbedZipFullVersionString = currentFileFullVersionString;
                        latestEmbedZipDate = currentFileModDate;
                    }
                    else if (currentEmbedZipFileName.Contains("-embeddable-") == bestEmbedZipFileName.Contains("-embeddable-"))
                    {
                        if (currentFileModDate > latestEmbedZipDate)
                        {
                            bestEmbedZipFileName = currentEmbedZipFileName;
                            bestEmbedZipFullVersionString = currentFileFullVersionString;
                            latestEmbedZipDate = currentFileModDate;
                        }
                    }
                }

                if (bestEmbedZipFileName != null)
                {
                    string downloadUrl = $"{bestVersionFolder.FolderUrl}{bestEmbedZipFileName}";
                    return (downloadUrl, bestEmbedZipFileName, bestEmbedZipFullVersionString);
                }
                else
                {
                    Console.Error.WriteLine($"No suitable Python embed/embeddable amd64.zip file found for version {bestVersionFolder.FullVersionString} in {bestVersionFolder.FolderUrl}");
                    Console.Error.WriteLine("Attempting a more general search for any AMD64 zip in this folder as a fallback.");

                    var genericEmbedZipRegex = new Regex(
                        $@"href=[""'](python-{Regex.Escape(bestVersionFolder.FullVersionString)}\S*?-amd64\.zip)[""']",
                        RegexOptions.IgnoreCase
                    );
                    Match genericMatch = genericEmbedZipRegex.Match(versionDirContent);
                    if (genericMatch.Success)
                    {
                        string genericFileName = genericMatch.Groups[1].Value;
                        string genericFileFullVersionString = Regex.Match(genericFileName, @"python-(\d+\.\d+\.\d+(?:a|b|rc)?\d*)").Groups[1].Value;
                        string downloadUrl = $"{bestVersionFolder.FolderUrl}{genericFileName}";
                        Console.WriteLine($"Found a generic AMD64 zip: {genericFileName}. Using it as fallback.");
                        return (downloadUrl, genericFileName, genericFileFullVersionString);
                    }
                    return (null, null, null);
                }
            }
            catch (HttpRequestException e)
            {
                Console.Error.WriteLine($"HTTP request error when trying to find latest Python version from {PythonBaseDownloadUrl}: {e.Message}");
                return (null, null, null);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"An unexpected error occurred while determining latest Python version: {e.Message}");
                return (null, null, null);
            }
        }
    }
}