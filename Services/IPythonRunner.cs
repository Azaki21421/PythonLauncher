using System;
using System.IO;
using System.Threading.Tasks;

namespace PythonRunnerApp.Services;

public class ExecutionOutputEventArgs : EventArgs
{
    public string Data { get; }
    public string Prefix { get; }

    public ExecutionOutputEventArgs(string data, string prefix = "")
    {
        Data = data;
        Prefix = prefix;
    }
}

public interface IPythonRunner
{
    event EventHandler<ExecutionOutputEventArgs> OutputReceived;
    Task<int> RunScriptAsync(string scriptPath, string arguments = "");
    Task StopAsync();
}
