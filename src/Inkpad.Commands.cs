using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

sealed partial class Inkpad
{
    readonly System.Drawing.Printing.PageSettings printSettings = new System.Drawing.Printing.PageSettings();

    static ImageSource ReadAppIcon()
    {
        using (var stream = typeof(Inkpad).Assembly.GetManifestResourceStream("Inkpad.Icon"))
        {
            var icon = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return icon.Frames.OrderBy(frame => Math.Abs(frame.PixelWidth - 48)).First();
        }
    }

    ContextMenu FileMenu()
    {
        var menu = new ContextMenu { MinWidth = 245 };
        AddItem(menu, "New tab", "Ctrl+N", delegate { AddNote(new Note()); });
        AddItem(menu, "New window", "Ctrl+Shift+N", delegate { NewWindow(null); });
        AddItem(menu, "New Markdown tab", "", delegate { AddNote(new Note { Markdown = true }); });
        AddItem(menu, "Open…", "Ctrl+O", Open);
        var recent = new MenuItem { Header = "Recent" };
        menu.Items.Add(recent);
        AddItem(menu, "Save", "Ctrl+S", delegate { Save(current, false); });
        AddItem(menu, "Save as…", "Ctrl+Shift+S", delegate { Save(current, true); });
        var saveAll = AddItem(menu, "Save all", "Ctrl+Alt+S", SaveAll);
        AddItem(menu, "Export text only…", "", ExportText);
        menu.Items.Add(new Separator());
        AddItem(menu, "Page setup…", "", PageSetup);
        AddItem(menu, "Print…", "Ctrl+P", Print);
        menu.Items.Add(new Separator());
        AddItem(menu, "Close tab", "Ctrl+W", delegate { CloseNote(current); });
        AddItem(menu, "Close window", "Ctrl+Shift+W", Close);
        AddItem(menu, "Exit", "", ExitWindows);
        menu.Opened += delegate
        {
            recent.Items.Clear();
            foreach (string path in Preferences.RecentFiles.ToArray())
            {
                string file = path;
                AddItem(recent, Path.GetFileName(file), "", delegate { OpenFiles(new[] { file }); }).ToolTip = file;
            }
            if (recent.HasItems) recent.Items.Add(new Separator());
            AddItem(recent, "Clear list", "", delegate { Preferences.RecentFiles.Clear(); Preferences.Save(); }).IsEnabled = recent.HasItems;
            recent.IsEnabled = Preferences.Recent && Preferences.RecentFiles.Count > 0;
            saveAll.IsEnabled = notes.Any(note => note.Dirty);
            InkpadStyle.StyleItems(recent);
        };
        return menu;
    }

    ContextMenu EditMenu()
    {
        var menu = new ContextMenu { MinWidth = 245 };
        var undo = AddItem(menu, "Undo", "Ctrl+Z", delegate { Undo(false); });
        var redo = AddItem(menu, "Redo", "Ctrl+Y", delegate { Undo(true); });
        menu.Items.Add(new Separator());
        var cut = AddItem(menu, "Cut", "Ctrl+X", delegate { current.TextBox.Cut(); });
        var copy = AddItem(menu, "Copy", "Ctrl+C", delegate { current.TextBox.Copy(); });
        AddItem(menu, "Paste", "Ctrl+V", delegate { current.TextBox.Paste(); });
        var delete = AddItem(menu, "Delete", "Del", delegate { current.TextBox.Selection.Text = ""; });
        menu.Items.Add(new Separator());
        AddItem(menu, "Clear formatting", "", ClearFormatting);
        AddItem(menu, "Spelling suggestions…", "Ctrl+E", ShowSpelling);
        menu.Items.Add(new Separator());
        AddItem(menu, "Find…", "Ctrl+F", ShowFind);
        AddItem(menu, "Find next", "F3", delegate { FindInDirection(false); });
        AddItem(menu, "Find previous", "Shift+F3", delegate { FindInDirection(true); });
        AddItem(menu, "Replace…", "Ctrl+H", ShowReplace);
        AddItem(menu, "Go to…", "Ctrl+G", GoToLine);
        menu.Items.Add(new Separator());
        AddItem(menu, "Select all", "Ctrl+A", delegate { SetDrawing(false); current.TextBox.SelectAll(); });
        AddItem(menu, "Time/date", "F5", InsertDate);
        menu.Items.Add(new Separator());
        AddItem(menu, "Font…", "", delegate { OpenSettings(true); });
        menu.Opened += delegate
        {
            undo.IsEnabled = drawing ? current.UndoInk.Count != 0 : current.TextBox.CanUndo;
            redo.IsEnabled = drawing ? current.RedoInk.Count != 0 : current.TextBox.CanRedo;
            cut.IsEnabled = copy.IsEnabled = delete.IsEnabled = !current.TextBox.Selection.IsEmpty;
        };
        return menu;
    }

