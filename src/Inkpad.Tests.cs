using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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
        note.SetPlainText("Project notes\n\n- Review the first draft\n- Sketch the layout\n\nNext steps");
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
        Layout(root);
        var preview = new RenderTargetBitmap(1120, 690, 96, 96, PixelFormats.Pbgra32);
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

    static void Layout(FrameworkElement root)
    {
        var size = new Size(1120, 690);
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
