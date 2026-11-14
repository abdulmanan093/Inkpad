using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class InkpadTests
{
    const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            string directory = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(directory);
            TestPlainText(directory);
            TestNoteFiles(directory);
            TestRestrictedFolderSave(directory);
            TestMediaFiles(directory);
            TestPreferences(directory);
            TestTablesAndLinks(directory);
            TestWindowPlacement(directory);
            TestShapes();
            TestEditor(directory);
            Console.WriteLine("All Inkpad integration checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    static void TestPreferences(string directory)
    {
        string path = Path.Combine(directory, "editor-preferences.xml");
        var settings = new InkpadPreferences { Theme = "Dark", Font = "Segoe UI", FontSize = 20, Restore = true, Spelling = true };
        settings.RecentFiles.Add(@"C:\Test\Recent.txt");
        settings.OpenFiles.Add(@"C:\Test\Open.txt");
        settings.RememberColor(Color.FromRgb(30, 60, 90));
        settings.RememberColor(Color.FromRgb(11, 22, 33));
        settings.RememberColor(Color.FromRgb(1, 2, 3));
        settings.RememberColor(Color.FromRgb(4, 5, 6));
        settings.RememberColor(Color.FromRgb(7, 8, 9));
        settings.RememberColor(Color.FromRgb(30, 60, 90));
        settings.Save(path);
        var loaded = InkpadPreferences.Load(path);
        Require(loaded.Theme == "Dark" && loaded.Font == "Segoe UI" && loaded.FontSize == 20 && loaded.Restore && loaded.Spelling,
            "Restore editor preferences from local settings");
        Require(loaded.CheckboxColors.SequenceEqual(new[] { 0x1E3C5A, 0x070809, 0x040506, 0x010203 }), "Keep only the four newest unique checkbox colors across launches");
        Require(loaded.RecentFiles.SequenceEqual(settings.RecentFiles) && loaded.OpenFiles.SequenceEqual(settings.OpenFiles), "Restore recent files and session paths");
        Require(Inkpad.TransformWriting("One. Two. Three.", "List") == "- One.\r\n- Two.\r\n- Three." &&
            Inkpad.TransformWriting("I can't go.", "Formal") == "I cannot go.", "Run writing tools offline");
    }

    static void TestMediaFiles(string directory)
    {
        var bitmap = BitmapSource.Create(24, 16, 96, 96, PixelFormats.Bgra32, null,
            Enumerable.Repeat((byte)255, 24 * 16 * 4).ToArray(), 24 * 4);
        var note = new Note();
        note.SetPlainText("Highlighted text\n");
        var p = (Paragraph)note.TextBox.Document.Blocks.FirstBlock;
        var color = Color.FromRgb(255, 226, 119);
        new TextRange(p.ContentStart, p.ContentEnd).ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(color));
        note.TextBox.CaretPosition = note.TextBox.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        var image = Inkpad.InsertBitmap(note, bitmap);
        var container = (Figure)((BlockUIContainer)image.Parent).Parent;
        Inkpad.ReplaceBitmap(note, container, new CroppedBitmap(bitmap, new Int32Rect(2, 3, 14, 9)), 28, FigureHorizontalAnchor.ContentRight);
        string path = Path.Combine(directory, "image-highlight.inknote");
        NoteFiles.Save(note, path, false);
        var restored = new Note();
        NoteFiles.Load(restored, path);
        var first = restored.TextBox.Document.Blocks.OfType<Paragraph>().First(block => new TextRange(block.ContentStart, block.ContentEnd).Text.Contains("Highlighted text"));
        var firstCharacter = first.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        var highlight = new TextRange(firstCharacter, firstCharacter.GetPositionAtOffset(1)).GetPropertyValue(TextElement.BackgroundProperty) as SolidColorBrush;
        Require(highlight != null && highlight.Color == color, "Preserve custom text highlighting in the saved note");
        var restoredImage = NoteFiles.DocumentImages(restored.TextBox.Document.Blocks).Single();
        Require(restoredImage.Source is BitmapSource && Math.Abs(restoredImage.Width - 28) < 1,
            "Preserve the cropped image and its display size in the saved note");
        var restoredContainer = (Figure)((BlockUIContainer)restoredImage.Parent).Parent;
        Require(restoredContainer.HorizontalAnchor == FigureHorizontalAnchor.ContentRight && restoredContainer.WrapDirection == WrapDirection.Both,
            "Restore floating image placement and wrapping");
        new TextRange(restoredContainer.ElementStart, restoredContainer.ElementEnd).Text = "";
        Require(!NoteFiles.DocumentImages(restored.TextBox.Document.Blocks).Any(),
            "Delete a selected embedded image");

        var legacy = new Note { Formatted = true };
        legacy.SetPlainText("An existing inline image");
        var line = (Paragraph)legacy.TextBox.Document.Blocks.FirstBlock;
        var inline = new InlineUIContainer(new Image { Source = bitmap, Width = 24, Height = 16 }, line.ContentStart);
        string legacyPath = Path.Combine(directory, "legacy-inline-image.inknote");
        NoteFiles.Save(legacy, legacyPath, false);
        var loadedLegacy = new Note();
        NoteFiles.Load(loadedLegacy, legacyPath);
        var legacyImage = NoteFiles.DocumentImages(loadedLegacy.TextBox.Document.Blocks).Single();
        Require(legacyImage.Parent is InlineUIContainer, "Read the previous inline image format");
        var migrated = Inkpad.ReplaceBitmap(loadedLegacy, (InlineUIContainer)legacyImage.Parent, (BitmapSource)legacyImage.Source, 24);
        Require(((BlockUIContainer)migrated.Parent).Parent is Figure, "Convert an existing inline image to a wrapping image when edited");
    }

    static void TestRestrictedFolderSave(string directory)
    {
        Func<string, FileStream> denySibling = path => { throw new UnauthorizedAccessException("Simulate a folder that forbids sibling files."); };
        string text = Path.Combine(directory, "restricted.txt");
        var note = new Note();
        File.WriteAllText(text, "Longer original content");
        NoteFiles.Load(note, text);
        note.SetPlainText("Shorter");
        NoteFiles.Save(note, text, false, denySibling);
        Require(File.ReadAllText(text) == "Shorter", "Save over an existing writable text file when the folder denies temporary files");

        note.Encoding = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        note.SetPlainText("Cannot encode \u2605");
        try { NoteFiles.Save(note, text, false, denySibling); throw new Exception("Expected encoding error"); }
        catch (EncoderFallbackException) { }
        Require(File.ReadAllText(text) == "Shorter", "Preserve the existing file if serialization fails before restricted-folder save");

        string ink = Path.Combine(directory, "restricted.inknote");
        note.Formatted = true;
        note.Ink.Strokes.Add(ExampleStroke());
        NoteFiles.Save(note, ink, false);
        note.SetPlainText("Updated ink note");
        NoteFiles.Save(note, ink, false, denySibling);
        var reopened = new Note();
        NoteFiles.Load(reopened, ink);
        Require(reopened.PlainText == "Updated ink note" && reopened.HasInk, "Save and reopen an existing ink note when the folder denies temporary files");

        using (var held = new FileStream(text, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            note.SetPlainText("Cannot overwrite a locked file");
            try { NoteFiles.Save(note, text, false, denySibling); throw new Exception("Expected sharing violation"); }
            catch (IOException) { }
        }
        Require(File.ReadAllText(text) == "Shorter", "Keep the original when access to the existing file is denied by a lock");
        string missing = Path.Combine(directory, "restricted-" + Guid.NewGuid().ToString("N") + ".txt");
        try { NoteFiles.Save(note, missing, false, denySibling); throw new Exception("Expected denied creation"); }
        catch (UnauthorizedAccessException) { }
        Require(!File.Exists(missing), "Report access denial for a new file in a restricted folder");
    }

    static void TestPlainText(string directory)
    {
        string path = Path.Combine(directory, "text.txt");
        string[] contents = { "", "First line", "One\r\nTwo\r\n\r\n", "Mixed\r\nNewlines\nHere\r", "  Spaces\t\u2605  \n" };
        var encodings = new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode };
        foreach (var encoding in encodings)
        foreach (string content in contents)
        {
            File.WriteAllText(path, content, encoding);
            var expected = File.ReadAllBytes(path);
            var note = new Note();
            NoteFiles.Load(note, path);
            string normalized = content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            Require(note.PlainText == normalized, "Plain text load for " + encoding.EncodingName + ": " +
                BitConverter.ToString(Encoding.UTF8.GetBytes(normalized)) + " read " + BitConverter.ToString(Encoding.UTF8.GetBytes(note.PlainText)));
            NoteFiles.Save(note, path, false);
            Require(expected.SequenceEqual(File.ReadAllBytes(path)), "Preserve text file bytes for " + encoding.EncodingName);
        }
        File.WriteAllText(path, "caf\u00e9\n", Encoding.GetEncoding(1252));
        var legacy = new Note();
        NoteFiles.Load(legacy, path);
        Require(legacy.Encoding.CodePage == 1252, "Open Windows-1252 text");
        legacy.SetPlainText("caf\u00e9\nTwo\n");
        NoteFiles.Save(legacy, path, false);
        Require(File.ReadAllText(path, Encoding.GetEncoding(1252)) == "caf\u00e9\nTwo\n", "Save edited text in original encoding and line endings");
        byte[] previous = File.ReadAllBytes(path);
        legacy.Range.Text = "Cannot encode \u2605";
        try { NoteFiles.Save(legacy, path, false); throw new Exception("Expected encoding error"); }
        catch (EncoderFallbackException) { }
        Require(previous.SequenceEqual(File.ReadAllBytes(path)), "Preserve original after a failed save");
    }

    static void TestNoteFiles(string directory)
    {
        string path = Path.Combine(directory, "drawing.inknote");
        string export = Path.Combine(directory, "drawing.txt");
        var note = new Note { PaperWidth = 805, Formatted = true };
        note.SetPlainText("Hello \u2605\nSecond line\n");
        note.Range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
        note.Ink.Strokes.Add(ExampleStroke());
        NoteFiles.Save(note, path, false);
        var reopened = new Note();
        NoteFiles.Load(reopened, path);
        Require(reopened.PlainText == note.PlainText, "Restore note text");
        Require(reopened.PaperWidth == 805 && reopened.Formatted, "Restore note metadata");
        var first = reopened.TextBox.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        Require(new TextRange(first, first.GetPositionAtOffset(1)).GetPropertyValue(TextElement.FontWeightProperty).Equals(FontWeights.Bold), "Restore bold formatting");
        Require(reopened.Ink.Strokes.Count == 1, "Restore ink strokes");
        var stroke = reopened.Ink.Strokes[0];
        Require(stroke.DrawingAttributes.Color == Colors.RoyalBlue && stroke.StylusPoints.Count == 4 &&
            Math.Abs(stroke.StylusPoints[2].X - 405) < 0.1, "Restore ink appearance and position");
        NoteFiles.Save(reopened, export, true);
        Require(File.ReadAllText(export) == note.PlainText, "Export text from a drawing note");
        reopened.Ink.Strokes.Clear();
        NoteFiles.Save(reopened, path, false);
        NoteFiles.Load(new Note(), path);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        try { NoteFiles.Load(new Note(), path); throw new Exception("Expected invalid note to be rejected"); }
        catch (InvalidDataException) { }
    }

    static void TestEditor(string directory)
    {
        var application = new Application();
        var editor = (Inkpad)Activator.CreateInstance(typeof(Inkpad), true);
        var note = (Note)Field(editor, "current");
        var root = (FrameworkElement)editor.Content;
        Layout(root);
        TestHeadingMenu(directory, editor, note, root);
        TestCheckboxes(directory, editor, note, root);
        TestChecklist(directory, editor, note, root);
        TestCheckboxColors(directory, editor, note, root);
        TestTabIndicators(directory, editor, note, root);
        TestSettingsAndEditing(directory, editor, note, root);
        TestFreeImageAndHistory(directory, editor, root);
        TestInkTools(directory, editor, note, root);
        Require(note.TextBox.ActualHeight > 400 && note.Ink.ActualHeight == note.TextBox.ActualHeight, "Editor and ink layer share a full-height page");
        Require(!note.Ink.IsHitTestVisible, "Typing mode permits text input");
        note.SetPlainText(string.Join("\n", Enumerable.Repeat("A line in a long note", 240)));
        Layout(root);
        Require(((ScrollViewer)Field(editor, "pageScroll")).ScrollableHeight > 3000, "Long notes scroll on the shared page");
        note.Range.Text = "11/13/26\r\n- Check the changes in the next review\r\n- Sketch the layout below\r\n";
        Require(note.Dirty, "Text edits mark the note dirty");
        Call(editor, "SetDrawing", true);
        Layout(root);
        Require(note.Ink.IsHitTestVisible && note.Ink.EditingMode == InkCanvasEditingMode.Ink, "Drawing mode activates ink input");
        note.Ink.Strokes.Add(ExampleStroke());
        Require(note.PaperWidth > 0, "Drawing preserves the paper width");
        Call(editor, "Undo", false);
        Require(!note.HasInk, "Undo removes the stroke");
        Call(editor, "Undo", true);
        Require(note.HasInk, "Redo restores the stroke");
        var eraser = (Button)Field(editor, "eraserButton");
        eraser.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, eraser));
        Require(note.Ink.EditingMode == InkCanvasEditingMode.EraseByStroke, "Eraser activates stroke removal");
        note.Ink.Strokes.RemoveAt(0);
        Call(editor, "Undo", false);
        Require(note.HasInk, "Undo restores an erased stroke");
        var shapesButton = (Button)Field(editor, "shapesButton");
        var rectangle = (MenuItem)shapesButton.ContextMenu.Items[(int)ShapeTool.Rectangle];
        rectangle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, rectangle));
        var shapeLayer = (ShapeLayer)Field(editor, "shapeLayer");
        Layout(root);
        Require(shapeLayer.Visibility == Visibility.Visible && !note.Ink.IsHitTestVisible && shapeLayer.ActualHeight > 400, "Shape tool activates the drawing surface");
        var publishShape = (Action<Stroke>)typeof(ShapeLayer).GetField("Committed", Instance).GetValue(shapeLayer);
        publishShape(ShapeLayer.CreateStroke(ShapeTool.Rectangle, new Point(420, 190), new Point(535, 281), false, shapeLayer.Attributes));
        Require(note.Ink.Strokes.Count == 2, "Place a shape on the page");
        Call(editor, "Undo", false);
        Require(note.Ink.Strokes.Count == 1, "Undo removes a shape");
        Call(editor, "Undo", true);
        Require(note.Ink.Strokes.Count == 2, "Redo restores a shape");
        note.Path = Path.Combine(directory, "editor.inknote");
        Call(editor, "Save", note, false);
        Require(((FrameworkElement)Field(editor, "saveFeedback")).Visibility == Visibility.Visible &&
            ((TextBlock)Field(editor, "saveFeedbackText")).Text == "Saved", "Show save confirmation after a successful write");
        var reopened = new Note();
        NoteFiles.Load(reopened, note.Path);
        Require(!note.Dirty && reopened.Ink.Strokes.Count == 2 && reopened.PlainText == note.PlainText, "Save and reopen text, pencil marks and shapes through the editor");
        var next = new Note();
        Call(editor, "AddNote", next);
        Require(!next.HasInk && !next.Ink.IsHitTestVisible, "New tab has independent text and ink");
        Call(editor, "ShowNote", note);
        Require(note.HasInk && !note.Ink.IsHitTestVisible, "Returning to the tab preserves ink and typing mode");
        RenderPreview(directory, editor, note, root, rectangle);
        VerifyWindowReopen(directory);
        application.Shutdown();
    }

    static void TestSettingsAndEditing(string directory, Inkpad editor, Note note, FrameworkElement root)
    {
        var settings = Descendants(root).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Settings");
        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, settings));
        Layout(root, new Size(1440, 900));
        Require(((ScrollViewer)Field(editor, "settingsPage")).Visibility == Visibility.Visible &&
            Descendants(root).OfType<Image>().Any(mark => ReferenceEquals(mark.Source, editor.Icon)), "Show Notepad-style settings with the same icon as the Desktop executable");
        var preview = new RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32);
        preview.Render(root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(preview));
        using (var file = File.Create(Path.Combine(directory, "settings.png"))) png.Save(file);
        Call(editor, "CloseSettings", false);
        Layout(root);

        note.SetPlainText("One apple, two apples.");
        ((TextBox)Field(editor, "findBox")).Text = "apple";
        ((TextBox)Field(editor, "replaceBox")).Text = "pear";
        Call(editor, "ReplaceText", true);
        Require(note.PlainText == "One pear, two pears.", "Replace all matches without discarding surrounding text");
        var selectionParagraph = (Paragraph)note.TextBox.Document.Blocks.FirstBlock;
        var selectionStart = selectionParagraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        note.TextBox.Selection.Select(selectionStart, selectionStart.GetPositionAtOffset(3));
        Call(editor, "HighlightText", Brushes.Gold);
        Require(((SolidColorBrush)note.TextBox.Selection.GetPropertyValue(TextElement.BackgroundProperty)).Color == Colors.Gold,
            "Highlight selected text from the editor command");
        TestHighlightToolbar(editor, note, root);
        note.Markdown = true;
        note.SetPlainText("# Heading\n** unfinished\n- A list item");
        Call(editor, "SetMarkdownPreview", true);
        Require(((RichTextBox)Field(editor, "markdownPreview")).Document.Blocks.Count == 3, "Preview Markdown and keep unfinished markup editable");
        Call(editor, "SetMarkdownPreview", false);
        note.Markdown = false;

        note.SetPlainText("Image test\n");
        var bitmap = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, new byte[256], 32);
        note.TextBox.CaretPosition = note.TextBox.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        var image = Inkpad.InsertBitmap(note, bitmap);
        var container = (Figure)((BlockUIContainer)image.Parent).Parent;
        Call(editor, "SelectImage", note, image);
        Require(((Border)Field(editor, "imageBar")).Visibility == Visibility.Visible, "Expose resize and crop controls for the selected image");
        note.TextBox.CaretPosition = note.TextBox.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        var data = new DataObject(DataFormats.Bitmap, bitmap);
        var paste = new DataObjectPastingEventArgs(data, false, DataFormats.Bitmap);
        note.TextBox.RaiseEvent(paste);
        Require(paste.CommandCancelled && note.Images.Count == 1,
            "Paste a bitmap through the editor's clipboard handler");
        Call(editor, "DeleteActiveImage");

        note.SetPlainText("The first paragraph stays above the image.\n" + string.Concat(Enumerable.Repeat("Text wraps around the edge of the image. ", 26)));
        var paragraph = (Paragraph)note.TextBox.Document.Blocks.LastBlock;
        note.TextBox.CaretPosition = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        var wrapBitmap = BitmapSource.Create(200, 120, 96, 96, PixelFormats.Bgra32, null, new byte[200 * 120 * 4], 200 * 4);
        var wrapped = Inkpad.InsertBitmap(note, wrapBitmap);
        Layout(root);
        var origin = wrapped.TranslatePoint(new Point(), note.TextBox);
        var firstText = paragraph.Inlines.LastInline.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        Require(firstText.X >= origin.X + wrapped.ActualWidth - 1 && Math.Abs(firstText.Y - origin.Y) < 20,
            "Lay text out beside the top of the image with no overlap");
        var figure = (Figure)((BlockUIContainer)wrapped.Parent).Parent;
        var right = Inkpad.ReplaceBitmap(note, figure, wrapBitmap, 170, FigureHorizontalAnchor.ContentRight);
        Layout(root);
        Require(right.TranslatePoint(new Point(), note.TextBox).X > origin.X + 150, "Reflow text when the image moves to the right");
        Call(editor, "SelectImage", note, right);
        Layout(root);
        Call(editor, "PositionImageOverlay");
        Layout(root);
        var handles = (Thumb[])Field(editor, "imageHandles");
        var overlay = (Canvas)Field(editor, "imageOverlay");
        var bottomRight = handles[4].TranslatePoint(new Point(8, 8), overlay);
        var hit = VisualTreeHelper.HitTest(overlay, bottomRight);
        var target = hit == null ? null : hit.VisualHit;
        while (target != null && !(target is Thumb)) target = VisualTreeHelper.GetParent(target) as Visual;
        Require(overlay.Visibility == Visibility.Visible && handles.All(handle => handle.Visibility == Visibility.Visible && handle.ActualWidth > 0) &&
            ReferenceEquals(target, handles[4]), "Display eight interactive image handles above the editor");
        Call(editor, "StartImageHandle", bottomRight);
        Call(editor, "MoveImageHandle", 4, bottomRight + new Vector(85, 51));
        Call(editor, "FinishImageHandle", false);
        Layout(root);
        var resized = (Image)Field(editor, "activeImage");
        Require(Math.Abs(resized.Width - 255) < 1 && Math.Abs(resized.Height - 153) < 1 &&
            ((Figure)((BlockUIContainer)resized.Parent).Parent).HorizontalAnchor == FigureHorizontalAnchor.ContentRight,
            "Drag to resize the selected image proportionally and preserve wrapping");
        Call(editor, "StartImageCrop");
        Call(editor, "StartImageHandle", new Point(0, 0));
        Call(editor, "MoveImageHandle", 0, new Point(25.5, 25.5));
        Call(editor, "FinishImageHandle", false);
        Call(editor, "StopImageCrop");
        Require(ReferenceEquals(resized, Field(editor, "activeImage")), "Cancel an inline crop without changing the image");
        Call(editor, "StartImageCrop");
        Call(editor, "StartImageHandle", new Point(0, 0));
        Call(editor, "MoveImageHandle", 0, new Point(25.5, 25.5));
        Call(editor, "FinishImageHandle", false);
        Call(editor, "CommitImageCrop");
        var cropped = (BitmapSource)((Image)Field(editor, "activeImage")).Source;
        string cropPath = Path.Combine(directory, "crop-handles.inknote");
        NoteFiles.Save(note, cropPath, false);
        var reloaded = new Note();
        NoteFiles.Load(reloaded, cropPath);
        var reloadedImage = NoteFiles.DocumentImages(reloaded.TextBox.Document.Blocks).Single();
        Require(cropped.PixelWidth == 180 && cropped.PixelHeight == 100 &&
            ((BitmapSource)reloadedImage.Source).PixelHeight == 100 && reloaded.PlainText == note.PlainText,
            "Apply a crop in the editor and reopen the cropped image with its surrounding text");
        note.SetPlainText("");
        note.Formatted = false;
        note.Dirty = false;
        Call(editor, "UpdateTabs");
        Call(editor, "UpdateStatus");
        Layout(root);
    }

    static void TestFreeImageAndHistory(string directory, Inkpad editor, FrameworkElement root)
    {
        var previous = (Note)Field(editor, "current");
        var note = new Note();
        note.SetPlainText("First paragraph\n" + string.Concat(Enumerable.Repeat("Text flows around the image. ", 25)));
        Call(editor, "AddNote", note);
        Layout(root);
        var bitmap = BitmapSource.Create(150, 80, 96, 96, PixelFormats.Bgra32, null, new byte[150 * 80 * 4], 150 * 4);
        var plainText = note.PlainText;
        Call(editor, "InsertPageImage", note, bitmap);
        Layout(root);
        var page = (Grid)Field(editor, "page");
        var item = note.Images.Single();
        Require(ReferenceEquals(page.Children[1], note.Ink) && page.Children.IndexOf((Canvas)Field(editor, "imageLayer")) > 1,
            "Place images in front of text and page ink");
        var from = item.Bounds.TopLeft;
        var delta = new Vector(287, 256);
        Call(editor, "StartImageMove", from, from);
        Call(editor, "MoveImagePreview", from + delta, from + delta);
        Call(editor, "FinishImageMove");
        Layout(root);
        var position = item.View.TranslatePoint(new Point(), page);
        Require((position - (from + delta)).Length < 2 && note.PlainText == plainText, "Move an image freely without reflowing text");
        item.Ink.Strokes.Add(ExampleStroke());
        var annotation = item.Ink.Strokes.GetBounds();
        Call(editor, "StartImageHandle", item.Bounds.BottomRight);
        Call(editor, "MoveImageHandle", 4, item.Bounds.BottomRight + new Vector(75, 40));
        Call(editor, "FinishImageHandle", false);
        Require(item.Bounds.Width == 225 && item.Ink.Strokes.GetBounds() == annotation, "Resize the image and preserve its image-local ink");
        Call(editor, "Undo", false);
        Require(item.Bounds.Width == 150 && item.Ink.Strokes.GetBounds() == annotation, "Undo the resize and keep the annotation");
        Call(editor, "SelectImage", note, item.Image);
        Call(editor, "StartImageCrop");
        Call(editor, "StartImageHandle", item.Bounds.TopLeft);
        Call(editor, "MoveImageHandle", 0, item.Bounds.TopLeft + new Vector(10, 10));
        Call(editor, "FinishImageHandle", false);
        Call(editor, "CommitImageCrop");
        Require(item.Crop.X == 10 && item.Crop.Width == 140 && item.Ink.Strokes.GetBounds() == annotation,
            "Crop the picture and its annotations together");
        Call(editor, "Undo", false);
        string path = Path.Combine(directory, "free-position.inknote");
        NoteFiles.Save(note, path, false);
        var reloaded = new Note();
        NoteFiles.Load(reloaded, path);
        Call(editor, "AddNote", reloaded);
        Layout(root);
        var restored = reloaded.Images.Single();
        Require(restored.Bounds == item.Bounds && restored.Ink.Strokes.Count == 1 && !reloaded.HasInk,
            "Reopen the image at its saved position with its own drawing");
        Call(editor, "ShowNote", note);
        note.Ink.Strokes.Add(ExampleStroke());
        var range = new TextRange(note.TextBox.Document.ContentStart, note.TextBox.Document.ContentEnd);
        Call(editor, "ApplyTextHighlight", note, range.Start, range.End, Brushes.Gold);
        Call(editor, "SetDrawing", true);
        Call(editor, "Undo", false);
        Require(note.Ink.Strokes.Count == 1 && !Equals(range.GetPropertyValue(TextElement.BackgroundProperty), Brushes.Gold),
            "Undo text styling while in drawing mode");
        Call(editor, "SetDrawing", false);
        Call(editor, "Undo", false);
        Require(!note.HasInk, "Undo a drawing while in text mode");
        Call(editor, "Undo", false);
        Require(item.Ink.Strokes.Count == 0, "Undo a drawing on an image independently of the page");
        Call(editor, "Undo", false);
        Require(item.Bounds.TopLeft == from, "Undo image movement and restore its previous position");
        Call(editor, "Undo", true);
        Call(editor, "Undo", true);
        Call(editor, "Undo", true);
        Call(editor, "Undo", true);
        Layout(root);
        Require(note.HasInk && item.Ink.Strokes.GetBounds() == annotation && (item.Bounds.TopLeft - position).Length < 2,
            "Redo image movement and ink in their original order");
        Call(editor, "Undo", false);
        Call(editor, "Undo", false);
        note.Ink.Strokes.Add(ExampleStroke());
        Require(!note.History.CanReplay(true), "A new drawing discards redo for both text and ink edits");
        Call(editor, "SetDrawing", true);
        Require(item.Ink.EditingMode == InkCanvasEditingMode.Ink && item.Ink.IsHitTestVisible, "Draw on an image through its own ink surface");
        Call(editor, "SetDrawing", false);
        Call(editor, "SelectImage", note, item.Image);
        Call(editor, "DeleteActiveImage");
        Require(note.Images.Count == 0, "Delete a floating image");
        Call(editor, "Undo", false);
        Require(note.Images.Single() == item && item.Ink.Strokes.Count == 1, "Undo image deletion and recover its ink");
        Call(editor, "ShowNote", previous);
    }

    static void TestHighlightToolbar(Inkpad editor, Note note, FrameworkElement root)
    {
        note.SetPlainText("Selected text\nAnother line");
        Layout(root);
        var toolbar = Descendants(root).OfType<Button>().ToArray();
        var apply = toolbar.Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Toggle text highlight");
        var colors = toolbar.Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Choose text highlight color");
        var indicator = ((Grid)((Viewbox)apply.Content).Child).Children.OfType<Border>().Single();
        var paragraph = (Paragraph)note.TextBox.Document.Blocks.FirstBlock;
        var start = paragraph.Inlines.FirstInline.ContentStart;
        note.TextBox.Selection.Select(start, start.GetPositionAtOffset(8));
        colors.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, colors));
        var popup = (Popup)Field(editor, "inkPicker");
        var palette = (FrameworkElement)popup.Child;
        palette.Measure(new Size(400, 400));
        palette.Arrange(new Rect(palette.DesiredSize));
        var items = Descendants(palette).ToArray();
        Require(!items.OfType<Canvas>().Any() && !items.OfType<Slider>().Any(), "Keep the text highlight palette free of drawing previews and thickness controls");
        var pink = items.OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Color #F481C2");
        pink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, pink));
        var color = Color.FromRgb(244, 129, 194);
        Require(!popup.IsOpen && ((SolidColorBrush)indicator.Background).Color == color && !note.TextBox.Selection.IsEmpty &&
            !(note.TextBox.Selection.GetPropertyValue(TextElement.BackgroundProperty) is SolidColorBrush),
            "Changing the palette color updates the icon without formatting or losing the selected text");
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, apply));
        var first = paragraph.Inlines.OfType<Run>().ToArray();
        Require(first[0].Text == "Selected" && ((SolidColorBrush)first[0].Background).Color == Color.FromArgb(126, color.R, color.G, color.B) &&
            !(first[1].Background is SolidColorBrush), "Apply the chosen color directly to the selected words");
        Require(System.Windows.Automation.AutomationProperties.GetItemStatus(apply) == "On", "Show the highlighter as active on highlighted text");
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, apply));
        var cleared = paragraph.Inlines.OfType<Run>().First().Background as SolidColorBrush;
        Require(System.Windows.Automation.AutomationProperties.GetItemStatus(apply) == "Off" && (cleared == null || cleared.Color.A == 0),
            "Click the active highlighter to remove the highlight from the selection");
        note.TextBox.CaretPosition = ((Paragraph)paragraph.NextBlock).ContentEnd;
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, apply));
        Require(System.Windows.Automation.AutomationProperties.GetItemStatus(apply) == "On", "Enable highlight at the caret for typing");
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, apply));
        Require(System.Windows.Automation.AutomationProperties.GetItemStatus(apply) == "Off", "Disable highlight at the caret with a second click");
    }

    static void TestInkTools(string directory, Inkpad editor, Note note, FrameworkElement root)
    {
        note.SetPlainText("Highlight this, leave this alone.");
        Call(editor, "SetDrawing", true);
        var options = (Button)Field(editor, "markerOptions");
        options.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, options));
        var popup = (Popup)Field(editor, "inkPicker");
        var panel = (FrameworkElement)popup.Child;
        panel.Measure(new Size(500, 500));
        panel.Arrange(new Rect(panel.DesiredSize));
        panel.UpdateLayout();
        var tools = Descendants(panel).ToArray();
        var slider = tools.OfType<Slider>().Single();
        Require(slider.Template.FindName("PART_Track", slider) is Track, "Render an interactive thickness slider in the highlighter palette");
        slider.Value = 24;
        var green = tools.OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Color #70EB4F");
        green.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, green));
        var attr = note.Ink.DefaultDrawingAttributes;
        Require(attr.IsHighlighter && attr.StylusTip == StylusTip.Rectangle && attr.Height == 24 &&
            attr.Color == Color.FromRgb(112, 235, 79), "Apply the highlighter palette and slider to freehand ink");
        note.Ink.Strokes.Add(new Stroke(new StylusPointCollection { new StylusPoint(24, 25), new StylusPoint(150, 25) }, attr));
        string path = Path.Combine(directory, "highlighter.inknote");
        NoteFiles.Save(note, path, false);
        var reloaded = new Note();
        NoteFiles.Load(reloaded, path);
        Require(reloaded.Ink.Strokes[0].DrawingAttributes.IsHighlighter &&
            reloaded.Ink.Strokes[0].DrawingAttributes.Height == 24, "Keep freehand highlighter strokes after saving and reopening");
        var toggle = tools.OfType<ToggleButton>().Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Text only highlight");
        toggle.IsChecked = true;
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, toggle));
        Require(!note.Ink.IsHitTestVisible && note.Ink.EditingMode == InkCanvasEditingMode.None,
            "Text-only highlighter sends drag input to the editor instead of drawing ink");
        popup.IsOpen = false;
        var start = ((Paragraph)note.TextBox.Document.Blocks.FirstBlock).Inlines.FirstInline.ContentStart;
        note.TextBox.Selection.Select(start, start.GetPositionAtOffset(14));
        Call(editor, "PaintSelectedText", note);
        var runs = ((Paragraph)note.TextBox.Document.Blocks.FirstBlock).Inlines.OfType<Run>().ToArray();
        Require(runs[0].Text == "Highlight this" && runs[0].Background is SolidColorBrush &&
            ((SolidColorBrush)runs[0].Background).Color == Color.FromArgb(126, 112, 235, 79) &&
            !(runs[1].Background is SolidColorBrush) && note.Ink.Strokes.Count == 1,
            "Paint only the chosen words without adding ink or highlighting the rest of the line");
        Call(editor, "Undo", false);
        Require(!((Paragraph)note.TextBox.Document.Blocks.FirstBlock).Inlines.OfType<Run>().Any(run => run.Background is SolidColorBrush),
            "Undo the text highlight without touching the freehand stroke");
        Call(editor, "ActivateInkTool", false);
        Require(!note.Ink.DefaultDrawingAttributes.IsHighlighter && note.Ink.DefaultDrawingAttributes.Width == 2.8,
            "Switching to pencil restores its own thickness and solid stroke");
        note.Ink.Strokes.Clear();
        note.PaperWidth = 0;
        Call(editor, "SetDrawing", false);
        note.SetPlainText("");
        note.Dirty = false;
        Layout(root);
    }

    static void TestTabIndicators(string directory, Inkpad editor, Note note, FrameworkElement root)
    {
        var tabs = (Panel)Field(editor, "tabs");
        note.Path = Path.Combine(directory, "a-long-note-filename.txt");
        Call(editor, "UpdateTabs");
        Layout(root);
        var initialWidth = ((FrameworkElement)tabs.Children[0]).ActualWidth;
        Require(TabDot(tabs.Children[0]).Visibility == Visibility.Collapsed, "A clean tab has no unsaved dot");

        note.SetPlainText("Unsaved note");
        Layout(root);
        var frame = (FrameworkElement)tabs.Children[0];
        var dot = TabDot(frame);
        Require(dot.Visibility == Visibility.Visible && Math.Abs(dot.ActualWidth - 7) < 1 && frame.ActualWidth == initialWidth && frame.ActualHeight <= 31,
            "Show a full unsaved dot on a compact tab with a long filename without changing its width");
        Require(Descendants(frame).OfType<Button>().Any(button => System.Windows.Automation.AutomationProperties.GetItemStatus(button) == "Unsaved changes"),
            "Expose unsaved status for assistive technology");

        var next = new Note { Path = Path.Combine(directory, "Readme.txt") };
        Call(editor, "AddNote", next);
        Layout(root);
        Require(TabDot(tabs.Children[0]).Visibility == Visibility.Visible && TabDot(tabs.Children[1]).Visibility == Visibility.Collapsed,
            "Keep the unsaved dot on the inactive tab only");
        next.SetPlainText("Another unsaved note");
        Call(editor, "Save", note, false);
        Layout(root);
        Require(TabDot(tabs.Children[0]).Visibility == Visibility.Collapsed && TabDot(tabs.Children[1]).Visibility == Visibility.Visible,
            "Clear the dot only on the note that was saved");
        Call(editor, "Save", next, false);
        Call(editor, "CloseNote", next);
        note.SetPlainText("");
        note.Path = null;
        note.Dirty = false;
        Call(editor, "UpdateTabs");
        Call(editor, "UpdateStatus");
        Layout(root);
    }

    static System.Windows.Shapes.Ellipse TabDot(DependencyObject tab)
    {
        return Descendants(tab).OfType<System.Windows.Shapes.Ellipse>().Single();
    }

    static void TestCheckboxColors(string directory, Inkpad editor, Note note, FrameworkElement root)
    {
        var buttons = Descendants(root).OfType<Button>().ToArray();
        var picker = buttons.Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Checkbox color");
        var insert = buttons.Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Insert checkbox (Ctrl+Shift+Space)");
        var blue = picker.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Blue");
        var reset = picker.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Default");
        var custom = Color.FromRgb(157, 43, 112);
        var editorBox = note.TextBox;
        note.SetPlainText("One two");
        var line = (Paragraph)editorBox.Document.Blocks.FirstBlock;
        var run = (Run)line.Inlines.FirstInline;
        editorBox.CaretPosition = run.ContentStart.GetPositionAtOffset(4);
        blue.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, blue));
        insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, insert));
        var glyph = FirstCheckbox(line);
        Require(CheckboxColor(glyph) == Color.FromRgb(23, 92, 184) && note.NeedsInkFile, "Use the toolbar color on a new checkbox and save as a formatted note");
        Require(CheckboxColor(new TextRange(line.ContentStart, glyph.Start)) == Colors.Black &&
            CheckboxColor(new TextRange(glyph.End, line.ContentEnd)) == Colors.Black, "Leave the text around the checkbox black");
        editorBox.Selection.Select(line.ContentStart, line.ContentEnd);
        Require(Inkpad.RecolorCheckboxes(note, new SolidColorBrush(custom)) && CheckboxColor(FirstCheckbox(line)) == custom,
            "Apply a custom color only to the checkbox in selected text");
        Inkpad.ToggleCheckbox(note, FirstCheckbox(line));
        Require(CheckboxColor(FirstCheckbox(line)) == custom && FirstCheckbox(line).Text == Inkpad.CheckedCheckbox,
            "Preserve color when the checkbox is checked");
        editorBox.Undo();
        Require(CheckboxColor(FirstCheckbox(line)) == custom && FirstCheckbox(line).Text == Inkpad.EmptyCheckbox,
            "Undo a toggle without losing the color");
        editorBox.Redo();

        string path = Path.Combine(directory, "colored-checkbox.inknote");
        NoteFiles.Save(note, path, false);
        var loaded = new Note();
        NoteFiles.Load(loaded, path);
        var loadedGlyph = FirstCheckbox((Paragraph)loaded.TextBox.Document.Blocks.FirstBlock);
        Require(loadedGlyph.Text == Inkpad.CheckedCheckbox && CheckboxColor(loadedGlyph) == custom, "Reopen a colored checkbox with its mark and custom RGB color");
        var plain = Path.Combine(directory, "colored-checkbox.txt");
        NoteFiles.Save(note, plain, true);
        Require(File.ReadAllText(plain).Contains(Inkpad.CheckedCheckbox), "Keep the checked mark in a plain-text export");

        note.SetPlainText("Checklist item");
        line = (Paragraph)editorBox.Document.Blocks.FirstBlock;
        editorBox.CaretPosition = line.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        Call(editor, "SetCheckboxColor", new SolidColorBrush(custom));
        Call(editor, "StartChecklist");
        editorBox.CaretPosition = line.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        Require(Inkpad.ContinueChecklist(note), "Continue a colored checklist");
        var next = (Paragraph)line.NextBlock;
        Require(CheckboxColor(FirstCheckbox(next)) == custom && FirstCheckbox(next).Text == Inkpad.EmptyCheckbox, "Use the previous checklist item’s color on the next unchecked box");
        var marker = FirstCheckbox(next);
        editorBox.Selection.Select(marker.Start, marker.End);
        reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, reset));
        Require(CheckboxColor(FirstCheckbox(next)) == Colors.Black, "Restore the default checkbox color");
        note.SetPlainText("");
        note.Formatted = false;
        note.Dirty = false;
        Call(editor, "UpdateTabs");
        Call(editor, "UpdateStatus");
        Layout(root);
    }

    static Color CheckboxColor(TextRange range)
    {
        return ((SolidColorBrush)range.GetPropertyValue(TextElement.ForegroundProperty)).Color;
    }

    static void TestCheckboxes(string directory, Inkpad editor, Note note, FrameworkElement root)
    {
        var textBox = note.TextBox;
        var insert = Descendants(root).OfType<Button>().Single(button =>
            System.Windows.Automation.AutomationProperties.GetName(button) == "Insert checkbox (Ctrl+Shift+Space)");
        note.SetPlainText("Before after\nSecond line");
        var first = (Paragraph)textBox.Document.Blocks.FirstBlock;
        var run = (Run)first.Inlines.FirstInline;
        textBox.CaretPosition = run.ContentStart.GetPositionAtOffset(7);
        insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, insert));
        Require(note.PlainText == "Before " + Inkpad.EmptyCheckbox + " after\r\nSecond line", "Insert a checkbox with space on both sides");
        Require(new TextRange(first.ContentStart, textBox.CaretPosition).Text == "Before " + Inkpad.EmptyCheckbox + " ",
            "Place the caret after the new checkbox");

        Layout(root);
        var match = FirstCheckbox(first);
        Rect left = match.Start.GetCharacterRect(LogicalDirection.Forward);
        Rect right = match.End.GetCharacterRect(LogicalDirection.Backward);
        var nearLeft = new Point(left.Left * .8 + right.Left * .2, left.Top + left.Height / 2);
        var nearRight = new Point(left.Left * .2 + right.Left * .8, left.Top + left.Height / 2);
        Require(Inkpad.CheckboxAt(textBox, nearLeft) != null && Inkpad.CheckboxAt(textBox, nearRight) != null,
            "Detect clicks on both halves of an inline checkbox");
        Require(Inkpad.CheckboxAt(textBox, new Point(right.Left + 3, nearRight.Y)) == null,
            "Leave nearby text clicks alone");
        Inkpad.ToggleCheckbox(note, Inkpad.CheckboxAt(textBox, nearRight));
        Require(note.PlainText.Contains(Inkpad.CheckedCheckbox), "Toggle the inline checkbox on");
        textBox.Undo();
        Require(note.PlainText.Contains(Inkpad.EmptyCheckbox), "Undo a checkbox toggle");
        textBox.Redo();
        Require(note.PlainText.Contains(Inkpad.CheckedCheckbox), "Redo a checkbox toggle");

        string path = Path.Combine(directory, "checkboxes.txt");
        NoteFiles.Save(note, path, false);
        var reopened = new Note();
        NoteFiles.Load(reopened, path);
        Require(reopened.PlainText == note.PlainText, "Keep checkbox state in plain text after reopening");
        path = Path.Combine(directory, "checkboxes.inknote");
        NoteFiles.Save(note, path, false);
        NoteFiles.Load(reopened = new Note(), path);
        Require(reopened.PlainText == note.PlainText, "Keep checkbox state in an Inkpad note after reopening");

        Inkpad.ToggleCheckbox(note, FirstCheckbox(first));
        Require(note.PlainText.Contains(Inkpad.EmptyCheckbox), "Toggle the inline checkbox off");
        var second = (Paragraph)first.NextBlock;
        textBox.Selection.Select(second.ContentStart.GetInsertionPosition(LogicalDirection.Forward), second.ContentEnd);
        insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, insert));
        Require(note.PlainText.EndsWith(Inkpad.EmptyCheckbox + " Second line", StringComparison.Ordinal),
            "Insert at the start of selected text without removing it");
        Layout(root);
        var atStart = FirstCheckbox(second);
        Rect startLeft = atStart.Start.GetCharacterRect(LogicalDirection.Forward);
        Rect startRight = atStart.End.GetCharacterRect(LogicalDirection.Backward);
        Require(Inkpad.CheckboxAt(textBox, new Point((startLeft.Left + startRight.Left) / 2, startLeft.Top + startLeft.Height / 2)) != null,
            "Detect the checkbox at the start of a line");

        note.Encoding = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        textBox.CaretPosition = second.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, insert));
        Require(note.Encoding.CodePage == 65001 && note.PlainText.EndsWith("Second line " + Inkpad.EmptyCheckbox + " ", StringComparison.Ordinal),
            "Insert at the end and upgrade a legacy encoding that cannot store the checkbox");

        var table = NoteTables.Insert(textBox, 1, 1);
        var cell = table.RowGroups[0].Rows[0].Cells[0];
        var cellParagraph = (Paragraph)cell.Blocks.FirstBlock;
        cellParagraph.FontWeight = FontWeights.Bold;
        cellParagraph.Inlines.Add(new Run(" Review"));
        textBox.CaretPosition = cellParagraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, insert));
        Layout(root);
        var cellCheckbox = FirstCheckbox(cellParagraph);
        Rect cellLeft = cellCheckbox.Start.GetCharacterRect(LogicalDirection.Forward);
        Rect cellRight = cellCheckbox.End.GetCharacterRect(LogicalDirection.Backward);
        var cellHit = Inkpad.CheckboxAt(textBox, new Point((cellLeft.Left + cellRight.Left) / 2, cellLeft.Top + cellLeft.Height / 2));
        Require(cellHit != null, "Detect a checkbox in a formatted table cell");
        Inkpad.ToggleCheckbox(note, cellHit);
        Require(new TextRange(cellParagraph.ContentStart, cellParagraph.ContentEnd).Text == Inkpad.CheckedCheckbox + " Review" &&
            Equals(FirstCheckbox(cellParagraph).GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold),
            "Toggle the checkbox without changing the surrounding table text or formatting");
        note.SetPlainText("");
        note.Encoding = new UTF8Encoding(false, true);
        note.Dirty = false;
        Call(editor, "UpdateTabs");
        Call(editor, "UpdateStatus");
        Layout(root);
    }

    static void TestChecklist(string directory, Inkpad editor, Note note, FrameworkElement root)
    {
        const string box = Inkpad.EmptyCheckbox + " ";
        var textBox = note.TextBox;
        var toolbar = Descendants(root).OfType<Button>().Single(button =>
            System.Windows.Automation.AutomationProperties.GetName(button) == "List styles");
        var checklist = toolbar.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Checklist");
        note.SetPlainText("");
        textBox.CaretPosition = textBox.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        checklist.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, checklist));
        Require(note.PlainText == box && Inkpad.ContinueChecklist(note) && note.PlainText == "",
            "Start and exit a checklist in a blank note");
        note.SetPlainText("  Draft\nOrdinary line");
        var first = (Paragraph)textBox.Document.Blocks.FirstBlock;
        textBox.CaretPosition = first.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        checklist.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, checklist));
        Require(note.PlainText == "  " + box + "Draft\r\nOrdinary line", "Start a checklist on the current line after existing indentation");
        checklist.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, checklist));
        Require(note.PlainText == "  " + box + "Draft\r\nOrdinary line", "Avoid inserting a duplicate checklist marker");
        Inkpad.ToggleCheckbox(note, FirstCheckbox(first));
        textBox.CaretPosition = first.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        Require(Inkpad.ContinueChecklist(note) && note.PlainText == "  " + Inkpad.CheckedCheckbox + " Draft\r\n  " + box + "\r\nOrdinary line",
            "Continue a checked item with an unchecked box and matching indentation");
        Require(Inkpad.ContinueChecklist(note) && note.PlainText == "  " + Inkpad.CheckedCheckbox + " Draft\r\n\r\nOrdinary line",
            "End the checklist by pressing Enter on the empty item");
        Require(!Inkpad.ContinueChecklist(note), "Enter after leaving the checklist uses normal text editing");
        textBox.Undo();
        Require(note.PlainText.Contains("\r\n  " + box + "\r\n"), "Undo restores the empty checklist item");

        note.SetPlainText(box + "One two");
        first = (Paragraph)textBox.Document.Blocks.FirstBlock;
        var run = (Run)first.Inlines.FirstInline;
        textBox.CaretPosition = run.ContentStart.GetPositionAtOffset(box.Length + 4);
        Require(Inkpad.ContinueChecklist(note) && note.PlainText == box + "One \r\n" + box + "two",
            "Split an item at the caret while preserving the text on both sides");
        string path = Path.Combine(directory, "checklist.txt");
        NoteFiles.Save(note, path, false);
        var reopened = new Note();
        NoteFiles.Load(reopened, path);
        reopened.TextBox.CaretPosition = reopened.TextBox.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        Require(Inkpad.ContinueChecklist(reopened) && reopened.PlainText.EndsWith("two\r\n" + box, StringComparison.Ordinal),
            "Continue a checklist after reopening a plain text note");

        note.SetPlainText("A " + box + "inline box");
        textBox.CaretPosition = textBox.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        Require(!Inkpad.ContinueChecklist(note), "An inline checkbox does not turn ordinary text into a checklist");
        note.SetPlainText("");
        note.Dirty = false;
        Call(editor, "UpdateTabs");
        Call(editor, "UpdateStatus");
        Layout(root);
    }

    static TextRange FirstCheckbox(Paragraph paragraph)
    {
        for (var position = paragraph.ContentStart; position != null && position.CompareTo(paragraph.ContentEnd) < 0; position = position.GetNextContextPosition(LogicalDirection.Forward))
        {
            string text = position.GetTextInRun(LogicalDirection.Forward);
            int index = text.IndexOfAny(new[] { Inkpad.EmptyCheckbox[0], Inkpad.CheckedCheckbox[0] });
            if (index >= 0) return new TextRange(position.GetPositionAtOffset(index), position.GetPositionAtOffset(index + 1));
        }
        throw new InvalidOperationException("No checkbox was found in the test paragraph.");
    }

    static void TestHeadingMenu(string directory, Inkpad editor, Note note, FrameworkElement root)
    {
        var button = Descendants(root).OfType<Button>().Single(item => System.Windows.Automation.AutomationProperties.GetName(item) == "Heading styles");
        var menu = button.ContextMenu;
        string[] expected = { "Title", "Subtitle", "Heading", "Subheading", "Section", "Subsection", "Body" };
        Require(menu.Items.OfType<MenuItem>().Select(item => (string)item.Header).SequenceEqual(expected), "Show all seven Notepad-style heading options");
        note.SetPlainText("First\nSecond\nThird");
        var paragraphs = note.TextBox.Document.Blocks.OfType<Paragraph>().ToArray();
        note.TextBox.Selection.Select(paragraphs[0].ContentStart, paragraphs[1].ContentEnd);
        var title = (MenuItem)menu.Items[0];
        title.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, title));
        Require(paragraphs[0].FontSize == 40 && paragraphs[1].FontSize == 40 && paragraphs[2].FontSize == 17, "Apply Title only to selected paragraphs");
        var body = (MenuItem)menu.Items[6];
        body.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, body));
        Require(paragraphs.All(paragraph => paragraph.FontSize == 17), "Restore body formatting");
        note.SetPlainText("");
        note.Dirty = false;
        note.Formatted = false;
        Call(editor, "UpdateTabs");
        Call(editor, "UpdateStatus");
        Layout(root);
        body.IsChecked = true;
        menu.ApplyTemplate();
        var anchor = button.TranslatePoint(new Point(menu.HorizontalOffset, button.ActualHeight), root);
        double availableHeight = editor.MinHeight - anchor.Y - 34;
        menu.Measure(new Size(240, availableHeight));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();
        var panel = (FrameworkElement)menu.Template.FindName("panel", menu);
        Require(!Descendants(menu).OfType<ScrollViewer>().Any(), "Heading menu has no scroll container");
        Require(menu.DesiredSize.Height < availableHeight && panel.ActualWidth < 185 &&
            menu.Items.OfType<MenuItem>().All(item => item.ActualHeight == item.Height) &&
            body.TranslatePoint(new Point(0, body.ActualHeight), panel).Y <= panel.ActualHeight,
            "All seven heading choices fit at the minimum window height");

        var windowImage = new RenderTargetBitmap(1120, 690, 96, 96, PixelFormats.Pbgra32);
        windowImage.Render(root);
        var menuImage = new RenderTargetBitmap((int)Math.Ceiling(menu.DesiredSize.Width), (int)Math.Ceiling(menu.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
        menuImage.Render(menu);
        var composed = new DrawingVisual();
        using (var context = composed.RenderOpen())
        {
            context.DrawImage(windowImage, new Rect(0, 0, 1120, 690));
            context.DrawImage(menuImage, new Rect(anchor.X, anchor.Y, menuImage.Width, menuImage.Height));
        }
        var preview = new RenderTargetBitmap(1120, 690, 96, 96, PixelFormats.Pbgra32);
        preview.Render(composed);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(preview));
        using (var file = File.Create(Path.Combine(directory, "heading-preview.png"))) png.Save(file);
    }

    static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    static void VerifyWindowReopen(string directory)
    {
        string path = Path.Combine(directory, "window-reopen.txt");
        var bounds = new Rect(SystemParameters.WorkArea.Left + 20, SystemParameters.WorkArea.Top + 20, 790, 440);
        InkpadWindows.SavePlacement(bounds, false, path);
        var first = new Inkpad(path);
        Require(first.Left == bounds.Left && first.Top == bounds.Top && first.Width == bounds.Width && first.Height == bounds.Height, "Restore dimensions and position in the window constructor");
        var windowHandle = new System.Windows.Interop.WindowInteropHelper(first).EnsureHandle();
        Require(windowHandle != IntPtr.Zero, "Create the native window");
        foreach (var choice in new[] { "Save", "Don’t save", "Cancel" })
        {
            first.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                var dialog = Application.Current.Windows.OfType<Window>().Single(window => window.Title == "Save changes?");
                var control = Descendants(dialog).OfType<Button>().Single(button => (string)button.Content == choice);
                control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, control));
            }));
            var method = typeof(Inkpad).GetMethod("ShowNoteDialog", Instance);
            var result = (MessageBoxResult)method.Invoke(first, new object[] { "Save changes?", "Save your changes before closing.", "Save", "Don’t save", true });
            var expected = choice == "Save" ? MessageBoxResult.Yes : choice == "Cancel" ? MessageBoxResult.Cancel : MessageBoxResult.No;
            Require(result == expected, "Handle " + choice + " in the redesigned save dialog");
        }
        var resized = new Rect(bounds.Left + 15, bounds.Top + 12, 833, 474);
        first.Left = resized.Left;
        first.Top = resized.Top;
        first.Width = resized.Width;
        first.Height = resized.Height;
        first.Close();
        var reopened = new Inkpad(path);
        Require(Math.Abs(reopened.Left - resized.Left) < 2 && Math.Abs(reopened.Top - resized.Top) < 2 &&
            Math.Abs(reopened.Width - resized.Width) < 2 && Math.Abs(reopened.Height - resized.Height) < 2, "Close and reopen a native window at its resized placement");
    }

    static void TestTablesAndLinks(string directory)
    {
        var note = new Note { Formatted = true };
        note.SetPlainText("Before\n\nAfter");
        var editor = note.TextBox;
        var blank = ((Paragraph)editor.Document.Blocks.FirstBlock).NextBlock;
        editor.CaretPosition = blank.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        var table = NoteTables.Insert(editor, 2, 3);
        Require(table.PreviousBlock != null && table.NextBlock == blank && table.RowGroups[0].Rows.Count == 2, "Insert editable table at the caret without removing nearby text");
        var group = table.RowGroups[0];
        var cell = group.Rows[0].Cells[0];
        Require(NoteTables.Ancestor<TableCell>(editor.CaretPosition) == cell, "Caret starts inside first cell");
        ((Paragraph)cell.Blocks.FirstBlock).Inlines.Add(new Run("Status"));
        Require(NoteTables.Navigate(editor, false) && NoteTables.Ancestor<TableCell>(editor.CaretPosition) == group.Rows[0].Cells[1], "Tab advances to the next cell");
        NoteTables.Edit(editor, cell, TableAction.AddRowBelow);
        NoteTables.Edit(editor, cell, TableAction.AddColumnRight);
        Require(group.Rows.Count == 3 && group.Rows.Cast<TableRow>().All(row => row.Cells.Count == 4), "Insert table rows and columns");
        NoteTables.Edit(editor, group.Rows[1].Cells[1], TableAction.DeleteRow);
        NoteTables.Edit(editor, group.Rows[0].Cells[1], TableAction.DeleteColumn);
        Require(group.Rows.Count == 2 && group.Rows[0].Cells.Count == 3, "Delete table rows and columns");
        editor.CaretPosition = ((Paragraph)blank.NextBlock).ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        var destination = new Uri("https://example.com/notes");
        Inkpad.WriteLink(editor, null, "Project link", destination);
        Require(note.PlainText.Contains("Project link"), "Insert a link without replacing nearby text");
        string path = Path.Combine(directory, "table.inknote");
        note.Ink.Strokes.Add(ExampleStroke());
        NoteFiles.Save(note, path, false);
        var loaded = new Note();
        NoteFiles.Load(loaded, path);
        var loadedTable = loaded.TextBox.Document.Blocks.OfType<Table>().Single();
        var loadedCell = loadedTable.RowGroups[0].Rows[0].Cells[0];
        Require(loadedTable.RowGroups[0].Rows.Count == 2 && loadedTable.RowGroups[0].Rows[0].Cells.Count == 3 &&
            new TextRange(loadedCell.ContentStart, loadedCell.ContentEnd).Text.Contains("Status"), "Reopen the table with its dimensions and text");
        Require(loaded.TextBox.Document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<Hyperlink>().Single().NavigateUri == destination, "Reopen the hyperlink with its destination");
        Require(loaded.HasInk, "Drawings survive alongside tables and links");
        NoteTables.Edit(loaded.TextBox, loadedCell, TableAction.DeleteTable);
        Require(!loaded.TextBox.Document.Blocks.OfType<Table>().Any() && loaded.PlainText.Contains("Before"), "Delete table while keeping surrounding text");
    }

    static void TestWindowPlacement(string directory)
    {
        var path = Path.Combine(directory, "window-placement.txt");
        var bounds = new Rect(36, 47, 907, 546);
        InkpadWindows.SavePlacement(bounds, true, path);
        Rect actual;
        bool maximized;
        Require(InkpadWindows.ReadPlacement(path, out actual, out maximized) && actual == bounds && maximized, "Persist normal bounds and maximized state");
        InkpadWindows.SavePlacement(bounds, false, path);
        Require(InkpadWindows.ReadPlacement(path, out actual, out maximized) && actual == bounds && !maximized, "Replace saved window placement");
        File.WriteAllText(path, "12\nNaN\n907\n546\nTrue");
        Require(!InkpadWindows.ReadPlacement(path, out actual, out maximized), "Ignore corrupt window placement");
        Require(InkpadWindows.EditCommand(@"C:\Program Files\Inkpad\Inkpad.exe") == "\"C:\\Program Files\\Inkpad\\Inkpad.exe\" \"%1\"", "Quote Explorer executable and file paths");
    }

    static void RenderPreview(string directory, Inkpad editor, Note note, FrameworkElement root, MenuItem rectangle)
    {
        note.Path = Path.Combine(directory, "Project notes.inknote");
        note.SetPlainText("Project notes\n\n\u2611 Review the first draft\n\u2610 Sketch the layout\n\nNext steps");
        var lines = note.TextBox.Document.Blocks.OfType<Paragraph>().ToArray();
        FirstCheckbox(lines[2]).ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.SeaGreen);
        FirstCheckbox(lines[3]).ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.RoyalBlue);
        var after = (Paragraph)note.TextBox.Document.Blocks.LastBlock;
        note.TextBox.CaretPosition = after.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        var table = NoteTables.Insert(note.TextBox, 3, 2);
        var entries = new[] { "Task", "Status", "Draft layout", "In progress", "Review notes", "Ready" };
        for (int index = 0; index < entries.Length; index++)
        {
            var cell = table.RowGroups[0].Rows[index / 2].Cells[index % 2];
            var rowText = new Run(entries[index]);
            if (index < 2) rowText.FontWeight = FontWeights.SemiBold;
            ((Paragraph)cell.Blocks.FirstBlock).Inlines.Add(rowText);
        }
        note.Ink.Strokes.Clear();
        var underline = new StylusPointCollection(Enumerable.Range(0, 17).Select(index =>
            new StylusPoint(20 + index * 7, 29 + Math.Sin(index * .27) * 1.8 + index * .09)));
        note.Ink.Strokes.Add(new Stroke(underline, new DrawingAttributes { Color = Color.FromRgb(23, 92, 184), Width = 2.5, Height = 2.5, FitToCurve = true }));
        var blue = new DrawingAttributes { Color = Color.FromRgb(23, 92, 184), Width = 2.8, Height = 2.8 };
        var green = new DrawingAttributes { Color = Color.FromRgb(8, 121, 93), Width = 2.8, Height = 2.8 };
        note.Ink.Strokes.Add(ShapeLayer.CreateStroke(ShapeTool.Rectangle, new Point(42, 278), new Point(174, 347), false, blue));
        note.Ink.Strokes.Add(ShapeLayer.CreateStroke(ShapeTool.Arrow, new Point(190, 312), new Point(251, 312), false, blue));
        note.Ink.Strokes.Add(ShapeLayer.CreateStroke(ShapeTool.Ellipse, new Point(268, 278), new Point(394, 347), false, green));
        note.Dirty = false;
        Call(editor, "UpdateTabs");
        Call(editor, "SetDrawing", true);
        rectangle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, rectangle));
        Layout(root, new Size(1440, 900));
        var preview = new RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32);
        preview.Render(root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(preview));
        using (var file = File.Create(Path.Combine(directory, "render.png"))) png.Save(file);
    }

    static void TestShapes()
    {
        var start = new Point(160, 175);
        var end = new Point(30, 35);
        foreach (ShapeTool tool in Enum.GetValues(typeof(ShapeTool)))
        {
            Point[] points = ShapeLayer.Points(tool, start, end, false).ToArray();
            Require(points.Length >= 2 && points.Distinct().Count() > 1, "Generate " + tool + " from a reverse drag");
            if (tool == ShapeTool.Rectangle || tool == ShapeTool.Ellipse || tool == ShapeTool.Triangle)
                Require(points.First() == points.Last(), "Close " + tool + " outline");
        }
        var square = ShapeLayer.Points(ShapeTool.Rectangle, start, new Point(10, 105), true).ToArray();
        Require(square.Max(point => point.X) - square.Min(point => point.X) == square.Max(point => point.Y) - square.Min(point => point.Y), "Shift constrains a square");
        var line = ShapeLayer.Points(ShapeTool.Line, start, new Point(245, 178), true).ToArray();
        Require(line[0].Y == line[1].Y, "Shift snaps a line angle");
    }

    static Stroke ExampleStroke()
    {
        return new Stroke(new StylusPointCollection(new[] { new StylusPoint(220, 240), new StylusPoint(310, 188), new StylusPoint(405, 242), new StylusPoint(310, 294) }),
            new DrawingAttributes { Color = Colors.RoyalBlue, Height = 3, Width = 3 });
    }

    static void Layout(FrameworkElement root) { Layout(root, new Size(1120, 690)); }

    static void Layout(FrameworkElement root, Size size)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
        }
    }

    static object Field(object target, string name) { return target.GetType().GetField(name, Instance).GetValue(target); }
    static void Call(object target, string method, params object[] args) { target.GetType().GetMethod(method, Instance).Invoke(target, args); }
    static void Require(bool result, string name) { if (!result) throw new Exception("Failed: " + name); Console.WriteLine("OK " + name); }
}
