using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PythonRunnerApp.Services;

public class EmbeddedPythonRunner : IPythonRunner
{
    private CancellationTokenSource? _cts;
    public event EventHandler<ExecutionOutputEventArgs>? OutputReceived;

    public async Task<int> RunScriptAsync(string scriptPath, string arguments = "")
    {
        _cts = new CancellationTokenSource();

        try
        {
            OutputReceived?.Invoke(this, new ExecutionOutputEventArgs("Starting embedded script execution...\n", "[INFO]"));

            if (!File.Exists(scriptPath))
            {
                OutputReceived?.Invoke(this, new ExecutionOutputEventArgs($"Script not found: {scriptPath}\n", "[ERROR]"));
                return 1;
            }

            var scriptCode = await File.ReadAllTextAsync(scriptPath, _cts.Token);

            return await Task.Run(() =>
            {
                try
                {
                    using var stringWriter = new StringWriter();
                    var originalOut = Console.Out;
                    Console.SetOut(stringWriter);

                    try
                    {
                        OutputReceived?.Invoke(this, new ExecutionOutputEventArgs($"Executing: {Path.GetFileName(scriptPath)}\n", "[SYSTEM]"));
                    }
                    finally
                    {
                        Console.SetOut(originalOut);
                    }

                    return 0;
                }
                catch (Exception ex)
                {
                    OutputReceived?.Invoke(this, new ExecutionOutputEventArgs($"Execution error: {ex.Message}\n", "[ERROR]"));
                    return -1;
                }
            }, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            OutputReceived?.Invoke(this, new ExecutionOutputEventArgs("Script execution cancelled.\n", "[SYSTEM]"));
            return -1;
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(this, new ExecutionOutputEventArgs($"Runtime error: {ex.Message}\n", "[ERROR]"));
            return -1;
        }
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        OutputReceived?.Invoke(this, new ExecutionOutputEventArgs("Stop signal sent.\n", "[SYSTEM]"));
        return Task.CompletedTask;
    }
}
