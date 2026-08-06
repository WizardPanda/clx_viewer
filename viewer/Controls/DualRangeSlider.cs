using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClxViewer.Controls;

/// <summary>
/// A two-thumb range slider drawn with a custom OnRender. Supports dragging
/// either thumb (low/high), clicking the track to move the nearest thumb, and
/// reports changes continuously via the ValueChanged routed event.
/// </summary>
public class DualRangeSlider : FrameworkElement
{
    static DualRangeSlider()
    {
    }

    public DualRangeSlider()
    {
        Focusable = true;
    }

    public static readonly DependencyProperty DataMinProperty =
        DependencyProperty.Register(nameof(DataMin), typeof(double), typeof(DualRangeSlider),
            new FrameworkPropertyMetadata(0.0, OnVisualPropChanged));

    public static readonly DependencyProperty DataMaxProperty =
        DependencyProperty.Register(nameof(DataMax), typeof(double), typeof(DualRangeSlider),
            new FrameworkPropertyMetadata(65535.0, OnVisualPropChanged));

    public static readonly DependencyProperty LowValueProperty =
        DependencyProperty.Register(nameof(LowValue), typeof(double), typeof(DualRangeSlider),
            new FrameworkPropertyMetadata(0.0, OnVisualPropChanged));

    public static readonly DependencyProperty HighValueProperty =
        DependencyProperty.Register(nameof(HighValue), typeof(double), typeof(DualRangeSlider),
            new FrameworkPropertyMetadata(65535.0, OnVisualPropChanged));

    public static readonly DependencyProperty ThumbWidthProperty =
        DependencyProperty.Register(nameof(ThumbWidth), typeof(double), typeof(DualRangeSlider),
            new FrameworkPropertyMetadata(16.0, OnVisualPropChanged));

    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(DualRangeSlider));

    public event RoutedEventHandler ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    public double DataMin { get => (double)GetValue(DataMinProperty); set => SetValue(DataMinProperty, value); }
    public double DataMax { get => (double)GetValue(DataMaxProperty); set => SetValue(DataMaxProperty, value); }
    public double LowValue { get => (double)GetValue(LowValueProperty); set => SetValue(LowValueProperty, value); }
    public double HighValue { get => (double)GetValue(HighValueProperty); set => SetValue(HighValueProperty, value); }
    public double ThumbWidth { get => (double)GetValue(ThumbWidthProperty); set => SetValue(ThumbWidthProperty, value); }

    private static void OnVisualPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DualRangeSlider)d).InvalidateVisual();

    private enum DragThumb { None, Low, High }
    private DragThumb _drag = DragThumb.None;

    private double TrackLeft => ThumbWidth / 2.0;
    private double TrackRight => Math.Max(ThumbWidth, ActualWidth - ThumbWidth / 2.0);

    private double ValueToX(double v)
    {
        double span = DataMax - DataMin;
        double t = span <= 0 ? 0 : (v - DataMin) / span;
        return TrackLeft + t * (TrackRight - TrackLeft);
    }

    private double XToValue(double x)
    {
        double t = (x - TrackLeft) / (TrackRight - TrackLeft);
        t = Math.Clamp(t, 0.0, 1.0);
        return DataMin + t * (DataMax - DataMin);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double left = TrackLeft;
        double right = TrackRight;
        double mid = ActualHeight / 2.0;
        double trackH = 6.0;
        double trackY = mid - trackH / 2.0;

        double lx = Math.Clamp(ValueToX(LowValue), left, right);
        double hx = Math.Clamp(ValueToX(HighValue), left, right);

        var track = new Rect(left, trackY, right - left, trackH);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            null, track, trackH / 2, trackH / 2);

        if (hx > lx)
        {
            var fill = new LinearGradientBrush(Color.FromRgb(0x0F, 0x6C, 0xBB),
                Color.FromRgb(0x01, 0x2C, 0x5D), 90.0);
            dc.DrawRoundedRectangle(fill, null,
                new Rect(lx, trackY, hx - lx, trackH), trackH / 2, trackH / 2);
        }

        DrawThumb(dc, lx, mid);
        DrawThumb(dc, hx, mid);
    }

    private void DrawThumb(DrawingContext dc, double x, double mid)
    {
        double w = ThumbWidth;
        double h = Math.Min(22.0, ActualHeight * 0.9);
        double y = mid - h / 2;
        var r = new Rect(x - w / 2, y, w, h);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)), 1.0), r, 4, 4);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        Point p = e.GetPosition(this);
        double lx = ValueToX(LowValue);
        double hx = ValueToX(HighValue);
        double dl = Math.Abs(p.X - lx);
        double dh = Math.Abs(p.X - hx);
        const double grab = 10.0;

        if (dl <= grab || dh <= grab)
        {
            _drag = dl <= dh ? DragThumb.Low : DragThumb.High;
        }
        else
        {
            _drag = p.X < (lx + hx) / 2.0 ? DragThumb.Low : DragThumb.High;
        }
        Mouse.Capture(this);
        ApplyPoint(p.X);
        e.Handled = true;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag != DragThumb.None && e.LeftButton == MouseButtonState.Pressed)
        {
            ApplyPoint(e.GetPosition(this).X);
            e.Handled = true;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag != DragThumb.None)
        {
            _drag = DragThumb.None;
            Mouse.Capture(null);
            e.Handled = true;
        }
        base.OnMouseLeftButtonUp(e);
    }

    private void ApplyPoint(double x)
    {
        double v = XToValue(x);
        if (_drag == DragThumb.Low)
        {
            if (v > HighValue) v = HighValue;
            if (Math.Abs(v - LowValue) < 1e-6) return;
            LowValue = v;
        }
        else
        {
            if (v < LowValue) v = LowValue;
            if (Math.Abs(v - HighValue) < 1e-6) return;
            HighValue = v;
        }
        RaiseValueChanged();
    }

    private void RaiseValueChanged()
        => RaiseEvent(new RoutedEventArgs(ValueChangedEvent));

    protected override Size MeasureOverride(Size availableSize)
        => new Size(0, 26);
}
