using Microsoft.Maui.Storage;
using PythonRunnerApp.Services;

namespace PythonRunnerApp;

public partial class SettingsPage : ContentPage
{
    private bool _isInitializing = true;

    private List<PythonReleaseInfo> _availableVersions = new();

    public SettingsPage()
    {
        InitializeComponent();
        LoadSettings();
        _ = LoadPythonVersionsAsync();
    }

    private void LoadSettings()
    {
        _isInitializing = true;

        int themeIndex = Preferences.Get("AppTheme", 0);
        ThemePicker.SelectedIndex = themeIndex;

        string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PortablePython");
        string installPath = Preferences.Get("PythonInstallLocation", defaultPath);
        InstallPathEntry.Text = installPath;

        int cleanupIndex = Preferences.Get("CleanupPolicy", 0);
        CleanupPicker.SelectedIndex = cleanupIndex;

        CheckCurrentInstalledVersion();

        _isInitializing = false;
    }

    private async Task LoadPythonVersionsAsync()
    {
        try
        {
            var versionService = new PythonVersionService();
            _availableVersions = await versionService.GetAvailableWindowsVersionsAsync();

            VersionPicker.Items.Clear();
            foreach (var v in _availableVersions)
            {
                VersionPicker.Items.Add(v.DisplayName);
            }

            string? savedVersion = Preferences.Get("SelectedPythonVersion", (string?)null);
            int selectedIdx = !string.IsNullOrEmpty(savedVersion) 
                ? _availableVersions.FindIndex(v => v.Version == savedVersion) 
                : -1;

            if (selectedIdx >= 0)
            {
                VersionPicker.SelectedIndex = selectedIdx;
            }
            else if (_availableVersions.Any())
            {
                VersionPicker.SelectedIndex = 0;
                Preferences.Set("SelectedPythonVersion", _availableVersions[0].Version);
                Preferences.Set("SelectedPythonDownloadUrl", _availableVersions[0].DownloadUrl);
            }
        }
        catch
        {
            PythonVersionStatusLabel.Text = "Failed to fetch online versions";
        }
    }

    private void OnVersionChanged(object sender, EventArgs e)
    {
        if (_isInitializing || VersionPicker.SelectedIndex < 0 || VersionPicker.SelectedIndex >= _availableVersions.Count) 
            return;

        var selected = _availableVersions[VersionPicker.SelectedIndex];
        Preferences.Set("SelectedPythonVersion", selected.Version);
        Preferences.Set("SelectedPythonDownloadUrl", selected.DownloadUrl);
    }

    private void OnThemeChanged(object sender, EventArgs e)
    {
        if (_isInitializing) return;

        int selectedIndex = ThemePicker.SelectedIndex;
        Preferences.Set("AppTheme", selectedIndex);

        if (Application.Current != null)
        {
            Application.Current.UserAppTheme = selectedIndex switch
            {
                1 => AppTheme.Light,
                2 => AppTheme.Dark,
                _ => AppTheme.Unspecified
            };
        }
    }

