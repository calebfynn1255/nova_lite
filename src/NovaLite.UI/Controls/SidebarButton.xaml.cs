using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System.Windows.Input;

namespace NovaLite.UI.Controls;

public partial class SidebarButton : UserControl
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<SidebarButton, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SidebarButton, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<SidebarButton, bool>(nameof(IsActive));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<SidebarButton, ICommand?>(nameof(Command));

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public SidebarButton()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconProperty)
        {
            var icon = this.FindControl<PathIcon>("IconPath");
            if (icon is not null) icon.Data = Icon;
        }
        else if (change.Property == LabelProperty)
        {
            var lbl = this.FindControl<TextBlock>("LabelText");
            if (lbl is not null) lbl.Text = Label;
        }
        else if (change.Property == IsActiveProperty)
        {
            var btn = this.FindControl<Button>("Btn");
            if (btn is null) return;
            if (IsActive) btn.Classes.Add("active");
            else btn.Classes.Remove("active");

            this.TryFindResource(IsActive ? "AccentBrush" : "TextSecondaryBrush", out var res);
            var accent = res as IBrush;

            var icon = this.FindControl<PathIcon>("IconPath");
            var lbl  = this.FindControl<TextBlock>("LabelText");
            var activeRail = this.FindControl<Border>("ActiveRail");
            if (activeRail is not null) activeRail.Opacity = IsActive ? 1 : 0;
            if (accent is not null)
            {
                if (icon is not null) icon.Foreground = accent;
                if (lbl  is not null) lbl.Foreground  = accent;
            }
        }
    }
}
