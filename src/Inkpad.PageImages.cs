using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

sealed class PageImage
{
    internal readonly BitmapSource Bitmap;
    internal readonly Image Image;
    internal readonly InkCanvas Ink = new InkCanvas { Background = Brushes.Transparent, ClipToBounds = true };
    internal readonly Viewbox View = new Viewbox { Stretch = Stretch.Fill };
    readonly Canvas frame = new Canvas { ClipToBounds = true, Background = Brushes.Transparent };
    readonly Canvas contents = new Canvas();
    internal Rect Bounds { get; private set; }
    internal Int32Rect Crop { get; private set; }

    internal PageImage(BitmapSource bitmap, Rect bounds)
    {
        Bitmap = bitmap;
        Image = new Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Stretch = Stretch.Fill, IsHitTestVisible = false };
        Image.Tag = this;
        Ink.Width = Image.Width;
        Ink.Height = Image.Height;
        Ink.UseCustomCursor = true;
        AutomationProperties.SetName(View, "Image. Drag to move or click for resize handles.");
        contents.Children.Add(Image);
        contents.Children.Add(Ink);
        frame.Children.Add(contents);
        View.Child = frame;
        Update(bounds, new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
    }

    internal void Update(Rect bounds, Int32Rect crop)
    {
        Bounds = bounds;
        Crop = crop;
        ShowAt(bounds);
        frame.Width = crop.Width;
        frame.Height = crop.Height;
        Canvas.SetLeft(contents, -crop.X);
        Canvas.SetTop(contents, -crop.Y);
    }

    internal void ShowAt(Rect bounds)
    {
        Canvas.SetLeft(View, bounds.X);
        Canvas.SetTop(View, bounds.Y);
        View.Width = bounds.Width;
        View.Height = bounds.Height;
    }

    internal void Draw(DrawingContext dc)
    {
        dc.PushTransform(new TranslateTransform(Bounds.X, Bounds.Y));
        Draw(dc, Bounds.Size);
        dc.Pop();
    }

    internal BitmapSource CopyBitmap()
    {
        if (Ink.Strokes.Count == 0) return Crop == new Int32Rect(0, 0, Bitmap.PixelWidth, Bitmap.PixelHeight) ? Bitmap : new CroppedBitmap(Bitmap, Crop);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) Draw(dc, new Size(Crop.Width, Crop.Height));
        var bitmap = new RenderTargetBitmap(Crop.Width, Crop.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    void Draw(DrawingContext dc, Size size)
    {
        dc.PushClip(new RectangleGeometry(new Rect(size)));
        double sx = size.Width / Crop.Width, sy = size.Height / Crop.Height;
        dc.PushTransform(new MatrixTransform(sx, 0, 0, sy, -Crop.X * sx, -Crop.Y * sy));
        dc.DrawImage(Bitmap, new Rect(0, 0, Bitmap.PixelWidth, Bitmap.PixelHeight));
        Ink.Strokes.Draw(dc);
        dc.Pop();
        dc.Pop();
    }
}

sealed partial class Inkpad
{
    readonly Canvas imageLayer = new Canvas();

    PageImage SelectedPageImage { get { return activeImage == null ? null : activeImage.Tag as PageImage; } }
    bool HasSelectedImage { get { return imageNote == current && (SelectedPageImage != null || ImageContainer(activeImage) != null); } }

    void InsertPageImage(Note note, BitmapSource bitmap)
    {
        if (drawing) SetDrawing(false);
        if (markdownPreview != null) SetMarkdownPreview(false);
        if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 40000000)
            throw new System.IO.InvalidDataException("Choose an image smaller than 40 megapixels.");
        var editor = note.TextBox;
        var line = editor.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
        var content = ImagePageBounds();
        double width = Math.Min(bitmap.PixelWidth, Math.Min(520, Math.Max(120, content.Width * .45)));
        var origin = line.IsEmpty ? content.TopLeft : editor.TranslatePoint(line.TopLeft, page);
        double left = Math.Max(content.Left, Math.Min(content.Right - width, origin.X));
        var item = new PageImage(bitmap, new Rect(left, Math.Max(content.Top, origin.Y), width, width * bitmap.PixelHeight / bitmap.PixelWidth));
        TrackPageImage(note, item);
        AddPageImage(note, item, note.Images.Count);
        int index = note.Images.Count - 1;
        note.History.Record(delegate { RemovePageImage(note, item); }, delegate { AddPageImage(note, item, index); });
        SelectImage(note, item.Image);
        MarkDirty(note);
    }

