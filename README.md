# Python Launcher (.NET MAUI)

A modern, cross-platform graphical application built with **.NET MAUI** that provides a portable, self-contained Python execution environment with zero system pollution.

## Features

- **Portable Python Environment:** Automatically downloads and configures clean, standalone Python runtimes (via `python-build-standalone`) directly to an isolated folder without requiring system Python or administrator rights.
- **Multi-Tab Execution:** Run multiple Python scripts simultaneously in isolated tabs. Each tab tracks its own script, dependencies, output log, and running process.
- **Detachable Windows (Multi-Window):** Detach any running tab into its own independent OS window, or drag tabs between windows to dock them back together.
- **Drag & Drop:**
  - Drag tabs to rearrange their order.
  - Drag a tab out of the tab bar to detach it into a new window.
  - Drag tabs across separate windows to transfer and dock them.
  - Drag and drop `.py` scripts from Windows Explorer directly into the application window to open or create new tabs.
- **Smart 3-Tier Dependency Auto-Healing:**
  1. Detects imports via AST analysis.
  2. Resolves imports to package names via `pipreqs` mapping and PyPI heuristics.
  3. Automatically intercepts `ModuleNotFoundError` during runtime, installs missing packages on-the-fly, and resumes the script.
- **Python Version Selector:** Easily choose, download, or switch Python versions from the Settings menu.
- **Clean Cleanup Policies:** Optional automatic wipe of downloaded libraries or portable runtimes on app exit.
- **Cross-Platform:** Supports Windows (WinUI 3) and Android.

## Getting Started

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- .NET MAUI Workload (`dotnet workload install maui`)

### Building from Source

To build for Windows:
```bash
dotnet build -f net8.0-windows10.0.19041.0
```

To build for Android:
```bash
dotnet build -f net8.0-android
```

To publish release executables:
```bash
# Windows x64 Publish
dotnet publish -f net8.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None

# Android APK Publish
dotnet publish -f net8.0-android -c Release
```

## License

This project is open-source and released under the [MIT License](LICENSE).
