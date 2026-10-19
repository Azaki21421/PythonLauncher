using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PythonRunnerApp.Services;

public class DependencyAnalyzerService
{
    private static readonly HashSet<string> _builtinModulesCache = new();
    private static readonly ConcurrentDictionary<string, string> _onlineModuleToPackageCache = new();
    private static bool _onlineMappingLoaded = false;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public class DependencyInfo
    {
        public string ModuleName { get; set; } = "";
        public string PackageName { get; set; } = "";
        public string ImportLine { get; set; } = "";
        public int LineNumber { get; set; }
        public string ImportType { get; set; } = "";
    }

    public async Task<List<DependencyInfo>> AnalyzeScriptDependencies(string scriptPath, string? pythonExecutablePath = null)
    {
        var dependencies = new List<DependencyInfo>();

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Script file not found: {scriptPath}");
        }

        var scriptDir = Path.GetDirectoryName(scriptPath) ?? "";

        var requirementsPath = Path.Combine(scriptDir, "requirements.txt");
        if (File.Exists(requirementsPath))
        {
            try
            {
                var reqLines = await File.ReadAllLinesAsync(requirementsPath);
                int lineNum = 0;
                foreach (var rLine in reqLines)
                {
                    lineNum++;
                    var trimmed = rLine.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("-"))
                        continue;

                    var pkgName = Regex.Split(trimmed, @"[=><~! ]")[0].Trim();
                    if (!string.IsNullOrEmpty(pkgName))
                    {
                        dependencies.Add(new DependencyInfo
                        {
                            ModuleName = pkgName,
                            PackageName = pkgName,
                            ImportLine = trimmed,
                            LineNumber = lineNum,
                            ImportType = "requirements.txt"
                        });
                    }
                }

                if (dependencies.Any())
                {
                    return dependencies.GroupBy(d => d.PackageName)
                                      .Select(g => g.First())
                                      .OrderBy(d => d.PackageName)
                                      .ToList();
                }
            }
            catch
            {
            }
        }

        var stdlibModules = await GetStandardLibraryModulesAsync(pythonExecutablePath);

        _ = EnsureOnlineMappingLoadedAsync();

        var content = await File.ReadAllTextAsync(scriptPath);
        var lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var deps = ParseImportLine(line, i + 1);
            foreach (var dep in deps)
            {
                if (stdlibModules.Contains(dep.ModuleName))
                    continue;

                if (!string.IsNullOrEmpty(scriptDir))
                {
                    var localPyFile = Path.Combine(scriptDir, $"{dep.ModuleName}.py");
                    var localPyPackage = Path.Combine(scriptDir, dep.ModuleName);
                    if (File.Exists(localPyFile) || Directory.Exists(localPyPackage))
                        continue;
                }

                dependencies.Add(dep);
            }
        }

        return dependencies.GroupBy(d => d.ModuleName)
                          .Select(g => g.First())
                          .OrderBy(d => d.ModuleName)
                          .ToList();
    }

    private async Task<HashSet<string>> GetStandardLibraryModulesAsync(string? pythonExecutablePath)
    {
        if (_builtinModulesCache.Count > 0)
            return _builtinModulesCache;

        if (!string.IsNullOrEmpty(pythonExecutablePath) && File.Exists(pythonExecutablePath))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pythonExecutablePath,
                    Arguments = "-c \"import sys; print(','.join(sys.stdlib_module_names if hasattr(sys, 'stdlib_module_names') else sys.builtin_module_names))\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    var output = await proc.StandardOutput.ReadToEndAsync();
                    await proc.WaitForExitAsync();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        var names = output.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var name in names)
                        {
                            _builtinModulesCache.Add(name.Trim());
                        }
                    }
                }
            }
            catch
            {
            }
        }

        return _builtinModulesCache;
    }


    private static async Task EnsureOnlineMappingLoadedAsync()
    {
        if (_onlineMappingLoaded) return;

        try
        {
            var text = await _httpClient.GetStringAsync("https://raw.githubusercontent.com/bndr/pipreqs/master/pipreqs/mapping");
            var lines = text.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || !trimmed.Contains(':'))
                    continue;

                var parts = trimmed.Split(':', 2);
                if (parts.Length == 2)
                {
                    var mod = parts[0].Trim().ToLowerInvariant();
                    var pkg = parts[1].Trim();
                    if (!string.IsNullOrEmpty(mod) && !string.IsNullOrEmpty(pkg))
                    {
                        _onlineModuleToPackageCache[mod] = pkg;
                    }
                }
            }

            _onlineMappingLoaded = true;
        }
        catch
        {
        }
    }


    public async Task<string> ResolvePackageNameAsync(string moduleName)
    {
        await EnsureOnlineMappingLoadedAsync();

        var normalized = moduleName.ToLowerInvariant();
        if (_onlineModuleToPackageCache.TryGetValue(normalized, out var mappedPkg))
        {
            return mappedPkg;
        }

        return moduleName;
    }


    public static List<string> GetPackageCandidates(string moduleName)
    {
        var candidates = new List<string> { moduleName };

        var pyPrefix = $"py{moduleName}";
        if (!candidates.Contains(pyPrefix, StringComparer.OrdinalIgnoreCase))
            candidates.Add(pyPrefix);

        var pythonSuffix = $"{moduleName}-python";
        if (!candidates.Contains(pythonSuffix, StringComparer.OrdinalIgnoreCase))
            candidates.Add(pythonSuffix);

        return candidates;
    }

    private List<DependencyInfo> ParseImportLine(string line, int lineNumber)
    {
        var dependencies = new List<DependencyInfo>();

        var importPattern = @"^import\s+([a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)*)\s*(?:as\s+[a-zA-Z_][a-zA-Z0-9_]*)?";
        var importMatch = Regex.Match(line, importPattern);

        if (importMatch.Success)
        {
            var moduleName = importMatch.Groups[1].Value.Split('.')[0];
            dependencies.Add(CreateDependencyInfo(moduleName, line, lineNumber, "import"));
        }

        var fromImportPattern = @"^from\s+([a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)*)\s+import";
        var fromImportMatch = Regex.Match(line, fromImportPattern);

        if (fromImportMatch.Success)
        {
            var moduleName = fromImportMatch.Groups[1].Value.Split('.')[0];
            dependencies.Add(CreateDependencyInfo(moduleName, line, lineNumber, "from_import"));
        }

        var multiImportPattern = @"^import\s+([a-zA-Z_][a-zA-Z0-9_.,\s]+)";
        var multiImportMatch = Regex.Match(line, multiImportPattern);

        if (multiImportMatch.Success && !importMatch.Success)
        {
            var modules = multiImportMatch.Groups[1].Value.Split(',');
            foreach (var module in modules)
            {
                var cleanModule = module.Trim().Split('.')[0];
                if (!string.IsNullOrWhiteSpace(cleanModule))
                {
                    dependencies.Add(CreateDependencyInfo(cleanModule, line, lineNumber, "import"));
                }
            }
        }

        return dependencies;
    }

    private DependencyInfo CreateDependencyInfo(string moduleName, string importLine, int lineNumber, string importType)
    {
        return new DependencyInfo
        {
            ModuleName = moduleName,
            PackageName = moduleName,
            ImportLine = importLine,
            LineNumber = lineNumber,
            ImportType = importType
        };
    }

    public List<DependencyInfo> GetExternalDependencies(List<DependencyInfo> allDependencies)
    {
        return allDependencies.ToList();
    }
}