    ContextMenu ViewMenu()
    {
        var menu = new ContextMenu { MinWidth = 210 };
        var zoomMenu = new MenuItem { Header = "Zoom" };
        menu.Items.Add(zoomMenu);
        AddItem(zoomMenu, "Zoom in", "Ctrl++", delegate { SetZoom(zoom + 10); });
        AddItem(zoomMenu, "Zoom out", "Ctrl+−", delegate { SetZoom(zoom - 10); });
        AddItem(zoomMenu, "Restore default zoom", "Ctrl+0", delegate { SetZoom(100); });
        var status = AddItem(menu, "Status bar", "", delegate { Preferences.Status = !Preferences.Status; PreferencesChanged(); });
        var wrapping = AddItem(menu, "Word wrap", "", delegate { SetWrap(!Preferences.Wrap); });
        status.IsCheckable = wrapping.IsCheckable = true;
        var markdown = new MenuItem { Header = "Markdown" };
        menu.Items.Add(markdown);
        AddItem(markdown, "Edit source", "", delegate { SetMarkdownPreview(false); });
        AddItem(markdown, "Preview", "", delegate { SetMarkdownPreview(true); });
        menu.Opened += delegate
        {
            wrapping.IsEnabled = current.PaperWidth == 0;
            wrapping.IsChecked = Preferences.Wrap;
            status.IsChecked = Preferences.Status;
            markdown.IsEnabled = current.Markdown;
        };
        return menu;
    }

    void OpenStartup(string[] paths)
    {
        if (paths.Length != 0) { foreach (var path in paths) OpenPath(path); return; }
        if (!Preferences.Restore) return;
        var active = Preferences.ActiveFile;
        foreach (var path in Preferences.OpenFiles.ToArray()) if (File.Exists(path)) OpenPath(path);
        var selected = notes.Find(note => string.Equals(note.Path, active, StringComparison.OrdinalIgnoreCase));
        if (selected != null) ShowNote(selected);
    }

