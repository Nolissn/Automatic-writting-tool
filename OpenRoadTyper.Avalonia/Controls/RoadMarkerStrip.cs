#nullable enable

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenRoadTyper.Avalonia.Controls;

/// <summary>
/// Small decorative "dashed road line" strip used under the title and in
/// dialog headers - a simplified Avalonia re-implementation of the WinForms
/// app's RoadMarkerStrip custom control (same visual idea, drawn with
/// Avalonia's DrawingContext instead of GDI+).
/// </summary>
public sealed class RoadMarkerStrip : Control
{
    private static readonly IBrush BaseBrush = new SolidColorBrush(Color.FromArgb(120, 0x0B, 0x0E, 0x0A));
    private static readonly IBrush RoadBrush = new SolidColorBrush(Color.FromRgb(0xDA, 0xB2, 0x49));
    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.FromArgb(180, 0x8B, 0xCF, 0x48));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 0x52, 0x5B, 0x43)), 1);

    public RoadMarkerStrip()
    {
        Height = 14;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        context.FillRectangle(BaseBrush, rect);
        context.DrawLine(BorderPen, new Point(0, rect.Height - 0.5), new Point(rect.Width, rect.Height - 0.5));

        const double segmentWidth = 42;
        const double gap = 14;
        var x = 0.0;
        while (x < rect.Width)
        {
            var width = System.Math.Min(segmentWidth, rect.Width - x);
            context.FillRectangle(RoadBrush, new Rect(x, 2, width, 4));

            var greenWidth = System.Math.Max(0, System.Math.Min(segmentWidth - 16, rect.Width - x - 8));
            if (greenWidth > 0)
            {
                context.FillRectangle(GreenBrush, new Rect(x + 8, 8, greenWidth, 3));
            }

            x += segmentWidth + gap;
        }
    }
}
