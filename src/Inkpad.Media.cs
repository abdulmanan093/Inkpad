using System;
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
    Button textHighlightButton;

    void BuildImageBar()
    {
        BuildImageOverlay();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 1, 0, 1) };
        var crop = ActionButton(WithIcon(Symbol(CropIcon, 16), "Crop"), "Crop selected image", 70, StartImageCrop);
        row.Children.Add(crop);
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

    void UpdateImageBar()
    {
        imageBar.Visibility = !drawing && HasSelectedImage ? Visibility.Visible : Visibility.Collapsed;
    }

    void SelectImage(Note note, Image image)
    {
        if (image != activeImage) StopImageCrop();
        CancelImageMove();
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

    void DeleteActiveImage()
    {
        if (!HasSelectedImage) return;
        var note = imageNote;
        var item = SelectedPageImage;
        if (item == null)
        {
            var container = ImageContainer(activeImage);
            new TextRange(container.ElementStart, container.ElementEnd).Text = "";
        }
        else
        {
            int index = note.Images.IndexOf(item);
            RemovePageImage(note, item);
            note.History.Record(delegate { AddPageImage(note, item, index); }, delegate { RemovePageImage(note, item); });
            MarkDirty(note);
        }
        SelectImage(null, null);
        note.TextBox.Focus();
    }
    StackPanel BuildHighlightToolbar()
    {
        var marker = Symbol("M 3,2 H 13 V 5 L 11,8 H 5 L 3,5 Z M 6,8 V 12 L 10,10 V 8", 18);
        ((Grid)marker.Child).Children.Add(new Border { Height = 2.5, Width = 13, CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Bottom, Background = markerIndicator });
        textHighlightButton = ActionButton(marker, "Toggle text highlight", 28, delegate
        {
            CloseInkPicker();
            ToggleTextHighlight();
        });
        var colors = ActionButton(Symbol("M 4,6 L 8,10 L 12,6", 12), "Choose text highlight color", 19, null);
        colors.Click += delegate { OpenInkPicker(colors, true, true); };
        var group = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        group.Children.Add(textHighlightButton);
        group.Children.Add(colors);
        return group;
    }

    static bool HasTextHighlight(TextSelection selection)
    {
        var background = selection.GetPropertyValue(TextElement.BackgroundProperty) as SolidColorBrush;
        return background != null && background.Color.A != 0 && background.Opacity != 0;
    }

    void ToggleTextHighlight()
    {
        if (markdownPreview != null) SetMarkdownPreview(false);
        if (drawing) SetDrawing(false);
        var editor = current.TextBox;
        var selection = editor.Selection;
        bool enabled = !HasTextHighlight(selection);
        editor.Focus();
        if (enabled || !selection.IsEmpty) current.Formatted = true;
        selection.ApplyPropertyValue(TextElement.BackgroundProperty, enabled ? TextHighlightBrush(markerColor) : Brushes.Transparent);
        UpdateSelection();
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
        return ActionButton(Symbol("M 2,3 H 14 V 13 H 2 Z M 2,11 L 6,7 L 9,10 L 11,8 L 14,11 M 10,5 H 10.3", 18),
            "Insert image from file", 32, InsertImageFile);
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
                InsertPageImage(current, decoder.Frames[0]);
            }
        }
        catch (Exception error) { FileError("insert an image into", error); }
    }

    void AddMediaEditing(Note note)
    {
        var editor = note.TextBox;
        editor.AddHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler(delegate(object sender, QueryCursorEventArgs args)
        {
            if (note == current && imageMove != null)
            {
                args.Cursor = Cursors.Arrow;
                args.Handled = true;
            }
            else if (note == current && TextHighlightActive)
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
                InsertPageImage(note, image);
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
            args.Handled = true;
            editor.Focus();
            if (args.ChangedButton == MouseButton.Left)
            {
                SelectImage(note, image);
                if (editor.CaptureMouse()) StartImageMove(args.GetPosition(page), args.GetPosition(this));
            }
            else if (args.ChangedButton == MouseButton.Right)
            {
                SelectImage(note, image);
                ShowImageMenu(note, image);
            }
        }), true);
        editor.PreviewMouseUp += delegate(object sender, MouseButtonEventArgs args)
        {
            if (args.ChangedButton == MouseButton.Left && EndTextHighlight(note, args.GetPosition(editor))) { args.Handled = true; return; }
            if (args.ChangedButton != MouseButton.Left || imageMove == null || note != current) return;
            MoveImagePreview(args.GetPosition(page), args.GetPosition(this));
            FinishImageMove();
            if (editor.IsMouseCaptured) editor.ReleaseMouseCapture();
            args.Handled = true;
        };
        editor.PreviewMouseMove += delegate(object sender, MouseEventArgs args)
        {
            if (highlightAnchor != null) DragTextHighlight(note, args.GetPosition(editor));
            else if (imageMove != null && note == current && editor.IsMouseCaptured)
                MoveImagePreview(args.GetPosition(page), args.GetPosition(this));
            else return;
            args.Handled = true;
        };
        editor.LostMouseCapture += delegate { if (imageNote == note) CancelImageMove(); highlightAnchor = null; };
        editor.SelectionChanged += delegate { if (imageNote == note) SelectImage(null, null); };
        editor.TextChanged += delegate
        {
            if (imageNote == note && !HasSelectedImage) SelectImage(null, null);
        };
        editor.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && TryPasteBitmap(note))
            { args.Handled = true; return; }
            if (imageNote == note && HasSelectedImage)
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
            InsertPageImage(note, bitmap);
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
            Stretch = Stretch.Uniform, Cursor = Cursors.Arrow, ToolTip = "Click to edit or drag to move" };
        AutomationProperties.SetName(image, "Embedded image");
        return image;
    }

    void ShowImageMenu(Note note, Image image)
    {
        var menu = new ContextMenu { PlacementTarget = note.TextBox, Placement = PlacementMode.MousePoint };
        AddItem(menu, "Copy image", "Ctrl+C", delegate { CopyBitmap(image); });
        AddItem(menu, "Crop", "", delegate { Dispatcher.BeginInvoke(new Action(StartImageCrop)); });
        menu.Items.Add(new Separator());
        AddItem(menu, "Delete image", "Del", DeleteActiveImage);
        InkpadStyle.Apply(menu);
        menu.IsOpen = true;
    }

    bool CopyBitmap(Image image)
    {
        try
        {
            var item = image.Tag as PageImage;
            Clipboard.SetImage(item == null ? (BitmapSource)image.Source : item.CopyBitmap());
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        { ShowNoteDialog("Clipboard busy", "Try copying the image again.", "OK", null); return false; }
    }

    internal static Image ReplaceBitmap(Note note, TextElement container, BitmapSource bitmap, double width,
        FigureHorizontalAnchor? alignment = null, WrapDirection? wrapping = null)
    {
        var old = container as Figure;
        var at = container.ElementStart;
        var editor = note.TextBox;
        editor.BeginChange();
        try
        {
            new TextRange(container.ElementStart, container.ElementEnd).Text = "";
            var image = NewInlineImage(bitmap, width);
            var replacement = PlaceBitmap(image, at,
                alignment ?? (old == null ? FigureHorizontalAnchor.ContentLeft : old.HorizontalAnchor),
                wrapping ?? (old == null ? WrapDirection.Both : old.WrapDirection));
            editor.CaretPosition = replacement.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
            return image;
        }
        finally { editor.EndChange(); }
    }
}
