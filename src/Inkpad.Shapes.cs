using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

enum ShapeTool { Line, Arrow, Rectangle, Ellipse, Triangle }

sealed class ShapeLayer : Canvas
{
    readonly Polyline preview = new Polyline { StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
    Point start;
    bool active;
    internal ShapeTool Tool;
    internal Point StartPoint { get { return start; } }
    internal DrawingAttributes Attributes;
    internal event Action<Stroke> Committed;

    internal ShapeLayer()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        Focusable = true;
        Children.Add(preview);
        System.Windows.Automation.AutomationProperties.SetName(this, "Draw a shape on the note");
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        if (!CaptureMouse()) return;
        Focus();
        start = Limit(args.GetPosition(this));
        active = true;
        preview.Stroke = new SolidColorBrush(Attributes.Color);
        preview.StrokeThickness = Attributes.Width;
        preview.Points.Clear();
        args.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        if (!active) return;
        preview.Points = new PointCollection(Points(Tool, start, Limit(args.GetPosition(this)), IsShiftDown()));
        args.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs args)
    {
        if (!active) return;
        Point end = Limit(args.GetPosition(this));
        bool constrain = IsShiftDown();
        Cancel();
        if ((end - start).Length >= 3 && Committed != null)
            Committed(CreateStroke(Tool, start, end, constrain, Attributes));
        args.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs args)
    {
        active = false;
        preview.Points.Clear();
        base.OnLostMouseCapture(args);
    }

    internal bool Cancel()
    {
        if (!active) return false;
        active = false;
        preview.Points.Clear();
        if (IsMouseCaptured) ReleaseMouseCapture();
        return true;
    }

    Point Limit(Point point)
    {
        return new Point(Math.Max(0, Math.Min(ActualWidth, point.X)), Math.Max(0, Math.Min(ActualHeight, point.Y)));
    }

    static bool IsShiftDown() { return (Keyboard.Modifiers & ModifierKeys.Shift) != 0; }

    internal static Stroke CreateStroke(ShapeTool tool, Point start, Point end, bool constrain, DrawingAttributes attributes)
    {
        var drawing = attributes.Clone();
        drawing.FitToCurve = false;
        drawing.IgnorePressure = true;
        return new Stroke(new StylusPointCollection(Points(tool, start, end, constrain)), drawing);
    }

    internal static IEnumerable<Point> Points(ShapeTool tool, Point start, Point end, bool constrain)
    {
        Vector delta = end - start;
        bool segment = tool == ShapeTool.Line || tool == ShapeTool.Arrow;
        if (constrain && segment)
        {
            double angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 4)) * Math.PI / 4;
            var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
            end = start + direction * (delta * direction);
        }
        else if (constrain)
        {
            double side = Math.Min(Math.Abs(delta.X), Math.Abs(delta.Y));
            end = start + new Vector(Math.Sign(delta.X) * side, Math.Sign(delta.Y) * side);
        }

        if (segment)
        {
            yield return start;
            yield return end;
            if (tool == ShapeTool.Arrow && (end - start).Length > 0)
            {
                var axis = end - start;
                double length = Math.Min(19, axis.Length * .42);
                axis.Normalize();
                var normal = new Vector(-axis.Y, axis.X) * (length * .52);
                yield return end - axis * length + normal;
                yield return end;
                yield return end - axis * length - normal;
            }
            yield break;
        }

        var box = new Rect(start, end);
        if (tool == ShapeTool.Ellipse)
        {
            const int samples = 96;
            for (int sample = 0; sample <= samples; sample++)
            {
                double angle = (sample % samples) * (2 * Math.PI / samples);
                yield return new Point(box.Left + (1 + Math.Cos(angle)) * box.Width / 2,
                    box.Top + (1 + Math.Sin(angle)) * box.Height / 2);
            }
        }
        else if (tool == ShapeTool.Triangle)
        {
            var top = new Point(box.Left + box.Width / 2, box.Top);
            yield return top;
            yield return box.BottomRight;
            yield return box.BottomLeft;
            yield return top;
        }
        else
        {
            yield return box.TopLeft;
            yield return box.TopRight;
            yield return box.BottomRight;
            yield return box.BottomLeft;
            yield return box.TopLeft;
        }
    }
}