    private async void OnBrowsePathClicked(object sender, EventArgs e)
    {
        try
        {
            var folderPicker = new FolderPickerService();
            var path = await folderPicker.PickFolderAsync();

            if (!string.IsNullOrEmpty(path))
            {
                string finalPath = Path.Combine(path, "PortablePython");
                Preferences.Set("PythonInstallLocation", finalPath);
                InstallPathEntry.Text = finalPath;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to select folder: {ex.Message}", "OK");
        }
    }

    private void OnResetPathClicked(object sender, EventArgs e)
    {
        string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PortablePython");
        Preferences.Set("PythonInstallLocation", defaultPath);
        InstallPathEntry.Text = defaultPath;
    }

    private void OnCleanupPolicyChanged(object sender, EventArgs e)
    {
        if (_isInitializing) return;

        Preferences.Set("CleanupPolicy", CleanupPicker.SelectedIndex);
    }

    private void CheckCurrentInstalledVersion()
    {
        string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PortablePython");
        string installLocation = Preferences.Get("PythonInstallLocation", defaultPath);
        string pythonExe = Path.Combine(installLocation, "python", "python.exe");

        if (File.Exists(pythonExe))
        {
            string savedVersion = Preferences.Get("SelectedPythonVersion", "Unknown");
            CurrentInstalledVersionLabel.Text = $"Installed in folder: Python {savedVersion} ({pythonExe})";
            CurrentInstalledVersionLabel.TextColor = Colors.Green;
            DeletePythonButton.IsEnabled = true;
        }
        else
        {
            CurrentInstalledVersionLabel.Text = "Installed: None (No portable Python detected in path)";
            CurrentInstalledVersionLabel.TextColor = Colors.Orange;
            DeletePythonButton.IsEnabled = false;
        }
    }

    private async void OnInstallSelectedVersionClicked(object sender, EventArgs e)
    {
        if (VersionPicker.SelectedIndex < 0 || VersionPicker.SelectedIndex >= _availableVersions.Count)
        {
            await DisplayAlert("Error", "Please select a Python version to install.", "OK");
            return;
        }

        var selected = _availableVersions[VersionPicker.SelectedIndex];
        bool confirm = await DisplayAlert("Install Python", $"Download and install {selected.DisplayName} to your portable folder?\n\nIf another version is present, it will be replaced.", "Install", "Cancel");
        if (!confirm) return;

        try
        {
            InstallSelectedVersionButton.IsEnabled = false;
            PythonVersionStatusLabel.Text = $"Downloading {selected.DisplayName}...";

            string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PortablePython");
            string installLocation = Preferences.Get("PythonInstallLocation", defaultPath);
            string pythonDir = Path.Combine(installLocation, "python");

            if (Directory.Exists(pythonDir))
            {
                try { Directory.Delete(pythonDir, true); } catch { }
            }

            var downloader = new PythonDownloaderService();
            var progress = new Progress<string>(msg =>
            {
                MainThread.BeginInvokeOnMainThread(() => PythonVersionStatusLabel.Text = msg);
            });

            bool success = await downloader.DownloadAndExtractPython(installLocation, selected.DownloadUrl, progress);
            if (success)
            {
                Preferences.Set("SelectedPythonVersion", selected.Version);
                Preferences.Set("SelectedPythonDownloadUrl", selected.DownloadUrl);
                CheckCurrentInstalledVersion();
                await DisplayAlert("Success", $"{selected.DisplayName} installed successfully!", "OK");
            }
            else
            {
                await DisplayAlert("Error", "Failed to download and install Python.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Installation error: {ex.Message}", "OK");
        }
        finally
        {
            InstallSelectedVersionButton.IsEnabled = true;
            PythonVersionStatusLabel.Text = "Fetched dynamically from official releases";
        }
    }

    private async void OnDeletePythonClicked(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Delete Python", "Are you sure you want to completely delete the installed Portable Python core and libraries?", "Delete", "Cancel");
        if (!confirm) return;

        try
        {
            string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PortablePython");
            string installLocation = Preferences.Get("PythonInstallLocation", defaultPath);
            string pythonDir = Path.Combine(installLocation, "python");

            if (Directory.Exists(pythonDir))
            {
                Directory.Delete(pythonDir, true);
            }

            CheckCurrentInstalledVersion();
            await DisplayAlert("Deleted", "Portable Python deleted successfully.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to delete Python: {ex.Message}", "OK");
        }
    }

    private async void OnClearLogsClicked(object sender, EventArgs e)
    {
        Preferences.Set("ClearOutputLogsRequested", true);
        await DisplayAlert("Logs", "Console output logs cleared.", "OK");
    }

    private async void OnClearLibrariesClicked(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Clear Libraries", "Delete all installed external pip packages (site-packages)? Python core will be preserved.", "Clear", "Cancel");
        if (!confirm) return;

        try
        {
            string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PortablePython");
            string installLocation = Preferences.Get("PythonInstallLocation", defaultPath);
            string sitePackages = Path.Combine(installLocation, "python", "Lib", "site-packages");

            if (Directory.Exists(sitePackages))
            {
                Directory.Delete(sitePackages, true);
                Directory.CreateDirectory(sitePackages);
                await DisplayAlert("Success", "Libraries (site-packages) cleared.", "OK");
            }
            else
            {
                await DisplayAlert("Info", "No site-packages folder found to delete.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to clear libraries: {ex.Message}", "OK");
        }
    }

    private async void OnWipeAllDataClicked(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Wipe Everything", "This will delete the entire PortablePython folder including Python, all libraries, and logs. Continue?", "Wipe Everything", "Cancel");
        if (!confirm) return;

        try
        {
            string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PortablePython");
            string installLocation = Preferences.Get("PythonInstallLocation", defaultPath);

            if (Directory.Exists(installLocation))
            {
                Directory.Delete(installLocation, true);
            }

            Preferences.Set("ClearOutputLogsRequested", true);
            CheckCurrentInstalledVersion();
            await DisplayAlert("Wipe Completed", "All portable data and files have been wiped.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to wipe data: {ex.Message}", "OK");
        }
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
