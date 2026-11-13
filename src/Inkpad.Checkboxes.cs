using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

sealed partial class Inkpad
{
    internal const string EmptyCheckbox = "\u2610";
    internal const string CheckedCheckbox = "\u2611";
    const string ChecklistPrefix = EmptyCheckbox + " ";

    void InsertCheckbox()
    {
        if (drawing) SetDrawing(false);
        var note = current;
        var editor = note.TextBox;
        editor.Focus();
        // Inserting at the start of a selection keeps the selected text intact.
        var insertion = editor.Selection.Start;
        var paragraph = insertion.Paragraph;
        if (paragraph == null) return;
        editor.Selection.Select(insertion, insertion);
        UseCheckboxEncoding(note);
        string before = new TextRange(paragraph.ContentStart, insertion).Text;
        string after = new TextRange(insertion, paragraph.ContentEnd).Text;
        bool spaceBefore = before.Length > 0 && !char.IsWhiteSpace(before[before.Length - 1]);
        bool spaceAfter = after.Length == 0 || !char.IsWhiteSpace(after[0]);
        editor.Selection.Text = (spaceBefore ? " " : "") + EmptyCheckbox + (spaceAfter ? " " : "");
        editor.Selection.Select(editor.Selection.End, editor.Selection.End);
    }

    void StartChecklist()
    {
        if (drawing) SetDrawing(false);
        var editor = current.TextBox;
        editor.Focus();
        var paragraph = editor.Selection.Start.Paragraph;
        if (paragraph == null) return;

        var line = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
        int indentation = IndentationLength(line);
        var existing = ChecklistMarker(paragraph, line, indentation);
        if (existing != null)
        {
            MovePastPrefix(editor, existing);
            return;
        }

        UseCheckboxEncoding(current);
        var insertion = AfterIndentation(paragraph, indentation);
        editor.Selection.Select(insertion, insertion);
        editor.Selection.Text = ChecklistPrefix;
        editor.Selection.Select(editor.Selection.End, editor.Selection.End);
    }

    internal static bool ContinueChecklist(Note note)
    {
        var editor = note.TextBox;
        var selection = editor.Selection;
        var paragraph = selection.Start.Paragraph;
        if (paragraph == null || selection.End.Paragraph != paragraph) return false;
        string line = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
        int indentation = IndentationLength(line);
        var marker = ChecklistMarker(paragraph, line, indentation);
        if (marker == null) return false;

        bool isEmpty = line.Substring(indentation + 1).Trim().Length == 0;
        if (!isEmpty && selection.Start.CompareTo(marker.End) < 0) return false;

        editor.BeginChange();
        try
        {
            if (isEmpty)
            {
                new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text = "";
                editor.CaretPosition = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                return true;
            }

            if (!selection.IsEmpty) selection.Text = "";
            var next = editor.CaretPosition.InsertParagraphBreak();
            editor.Selection.Select(next, next);
            UseCheckboxEncoding(note);
            editor.Selection.Text = line.Substring(0, indentation) + ChecklistPrefix;
            editor.Selection.Select(editor.Selection.End, editor.Selection.End);
            return true;
        }
        finally { editor.EndChange(); }
    }

    static int IndentationLength(string line)
    {
        int length = 0;
        while (length < line.Length && (line[length] == ' ' || line[length] == '\t')) length++;
        return length;
    }

    static TextPointer AfterIndentation(Paragraph paragraph, int indentation)
    {
        var position = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        for (int index = 0; index < indentation; index++)
            position = position.GetNextInsertionPosition(LogicalDirection.Forward);
        return position;
    }

    static TextRange ChecklistMarker(Paragraph paragraph, string line, int index)
    {
        if (index >= line.Length || (line[index] != EmptyCheckbox[0] && line[index] != CheckedCheckbox[0]) ||
            (index + 1 < line.Length && !char.IsWhiteSpace(line[index + 1]))) return null;
        return AdjacentCheckbox(AfterIndentation(paragraph, index), LogicalDirection.Forward);
    }

    static void MovePastPrefix(RichTextBox editor, TextRange marker)
    {
        var end = marker.End.GetInsertionPosition(LogicalDirection.Forward);
        var next = end.GetNextInsertionPosition(LogicalDirection.Forward);
        if (next != null && next.Paragraph == end.Paragraph && new TextRange(end, next).Text == " ") end = next;
        editor.CaretPosition = end;
    }

    void AddCheckboxPointer(Note note)
    {
        var editor = note.TextBox;
        editor.AddHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler(delegate(object sender, QueryCursorEventArgs args)
        {
            if (CheckboxAt(editor, args.GetPosition(editor)) == null) return;
            args.Cursor = Cursors.Hand;
            args.Handled = true;
        }), true);
    }

    static TextRange AdjacentCheckbox(TextPointer position, LogicalDirection direction)
    {
        if (position == null) return null;
        position = position.GetInsertionPosition(direction);
        while (position != null && position.GetPointerContext(direction) != TextPointerContext.Text)
        {
            var next = position.GetNextContextPosition(direction);
            if (next == null || next.Paragraph != position.Paragraph) return null;
            position = next;
        }
        if (position == null) return null;
        var other = position.GetPositionAtOffset(direction == LogicalDirection.Forward ? 1 : -1, direction);
        if (other == null) return null;
        var range = direction == LogicalDirection.Forward ? new TextRange(position, other) : new TextRange(other, position);
        return range.Text == EmptyCheckbox || range.Text == CheckedCheckbox ? range : null;
    }

    internal static TextRange CheckboxAt(RichTextBox editor, Point point)
    {
        var position = editor.GetPositionFromPoint(point, false);
        foreach (var direction in new[] { LogicalDirection.Forward, LogicalDirection.Backward })
        {
            var checkbox = AdjacentCheckbox(position, direction);
            if (checkbox == null) continue;
            Rect leading = checkbox.Start.GetCharacterRect(LogicalDirection.Forward);
            Rect trailing = checkbox.End.GetCharacterRect(LogicalDirection.Backward);
            if (leading.IsEmpty || trailing.IsEmpty || Math.Abs(leading.Top - trailing.Top) > 1) continue;
            leading.Union(trailing);
            if (leading.Contains(point)) return checkbox;
        }
        return null;
    }

    bool ToggleCheckboxAtCaret()
    {
        var editor = current.TextBox;
        if (drawing || !editor.IsKeyboardFocusWithin) return false;
        var checkbox = AdjacentCheckbox(editor.CaretPosition, LogicalDirection.Forward) ??
            AdjacentCheckbox(editor.CaretPosition, LogicalDirection.Backward);
        if (checkbox == null) return false;
        ToggleCheckbox(current, checkbox);
        return true;
    }

    internal static void ToggleCheckbox(Note note, TextRange checkbox)
    {
        UseCheckboxEncoding(note);
        checkbox.Text = checkbox.Text == CheckedCheckbox ? EmptyCheckbox : CheckedCheckbox;
    }

    static void UseCheckboxEncoding(Note note)
    {
        try { note.Encoding.GetByteCount(EmptyCheckbox + CheckedCheckbox); }
        catch (EncoderFallbackException) { note.Encoding = new UTF8Encoding(false, true); }
    }
}
