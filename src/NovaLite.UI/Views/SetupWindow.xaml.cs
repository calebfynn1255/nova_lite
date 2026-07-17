using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using NovaLite.Setup;
using NovaLite.UI.ViewModels;

namespace NovaLite.UI.Views;

public partial class SetupWindow : Window
{
    private readonly WindowNotificationManager _notifications;

    public SetupWindow()
    {
        InitializeComponent();
        _notifications = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 3
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is SetupWindowViewModel vm)
        {
            vm.CloseAction = () => Close();
            vm.ShowNotificationAction = (title, message) =>
                _notifications.Show(new Notification(title, message, NotificationType.Success, TimeSpan.FromSeconds(5)));
            vm.PickFolderAction = async () =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Model Directory",
                    AllowMultiple = false
                });

                return folders.Count > 0 ? folders[0].Path.LocalPath : null;
            };

            vm.SelectModelInUiAction = (model) =>
            {
                ModelsListBox.SelectedItems?.Clear();
                if (model != null)
                {
                    ModelsListBox.SelectedItems?.Add(model);
                }
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
                if (item is RecommendedModelViewModel vm2)
                    vm.SelectedModels.Add(vm2.Source);
            }
        }
    }
}
