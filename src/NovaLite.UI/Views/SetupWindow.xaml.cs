using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NovaLite.Setup;
using NovaLite.UI.ViewModels;

namespace NovaLite.UI.Views;

public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is SetupWindowViewModel vm)
        {
            vm.CloseAction = () => Close();
            vm.PickFolderAction = async () =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Model Directory",
                    AllowMultiple = false
                });

                return folders.Count > 0 ? folders[0].Path.LocalPath : null;
            };
        }
    }

    public void OnModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SetupWindowViewModel vm && sender is ListBox lb)
        {
            vm.SelectedModels.Clear();
            foreach (var item in lb.SelectedItems!)
            {
                if (item is RecommendedModel model)
                    vm.SelectedModels.Add(model);
            }
        }
    }
}
