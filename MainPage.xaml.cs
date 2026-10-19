using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Maui.Storage;
using PythonRunnerApp.Services;

namespace PythonRunnerApp;

public partial class MainPage : ContentPage
{
    private readonly List<ScriptSession> _sessions = new();
    private ScriptSession? _currentSession;
    private string _pythonExecutablePath = "";

    private string AppDataPath => Preferences.Get("PythonInstallLocation", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PortablePython"));
    private string PythonPortablePath => Path.Combine(AppDataPath, "python");

    public MainPage() : this(null)
    {
    }

    public MainPage(ScriptSession? initialSession)
    {
        InitializeComponent();

        if (initialSession != null)
        {
            _sessions.Add(initialSession);
            _currentSession = initialSession;
        }
        else
        {
            var defaultSession = new ScriptSession();
            _sessions.Add(defaultSession);
            _currentSession = defaultSession;
        }

        _currentSession.OutputAppended += OnCurrentSessionOutputAppended;
        _currentSession.ExecutionCompleted += OnCurrentSessionCompleted;

        SetupDragAndDropZones();
    }

    private void SetupDragAndDropZones()
    {
        var detachDrop = new DropGestureRecognizer();
        detachDrop.Drop += (s, e) =>
        {
            var dragged = DragDropSessionManager.CurrentDraggedSession;
            var source = DragDropSessionManager.SourcePage;
            DragDropSessionManager.Clear();

            if (dragged != null && source == this)
            {
                DetachSession(dragged);
            }
        };
        DetachButton.GestureRecognizers.Add(detachDrop);

        var attachDrop = new DropGestureRecognizer();
        attachDrop.Drop += (s, e) =>
        {
            var dragged = DragDropSessionManager.CurrentDraggedSession;
            DragDropSessionManager.Clear();

            if (dragged != null)
            {
                AttachSessionToMainWindow(dragged);
            }
        };
        AttachButton.GestureRecognizers.Add(attachDrop);

        var tabsContainerDrop = new DropGestureRecognizer();
        tabsContainerDrop.Drop += (s, e) =>
        {
            var dragged = DragDropSessionManager.CurrentDraggedSession;
            var source = DragDropSessionManager.SourcePage;
            DragDropSessionManager.Clear();

            if (dragged != null && source != null)
            {
                if (source != this)
                {
                    source.TransferSessionToTargetPage(dragged, this);
                }
                else
                {
                    _sessions.Remove(dragged);
                    _sessions.Add(dragged);
                    RenderTabs();
                }
            }
        };
        TabsContainer.GestureRecognizers.Add(tabsContainerDrop);

        var rootDrop = new DropGestureRecognizer();
        rootDrop.Drop += (s, e) =>
        {
            var dragged = DragDropSessionManager.CurrentDraggedSession;
            var source = DragDropSessionManager.SourcePage;
            DragDropSessionManager.Clear();

            if (dragged != null && source != null)
            {
                if (source == this)
                {
                    DetachSession(dragged);
                }
                else
                {
                    source.TransferSessionToTargetPage(dragged, this);
                }
            }
        };
        RootGrid.GestureRecognizers.Add(rootDrop);
    }

#if WINDOWS
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
        {
            frameworkElement.AllowDrop = true;
            frameworkElement.DragOver += (s, e) =>
            {
                if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                {
                    e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                }
            };
            frameworkElement.Drop += async (s, e) =>
            {
                if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    foreach (var item in items)
                    {
                        if (item.Name.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                        {
                            await HandleDroppedPythonFile(item.Path);
                        }
                    }
                }
            };
        }
    }
#endif

    private async Task HandleDroppedPythonFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (_currentSession != null && string.IsNullOrEmpty(_currentSession.ScriptPath) && !_currentSession.IsRunning)
            {
                _currentSession.ScriptPath = filePath;
                await AnalyzeDependenciesForSession(_currentSession);
                SyncUiWithCurrentSession();
            }
            else
            {
                var newSession = new ScriptSession
                {
                    ScriptPath = filePath
                };
                _sessions.Add(newSession);
                SwitchToSession(newSession);
                await AnalyzeDependenciesForSession(newSession);
                SyncUiWithCurrentSession();
            }
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (Preferences.Get("ClearOutputLogsRequested", false))
        {
            Preferences.Set("ClearOutputLogsRequested", false);
            if (_currentSession != null)
            {
                _currentSession.Output = "";
            }
            OutputLabel.Text = "";
        }

