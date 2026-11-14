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
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 1, 0, 1) };
        row.Children.Add(ActionButton("Resize…", "Resize selected image", 76, delegate { EditActiveImage(ResizeBitmap); }));
        row.Children.Add(ActionButton("Crop…", "Crop selected image", 69, delegate { EditActiveImage(CropBitmap); }));
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
        imageBar.Child = Rule(row, Surface);
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
        if (activeImage != null) activeImage.Opacity = 1;
        activeImage = image;
        imageNote = note;
        if (image != null) image.Opacity = .84;
        UpdateImageBar();
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
        var button = ActionButton(WithChevron(Symbol("M 3,11 L 10,3 L 13,6 L 6,13 H 3 Z M 2,15 H 14 M 5,9 L 8,12", 18)), "Text highlight color", 43, null);
        var menu = new ContextMenu();
        foreach (var preset in new[] { "#FFF09B", "#BFF1C9", "#C7E7FF", "#FBD4E2" })
        {
            string color = preset;
            var item = AddItem(menu, color, "", delegate { HighlightText(Brush(color)); });
            item.Icon = new Border { Width = 13, Height = 13, Background = Brush(color), CornerRadius = new CornerRadius(2) };
        }
        AddItem(menu, "More colors…", "", delegate { Dispatcher.BeginInvoke(new Action(delegate
        {
            var color = PickColor(ColorOf("#FFF09B"));
            if (color.HasValue) HighlightText(new SolidColorBrush(color.Value));
        })); });
        AddItem(menu, "Remove highlight", "", delegate { HighlightText(Brushes.Transparent); });
        AttachMenu(button, menu);
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
        if (start.CompareTo(end) == 0) return;
        current.Formatted = true;
        new TextRange(start, end).ApplyPropertyValue(TextElement.BackgroundProperty, color);
        MarkDirty(current);
        editor.Focus();
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
            var image = ImageAt(editor, point);
            if (image == null) { if (imageNote == note) SelectImage(null, null); return; }
            var container = ImageContainer(image);
            args.Handled = true;
            editor.Focus();
            if (args.ChangedButton == MouseButton.Left) { imageDragStart = point; editor.CaptureMouse(); }
            else if (args.ChangedButton == MouseButton.Right)
            {
                SelectImage(note, image);
                ShowImageMenu(note, image, container);
            }
        }), true);
        editor.PreviewMouseUp += delegate(object sender, MouseButtonEventArgs args)
        {
            if (args.ChangedButton != MouseButton.Left || !imageDragStart.HasValue) return;
            var start = imageDragStart.Value;
            imageDragStart = null;
            var point = args.GetPosition(editor);
            var image = ImageAt(editor, start);
            if (editor.IsMouseCaptured) editor.ReleaseMouseCapture();
            if (image == null) return;
            if (Math.Abs(point.X - start.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(point.Y - start.Y) > SystemParameters.MinimumVerticalDragDistance)
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
        editor.LostMouseCapture += delegate { imageDragStart = null; };
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
        AddItem(menu, "Crop…", "", delegate { Dispatcher.BeginInvoke(new Action(delegate { CropBitmap(note, image, container); })); });
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

    void CropBitmap(Note note, Image image, TextElement container)
    {
        var bitmap = (BitmapSource)image.Source;
        var dialog = new Window { Owner = this, Title = "Crop image", Width = 570, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = SettingsBackground };
        double scale = Math.Min(1, Math.Min(520.0 / bitmap.PixelWidth, 380.0 / bitmap.PixelHeight));
        var surface = new Canvas { Width = bitmap.PixelWidth * scale, Height = bitmap.PixelHeight * scale, ClipToBounds = true, Cursor = Cursors.Cross, HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent };
        surface.Children.Add(new Image { Source = bitmap, Width = surface.Width, Height = surface.Height, Stretch = Stretch.Fill, IsHitTestVisible = false });
        var outline = new Border { BorderBrush = Brushes.DeepSkyBlue, BorderThickness = new Thickness(2), Background = Brush("#330077CC"), IsHitTestVisible = false };
        surface.Children.Add(outline);
        Point origin = new Point();
        var selection = Rect.Empty;
        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = "Drag over the area you want to keep.", Foreground = Text, Margin = new Thickness(0, 0, 0, 10) });
        content.Children.Add(surface);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var crop = SettingsButton("Crop", delegate { dialog.DialogResult = true; });
        crop.IsEnabled = false;
        crop.IsDefault = true;
        var cancel = SettingsButton("Cancel", null); cancel.IsCancel = true; cancel.Margin = new Thickness(6, 0, 0, 0);
        actions.Children.Add(crop); actions.Children.Add(cancel); content.Children.Add(actions);
        surface.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args) { origin = args.GetPosition(surface); surface.CaptureMouse(); };
        surface.MouseMove += delegate(object sender, MouseEventArgs args)
        {
            if (!surface.IsMouseCaptured) return;
            var at = args.GetPosition(surface);
            at = new Point(Math.Max(0, Math.Min(surface.Width, at.X)), Math.Max(0, Math.Min(surface.Height, at.Y)));
            selection = new Rect(origin, at);
            Canvas.SetLeft(outline, selection.X); Canvas.SetTop(outline, selection.Y);
            outline.Width = selection.Width; outline.Height = selection.Height;
            crop.IsEnabled = selection.Width >= 2 && selection.Height >= 2;
        };
        surface.MouseLeftButtonUp += delegate { surface.ReleaseMouseCapture(); };
        dialog.Content = content;
        if (dialog.ShowDialog() != true || selection.IsEmpty) return;
        int x = (int)(selection.X / scale), y = (int)(selection.Y / scale);
        int width = Math.Min(bitmap.PixelWidth - x, Math.Max(1, (int)Math.Ceiling(selection.Width / scale)));
        int height = Math.Min(bitmap.PixelHeight - y, Math.Max(1, (int)Math.Ceiling(selection.Height / scale)));
        if (width <= 0 || height <= 0) return;
        var cropped = new CroppedBitmap(bitmap, new Int32Rect(x, y, width, height));
        SelectImage(note, ReplaceBitmap(note, container, cropped, Math.Max(12, image.Width * width / bitmap.PixelWidth)));
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
