using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace NovaLite.UI.Controls;

public partial class TypingIndicator : UserControl
{
    private DispatcherTimer? _timer;
    private int _step;
    private Ellipse? _dot1;
    private Ellipse? _dot2;
    private Ellipse? _dot3;

    public TypingIndicator()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _step = 0;
        _dot1 = this.FindControl<Ellipse>("Dot1");
        _dot2 = this.FindControl<Ellipse>("Dot2");
        _dot3 = this.FindControl<Ellipse>("Dot3");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _timer.Tick += OnTick;
        _timer.Start();
        OnTick(this, EventArgs.Empty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
        _dot1 = _dot2 = _dot3 = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _step = (_step + 1) % 18;
        AnimateDot(_dot1, 0);
        AnimateDot(_dot2, 6);
        AnimateDot(_dot3, 12);
    }

    private void AnimateDot(Ellipse? dot, int phase)
    {
        if (dot is null) return;

        double wave = (Math.Sin((_step - phase) * Math.PI / 9) + 1) / 2;
        dot.Opacity = 0.28 + (wave * 0.72);
        dot.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(0.85 + (wave * 0.3), 0.85 + (wave * 0.3)),
                new TranslateTransform(0, -2 * wave)
            }
        };
    }
}
