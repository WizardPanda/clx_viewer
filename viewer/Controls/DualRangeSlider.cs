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

    private enum ThumbId { None, Low, High }
    private ThumbId _drag = ThumbId.None;
    private ThumbId _hover = ThumbId.None;

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
        const double trackH = 4.0;
        double trackY = mid - trackH / 2.0;

        double lx = Math.Clamp(ValueToX(LowValue), left, right);
        double hx = Math.Clamp(ValueToX(HighValue), left, right);

        // Track
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x33)),
            null, new Rect(left, trackY, right - left, trackH), trackH / 2, trackH / 2);

        // Active range (accent gradient)
        if (hx - lx > 0.5)
        {
            var fill = new LinearGradientBrush(Color.FromRgb(0x2B, 0x8A, 0xE0),
                Color.FromRgb(0x0A, 0x4C, 0x94), 90.0);
            dc.DrawRoundedRectangle(fill, null, new Rect(lx, trackY, hx - lx, trackH),
                trackH / 2, trackH / 2);
        }

        DrawThumb(dc, lx, mid, isLow: true);
        DrawThumb(dc, hx, mid, isLow: false);
    }

    private void DrawThumb(DrawingContext dc, double x, double mid, bool isLow)
    {
        double w = ThumbWidth;
        double h = Math.Min(20.0, ActualHeight * 0.85);
        double y = mid - h / 2;
        var r = new Rect(x - w / 2, y, w, h);

        ThumbId id = isLow ? ThumbId.Low : ThumbId.High;
        bool hovered = _hover == id;
        bool active = _drag == id;

        Brush fill;
        Pen border;
        if (active)
        {
            fill = new SolidColorBrush(Color.FromRgb(0xC6, 0xE2, 0xF7));
            border = new Pen(new SolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBB)), 1.5);
        }
        else if (hovered)
        {
            fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            border = new Pen(new SolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBB)), 1.2);
        }
        else
        {
            fill = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
            border = new Pen(new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A)), 1.0);
        }

        dc.DrawRoundedRectangle(fill, border, r, 4, 4);

        // Grip notch
        var grip = new Pen(
            new SolidColorBrush(active || hovered ? Color.FromRgb(0x0F, 0x6C, 0xBB) : Color.FromRgb(0x8A, 0x8A, 0x8A)),
            1.0);
        dc.DrawLine(grip, new Point(x, mid - 4), new Point(x, mid + 4));
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
            _drag = dl <= dh ? ThumbId.Low : ThumbId.High;
        }
        else
        {
            _drag = p.X < (lx + hx) / 2.0 ? ThumbId.Low : ThumbId.High;
        }
        _hover = _drag;
        Mouse.Capture(this);
        ApplyPoint(p.X);
        e.Handled = true;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag != ThumbId.None && e.LeftButton == MouseButtonState.Pressed)
        {
            ApplyPoint(e.GetPosition(this).X);
            e.Handled = true;
            return;
        }
        UpdateHover(e.GetPosition(this));
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag != ThumbId.None)
        {
            _drag = ThumbId.None;
            Mouse.Capture(null);
            UpdateHover(e.GetPosition(this));
            e.Handled = true;
        }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover != ThumbId.None)
        {
            _hover = ThumbId.None;
            InvalidateVisual();
        }
        base.OnMouseLeave(e);
    }

    private void UpdateHover(Point p)
    {
        double lx = ValueToX(LowValue);
        double hx = ValueToX(HighValue);
        double dl = Math.Abs(p.X - lx);
        double dh = Math.Abs(p.X - hx);
        const double grab = 8.0;
        ThumbId hov = (dl <= grab || dh <= grab)
            ? (dl <= dh ? ThumbId.Low : ThumbId.High)
            : ThumbId.None;
        if (hov != _hover)
        {
            _hover = hov;
            InvalidateVisual();
        }
    }

    private void ApplyPoint(double x)
    {
        double v = XToValue(x);
        if (_drag == ThumbId.Low)
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
