using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace HddSpindown;

/// <summary>
/// Spinning loading ring in the Windows accent color (like the Windows 11 ProgressRing):
/// a three-quarter arc on a subtle track. Only spins while visible.
/// </summary>
public sealed class LoadingRing : Grid
{
    private readonly RotateTransform _spin = new();

    public LoadingRing()
    {
        Width = Height = 20;
        IsHitTestVisible = false;

        var track = new Ellipse { StrokeThickness = 2.5, Opacity = 0.2 };
        track.SetResourceReference(Shape.StrokeProperty, "AccentFillColorDefaultBrush");

        var arc = new Path
        {
            StrokeThickness = 2.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.None,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _spin,
        };
        arc.SetResourceReference(Shape.StrokeProperty, "AccentFillColorDefaultBrush");
        SizeChanged += (_, _) => arc.Data = Arc(ActualWidth, ActualHeight, 2.5);

        Children.Add(track);
        Children.Add(arc);
        IsVisibleChanged += (_, _) => Animate();
    }

    /// <summary>Three-quarter arc, inset by half the stroke width.</summary>
    private static Geometry Arc(double w, double h, double stroke)
    {
        double r = Math.Min(w, h) / 2 - stroke / 2;
        var c = new Point(w / 2, h / 2);
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X - r, c.Y);  // 270 degrees clockwise
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, true, SweepDirection.Clockwise, true));
        return new PathGeometry([fig]);
    }

    private void Animate()
    {
        _spin.BeginAnimation(RotateTransform.AngleProperty, null);
        if (!IsVisible) return;
        _spin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}
