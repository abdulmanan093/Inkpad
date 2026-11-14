using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

sealed partial class Inkpad
{
    internal const string EmptyCheckbox = "\u2610";
    internal const string CheckedCheckbox = "\u2611";
    const string CheckboxIcon = "M 4,2.5 H 12 Q 13.5,2.5 13.5,4 V 12 Q 13.5,13.5 12,13.5 H 4 Q 2.5,13.5 2.5,12 V 4 Q 2.5,2.5 4,2.5 Z M 5,8 L 7.2,10.2 L 11,6.2";
    SolidColorBrush checkboxBrush;
    Button checkboxInsertButton;
    Border checkboxSwatch;

    StackPanel BuildCheckboxToolbar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        checkboxInsertButton = ActionButton(Symbol(CheckboxIcon, 19), "Insert checkbox (Ctrl+Shift+Space)", 31, InsertCheckbox);
        checkboxSwatch = new Border { Width = 11, Height = 11, Background = Text, CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1), BorderBrush = Brush("#25000000"), VerticalAlignment = VerticalAlignment.Center };
        var picker = ActionButton(WithChevron(checkboxSwatch), "Checkbox color", 34, null);
        var menu = new ContextMenu();
        var colors = new[] { "Default", "Blue", "Green", "Red", "Orange", "Purple" };
        var values = new[] { null, "#175CB8", "#08795D", "#BF293D", "#B35413", "#783FA1" };
        for (int index = 0; index < colors.Length; index++)
        {
            var choice = values[index] == null ? null : (SolidColorBrush)Brush(values[index]);
            var item = AddItem(menu, colors[index], "", delegate { SetCheckboxColor(choice); });
            item.Icon = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Background = choice ?? Text };
            item.IsCheckable = true;
            menu.Opened += delegate { item.IsChecked = choice == null ? checkboxBrush == null : checkboxBrush != null && checkboxBrush.Color == choice.Color; };
        }
        var recent = new MenuItem { Header = "Recent colors" };
        menu.Items.Add(recent);
        menu.Opened += delegate
        {
            recent.Items.Clear();
            foreach (int rgb in Preferences.CheckboxColors)
            {
                var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
                var choice = AddItem(recent, "#" + rgb.ToString("X6"), "", delegate { UseCustomCheckboxColor(brush.Color); });
                choice.Icon = new Border { Width = 12, Height = 12, Background = brush, CornerRadius = new CornerRadius(2) };
            }
            if (!recent.HasItems) AddItem(recent, "No recent colors", "", delegate { }).IsEnabled = false;
            InkpadStyle.StyleItems(recent);
        };
        menu.Items.Add(new Separator());
        AddItem(menu, "More colors…", "", delegate { Dispatcher.BeginInvoke(new Action(PickCustomCheckboxColor)); });
        AttachMenu(picker, menu);
        panel.Children.Add(checkboxInsertButton);
        panel.Children.Add(picker);
        return panel;
    }

    void PickCustomCheckboxColor()
    {
        var color = PickColor((checkboxBrush ?? (SolidColorBrush)Text).Color, Preferences.CheckboxColors);
        if (!color.HasValue) return;
        UseCustomCheckboxColor(color.Value);
    }

    void UseCustomCheckboxColor(Color color)
    {
        Preferences.RememberColor(color);
        Preferences.Save();
        SetCheckboxColor(new SolidColorBrush(color));
    }

    Color? PickColor(Color color, List<int> custom = null)
    {
        using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true,
            Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B) })
        {
            if (custom != null && custom.Count != 0)
            {
                var colors = new int[16];
                for (int index = 0; index < custom.Count; index++)
                {
                    int rgb = custom[index];
                    colors[index] = ((rgb & 255) << 16) | (rgb & 0xff00) | ((rgb >> 16) & 255);
                }
                dialog.CustomColors = colors;
            }
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (dialog.ShowDialog(new ColorDialogOwner(handle)) != System.Windows.Forms.DialogResult.OK) return null;
            return Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
        }
    }

    void SetCheckboxColor(SolidColorBrush brush)
    {
        checkboxBrush = brush;
        checkboxSwatch.Background = brush ?? Text;
        checkboxInsertButton.Content = Symbol(CheckboxIcon, 19, stroke: brush);
        if (drawing) SetDrawing(false);
        if (RecolorCheckboxes(current, brush ?? Brushes.Black)) MarkDirty(current);
        current.TextBox.Focus();
    }

    internal static bool RecolorCheckboxes(Note note, SolidColorBrush brush)
    {
        var editor = note.TextBox;
        var selection = editor.Selection;
        var targets = new List<TextRange>();
        if (selection.IsEmpty)
        {
            var adjacent = AdjacentCheckbox(editor.CaretPosition, LogicalDirection.Forward) ??
                AdjacentCheckbox(editor.CaretPosition, LogicalDirection.Backward);
            if (adjacent != null) targets.Add(adjacent);
        }
        else
        {
            for (var at = selection.Start; at != null && at.CompareTo(selection.End) < 0;
                at = at.GetNextContextPosition(LogicalDirection.Forward))
            {
                string run = at.GetTextInRun(LogicalDirection.Forward);
                if (run.Length == 0) continue;
                int limit = Math.Min(run.Length, at.GetOffsetToPosition(selection.End));
                for (int i = 0; i < limit; i++)
                {
                    if (run[i] == EmptyCheckbox[0] || run[i] == CheckedCheckbox[0])
                        targets.Add(new TextRange(at.GetPositionAtOffset(i), at.GetPositionAtOffset(i + 1)));
                }
            }
        }
        if (targets.Count == 0) return false;
        bool changed = false;
        editor.BeginChange();
        try
        {
            foreach (var target in targets)
            {
                var old = target.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush;
                if (old != null && old.Color == brush.Color) continue;
                note.Formatted = true;
                target.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                changed = true;
            }
        }
        finally { editor.EndChange(); }
        return changed;
    }

    sealed class ColorDialogOwner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; private set; }
        internal ColorDialogOwner(IntPtr handle) { Handle = handle; }
    }

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
        WriteCheckbox(note, spaceBefore ? " " : "", spaceAfter ? " " : "", checkboxBrush);
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
        WriteCheckbox(current, "", " ", checkboxBrush);
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

            var color = marker.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush;
            if (color != null && (color.Color == Colors.Black || color.Color == ((SolidColorBrush)Text).Color)) color = null;
            if (!selection.IsEmpty) selection.Text = "";
            var next = editor.CaretPosition.InsertParagraphBreak();
            editor.Selection.Select(next, next);
            UseCheckboxEncoding(note);
            WriteCheckbox(note, line.Substring(0, indentation), " ", color);
            return true;
        }
        finally { editor.EndChange(); }
    }

    static void WriteCheckbox(Note note, string before, string after, SolidColorBrush color)
    {
        var editor = note.TextBox;
        var selection = editor.Selection;
        editor.BeginChange();
        try
        {
            selection.Text = before + EmptyCheckbox + after;
            var caret = selection.End;
            if (color != null)
            {
                var edge = caret;
                for (int index = 0; index < after.Length; index++) edge = edge.GetNextInsertionPosition(LogicalDirection.Backward);
                var marker = AdjacentCheckbox(edge, LogicalDirection.Backward);
                if (marker != null)
                {
                    note.Formatted = true;
                    marker.ApplyPropertyValue(TextElement.ForegroundProperty, color);
                }
            }
            selection.Select(caret, caret);
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
        var editor = note.TextBox;
        var color = checkbox.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush;
        editor.BeginChange();
        try
        {
            checkbox.Text = checkbox.Text == CheckedCheckbox ? EmptyCheckbox : CheckedCheckbox;
            if (color != null) checkbox.ApplyPropertyValue(TextElement.ForegroundProperty, color);
        }
        finally { editor.EndChange(); }
    }

    static void UseCheckboxEncoding(Note note)
    {
        try { note.Encoding.GetByteCount(EmptyCheckbox + CheckedCheckbox); }
        catch (EncoderFallbackException) { note.Encoding = new UTF8Encoding(false, true); }
    }
}
