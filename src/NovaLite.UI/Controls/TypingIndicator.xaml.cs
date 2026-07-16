using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;

namespace NovaLite.UI.Controls;

public partial class TypingIndicator : UserControl
{
    private DispatcherTimer? _timer;
    private int _step;

    public TypingIndicator()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _step = 0;
        /*
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _timer.Tick += OnTick;
        _timer.Start();
        */
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        /*
        _timer?.Stop();
        _timer = null;
        */
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _step = (_step + 1) % 9;   // 9-step cycle: each dot is bright for 3 steps

        var d1 = this.FindControl<Ellipse>("Dot1");
        var d2 = this.FindControl<Ellipse>("Dot2");
        var d3 = this.FindControl<Ellipse>("Dot3");

        if (d1 is null || d2 is null || d3 is null) return;

        d1.Opacity = _step is 0 or 1 or 2 ? 1.0 : 0.25;
        d2.Opacity = _step is 3 or 4 or 5 ? 1.0 : 0.25;
        d3.Opacity = _step is 6 or 7 or 8 ? 1.0 : 0.25;

        // Also scale up the active dot slightly
        d1.Width = d1.Height = _step is 0 or 1 or 2 ? 8 : 7;
        d2.Width = d2.Height = _step is 3 or 4 or 5 ? 8 : 7;
        d3.Width = d3.Height = _step is 6 or 7 or 8 ? 8 : 7;
    }
}
