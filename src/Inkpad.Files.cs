using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Media.Imaging;

sealed class Note
{
    internal readonly RichTextBox TextBox = new RichTextBox();
    internal readonly InkCanvas Ink = new InkCanvas();
    internal readonly NoteHistory History;
    internal readonly List<PageImage> Images = new List<PageImage>();
    internal bool Dirty;
    internal bool Formatted;
    internal bool Markdown;
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
    internal bool NeedsInkFile { get { return HasInk || Formatted || Images.Count != 0; } }

    internal Note()
    {
        History = new NoteHistory(TextBox, Ink);
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
            if (args.SourceDataObject.GetDataPresent(DataFormats.Bitmap)) return;
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
    internal static IEnumerable<Inline> DocumentInlines(TextRange range)
    {
        var seen = new HashSet<Inline>();
        for (var at = range.Start; at != null && at.CompareTo(range.End) < 0; at = at.GetNextContextPosition(LogicalDirection.Forward))
        {
            var current = at.Parent as Inline;
            if (current != null && seen.Add(current)) yield return current;
            if (at.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart)
            {
                var next = at.GetAdjacentElement(LogicalDirection.Forward) as Inline;
                if (next != null && seen.Add(next)) yield return next;
            }
        }
    }

    internal static IEnumerable<Image> DocumentImages(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            var paragraph = block as Paragraph;
            if (paragraph != null)
                foreach (var image in InlineImages(paragraph.Inlines)) yield return image;
            var section = block as Section;
            if (section != null)
                foreach (var image in DocumentImages(section.Blocks)) yield return image;
            var table = block as Table;
            if (table != null)
                foreach (var cell in table.RowGroups.SelectMany(group => group.Rows).SelectMany(row => row.Cells))
                    foreach (var image in DocumentImages(cell.Blocks)) yield return image;
            var list = block as List;
            if (list != null)
                foreach (var item in list.ListItems)
                    foreach (var image in DocumentImages(item.Blocks)) yield return image;
            var host = block as BlockUIContainer;
            if (host != null && host.Child is Image) yield return (Image)host.Child;
        }
    }