    void OpenFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Preferences.NewWindow) NewWindow(path);
            else OpenPath(path);
        }
    }

    void NewWindow(string path)
    {
        var window = new Inkpad { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Show();
        if (path != null) window.OpenPath(path);
    }

    void SaveAll()
    {
        var selected = current;
        foreach (var note in notes.Where(note => note.Dirty).ToArray())
        {
            ShowNote(note);
            if (!Save(note, false)) return;
        }
        ShowNote(selected);
    }

    void ExitWindows()
    {
        foreach (var window in Application.Current.Windows.OfType<Inkpad>().Where(window => window != this).ToArray())
        {
            window.Close();
            if (window.IsVisible) return;
        }
        Close();
    }

    static void RememberFile(string path)
    {
        if (!Preferences.Recent) return;
        Preferences.RecentFiles.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        Preferences.RecentFiles.Insert(0, path);
        if (Preferences.RecentFiles.Count > 10) Preferences.RecentFiles.RemoveAt(10);
        Preferences.Save();
    }

    void PageSetup()
    {
        using (var dialog = new System.Windows.Forms.PageSetupDialog { PageSettings = printSettings, EnableMetric = true })
        {
            try { dialog.ShowDialog(); }
            catch (System.Drawing.Printing.InvalidPrinterException) { ShowNoteDialog("Page setup", "Windows couldn’t find a printer. Add a printer or enable Microsoft Print to PDF.", "OK", null); }
        }
    }

    void Print()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        try
        {
            var copy = NoteFiles.CloneDocument(current.Range);
            copy.FontFamily = current.TextBox.Document.FontFamily;
            copy.FontSize = current.TextBox.Document.FontSize;
            double width = dialog.PrintableAreaWidth, height = dialog.PrintableAreaHeight;
            if (printSettings.Landscape && height > width) { double swap = width; width = height; height = swap; }
            var margins = printSettings.Margins;
            copy.PagePadding = new Thickness(margins.Left * .96, margins.Top * .96, margins.Right * .96, margins.Bottom * .96);
            copy.ColumnWidth = double.PositiveInfinity;
            var paginator = ((IDocumentPaginatorSource)copy).DocumentPaginator;
            paginator.PageSize = new Size(width, height);
            dialog.PrintDocument(current.HasInk ? new InkNotePaginator(paginator, current, copy.PagePadding) : paginator, current.Name);
        }
        catch (Exception error) { FileError("print", error); }
    }

    void InsertDate()
    {
        var now = DateTime.Now;
        current.TextBox.Selection.Text = now.ToString("t") + " " + now.ToString("d");
        current.TextBox.Focus();
    }

    string AskText(string title, string prompt, string initial, bool multiline = false)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 410, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = SettingsBackground, Foreground = Text };
        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(new TextBlock { Text = prompt, Foreground = Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var entry = new TextBox { Text = initial, MinHeight = multiline ? 145 : 29, MaxHeight = multiline ? 300 : 40,
            AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, Padding = new Thickness(5, 3, 5, 3) };
        content.Children.Add(entry);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = SettingsButton("OK", delegate { dialog.DialogResult = true; });
        ok.IsDefault = true;
        var cancel = SettingsButton("Cancel", null);
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(7, 0, 0, 0);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        content.Children.Add(buttons);
        dialog.Content = content;
        dialog.Loaded += delegate { entry.Focus(); entry.SelectAll(); };
        return dialog.ShowDialog() == true ? entry.Text : null;
    }

    void GoToLine()
    {
        var value = AskText("Go to line", "Line number", "1");
        if (value == null) return;
        int line;
        var parts = current.PlainText.Split('\n');
        if (!int.TryParse(value, out line) || line < 1 || line > parts.Length)
        { ShowNoteDialog("Go to line", "Enter a line number from 1 to " + parts.Length + ".", "OK", null); return; }
        int offset = parts.Take(line - 1).Sum(part => part.Length + 1);
        SelectText(offset, 0);
    }

    void FindInDirection(bool backwards)
    {
        if (markdownPreview != null) SetMarkdownPreview(false);
        if (findBox.Text.Length == 0) { ShowFind(); return; }
        var editor = current.TextBox;
        string all = current.PlainText, needle = findBox.Text;
        int match = -1;
        if (backwards)
        {
            int boundary = Math.Min(all.Length, new TextRange(editor.Document.ContentStart, editor.Selection.Start).Text.Length);
            match = all.Substring(0, boundary).LastIndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (match < 0) match = all.LastIndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            int from = Math.Min(all.Length, new TextRange(editor.Document.ContentStart, editor.Selection.End).Text.Length);
            match = all.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (match < 0 && from != 0) match = all.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }
        findFeedback.Text = match < 0 ? "No matches" : "";
        if (match >= 0) SelectText(match, needle.Length);
    }

    void SelectText(int start, int length)
    {
        var editor = current.TextBox;
        editor.Focus();
        var first = TextLocation(editor.Document, start);
        editor.Selection.Select(first, TextLocation(editor.Document, start + length));
        var rectangle = first.GetCharacterRect(LogicalDirection.Forward);
        if (!rectangle.IsEmpty) editor.BringIntoView(rectangle);
    }

    void ShowReplace()
    {
        ShowFind();
        replaceRow.Visibility = Visibility.Visible;
    }

    void ReplaceText(bool all)
    {
        string query = findBox.Text;
        if (query.Length == 0) return;
        var editor = current.TextBox;
        if (!all)
        {
            if (string.Equals(editor.Selection.Text, query, StringComparison.OrdinalIgnoreCase)) editor.Selection.Text = replaceBox.Text;
            FindInDirection(false);
            return;
        }
        var ranges = Regex.Matches(current.PlainText, Regex.Escape(query), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Cast<Match>().Select(match => new TextRange(TextLocation(editor.Document, match.Index), TextLocation(editor.Document, match.Index + match.Length))).ToArray();
        editor.BeginChange();
        try { for (int index = ranges.Length - 1; index >= 0; index--) ranges[index].Text = replaceBox.Text; }
        finally { editor.EndChange(); }
        findFeedback.Text = ranges.Length == 0 ? "No matches" : ranges.Length + " replaced";
    }

    void ShowSpelling()
    {
        var editor = current.TextBox;
        if (!Preferences.Spelling)
        { ShowNoteDialog("Spelling suggestions", "Turn on Spell check in Settings to use the local dictionary.", "OK", null); return; }
        var error = editor.GetSpellingError(editor.CaretPosition) ?? editor.GetSpellingError(editor.Selection.Start);
        var suggestions = error == null ? new string[0] : error.Suggestions.Take(8).ToArray();
        if (suggestions.Length == 0)
        { ShowNoteDialog("Spelling suggestions", "Place the cursor in an underlined word to see local suggestions.", "OK", null); return; }
        var menu = new ContextMenu { PlacementTarget = editor, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        foreach (string suggestion in suggestions) AddItem(menu, suggestion, "", delegate { error.Correct(suggestion); });
        AddItem(menu, "Ignore all", "", error.IgnoreAll);
        InkpadStyle.Apply(menu);
        menu.IsOpen = true;
    }

    static readonly Dictionary<string, string> CommonTypos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { { "teh", "the" }, { "adn", "and" }, { "recieve", "receive" }, { "seperate", "separate" }, { "definately", "definitely" }, { "occured", "occurred" }, { "wiht", "with" }, { "thier", "their" } };

    void CorrectPreviousWord(Note note)
    {
        if (!Preferences.Spelling || !Preferences.Autocorrect || !note.TextBox.Selection.IsEmpty) return;
        var caret = note.TextBox.CaretPosition;
        string prefix = caret.GetTextInRun(LogicalDirection.Backward);
        var match = Regex.Match(prefix, @"[A-Za-z]+$");
        string correction;
        if (!match.Success || !CommonTypos.TryGetValue(match.Value, out correction)) return;
        if (match.Value.All(char.IsUpper)) correction = correction.ToUpperInvariant();
        else if (char.IsUpper(match.Value[0])) correction = char.ToUpperInvariant(correction[0]) + correction.Substring(1);
        var range = new TextRange(caret.GetPositionAtOffset(-match.Length), caret);
        range.Text = correction;
        note.TextBox.CaretPosition = range.End;
    }

    void SetMarkdownPreview(bool show)
    {
        if (!current.Markdown) return;
        if (drawing) SetDrawing(false);
        if (!show)
        {
            if (markdownPreview != null) page.Children.Remove(markdownPreview);
            markdownPreview = null;
            current.TextBox.Visibility = Visibility.Visible;
            current.TextBox.Focus();
            return;
        }
        if (markdownPreview != null) return;
        var document = new FlowDocument { PagePadding = new Thickness(20), FontFamily = new FontFamily("Segoe UI"), FontSize = 17, Foreground = Text };
        foreach (string line in current.PlainText.Replace("\r\n", "\n").Split('\n'))
        {
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            string value = heading.Success ? heading.Groups[2].Value : Regex.Replace(line, @"^\s*[-*+]\s+", "•  ");
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 5) };
            if (heading.Success) { paragraph.FontSize = 34 - 2 * heading.Groups[1].Length; paragraph.FontWeight = FontWeights.SemiBold; }
            foreach (var piece in Regex.Split(value, "(\\*\\*[^*]+\\*\\*|`[^`]+`|\\*[^*]+\\*)"))
            {
                if (piece.Length > 4 && piece.StartsWith("**", StringComparison.Ordinal) && piece.EndsWith("**", StringComparison.Ordinal)) paragraph.Inlines.Add(new Bold(new Run(piece.Substring(2, piece.Length - 4))));
                else if (piece.Length > 2 && piece[0] == '*' && piece[piece.Length - 1] == '*') paragraph.Inlines.Add(new Italic(new Run(piece.Substring(1, piece.Length - 2))));
                else if (piece.Length > 2 && piece[0] == '`' && piece[piece.Length - 1] == '`') paragraph.Inlines.Add(new Run(piece.Substring(1, piece.Length - 2)) { FontFamily = new FontFamily("Consolas") });
                else paragraph.Inlines.Add(new Run(piece));
            }
            document.Blocks.Add(paragraph);
        }
        markdownPreview = new RichTextBox { Document = document, IsReadOnly = true, Background = Paper, Foreground = Text, BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        current.TextBox.Visibility = Visibility.Collapsed;
        page.Children.Add(markdownPreview);
        markdownPreview.Focus();
    }

    RichTextBox markdownPreview;
    StackPanel replaceRow;
    readonly TextBox replaceBox = new TextBox();

    sealed class InkNotePaginator : DocumentPaginator
    {
        readonly DocumentPaginator inner;
        readonly Note note;
        readonly Thickness padding;
        internal InkNotePaginator(DocumentPaginator inner, Note note, Thickness padding) { this.inner = inner; this.note = note; this.padding = padding; }
        public override bool IsPageCountValid { get { return inner.IsPageCountValid; } }
        public override int PageCount { get { return Math.Max(inner.PageCount, (int)Math.Ceiling(note.Ink.Strokes.GetBounds().Bottom / Math.Max(1, PageSize.Height - padding.Top - padding.Bottom))); } }
        public override Size PageSize { get { return inner.PageSize; } set { inner.PageSize = value; } }
        public override IDocumentPaginatorSource Source { get { return inner.Source; } }
        public override void ComputePageCount() { inner.ComputePageCount(); }
        public override DocumentPage GetPage(int pageNumber)
        {
            if (pageNumber >= PageCount && IsPageCountValid) return DocumentPage.Missing;
            var page = inner.GetPage(pageNumber);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (page != DocumentPage.Missing)
                {
                    var brush = new VisualBrush(page.Visual) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
                    dc.DrawRectangle(brush, null, new Rect(PageSize));
                }
                double height = Math.Max(1, PageSize.Height - padding.Top - padding.Bottom);
                dc.PushClip(new RectangleGeometry(new Rect(padding.Left, padding.Top, Math.Max(1, PageSize.Width - padding.Left - padding.Right), height)));
                dc.PushTransform(new TranslateTransform(padding.Left, padding.Top - pageNumber * height));
                note.Ink.Strokes.Draw(dc);
                dc.Pop(); dc.Pop();
            }
            return new DocumentPage(visual, PageSize, new Rect(PageSize), new Rect(PageSize));
        }
    }
}
