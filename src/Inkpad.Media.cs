using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

sealed partial class Inkpad
{
    readonly Border imageBar = new Border { Visibility = Visibility.Collapsed };
    Note imageNote;
    Image activeImage;
    Point? imageDragStart;

    void BuildImageBar()
    {
        BuildImageOverlay();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 1, 0, 1) };
        var resize = ActionButton(WithIcon(Symbol("M 2,7 V 2 H 7 M 2,2 L 7,7 M 9,14 H 14 V 9 M 14,14 L 9,9", 15), "Resize"), "Resize selected image", 76, delegate { EditActiveImage(ResizeBitmap); });
        var crop = ActionButton(WithIcon(Symbol(CropIcon, 16), "Crop"), "Crop selected image", 70, StartImageCrop);
        row.Children.Add(resize);
        row.Children.Add(crop);
        var layout = ActionButton(WithChevron(Label("Wrap", 13, Text)), "Image wrap and placement", 83, null);
        var menu = new ContextMenu();
        AddItem(menu, "Wrap left", "", delegate { AlignImage(FigureHorizontalAnchor.ContentLeft, WrapDirection.Both); });
        AddItem(menu, "Wrap center", "", delegate { AlignImage(FigureHorizontalAnchor.ContentCenter, WrapDirection.Both); });
        AddItem(menu, "Wrap right", "", delegate { AlignImage(FigureHorizontalAnchor.ContentRight, WrapDirection.Both); });
        AddItem(menu, "Above and below", "", delegate { AlignImage(FigureHorizontalAnchor.ContentCenter, WrapDirection.None); });
        AttachMenu(layout, menu);
        row.Children.Add(layout);
        row.Children.Add(ActionButton("Copy", "Copy selected image", 60, delegate { if (activeImage != null) CopyBitmap(activeImage); }));
        row.Children.Add(ActionButton("Delete", "Delete selected image", 66, DeleteActiveImage));
        row.Children.Add(ActionButton("Done", "Finish editing image", 60, delegate { SelectImage(null, null); if (current != null) current.TextBox.Focus(); }));
        normalImageTools = row;
        var tools = new Grid();
        tools.Children.Add(row);
        tools.Children.Add(BuildCropTools());
        imageBar.Child = Rule(tools, Surface);
        Place(imageBar, Dock.Top);
    }

    void EditActiveImage(Action<Note, Image, TextElement> edit)
    {
        var container = ImageContainer(activeImage);
        if (container == null || imageNote != current) return;
        edit(imageNote, activeImage, container);
    }

    void UpdateImageBar()
    {
        imageBar.Visibility = !drawing && imageNote == current && ImageContainer(activeImage) != null ? Visibility.Visible : Visibility.Collapsed;
    }

    void SelectImage(Note note, Image image)
    {
        if (image != activeImage) StopImageCrop();
        imageResize = null;
        activeImage = image;
        imageNote = note;
        UpdateImageBar();
        PositionImageOverlay();
    }

    static TextElement ImageContainer(Image image)
    {
        if (image == null) return null;
        var block = image.Parent as BlockUIContainer;
        if (block != null) return block.Parent as Figure;
        return image.Parent as InlineUIContainer;
    }

    static Image ImageAt(RichTextBox editor, Point point)
    {
        var hit = editor.InputHitTest(point) as Image;
        if (ImageContainer(hit) != null) return hit;
        return NoteFiles.DocumentImages(editor.Document.Blocks).LastOrDefault(image =>
            image.IsArrangeValid && image.ActualWidth > 0 && image.ActualHeight > 0 &&
            new Rect(image.TranslatePoint(new Point(), editor), image.RenderSize).Contains(point));
    }

    void AlignImage(FigureHorizontalAnchor anchor, WrapDirection wrapDirection)
    {
        EditActiveImage(delegate(Note note, Image image, TextElement container)
        {
            var replacement = ReplaceBitmap(note, container, (BitmapSource)image.Source, image.Width, anchor, wrapDirection);
            SelectImage(note, replacement);
            note.TextBox.Focus();
        });
    }

    void DeleteActiveImage()
    {
        EditActiveImage(delegate(Note note, Image image, TextElement container)
        {
            new TextRange(container.ElementStart, container.ElementEnd).Text = "";
            SelectImage(null, null);
            note.TextBox.Focus();
        });
    }
    Button BuildHighlightButton()
    {
        var button = ActionButton(WithChevron(Symbol(MarkerIcon, 18)), "Text highlight color", 43, null);
        button.Click += delegate { OpenInkPicker(button, true, true); };
        return button;
    }

    void HighlightText(Brush color)
    {
        if (markdownPreview != null) SetMarkdownPreview(false);
        if (drawing) SetDrawing(false);
        var editor = current.TextBox;
        var start = editor.Selection.Start;
        var end = editor.Selection.End;
        if (editor.Selection.IsEmpty)
        {
            var line = editor.CaretPosition.GetLineStartPosition(0);
            if (line == null || line.Paragraph == null) return;
            var next = line.GetLineStartPosition(1);
            start = line;
            end = next != null && next.Paragraph == line.Paragraph ? next : line.Paragraph.ContentEnd;
        }
        ApplyTextHighlight(current, start, end, color);
        editor.Focus();
    }

    void ApplyTextHighlight(Note note, TextPointer start, TextPointer end, Brush color)
    {
        if (start.CompareTo(end) == 0) return;
        note.Formatted = true;
        new TextRange(start, end).ApplyPropertyValue(TextElement.BackgroundProperty, color);
        MarkDirty(note);
        if (note == current) UpdateStatus();
    }

    Button BuildImageButton()
    {
        var button = ActionButton(Symbol("M 2,3 H 14 V 13 H 2 Z M 2,11 L 6,7 L 9,10 L 11,8 L 14,11 M 10,5 H 10.3", 18), "Insert image", 32, null);
        var menu = new ContextMenu();
        AddItem(menu, "Insert image from file…", "", InsertImageFile);
        AddItem(menu, "Paste image", "Ctrl+V", delegate
        {
            if (!TryPasteBitmap(current)) ShowNoteDialog("Paste image", "Copy an image to the clipboard first.", "OK", null);
        });
        AttachMenu(button, menu);
        return button;
    }

    void InsertImageFile()
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*", Title = "Insert image" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using (var file = File.OpenRead(dialog.FileName))
            {
                var decoder = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                InsertBitmap(current, decoder.Frames[0]);
            }
        }
        catch (Exception error) { FileError("insert an image into", error); }
    }

    void AddMediaEditing(Note note)
    {
        var editor = note.TextBox;
        editor.AddHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler(delegate(object sender, QueryCursorEventArgs args)
        {
            if (note == current && TextHighlightActive)
            {
                args.Cursor = Cursors.IBeam;
                args.Handled = true;
            }
        }), true);
        DataObject.AddPastingHandler(editor, delegate(object sender, DataObjectPastingEventArgs args)
        {
            try
            {
                var image = args.SourceDataObject.GetData(DataFormats.Bitmap, true) as BitmapSource;
                if (image == null) return;
                args.CancelCommand();
                InsertBitmap(note, image);
            }
            catch (Exception error) { args.CancelCommand(); FileError("paste an image into", error); }
        });
        editor.PreviewTextInput += delegate(object sender, TextCompositionEventArgs args)
        {
            if (args.Text == " ") CorrectPreviousWord(note);
        };
        editor.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(delegate(object sender, MouseButtonEventArgs args)
        {
            var point = args.GetPosition(editor);
            if (args.ChangedButton == MouseButton.Left && BeginTextHighlight(note, point)) { args.Handled = true; return; }
            if (imageCrop.HasValue) StopImageCrop();
            var image = ImageAt(editor, point);
            if (image == null) { if (imageNote == note) SelectImage(null, null); return; }
            var container = ImageContainer(image);
            args.Handled = true;
            editor.Focus();
            if (args.ChangedButton == MouseButton.Left)
            {
                SelectImage(note, image);
                imageDragStart = args.GetPosition(this);
                editor.CaptureMouse();
            }
            else if (args.ChangedButton == MouseButton.Right)
            {
                SelectImage(note, image);
                ShowImageMenu(note, image, container);
            }
        }), true);
        editor.PreviewMouseUp += delegate(object sender, MouseButtonEventArgs args)
        {
            if (args.ChangedButton == MouseButton.Left && EndTextHighlight(note, args.GetPosition(editor))) { args.Handled = true; return; }
            if (args.ChangedButton != MouseButton.Left || !imageDragStart.HasValue) return;
            var start = imageDragStart.Value;
            imageDragStart = null;
            var point = args.GetPosition(editor);
            var image = imageNote == note ? activeImage : null;
            if (editor.IsMouseCaptured) editor.ReleaseMouseCapture();
            if (image == null) return;
            var movement = args.GetPosition(this) - start;
            if (Math.Abs(movement.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(movement.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var target = editor.GetPositionFromPoint(point, true);
                var anchor = point.X < editor.ActualWidth / 3 ? FigureHorizontalAnchor.ContentLeft :
                    point.X > editor.ActualWidth * 2 / 3 ? FigureHorizontalAnchor.ContentRight : FigureHorizontalAnchor.ContentCenter;
                var container = ImageContainer(image);
                if (target != null && !new TextRange(container.ElementStart, container.ElementEnd).Contains(target))
                    image = MoveBitmap(note, container, target, (BitmapSource)image.Source, image.Width, anchor);
            }
            SelectImage(note, image);
            args.Handled = true;
        };
        editor.PreviewMouseMove += delegate(object sender, MouseEventArgs args)
        {
            if (highlightAnchor == null) return;
            DragTextHighlight(note, args.GetPosition(editor));
            args.Handled = true;
        };
        editor.LostMouseCapture += delegate { imageDragStart = null; highlightAnchor = null; };
        editor.SelectionChanged += delegate { if (imageNote == note) SelectImage(null, null); };
        editor.TextChanged += delegate { if (imageNote == note) UpdateImageBar(); };
        editor.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && TryPasteBitmap(note))
            { args.Handled = true; return; }
            if (imageNote == note && ImageContainer(activeImage) != null)
            {
                if (args.Key == Key.Delete || args.Key == Key.Back) { DeleteActiveImage(); args.Handled = true; return; }
                if ((args.Key == Key.C || args.Key == Key.X) && Keyboard.Modifiers == ModifierKeys.Control)
                { if (CopyBitmap(activeImage) && args.Key == Key.X) DeleteActiveImage(); args.Handled = true; return; }
                SelectImage(null, null);
            }
            var image = SelectedImage(editor);
            if (image == null) return;
            if (args.Key == Key.Delete || args.Key == Key.Back)
            { editor.Selection.Text = ""; args.Handled = true; }
            else if (args.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            { CopyBitmap(image); args.Handled = true; }
            else if (args.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
            { if (CopyBitmap(image)) editor.Selection.Text = ""; args.Handled = true; }
        };
    }

    bool TryPasteBitmap(Note note)
    {
        try
        {
            if (!Clipboard.ContainsImage()) return false;
            var bitmap = Clipboard.GetImage();
            if (bitmap == null) return false;
            InsertBitmap(note, bitmap);
        }
        catch (Exception error) { FileError("paste an image into", error); }
        return true;
    }

    internal static Image SelectedImage(RichTextBox editor)
    {
        if (editor.Selection.IsEmpty) return null;
        var start = editor.Selection.Start;
        var end = editor.Selection.End;
        for (var at = start; at != null && at.CompareTo(end) < 0; at = at.GetNextContextPosition(LogicalDirection.Forward))
        {
            var container = at.GetAdjacentElement(LogicalDirection.Forward) as InlineUIContainer;
            if (container == null) continue;
            if (!new TextRange(start, container.ElementStart).IsEmpty || !new TextRange(container.ElementEnd, end).IsEmpty) return null;
            return container.Child as Image;
        }
        return null;
    }

    internal static Image InsertBitmap(Note note, BitmapSource bitmap)
    {
        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0 || (long)bitmap.PixelWidth * bitmap.PixelHeight > 40000000)
            throw new InvalidDataException("Choose an image smaller than 40 megapixels.");
        var editor = note.TextBox;
        var at = editor.Selection.Start;
        if (at.Paragraph == null) at = editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
        double width = Math.Min(Math.Max(120, editor.ActualWidth * .45), Math.Min(520, bitmap.PixelWidth));
        if (editor.ActualWidth == 0) width = Math.Min(520, bitmap.PixelWidth);
        var image = NewInlineImage(bitmap, width);
        editor.BeginChange();
        try
        {
            editor.Selection.Text = "";
            var container = PlaceBitmap(image, at, FigureHorizontalAnchor.ContentLeft, WrapDirection.Both);
            editor.CaretPosition = container.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
            note.Formatted = true;
        }
        finally { editor.EndChange(); }
        return image;
    }

    internal static TextElement PlaceBitmap(Image image, TextPointer at, FigureHorizontalAnchor anchor, WrapDirection wrapDirection)
    {
        at = at.GetInsertionPosition(LogicalDirection.Forward);
        if (NoteTables.Ancestor<TableCell>(at) != null || NoteTables.Ancestor<AnchoredBlock>(at) != null)
            return new InlineUIContainer(image, at) { BaselineAlignment = BaselineAlignment.Top };
        return new Figure(new BlockUIContainer(image) { Margin = new Thickness(0) }, at)
        {
            Width = new FigureLength(image.Width),
            HorizontalAnchor = anchor, VerticalAnchor = FigureVerticalAnchor.ParagraphTop,
            WrapDirection = wrapDirection, Margin = new Thickness(6, 3, 10, 7), Padding = new Thickness(0),
            CanDelayPlacement = false
        };
    }

    static Image NewInlineImage(BitmapSource source, double width)
    {
        var image = new Image { Source = source, Width = width, Height = width * source.PixelHeight / source.PixelWidth,
            Stretch = Stretch.Uniform, Cursor = Cursors.SizeAll, ToolTip = "Click to edit or drag to move" };
        AutomationProperties.SetName(image, "Embedded image");
        return image;
    }

    void ShowImageMenu(Note note, Image image, TextElement container)
    {
        var menu = new ContextMenu { PlacementTarget = note.TextBox, Placement = PlacementMode.MousePoint };
        AddItem(menu, "Copy image", "Ctrl+C", delegate { CopyBitmap(image); });
        AddItem(menu, "Resize…", "", delegate { Dispatcher.BeginInvoke(new Action(delegate { ResizeBitmap(note, image, container); })); });
        AddItem(menu, "Crop", "", delegate { Dispatcher.BeginInvoke(new Action(StartImageCrop)); });
        menu.Items.Add(new Separator());
        AddItem(menu, "Delete image", "Del", DeleteActiveImage);
        InkpadStyle.Apply(menu);
        menu.IsOpen = true;
    }

    bool CopyBitmap(Image image)
    {
        try { Clipboard.SetImage((BitmapSource)image.Source); return true; }
        catch (System.Runtime.InteropServices.ExternalException)
        { ShowNoteDialog("Clipboard busy", "Try copying the image again.", "OK", null); return false; }
    }

    void ResizeBitmap(Note note, Image image, TextElement container)
    {
        double width;
        string value = AskText("Resize image", "Width in pixels. The aspect ratio stays the same.", Math.Round(image.Width).ToString(CultureInfo.CurrentCulture));
        if (value == null) return;
        if (!double.TryParse(value, out width) || double.IsNaN(width) || width < 12 || width > 10000)
        { ShowNoteDialog("Resize image", "Enter a width from 12 to 10,000 pixels.", "OK", null); return; }
        SelectImage(note, ReplaceBitmap(note, container, (BitmapSource)image.Source, width));
    }

    internal static Image ReplaceBitmap(Note note, TextElement container, BitmapSource bitmap, double width,
        FigureHorizontalAnchor? alignment = null, WrapDirection? wrapping = null)
    {
        var old = container as Figure;
        return MoveBitmap(note, container, container.ElementStart, bitmap, width,
            alignment ?? (old == null ? FigureHorizontalAnchor.ContentLeft : old.HorizontalAnchor),
            wrapping ?? (old == null ? WrapDirection.Both : old.WrapDirection));
    }

    static Image MoveBitmap(Note note, TextElement container, TextPointer at, BitmapSource bitmap, double width,
        FigureHorizontalAnchor alignment, WrapDirection wrapping = WrapDirection.Both)
    {
        var editor = note.TextBox;
        editor.BeginChange();
        try
        {
            new TextRange(container.ElementStart, container.ElementEnd).Text = "";
            var image = NewInlineImage(bitmap, width);
            var replacement = PlaceBitmap(image, at, alignment, wrapping);
            editor.CaretPosition = replacement.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
            return image;
        }
        finally { editor.EndChange(); }
    }
}