    static IEnumerable<Image> InlineImages(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            var host = inline as InlineUIContainer;
            if (host != null && host.Child is Image) yield return (Image)host.Child;
            var anchored = inline as AnchoredBlock;
            if (anchored != null)
                foreach (var image in DocumentImages(anchored.Blocks)) yield return image;
            var span = inline as Span;
            if (span != null)
                foreach (var image in InlineImages(span.Inlines)) yield return image;
        }
    }

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
                if (metadata.Length < 3 || (metadata[0] != "Inkpad 1" && metadata[0] != "Inkpad 2") ||
                    !double.TryParse(metadata[1], NumberStyles.Float, CultureInfo.InvariantCulture, out width) ||
                    double.IsNaN(width) || width < 0 || width > 50000 ||
                    (metadata[2] != "0" && metadata[2] != "1"))
                    throw new InvalidDataException("This Inkpad note has an unsupported format.");
                note.PaperWidth = width;
                note.Formatted = metadata[2] == "1";
                using (var text = Entry(archive, "text.rtf", 64L * 1024 * 1024))
                    note.Range.Load(text, DataFormats.Rtf);
                RestoreHighlights(note, archive);
                RestoreImageLayout(note, archive);
                RestoreEmbeddedImages(note);
                if (metadata[0] == "Inkpad 2") RestorePageImages(note, archive);
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
        note.Markdown = string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase);
    }

    internal static void Save(Note note, string path, bool export, Func<string, FileStream> createSibling = null)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(path), ".inkpad-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool ink = !export && IsInkFile(path);
        string content = ink ? null : TextForSave(note);
        FileStream staging;
        try { staging = createSibling == null ? CreateStagingFile(temporary) : createSibling(temporary); }
        catch (UnauthorizedAccessException)
        {
            if (!File.Exists(path)) throw;
            SaveExistingFile(path, file => WriteNote(note, file, ink, export, content));
            if (!export && !ink) note.OriginalText = content;
            return;
        }

        try
        {
            using (staging)
            {
                WriteNote(note, staging, ink, export, content);
                staging.Flush(true);
            }
            if (File.Exists(path))
            {
                try { File.Replace(temporary, path, null); }
                catch (UnauthorizedAccessException) { SaveExistingFile(path, file => WriteNote(note, file, ink, export, content)); }
            }
            else File.Move(temporary, path);
            if (!export && !ink) note.OriginalText = content;
        }
        finally { TryDelete(temporary); }
    }

    static FileStream CreateStagingFile(string path)
    {
        return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    static void SaveExistingFile(string path, Action<Stream> write)
    {
        // Some folders allow editing an existing file without permitting new files alongside it.
        string temporary = Path.GetTempPath();
        string stagedPath = Path.Combine(temporary, "inkpad-" + Guid.NewGuid().ToString("N") + ".tmp");
        string backupPath = Path.Combine(temporary, "inkpad-" + Guid.NewGuid().ToString("N") + ".backup");
        bool retainBackup = false;
        try
        {
            using (var staged = CreateStagingFile(stagedPath))
            {
                write(staged);
                staged.Flush(true);
                using (var target = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                using (var backup = CreateStagingFile(backupPath))
                {
                    target.CopyTo(backup);
                    backup.Flush(true);
                    try { Overwrite(staged, target); }
                    catch (IOException) { RestoreBackup(backup, target, backupPath, ref retainBackup); throw; }
                    catch (UnauthorizedAccessException) { RestoreBackup(backup, target, backupPath, ref retainBackup); throw; }
                }
            }
        }
        finally
        {
            TryDelete(stagedPath);
            if (!retainBackup) TryDelete(backupPath);
        }
    }

    static void Overwrite(Stream source, FileStream target)
    {
        source.Position = target.Position = 0;
        source.CopyTo(target);
        target.SetLength(source.Length);
        target.Flush(true);
    }

    static void RestoreBackup(Stream backup, FileStream target, string backupPath, ref bool retainBackup)
    {
        retainBackup = true;
        try { Overwrite(backup, target); }
        catch (IOException error) { throw new IOException("The original file backup is available at " + backupPath, error); }
        catch (UnauthorizedAccessException error) { throw new IOException("The original file backup is available at " + backupPath, error); }
        retainBackup = false;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static void WriteNote(Note note, Stream file, bool ink, bool export, string content)
    {
        if (!ink)
        {
            var encoding = export ? new UTF8Encoding(false, true) : note.Encoding;
            using (var writer = new StreamWriter(file, encoding, 4096, true)) writer.Write(content);
            return;
        }

        using (var archive = new ZipArchive(file, ZipArchiveMode.Create, true))
        {
            using (var info = new StreamWriter(archive.CreateEntry("note.info").Open(), new UTF8Encoding(false)))
                info.Write((note.Images.Count == 0 ? "Inkpad 1\n" : "Inkpad 2\n") +
                    note.PaperWidth.ToString("R", CultureInfo.InvariantCulture) + "\n" + (note.Formatted ? "1" : "0"));
            var document = note.TextBox.Document;
            if (DocumentImages(document.Blocks).Any(image => image.Parent is BlockUIContainer)) document = PrepareImageLayout(note, archive);
            var range = new TextRange(document.ContentStart, document.ContentEnd);
            using (var text = new MemoryStream())
            {
                range.Save(text, DataFormats.Rtf);
                text.Position = 0;
                using (var entry = archive.CreateEntry("text.rtf").Open()) text.CopyTo(entry);
            }
            SaveHighlights(range, archive);
            SavePageImages(note, archive);
            using (var strokes = new MemoryStream())
            {
                note.Ink.Strokes.Save(strokes);
                strokes.Position = 0;
                using (var entry = archive.CreateEntry("ink.isf").Open()) strokes.CopyTo(entry);
            }
        }
    }

    static void SavePageImages(Note note, ZipArchive archive)
    {
        if (note.Images.Count == 0) return;
        var layout = new StringBuilder();
        for (int i = 0; i < note.Images.Count; i++)
        {
            var item = note.Images[i];
            var b = item.Bounds;
            var crop = item.Crop;
            layout.AppendFormat(CultureInfo.InvariantCulture, "{0:R}\t{1:R}\t{2:R}\t{3:R}\t{4}\t{5}\t{6}\t{7}",
                b.X, b.Y, b.Width, b.Height, crop.X, crop.Y, crop.Width, crop.Height).AppendLine();
            string name = "images/" + i.ToString(CultureInfo.InvariantCulture);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(item.Bitmap));
            using (var buffer = new MemoryStream())
            {
                png.Save(buffer);
                buffer.Position = 0;
                using (var file = archive.CreateEntry(name + ".png", CompressionLevel.NoCompression).Open()) buffer.CopyTo(file);
            }
            if (item.Ink.Strokes.Count != 0)
                using (var file = archive.CreateEntry(name + ".isf").Open()) item.Ink.Strokes.Save(file);
        }
        using (var writer = new StreamWriter(archive.CreateEntry("page-images.tsv").Open(), new UTF8Encoding(false)))
            writer.Write(layout.ToString());
    }

    static void RestorePageImages(Note note, ZipArchive archive)
    {
        using (var layout = new StreamReader(Entry(archive, "page-images.tsv", 1024 * 1024)))
        {
            int index = 0;
            for (string line = layout.ReadLine(); line != null; line = layout.ReadLine())
            {
                var fields = line.Split('\t');
                var bounds = new double[4];
                var crop = new int[4];
                bool valid = fields.Length == 8;
                for (int i = 0; valid && i < 4; i++)
                    valid = double.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out bounds[i]) &&
                        bounds[i] >= 0 && bounds[i] <= 1000000 &&
                        int.TryParse(fields[i + 4], NumberStyles.None, CultureInfo.InvariantCulture, out crop[i]);
                if (!valid || bounds[2] == 0 || bounds[3] == 0 || crop[2] == 0 || crop[3] == 0)
                    throw new InvalidDataException("An image has an invalid position or size.");
                string name = "images/" + (index++).ToString(CultureInfo.InvariantCulture);
                BitmapSource bitmap;
                using (var data = new MemoryStream())
                {
                    using (var input = Entry(archive, name + ".png", 200L * 1024 * 1024)) input.CopyTo(data);
                    data.Position = 0;
                    bitmap = new PngBitmapDecoder(data, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                }
                if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 40000000 || crop[0] > bitmap.PixelWidth - crop[2] || crop[1] > bitmap.PixelHeight - crop[3])
                    throw new InvalidDataException("An image has invalid pixel dimensions.");
                var rect = new Rect(bounds[0], bounds[1], bounds[2], bounds[3]);
                var item = new PageImage(bitmap, rect);
                item.Update(rect, new Int32Rect(crop[0], crop[1], crop[2], crop[3]));
                if (archive.GetEntry(name + ".isf") != null)
                    using (var data = new MemoryStream())
                    {
                        using (var input = Entry(archive, name + ".isf", 64L * 1024 * 1024)) input.CopyTo(data);
                        data.Position = 0;
                        item.Ink.Strokes = new StrokeCollection(data);
                    }
                note.Images.Add(item);
            }
        }
    }

    static FlowDocument PrepareImageLayout(Note note, ZipArchive archive)
    {
        var clone = CloneDocument(note.Range);
        var images = DocumentImages(clone.Blocks).ToArray();
        using (var data = new StreamWriter(archive.CreateEntry("image-layout.tsv").Open(), new UTF8Encoding(false)))
            for (int index = 0; index < images.Length; index++)
            {
                var image = images[index];
                var block = image.Parent as BlockUIContainer;
                var figure = block == null ? null : block.Parent as Figure;
                if (figure == null) continue;
                data.WriteLine(index.ToString(CultureInfo.InvariantCulture) + "\t" + figure.HorizontalAnchor + "\t" + figure.WrapDirection);
                var at = figure.ElementStart;
                block.Child = null;
                new TextRange(at, figure.ElementEnd).Text = "";
                new InlineUIContainer(image, at.GetInsertionPosition(LogicalDirection.Forward)) { BaselineAlignment = BaselineAlignment.Top };
            }
        return clone;
    }

    internal static FlowDocument CloneDocument(TextRange range)
    {
        // The XAML package is generated in memory from the editor. Note files are read as RTF and data only.
        var clone = new FlowDocument(new Paragraph());
        using (var buffer = new MemoryStream())
        {
            range.Save(buffer, DataFormats.XamlPackage);
            buffer.Position = 0;
            new TextRange(clone.ContentStart, clone.ContentEnd).Load(buffer, DataFormats.XamlPackage);
        }
        return clone;
    }

    static void RestoreImageLayout(Note note, ZipArchive archive)
    {
        if (archive.GetEntry("image-layout.tsv") == null) return;
        var images = DocumentImages(note.TextBox.Document.Blocks).ToArray();
        using (var data = new StreamReader(Entry(archive, "image-layout.tsv", 1024 * 1024)))
            for (string line = data.ReadLine(); line != null; line = data.ReadLine())
            {
                var fields = line.Split('\t');
                int index;
                FigureHorizontalAnchor anchor;
                WrapDirection wrap;
                if (fields.Length != 3 || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out index) || index >= images.Length ||
                    !Enum.TryParse(fields[1], out anchor) || (anchor != FigureHorizontalAnchor.ContentLeft && anchor != FigureHorizontalAnchor.ContentRight && anchor != FigureHorizontalAnchor.ContentCenter) ||
                    !Enum.TryParse(fields[2], out wrap) || (wrap != WrapDirection.Both && wrap != WrapDirection.None)) continue;
                var image = images[index];
                var host = image.Parent as InlineUIContainer;
                if (host == null || double.IsNaN(image.Width) || image.Width <= 0) continue;
                var at = host.ElementStart;
                host.Child = null;
                new TextRange(host.ElementStart, host.ElementEnd).Text = "";
                Inkpad.PlaceBitmap(image, at, anchor, wrap);
            }
    }

    static void SaveHighlights(TextRange range, ZipArchive archive)
    {
        var origin = range.Start;
        var runs = DocumentInlines(range).OfType<Run>().Where(run => run.Background is System.Windows.Media.SolidColorBrush).ToArray();
        if (runs.Length == 0) return;
        using (var data = new StreamWriter(archive.CreateEntry("highlights.tsv").Open(), new UTF8Encoding(false)))
            foreach (var run in runs)
            {
                var color = ((System.Windows.Media.SolidColorBrush)run.Background).Color;
                if (color.A == 0 || run.Text.Length == 0) continue;
                int start = new TextRange(origin, run.ContentStart).Text.Length;
                data.WriteLine(start.ToString(CultureInfo.InvariantCulture) + "\t" + run.Text.Length.ToString(CultureInfo.InvariantCulture) + "\t" + color);
            }
    }

    static void RestoreHighlights(Note note, ZipArchive archive)
    {
        if (archive.GetEntry("highlights.tsv") == null) return;
        var document = note.TextBox.Document;
        int length = note.PlainText.Length;
        using (var data = new StreamReader(Entry(archive, "highlights.tsv", 8 * 1024 * 1024)))
            for (string line = data.ReadLine(); line != null; line = data.ReadLine())
            {
                var fields = line.Split('\t');
                int start, count;
                uint color;
                if (fields.Length != 3 || !int.TryParse(fields[0], out start) || !int.TryParse(fields[1], out count) || start < 0 || count <= 0 || start > length - count ||
                    fields[2].Length != 9 || fields[2][0] != '#' || !uint.TryParse(fields[2].Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color)) continue;
                var paint = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color));
                new TextRange(Inkpad.TextLocation(document, start), Inkpad.TextLocation(document, start + count)).ApplyPropertyValue(TextElement.BackgroundProperty, paint);
            }
    }

    static void RestoreEmbeddedImages(Note note)
    {
        foreach (var image in DocumentImages(note.TextBox.Document.Blocks))
        {
            image.Cursor = System.Windows.Input.Cursors.SizeAll;
            image.ToolTip = "Click to edit or drag to move";
            System.Windows.Automation.AutomationProperties.SetName(image, "Embedded image");
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