        InitializePython();
        SyncUiWithCurrentSession();
    }

    private void SyncUiWithCurrentSession()
    {
        if (_currentSession == null) return;

        SelectedScriptLabel.Text = string.IsNullOrEmpty(_currentSession.ScriptPath)
            ? "No script selected"
            : $"Selected: {Path.GetFileName(_currentSession.ScriptPath)}";

        if (_currentSession.DetectedDependencies.Any())
        {
            DependenciesLabel.Text = string.Join(", ", _currentSession.DetectedDependencies);
            DependenciesFrame.IsVisible = true;
            InstallDepsButton.IsVisible = true;
        }
        else if (!string.IsNullOrEmpty(_currentSession.ScriptPath))
        {
            DependenciesLabel.Text = "No external dependencies detected";
            DependenciesFrame.IsVisible = true;
            InstallDepsButton.IsVisible = false;
        }
        else
        {
            DependenciesFrame.IsVisible = false;
        }

        OutputLabel.Text = _currentSession.Output;

        RunScriptButton.IsEnabled = !_currentSession.IsRunning &&
                                    !string.IsNullOrEmpty(_currentSession.ScriptPath) &&
                                    !string.IsNullOrEmpty(_pythonExecutablePath);
        StopScriptButton.IsEnabled = _currentSession.IsRunning;
        StatusLabel.Text = _currentSession.IsRunning ? "Running script..." : "";

        bool isMainWindow = Application.Current != null && Application.Current.Windows.Count > 0 && Application.Current.Windows[0] == Window;
        AttachButton.IsVisible = !isMainWindow && (Application.Current?.Windows.Count > 1);

        RenderTabs();
    }

    private void RenderTabs()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            TabsContainer.Children.Clear();

            foreach (var session in _sessions)
            {
                bool isActive = session == _currentSession;

                var tabBorder = new Border
                {
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
                    StrokeThickness = 1,
                    Padding = new Thickness(12, 6),
                    BackgroundColor = isActive
                        ? (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#311B92") : Color.FromArgb("#EDE7F6"))
                        : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#212121") : Color.FromArgb("#F5F5F5")),
                    Stroke = isActive
                        ? Color.FromArgb("#512BD4")
                        : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#424242") : Color.FromArgb("#E0E0E0"))
                };

                var stack = new HorizontalStackLayout
                {
                    Spacing = 8,
                    VerticalOptions = LayoutOptions.Center
                };

                string titlePrefix = session.IsRunning ? "[Running] " : "";
                var titleLabel = new Label
                {
                    Text = $"{titlePrefix}{session.Title}",
                    FontSize = 13,
                    FontAttributes = isActive ? FontAttributes.Bold : FontAttributes.None,
                    TextColor = isActive
                        ? (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#D1C4E9") : Color.FromArgb("#512BD4"))
                        : (Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.LightGray : Color.FromArgb("#555555")),
                    VerticalOptions = LayoutOptions.Center
                };

                var tabTap = new TapGestureRecognizer();
                tabTap.Tapped += (s, e) => SwitchToSession(session);
                tabBorder.GestureRecognizers.Add(tabTap);

                var dragGesture = new DragGestureRecognizer { CanDrag = true };
                dragGesture.DragStarting += (s, e) =>
                {
                    DragDropSessionManager.StartDrag(session, this);
                    e.Data.Properties["SessionId"] = session.Id;
                    e.Data.Text = session.Title;
                };
                dragGesture.DropCompleted += (s, e) =>
                {
                    DragDropSessionManager.Clear();
                };
                tabBorder.GestureRecognizers.Add(dragGesture);

                var tabDrop = new DropGestureRecognizer();
                tabDrop.DragOver += (s, e) =>
                {
                    var dragged = DragDropSessionManager.CurrentDraggedSession;
                    if (dragged != null && dragged != session)
                    {
                        tabBorder.Stroke = Color.FromArgb("#FF9800");
                    }
                };
                tabDrop.DragLeave += (s, e) =>
                {
                    tabBorder.Stroke = (session == _currentSession)
                        ? Color.FromArgb("#512BD4")
                        : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#424242") : Color.FromArgb("#E0E0E0"));
                };
                tabDrop.Drop += (s, e) =>
                {
                    var dragged = DragDropSessionManager.CurrentDraggedSession;
                    var source = DragDropSessionManager.SourcePage;
                    DragDropSessionManager.Clear();

                    if (dragged != null && source != null)
                    {
                        if (source != this)
                        {
                            int targetIdx = _sessions.IndexOf(session);
                            source.TransferSessionToTargetPage(dragged, this, targetIdx);
                        }
                        else if (dragged != session)
                        {
                            int fromIndex = _sessions.IndexOf(dragged);
                            int toIndex = _sessions.IndexOf(session);
                            if (fromIndex >= 0 && toIndex >= 0)
                            {
                                _sessions.RemoveAt(fromIndex);
                                _sessions.Insert(toIndex, dragged);
                                RenderTabs();
                            }
                        }
                    }
                };
                tabBorder.GestureRecognizers.Add(tabDrop);

                var closeButton = new Button
                {
                    Text = "X",
                    FontSize = 11,
                    Padding = new Thickness(0),
                    HeightRequest = 22,
                    WidthRequest = 22,
                    CornerRadius = 11,
                    BackgroundColor = Colors.Transparent,
                    TextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.Gray : Color.FromArgb("#888888"),
                    VerticalOptions = LayoutOptions.Center
                };
                closeButton.Clicked += async (s, e) => await CloseSessionAsync(session);

                stack.Children.Add(titleLabel);
                if (_sessions.Count > 1 || !string.IsNullOrEmpty(session.ScriptPath) || !string.IsNullOrEmpty(session.Output))
                {
                    stack.Children.Add(closeButton);
                }

                tabBorder.Content = stack;
                TabsContainer.Children.Add(tabBorder);
            }
        });
    }

    private void SwitchToSession(ScriptSession newSession)
    {
        if (_currentSession == newSession) return;

        if (_currentSession != null)
        {
            _currentSession.Output = OutputLabel.Text;
            _currentSession.OutputAppended -= OnCurrentSessionOutputAppended;
            _currentSession.ExecutionCompleted -= OnCurrentSessionCompleted;
        }

        _currentSession = newSession;
        _currentSession.OutputAppended += OnCurrentSessionOutputAppended;
        _currentSession.ExecutionCompleted += OnCurrentSessionCompleted;

        SyncUiWithCurrentSession();
    }

    private void OnNewTabClicked(object sender, EventArgs e)
    {
        var newSession = new ScriptSession();
        _sessions.Add(newSession);
        SwitchToSession(newSession);
    }

    private async Task CloseSessionAsync(ScriptSession session)
    {
        if (session.IsRunning)
        {
            bool confirm = await DisplayAlert("Script Running", $"The script '{session.Title}' is currently running. Stop it and close tab?", "Yes", "No");
            if (!confirm) return;

            try
            {
                if (session.Runner != null)
                {
                    await session.Runner.StopAsync();
                }
            }
            catch { }
        }

        session.OutputAppended -= OnCurrentSessionOutputAppended;
        session.ExecutionCompleted -= OnCurrentSessionCompleted;

        _sessions.Remove(session);

        if (_sessions.Count == 0)
        {
            var newSession = new ScriptSession();
            _sessions.Add(newSession);
            SwitchToSession(newSession);
        }
        else if (session == _currentSession)
        {
            SwitchToSession(_sessions.Last());
        }
        else
        {
            RenderTabs();
        }
    }

    private void OnDetachTabClicked(object sender, EventArgs e)
    {
        if (_currentSession != null)
        {
            DetachSession(_currentSession);
        }
    }

    private void DetachSession(ScriptSession sessionToDetach)
    {
        if (!_sessions.Contains(sessionToDetach)) return;

        if (sessionToDetach == _currentSession)
        {
            sessionToDetach.Output = OutputLabel.Text;
        }

        sessionToDetach.OutputAppended -= OnCurrentSessionOutputAppended;
        sessionToDetach.ExecutionCompleted -= OnCurrentSessionCompleted;

        _sessions.Remove(sessionToDetach);

        if (_sessions.Count == 0)
        {
            var blankSession = new ScriptSession();
            _sessions.Add(blankSession);
            _currentSession = blankSession;
        }
        else if (_currentSession == sessionToDetach)
        {
            _currentSession = _sessions.Last();
        }

        if (_currentSession != null)
        {
            _currentSession.OutputAppended += OnCurrentSessionOutputAppended;
            _currentSession.ExecutionCompleted += OnCurrentSessionCompleted;
        }

        SyncUiWithCurrentSession();

        try
        {
            var detachedPage = new MainPage(sessionToDetach);
            var newWindow = new Window(detachedPage)
            {
                Title = $"{sessionToDetach.Title} - Portable Python Runner"
            };
#if WINDOWS
            newWindow.Width = 1000;
            newWindow.Height = 700;
#endif
            Application.Current?.OpenWindow(newWindow);
        }
        catch (Exception ex)
        {
            DisplayAlert("Error", $"Could not open new window: {ex.Message}", "OK");
            _sessions.Add(sessionToDetach);
            SwitchToSession(sessionToDetach);
        }
    }

    public void TransferSessionToTargetPage(ScriptSession session, MainPage targetPage, int targetIndex = -1)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            session.OutputAppended -= OnCurrentSessionOutputAppended;
            session.ExecutionCompleted -= OnCurrentSessionCompleted;

            if (session == _currentSession)
            {
                session.Output = OutputLabel.Text;
            }

            _sessions.Remove(session);

            targetPage.ReceiveTransferredSession(session, targetIndex);

            if (_sessions.Count == 0)
            {
                if (Window != null && Application.Current != null && Application.Current.Windows.Count > 1)
                {
                    Application.Current.CloseWindow(Window);
                }
                else
                {
                    var blank = new ScriptSession();
                    _sessions.Add(blank);
                    SwitchToSession(blank);
                }
            }
            else if (_currentSession == session)
            {
                SwitchToSession(_sessions.Last());
            }
            else
            {
                RenderTabs();
            }
        });
    }

    public void ReceiveTransferredSession(ScriptSession session, int targetIndex = -1)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_sessions.Contains(session))
            {
                if (targetIndex >= 0 && targetIndex <= _sessions.Count)
                {
                    _sessions.Insert(targetIndex, session);
                }
                else
                {
                    _sessions.Add(session);
                }
            }

            SwitchToSession(session);
        });
    }

    private void OnAttachToMainClicked(object sender, EventArgs e)
    {
        if (_currentSession != null)
        {
            AttachSessionToMainWindow(_currentSession);
        }
    }

    private void AttachSessionToMainWindow(ScriptSession session)
    {
        var firstWindow = Application.Current?.Windows.FirstOrDefault();
        if (firstWindow != null)
        {
            var targetMainPage = GetMainPageFromWindow(firstWindow);
            if (targetMainPage != null && targetMainPage != this)
            {
                TransferSessionToTargetPage(session, targetMainPage);
            }
        }
    }

    private static MainPage? GetMainPageFromWindow(Window window)
    {
        if (window.Page is MainPage mp) return mp;
        if (window.Page is AppShell shell)
        {
            return shell.CurrentPage as MainPage;
        }
        return null;
    }

    private void OnCurrentSessionOutputAppended(string text, string prefix)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                OutputLabel.Text += $"{prefix} {text}";
            }
            else
            {
                OutputLabel.Text += text;
            }
        });
    }

    private void OnCurrentSessionCompleted(int exitCode)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RunScriptButton.IsEnabled = !string.IsNullOrEmpty(_currentSession?.ScriptPath) && !string.IsNullOrEmpty(_pythonExecutablePath);
            StopScriptButton.IsEnabled = false;
            StatusLabel.Text = "";
            RenderTabs();
        });
    }

    private async void InitializePython()
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            StatusLabel.Text = "Checking Python environment...";

            await CheckPythonInstallation();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to initialize Python: {ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            StatusLabel.Text = "";
        }
    }

    private async Task CheckPythonInstallation()
    {
        var pythonExe = Path.Combine(PythonPortablePath, "python.exe");

        if (File.Exists(pythonExe))
        {
            _pythonExecutablePath = pythonExe;
            PythonStatusLabel.Text = "Portable Python Ready";
            PythonStatusLabel.TextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.LightGreen : Colors.Green;

            try
            {
                var versionInfo = await RunPythonCommand("--version");
                PythonStatusLabel.Text += $" ({versionInfo.Trim()})";
            }
            catch { }

            SyncUiWithCurrentSession();
        }
        else
        {
            PythonStatusLabel.Text = "Python not found";
            PythonStatusLabel.TextColor = Colors.Orange;

            bool shouldDownload = await DisplayAlert(
                "Python Not Found",
                "Portable Python is not installed. Would you like to download it now?",
                "Download", "Cancel");

            if (shouldDownload)
            {
                await PromptForInstallLocationAndDownload();
            }
        }
    }

    private async Task PromptForInstallLocationAndDownload()
    {
        bool usePicker = await DisplayAlert("Select Path", "Choose a folder to install Portable Python?", "Browse", "Default (App Folder)");

        if (usePicker)
        {
            try
            {
                var folderPicker = new PythonRunnerApp.Services.FolderPickerService();
                var path = await folderPicker.PickFolderAsync();

                if (!string.IsNullOrEmpty(path))
                {
                    string finalPath = Path.Combine(path, "PortablePython");
                    Preferences.Set("PythonInstallLocation", finalPath);
                    await DownloadPortablePython();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Folder selection failed: {ex.Message}", "OK");
            }
        }
        else
        {
            string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PortablePython");
            Preferences.Set("PythonInstallLocation", defaultPath);
            await DownloadPortablePython();
        }
    }

    private async Task DownloadPortablePython()
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            StatusLabel.Text = "Downloading portable Python...";

            Directory.CreateDirectory(AppDataPath);
            Directory.CreateDirectory(PythonPortablePath);

            var downloader = new PythonDownloaderService();
            var progress = new Progress<string>(msg =>
            {
                MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = msg);
            });

            string? selectedUrl = Preferences.Get("SelectedPythonDownloadUrl", null);
            bool success = await downloader.DownloadAndExtractPython(AppDataPath, selectedUrl, progress);

            if (success)
            {
                await CheckPythonInstallation();
            }
            else
            {
                await DisplayAlert("Error", "Failed to download and setup Python.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to download Python: {ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            StatusLabel.Text = "";
        }
    }

    private async void OnSelectScriptClicked(object sender, EventArgs e)
    {
        if (_currentSession == null) return;

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".py" } },
                    { DevicePlatform.macOS, new[] { "py" } },
                    { DevicePlatform.iOS, new[] { "public.python-script" } },
                    { DevicePlatform.Android, new[] { "text/x-python" } }
                })
            });

            if (result != null)
            {
                _currentSession.ScriptPath = result.FullPath;
                SelectedScriptLabel.Text = $"Selected: {Path.GetFileName(_currentSession.ScriptPath)}";

                await AnalyzeDependenciesForSession(_currentSession);
                SyncUiWithCurrentSession();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to select script: {ex.Message}", "OK");
        }
    }

    private async Task AnalyzeDependenciesForSession(ScriptSession session)
    {
        try
        {
            var analyzer = new DependencyAnalyzerService();
            var allDeps = await analyzer.AnalyzeScriptDependencies(session.ScriptPath, _pythonExecutablePath);
            var externalDeps = analyzer.GetExternalDependencies(allDeps);

            session.DetectedDependencies.Clear();

            foreach (var dep in externalDeps)
            {
                var resolvedName = await analyzer.ResolvePackageNameAsync(dep.PackageName);
                if (!session.DetectedDependencies.Contains(resolvedName))
                {
                    session.DetectedDependencies.Add(resolvedName);
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to analyze dependencies: {ex.Message}", "OK");
        }
    }

    private async void OnInstallDependenciesClicked(object sender, EventArgs e)
    {
        if (_currentSession == null || !_currentSession.DetectedDependencies.Any()) return;

        if (string.IsNullOrEmpty(_pythonExecutablePath))
        {
            await DisplayAlert("Error", "Python is not installed or still downloading. Please wait.", "OK");
            return;
        }

        try
        {
            LoadingIndicator.IsVisible = true;
            StatusLabel.Text = "Installing dependencies...";
            InstallDepsButton.IsEnabled = false;

            foreach (var dependency in _currentSession.DetectedDependencies)
            {
                StatusLabel.Text = $"Installing {dependency}...";
                var candidates = DependencyAnalyzerService.GetPackageCandidates(dependency);

                foreach (var candidate in candidates)
                {
                    try
                    {
                        var result = await RunPythonCommand($"-m pip install {candidate}");
                        _currentSession.Append($"Installed {candidate}\n{result}\n", "[INFO]");
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (candidate == candidates.Last())
                        {
                            _currentSession.Append($"Failed to install {dependency}: {ex.Message}\n", "[ERROR]");
                        }
                    }
                }
            }

            _currentSession.Append("Dependencies installation completed.\n", "[INFO]");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to install dependencies: {ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            StatusLabel.Text = "";
            InstallDepsButton.IsEnabled = true;
        }
    }

    private async void OnRunScriptClicked(object sender, EventArgs e)
    {
        if (_currentSession == null || string.IsNullOrEmpty(_currentSession.ScriptPath) || string.IsNullOrEmpty(_pythonExecutablePath))
        {
            await DisplayAlert("Error", "Please select a script and ensure Python is available", "OK");
            return;
        }

        try
        {
            _currentSession.IsRunning = true;
            RunScriptButton.IsEnabled = false;
            StopScriptButton.IsEnabled = true;
            StatusLabel.Text = "Running script...";
            RenderTabs();

            _currentSession.Append($"Running: {Path.GetFileName(_currentSession.ScriptPath)}\n", "[SYSTEM]");
            _currentSession.Append("=====================================\n", "[SYSTEM]");

            await RunScriptAsync(_currentSession);
        }
        catch (Exception ex)
        {
            _currentSession.Append($"Error: {ex.Message}\n", "[ERROR]");
            await DisplayAlert("Error", $"Failed to run script: {ex.Message}", "OK");
            _currentSession.IsRunning = false;
            RunScriptButton.IsEnabled = true;
            StopScriptButton.IsEnabled = false;
            StatusLabel.Text = "";
            RenderTabs();
        }
    }

    private async Task RunScriptAsync(ScriptSession session, int retryCount = 0)
    {
#if WINDOWS
        var windowsRunner = new WindowsProcessPythonRunner(_pythonExecutablePath);
        session.Runner = windowsRunner;
#else
        var embeddedRunner = new EmbeddedPythonRunner();
        session.Runner = embeddedRunner;
#endif

        string? missingModule = null;
        var moduleNotFoundRegex = new Regex(@"ModuleNotFoundError: No module named '([^']+)'");

        session.Runner.OutputReceived += (s, args) =>
        {
            if (args.Prefix == "[ERROR]")
            {
                var match = moduleNotFoundRegex.Match(args.Data);
                if (match.Success && string.IsNullOrEmpty(missingModule))
                {
                    missingModule = match.Groups[1].Value;
                }
            }

            session.Append(args.Data, args.Prefix);
        };

        int exitCode = await session.Runner.RunScriptAsync(session.ScriptPath);

        if (exitCode != 0 && !string.IsNullOrEmpty(missingModule) && retryCount < 3)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                session.Append($"\n--- Auto-healing: Missing module '{missingModule}' detected. Installing automatically... ---\n", "[AUTO-HEAL]");
                try
                {
                    var analyzer = new DependencyAnalyzerService();
                    var resolved = await analyzer.ResolvePackageNameAsync(missingModule);
                    var candidates = DependencyAnalyzerService.GetPackageCandidates(resolved);
                    bool installed = false;

                    foreach (var candidate in candidates)
                    {
                        try
                        {
                            var result = await RunPythonCommand($"-m pip install {candidate}");
                            session.Append($"Successfully installed {candidate}\n", "[AUTO-HEAL]");
                            installed = true;
                            break;
                        }
                        catch
                        {
                        }
                    }

                    if (installed)
                    {
                        session.Append("--- Restarting script ---\n\n", "[SYSTEM]");
                        await RunScriptAsync(session, retryCount + 1);
                    }
                    else
                    {
                        session.Append($"Auto-heal could not resolve installable package for '{missingModule}'.\n", "[ERROR]");
                        session.Append("=====================================\n", "[SYSTEM]");
                        session.Append($"Script completed with exit code: {exitCode}\n", "[SYSTEM]");
                        session.NotifyCompleted(exitCode);
                    }
                }
                catch (Exception ex)
                {
                    session.Append($"Auto-heal error: {ex.Message}\n", "[ERROR]");
                    session.Append("=====================================\n", "[SYSTEM]");
                    session.Append($"Script completed with exit code: {exitCode}\n", "[SYSTEM]");
                    session.NotifyCompleted(exitCode);
                }
            });
            return;
        }

        session.Append("=====================================\n", "[SYSTEM]");
        session.Append($"Script completed with exit code: {exitCode}\n", "[SYSTEM]");
        session.NotifyCompleted(exitCode);
    }

    private async void OnStopScriptClicked(object sender, EventArgs e)
    {
        if (_currentSession?.Runner != null)
        {
            try
            {
                await _currentSession.Runner.StopAsync();
                _currentSession.IsRunning = false;
                StopScriptButton.IsEnabled = false;
                RunScriptButton.IsEnabled = true;
                StatusLabel.Text = "";
                RenderTabs();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to stop script: {ex.Message}", "OK");
            }
        }
    }

    private void OnClearOutputClicked(object sender, EventArgs e)
    {
        if (_currentSession != null)
        {
            _currentSession.Output = "";
        }
        OutputLabel.Text = "";
    }

    private async Task<string> RunPythonCommand(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _pythonExecutablePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null) throw new Exception("Failed to start python process");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Python command failed: {error}");
        }

        return output;
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new SettingsPage());
    }
}