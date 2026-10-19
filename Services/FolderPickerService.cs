using System;
using System.Threading.Tasks;

#if WINDOWS
using Microsoft.Maui.Platform;
#endif

namespace PythonRunnerApp.Services;

public class FolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
#if WINDOWS
        var folderPicker = new Windows.Storage.Pickers.FolderPicker();
        folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
        folderPicker.FileTypeFilter.Add("*");

        var window = App.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as MauiWinUIWindow;
        if (window != null)
        {
            var hwnd = window.WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
        }

        var result = await folderPicker.PickSingleFolderAsync();
        return result?.Path;
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}
