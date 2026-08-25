using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NovaLite.UI.ViewModels;

namespace NovaLite.UI.Views;

public partial class ModelManagerPage : UserControl
{
    public ModelManagerPage() => InitializeComponent();

    // Called from the Browse button — opens a folder picker dialog in the view.
    private async void BrowseFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ModelManagerViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select model directory",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                await vm.SetDirectoryAndScan(path);
        }
    }
}
