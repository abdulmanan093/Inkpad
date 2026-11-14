using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

sealed partial class Inkpad
{
    const string CropIcon = "M 4,1 V 12 H 15 M 1,4 H 12 V 15 M 7,9 L 14,2";
    static readonly Brush ImageSelectionBlue = Brush("#0078D4");
    readonly Canvas imageOverlay = new Canvas { Visibility = Visibility.Collapsed };
    readonly Border imageFrame = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };
    readonly Path cropShade = new Path { Fill = Brush("#88000000"), IsHitTestVisible = false };
    readonly Thumb[] imageHandles = new Thumb[8];
    static readonly Point[] HandlePositions = {
        new Point(0,0), new Point(.5,0), new Point(1,0), new Point(1,.5),
        new Point(1,1), new Point(.5,1), new Point(0,1), new Point(0,.5)
    };
    static readonly string[] HandleNames = { "Top left", "Top", "Top right", "Right", "Bottom right", "Bottom", "Bottom left", "Left" };
    static readonly Cursor[] HandleCursors = { Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE };
    FrameworkElement normalImageTools, cropImageTools;
    Rect? imageCrop;
    ImageHandleDrag imageResize;

    sealed class ImageHandleDrag
    {
        internal Image Image;
        internal Point Start;
        internal Rect Bounds, Initial, Preview;
    }

    void BuildImageOverlay()
    {
        var template = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'>
            <Grid Background='Transparent'>
                <Border Width='8' Height='8' CornerRadius='1.5' Background='White' BorderBrush='#0067B8' BorderThickness='1' />
            </Grid></ControlTemplate>");
        imageOverlay.Children.Add(cropShade);
        imageOverlay.Children.Add(imageFrame);
        for (int i = 0; i < imageHandles.Length; i++)
        {
            int index = i;
            var handle = new Thumb { Width = 16, Height = 16, Cursor = HandleCursors[i], Template = template, Focusable = false };
            imageHandles[i] = handle;
            handle.DragStarted += delegate { StartImageHandle(Mouse.GetPosition(page)); };
            handle.DragDelta += delegate { MoveImageHandle(index, Mouse.GetPosition(page)); };
            handle.DragCompleted += delegate(object sender, DragCompletedEventArgs args) { FinishImageHandle(args.Canceled); };
            imageOverlay.Children.Add(handle);
        }
    }

    FrameworkElement BuildCropTools()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 1, 0, 1),
            Visibility = Visibility.Collapsed };
        row.Children.Add(ActionButton(WithIcon(Symbol(CropIcon, 16), "Apply crop"), "Apply crop", 104, CommitImageCrop));
        row.Children.Add(ActionButton("Cancel", "Cancel crop (Esc)", 64, delegate { StopImageCrop(); current.TextBox.Focus(); }));
        var instruction = Label("Drag the handles to keep the part you want.", 12, Muted);
        instruction.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(instruction);
        cropImageTools = row;
        return row;
    }

    Rect ImageBounds()
    {
        if (page == null || imageNote != current || ImageContainer(activeImage) == null ||
            !activeImage.IsArrangeValid || activeImage.ActualWidth < 1 || activeImage.ActualHeight < 1) return Rect.Empty;
        return new Rect(activeImage.TranslatePoint(new Point(), page), activeImage.RenderSize);
    }

    void PositionImageOverlay()
    {
        Rect bounds = ImageBounds();
        if (drawing || markdownPreview != null || bounds.IsEmpty)
        {
            imageOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        imageOverlay.Visibility = Visibility.Visible;
        Rect frame = imageCrop.HasValue ? CropOnPage(bounds, imageCrop.Value) :
            imageResize != null ? imageResize.Preview : bounds;
        imageFrame.BorderBrush = imageCrop.HasValue ? Brushes.White : ImageSelectionBlue;
        Canvas.SetLeft(imageFrame, frame.Left); Canvas.SetTop(imageFrame, frame.Top);
        imageFrame.Width = frame.Width; imageFrame.Height = frame.Height;
        if (imageCrop.HasValue)
        {
            var previous = cropShade.Data as CombinedGeometry;
            if (previous == null || ((RectangleGeometry)previous.Geometry1).Rect != bounds ||
                ((RectangleGeometry)previous.Geometry2).Rect != frame)
                cropShade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds), new RectangleGeometry(frame));
            cropShade.Visibility = Visibility.Visible;
        }
        else cropShade.Visibility = Visibility.Collapsed;
        for (int i = 0; i < imageHandles.Length; i++)
        {
            var handle = imageHandles[i];
            var at = HandlePositions[i];
            Canvas.SetLeft(handle, frame.Left + at.X * frame.Width - handle.Width / 2);
            Canvas.SetTop(handle, frame.Top + at.Y * frame.Height - handle.Height / 2);
            string name = (imageCrop.HasValue ? "Crop " : "Resize ") + HandleNames[i].ToLowerInvariant();
            if (AutomationProperties.GetName(handle) != name) AutomationProperties.SetName(handle, name);
        }
    }

    void StartImageCrop()
    {
        if (imageNote != current || ImageContainer(activeImage) == null) return;
        imageResize = null;
        imageCrop = new Rect(0, 0, 1, 1);
        normalImageTools.Visibility = Visibility.Collapsed;
        cropImageTools.Visibility = Visibility.Visible;
        current.TextBox.Focus();
        PositionImageOverlay();
    }

    void StopImageCrop()
    {
        if (!imageCrop.HasValue) return;
        imageCrop = null;
        imageResize = null;
        normalImageTools.Visibility = Visibility.Visible;
        cropImageTools.Visibility = Visibility.Collapsed;
        PositionImageOverlay();
    }

    void CommitImageCrop()
    {
        if (!imageCrop.HasValue || imageNote != current || ImageContainer(activeImage) == null) return;
        var image = activeImage;
        var crop = imageCrop.Value;
        StopImageCrop();
        if (crop == new Rect(0, 0, 1, 1)) { current.TextBox.Focus(); return; }
        var bitmap = (BitmapSource)image.Source;
        var pixels = CropPixels(crop, bitmap.PixelWidth, bitmap.PixelHeight);
        var cropped = new CroppedBitmap(bitmap, pixels);
        SelectImage(current, ReplaceBitmap(current, ImageContainer(image), cropped, Math.Max(1, image.Width * pixels.Width / bitmap.PixelWidth)));
        current.TextBox.Focus();
    }

    static Rect CropOnPage(Rect bounds, Rect crop)
    {
        return new Rect(bounds.X + bounds.Width * crop.X, bounds.Y + bounds.Height * crop.Y,
            bounds.Width * crop.Width, bounds.Height * crop.Height);
    }

    internal static Int32Rect CropPixels(Rect crop, int width, int height)
    {
        int left = Math.Min(width - 1, Math.Max(0, (int)Math.Round(crop.Left * width)));
        int top = Math.Min(height - 1, Math.Max(0, (int)Math.Round(crop.Top * height)));
        int right = Math.Max(left + 1, Math.Min(width, (int)Math.Round(crop.Right * width)));
        int bottom = Math.Max(top + 1, Math.Min(height, (int)Math.Round(crop.Bottom * height)));
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    void StartImageHandle(Point pointer)
    {
        var bounds = ImageBounds();
        if (bounds.IsEmpty) return;
        imageResize = new ImageHandleDrag { Image = activeImage, Start = pointer, Bounds = bounds,
            Initial = imageCrop.HasValue ? imageCrop.Value : bounds, Preview = bounds };
    }

    void MoveImageHandle(int index, Point pointer)
    {
        if (imageResize == null || activeImage != imageResize.Image) return;
        var drag = imageResize;
        var delta = pointer - drag.Start;
        var handle = HandlePositions[index];
        if (imageCrop.HasValue)
        {
            var bitmap = (BitmapSource)activeImage.Source;
            var start = drag.Initial;
            double x = delta.X / drag.Bounds.Width, y = delta.Y / drag.Bounds.Height;
            double l = handle.X == 0 ? Math.Max(0, Math.Min(start.Right - 1.0 / bitmap.PixelWidth, start.Left + x)) : start.Left;
            double t = handle.Y == 0 ? Math.Max(0, Math.Min(start.Bottom - 1.0 / bitmap.PixelHeight, start.Top + y)) : start.Top;
            double r = handle.X == 1 ? Math.Min(1, Math.Max(start.Left + 1.0 / bitmap.PixelWidth, start.Right + x)) : start.Right;
            double b = handle.Y == 1 ? Math.Min(1, Math.Max(start.Top + 1.0 / bitmap.PixelHeight, start.Bottom + y)) : start.Bottom;
            imageCrop = new Rect(new Point(l, t), new Point(r, b));
        }
        else
        {
            var start = drag.Bounds;
            double sx = handle.X == .5 ? 0 : (handle.X == 0 ? -1 : 1) * start.Width;
            double sy = handle.Y == .5 ? 0 : (handle.Y == 0 ? -1 : 1) * start.Height;
            double scale = 1 + (delta.X * sx + delta.Y * sy) / (sx * sx + sy * sy);
            var figure = ImageContainer(activeImage) as Figure;
            double available = Math.Max(start.Width, current.TextBox.ActualWidth - 48);
            double width = Math.Min(available, Math.Max(Math.Min(12, start.Width), start.Width * scale));
            double align = figure == null ? 0 : figure.HorizontalAnchor == FigureHorizontalAnchor.ContentRight ? 1 :
                figure.HorizontalAnchor == FigureHorizontalAnchor.ContentCenter ? .5 : 0;
            drag.Preview = new Rect(start.X + (start.Width - width) * align, start.Y, width, width * start.Height / start.Width);
        }
        PositionImageOverlay();
    }

    void FinishImageHandle(bool canceled)
    {
        var drag = imageResize;
        imageResize = null;
        if (drag != null && activeImage == drag.Image)
        {
            if (imageCrop.HasValue)
            {
                if (canceled) imageCrop = drag.Initial;
            }
            else if (!canceled && Math.Abs(drag.Preview.Width - drag.Bounds.Width) >= .5 && ImageContainer(activeImage) != null)
                SelectImage(current, ReplaceBitmap(current, ImageContainer(activeImage), (BitmapSource)activeImage.Source, drag.Preview.Width));
        }
        PositionImageOverlay();
    }
}
