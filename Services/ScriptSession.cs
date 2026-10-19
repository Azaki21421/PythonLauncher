using System;
using System.Collections.Generic;

namespace PythonRunnerApp.Services;

public class ScriptSession
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public string ScriptPath { get; set; } = "";
    public string Title => string.IsNullOrEmpty(ScriptPath) ? "New Script" : System.IO.Path.GetFileName(ScriptPath);
    public string Output { get; set; } = "";
    public List<string> DetectedDependencies { get; set; } = new();
    public bool IsRunning { get; set; }
    public IPythonRunner? Runner { get; set; }

    public event Action<string, string>? OutputAppended;
    public event Action<int>? ExecutionCompleted;

    public void Append(string text, string prefix = "")
    {
        string formatted = string.IsNullOrEmpty(prefix) ? text : $"{prefix} {text}";
        Output += formatted;
        OutputAppended?.Invoke(text, prefix);
    }

    public void NotifyCompleted(int exitCode)
    {
        IsRunning = false;
        ExecutionCompleted?.Invoke(exitCode);
    }
}
