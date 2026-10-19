using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PythonRunnerApp.Services;

public class WindowsProcessPythonRunner : IPythonRunner
{
    private readonly string _pythonExecutablePath;
    private Process? _currentProcess;

    public event EventHandler<ExecutionOutputEventArgs>? OutputReceived;

    public WindowsProcessPythonRunner(string pythonExecutablePath)
    {
        _pythonExecutablePath = pythonExecutablePath;
    }

    public async Task<int> RunScriptAsync(string scriptPath, string arguments = "")
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _pythonExecutablePath,
            Arguments = $"\"{scriptPath}\" {arguments}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)
        };

        _currentProcess = new Process { StartInfo = startInfo };

        _currentProcess.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                OutputReceived?.Invoke(this, new ExecutionOutputEventArgs(e.Data + "\n"));
            }
        };

        _currentProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                OutputReceived?.Invoke(this, new ExecutionOutputEventArgs($"ERROR: {e.Data}\n", "[ERROR]"));
            }
        };

        _currentProcess.Start();
        _currentProcess.BeginOutputReadLine();
        _currentProcess.BeginErrorReadLine();

        await Task.Run(() => _currentProcess.WaitForExit());

        return _currentProcess.ExitCode;
    }

    public Task StopAsync()
    {
        try
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                _currentProcess.Kill();
                OutputReceived?.Invoke(this, new ExecutionOutputEventArgs("Script execution stopped.\n", "[SYSTEM]"));
            }
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(this, new ExecutionOutputEventArgs($"Error stopping process: {ex.Message}\n", "[ERROR]"));
        }

        return Task.CompletedTask;
    }
}
