using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NovaLite.Setup;
using NovaLite.UI.Services;
using NovaLite.UI.ViewModels;

namespace NovaLite.UI.Views;

public partial class SetupWindow : Window
{
    private readonly WindowsToastService _notifications;
    private readonly DispatcherTimer _logoAnimationTimer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private double _logoAnimationPhase;

    public SetupWindow()
    {
        InitializeComponent();
        _notifications = new WindowsToastService(this);
        _logoAnimationTimer.Tick += AnimateSetupLogo;
        Opened += (_, _) => _logoAnimationTimer.Start();
        Closed += (_, _) => _logoAnimationTimer.Stop();
    }

    private void AnimateSetupLogo(object? sender, EventArgs e)
    {
        _logoAnimationPhase += 0.045;
        var scale = 1 + Math.Sin(_logoAnimationPhase) * 0.025;
        var floatOffset = Math.Sin(_logoAnimationPhase * 0.7) * 4;

        if (this.FindControl<Image>("SetupHeroLogo") is { } logo)
        {
            logo.RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(scale, scale),
                    new TranslateTransform(0, floatOffset)
                }
            };
        }

        if (this.FindControl<Control>("SetupLogoGlow") is { } glow)
        {
            glow.RenderTransform = new ScaleTransform(1 + Math.Sin(_logoAnimationPhase) * 0.10, 1 + Math.Sin(_logoAnimationPhase) * 0.10);
            glow.Opacity = 0.10 + ((Math.Sin(_logoAnimationPhase) + 1) * 0.06);
        }

        if (this.FindControl<Control>("SetupLogoHalo") is { } halo)
            halo.Opacity = 0.14 + ((Math.Cos(_logoAnimationPhase) + 1) * 0.08);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is SetupWindowViewModel vm)
        {
            vm.CloseAction = () => Close();
            vm.ShowNotificationAction = _notifications.Show;
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
