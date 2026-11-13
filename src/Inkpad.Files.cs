using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;

sealed class Note
{
    internal readonly RichTextBox TextBox = new RichTextBox();
    internal readonly InkCanvas Ink = new InkCanvas();
    internal readonly System.Collections.Generic.Stack<Stroke[]> UndoInk = new System.Collections.Generic.Stack<Stroke[]>();
    internal readonly System.Collections.Generic.Stack<Stroke[]> RedoInk = new System.Collections.Generic.Stack<Stroke[]>();
    internal Stroke[] PreviousInk = new Stroke[0];
    internal bool ReplayingInk;
    internal bool Dirty;
    internal bool Formatted;
    internal int? CharacterCount;
    internal string Path;
    internal Encoding Encoding = new UTF8Encoding(false, true);
    internal string Newline = "\r\n";
    internal string OriginalText;
    internal double PaperWidth;
    internal double ScrollX;
    internal double ScrollY;
    internal string Name { get { return Path == null ? "Untitled" : System.IO.Path.GetFileName(Path); } }
    internal bool HasInk { get { return Ink.Strokes.Count != 0; } }
    internal bool NeedsInkFile { get { return HasInk || Formatted; } }

    internal Note()
    {
        var paragraphStyle = new Style(typeof(Paragraph));
        paragraphStyle.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
        TextBox.Resources.Add(typeof(Paragraph), paragraphStyle);
        TextBox.Document = new FlowDocument(new Paragraph());
        TextBox.Document.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        TextBox.Document.FontSize = 17;
        TextBox.Document.IsHyphenationEnabled = false;
        TextBox.BorderThickness = new Thickness(0);
        TextBox.Padding = new Thickness(14, 8, 14, 14);
        TextBox.Background = System.Windows.Media.Brushes.Transparent;
        TextBox.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        TextBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        TextBox.AcceptsTab = true;
        TextBox.IsDocumentEnabled = false;
        TextBox.SpellCheck.IsEnabled = false;
        Ink.Background = System.Windows.Media.Brushes.Transparent;
        Ink.Focusable = true;
        Ink.EditingMode = InkCanvasEditingMode.None;
        Ink.EditingModeInverted = InkCanvasEditingMode.None;
        Ink.UseCustomCursor = true;
        Ink.ClipToBounds = true;
        System.Windows.Automation.AutomationProperties.SetName(TextBox, "Note text");
        System.Windows.Automation.AutomationProperties.SetName(Ink, "Drawing on the note");
        DataObject.AddPastingHandler(TextBox, delegate(object sender, DataObjectPastingEventArgs args)
        {
            if (args.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
                args.FormatToApply = DataFormats.UnicodeText;
            else if (args.SourceDataObject.GetDataPresent(DataFormats.Text))
                args.FormatToApply = DataFormats.Text;
            else
                args.CancelCommand();
        });
    }

    internal TextRange Range { get { return new TextRange(TextBox.Document.ContentStart, TextBox.Document.ContentEnd); } }
    internal string PlainText
    {
        get
        {
            string text = Range.Text;
            return text.EndsWith("\r\n", StringComparison.Ordinal) ? text.Substring(0, text.Length - 2) : text;
        }
    }

    internal void SetPlainText(string content)
    {
        var document = TextBox.Document;
        document.Blocks.Clear();
        foreach (string line in content.Split('\n'))
            document.Blocks.Add(new Paragraph(new Run(line)));
    }
}

static class NoteFiles
{
    internal const string InkExtension = ".inknote";

    internal static bool IsInkFile(string path)
    {
        return string.Equals(Path.GetExtension(path), InkExtension, StringComparison.OrdinalIgnoreCase);
    }