    void TrackPageImage(Note note, PageImage item)
    {
        note.History.TrackInk(item.Ink.Strokes);
        item.Ink.Strokes.StrokesChanged += delegate { if (!note.History.Replaying) MarkDirty(note); };
        item.View.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs args)
        {
            if (drawing || note != current) return;
            if (args.ChangedButton != MouseButton.Left && args.ChangedButton != MouseButton.Right) return;
            note.TextBox.Focus();
            SelectImage(note, item.Image);
            if (args.ChangedButton == MouseButton.Right) ShowImageMenu(note, item.Image);
            else if (note.TextBox.CaptureMouse()) StartImageMove(args.GetPosition(page), args.GetPosition(this));
            args.Handled = true;
        };
    }

    void AddPageImage(Note note, PageImage item, int index)
    {
        note.Images.Insert(index, item);
        if (note == current)
        {
            imageLayer.Children.Insert(index, item.View);
            ConfigureImageInk(item);
            ResizePage();
        }
    }

    void RemovePageImage(Note note, PageImage item)
    {
        note.Images.Remove(item);
        if (note == current)
        {
            imageLayer.Children.Remove(item.View);
            ResizePage();
        }
    }

    void ChangePageImage(PageImage item, Rect bounds, Int32Rect crop)
    {
        var before = item.Bounds;
        var previousCrop = item.Crop;
        if (before == bounds && previousCrop == crop) return;
        var note = current;
        item.Update(bounds, crop);
        note.History.Record(delegate { item.Update(before, previousCrop); }, delegate { item.Update(bounds, crop); });
        ResizePage();
        PositionImageOverlay();
        MarkDirty(note);
    }

    void ConfigureImageInk(PageImage item)
    {
        var attributes = current.Ink.DefaultDrawingAttributes.Clone();
        attributes.Width = Math.Max(DrawingAttributes.MinWidth, Math.Min(DrawingAttributes.MaxWidth, attributes.Width * item.Crop.Width / item.Bounds.Width));
        attributes.Height = Math.Max(DrawingAttributes.MinHeight, Math.Min(DrawingAttributes.MaxHeight, attributes.Height * item.Crop.Height / item.Bounds.Height));
        item.Ink.DefaultDrawingAttributes = attributes;
        item.Ink.EditingMode = current.Ink.EditingMode;
        item.Ink.EditingModeInverted = current.Ink.EditingModeInverted;
        bool inkInput = current.Ink.IsHitTestVisible;
        item.Ink.IsHitTestVisible = inkInput;
        item.Ink.Cursor = inkInput ? current.Ink.Cursor : Cursors.Arrow;
        item.View.Cursor = inkInput ? null : Cursors.Arrow;
        item.View.ForceCursor = !inkInput;
        item.View.IsHitTestVisible = !TextHighlightActive;
    }

    void CommitShape(Stroke stroke)
    {
        var item = current.Images.LastOrDefault(image => image.Bounds.Contains(shapeLayer.StartPoint));
        if (item == null) { current.Ink.Strokes.Add(stroke); return; }
        double sx = item.Crop.Width / item.Bounds.Width, sy = item.Crop.Height / item.Bounds.Height;
        stroke.Transform(new Matrix(sx, 0, 0, sy, item.Crop.X - item.Bounds.X * sx, item.Crop.Y - item.Bounds.Y * sy), true);
        item.Ink.Strokes.Add(stroke);
    }

    void ConvertLegacyImage(Note note, Image image, Rect bounds)
    {
        var container = ImageContainer(image);
        if (container == null) return;
        var item = new PageImage((BitmapSource)image.Source, bounds);
        TrackPageImage(note, item);
        int index = note.Images.Count;
        note.TextBox.LockCurrentUndoUnit();
        new TextRange(container.ElementStart, container.ElementEnd).Text = "";
        AddPageImage(note, item, index);
        note.History.AttachToDocumentEdit(delegate { RemovePageImage(note, item); }, delegate { AddPageImage(note, item, index); });
        SelectImage(note, item.Image);
        MarkDirty(note);
    }

    void ConvertLegacyImagesForDrawing()
    {
        current.TextBox.UpdateLayout();
        var embedded = NoteFiles.DocumentImages(current.TextBox.Document.Blocks)
            .Where(image => image.IsArrangeValid && image.ActualWidth > 0 && image.ActualHeight > 0)
            .Select(image => Tuple.Create(image, new Rect(image.TranslatePoint(new Point(), page), image.RenderSize)))
            .ToArray();
        foreach (var pair in embedded)
        {
            var bounds = pair.Item2;
            bounds.X = Math.Max(0, bounds.X);
            bounds.Y = Math.Max(0, bounds.Y);
            ConvertLegacyImage(current, pair.Item1, bounds);
        }
    }
}