    internal static void Load(Note note, string path)
    {
        note.TextBox.IsUndoEnabled = false;
        if (IsInkFile(path))
        {
            using (var file = File.OpenRead(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                string[] metadata;
                using (var reader = new StreamReader(Entry(archive, "note.info", 4096)))
                    metadata = reader.ReadToEnd().Split('\n');
                double width;
                if (metadata.Length < 3 || metadata[0] != "Inkpad 1" ||
                    !double.TryParse(metadata[1], NumberStyles.Float, CultureInfo.InvariantCulture, out width) ||
                    double.IsNaN(width) || width < 0 || width > 50000 ||
                    (metadata[2] != "0" && metadata[2] != "1"))
                    throw new InvalidDataException("This Inkpad note has an unsupported format.");
                note.PaperWidth = width;
                note.Formatted = metadata[2] == "1";
                using (var text = Entry(archive, "text.rtf", 64L * 1024 * 1024))
                    note.Range.Load(text, DataFormats.Rtf);
                NoteTables.StyleLoaded(note.TextBox.Document);
                using (var ink = Entry(archive, "ink.isf", 64L * 1024 * 1024))
                {
                    // WPF ink readers may require a seekable input stream.
                    using (var data = new MemoryStream())
                    {
                        ink.CopyTo(data);
                        data.Position = 0;
                        note.Ink.Strokes = new StrokeCollection(data);
                    }
                }
            }
        }
        else
        {
            byte[] bytes = File.ReadAllBytes(path);
            string content;
            using (var data = new MemoryStream(bytes, false))
            using (var reader = new StreamReader(data, new UTF8Encoding(false, true), true))
            {
                try
                {
                    content = reader.ReadToEnd();
                    note.Encoding = reader.CurrentEncoding;
                }
                catch (DecoderFallbackException)
                {
                    note.Encoding = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    content = note.Encoding.GetString(bytes);
                }
            }
            if (content.IndexOf('\0') >= 0)
                throw new InvalidDataException("Choose a text file or an Inkpad note.");
            int newline = content.IndexOfAny(new[] { '\r', '\n' });
            if (newline >= 0)
                note.Newline = content[newline] == '\n' ? "\n" :
                    (newline + 1 < content.Length && content[newline + 1] == '\n' ? "\r\n" : "\r");
            note.OriginalText = content;
            note.SetPlainText(Normalize(content, "\n"));
        }
        note.TextBox.IsUndoEnabled = true;
        note.TextBox.CaretPosition = note.TextBox.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        note.Path = path;
    }

    internal static void Save(Note note, string path, bool export)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(path), ".inkpad-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!export && IsInkFile(path))
                {
                    using (var archive = new ZipArchive(file, ZipArchiveMode.Create, true))
                    {
                        using (var info = new StreamWriter(archive.CreateEntry("note.info").Open(), new UTF8Encoding(false)))
                        {
                            info.Write("Inkpad 1\n" + note.PaperWidth.ToString("R", CultureInfo.InvariantCulture) + "\n" +
                                (note.Formatted ? "1" : "0"));
                        }
                        using (var text = new MemoryStream())
                        {
                            note.Range.Save(text, DataFormats.Rtf);
                            text.Position = 0;
                            using (var entry = archive.CreateEntry("text.rtf").Open()) text.CopyTo(entry);
                        }
                        using (var ink = new MemoryStream())
                        {
                            note.Ink.Strokes.Save(ink);
                            ink.Position = 0;
                            using (var entry = archive.CreateEntry("ink.isf").Open()) ink.CopyTo(entry);
                        }
                    }
                }
                else
                {
                    string content = TextForSave(note);
                    var encoding = export ? new UTF8Encoding(false, true) : note.Encoding;
                    using (var writer = new StreamWriter(file, encoding, 4096, true)) writer.Write(content);
                }
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            if (!export && !IsInkFile(path)) note.OriginalText = TextForSave(note);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    static Stream Entry(ZipArchive archive, string name, long limit)
    {
        var entry = archive.GetEntry(name);
        if (entry == null || entry.Length > limit) throw new InvalidDataException("The Inkpad note is incomplete or too large.");
        return entry.Open();
    }

    static string TextForSave(Note note)
    {
        string text = Normalize(note.PlainText, "\n");
        if (note.OriginalText != null && Normalize(note.OriginalText, "\n") == text) return note.OriginalText;
        return text.Replace("\n", note.Newline);
    }

    static string Normalize(string text, string newline)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", newline);
    }
}
