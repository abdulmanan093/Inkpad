using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

sealed partial class Inkpad
{
    const int TableGridSize = 5;

    void BuildTableMenu(Button button)
    {
        var menu = new ContextMenu();
        var insert = AddItem(menu, "Insert table…", "", delegate
        {
            Dispatcher.BeginInvoke(new Action(delegate { OpenTableGrid(button); }));
        });
        insert.Icon = Symbol("M 2,3 H 14 V 13 H 2 Z M 2,8 H 14 M 8,3 V 13", 16);
        menu.Items.Add(new Separator());
        AddTableActions(menu);
        AttachMenu(button, menu, delegate
        {
            OpenTableGrid(button, menu);
            return false;
        });
    }

    void OpenTableGrid(Button owner, ContextMenu editMenu = null)
    {
        var popup = new Popup { PlacementTarget = owner, Placement = PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true, HorizontalOffset = -3 };
        var contents = new StackPanel { Margin = new Thickness(10, 7, 10, 10) };
        var label = Label("Insert table", 14, Muted);
        label.Margin = new Thickness(2, 0, 0, 8);
        contents.Children.Add(label);
        var grid = new UniformGrid { Rows = TableGridSize, Columns = TableGridSize };
        contents.Children.Add(grid);
        var cells = new Button[TableGridSize, TableGridSize];
        Action<int, int> highlight = delegate(int rows, int columns)
        {
            label.Text = columns + " × " + rows + " table";
            for (int y = 0; y < TableGridSize; y++)
            for (int x = 0; x < TableGridSize; x++)
            {
                bool selected = y < rows && x < columns;
                cells[y, x].Background = selected ? PaleBlue : SettingsField;
                cells[y, x].BorderBrush = selected ? Brush("#7AA8D6") : Brush("#929292");
            }
        };
        for (int y = 0; y < TableGridSize; y++)
        for (int x = 0; x < TableGridSize; x++)
        {
            int rows = y + 1, columns = x + 1;
            var cell = ActionButton(null, columns + " columns, " + rows + " rows", 31, delegate
            {
                popup.IsOpen = false;
                InsertTable(rows, columns);
            });
            cell.ToolTip = null;
            cell.Height = 31;
            cell.Margin = new Thickness(2);
            cell.MouseEnter += delegate { highlight(rows, columns); };
            cell.GotKeyboardFocus += delegate { highlight(rows, columns); };
            cells[y, x] = cell;
            grid.Children.Add(cell);
        }
        highlight(0, 0);
        label.Text = "Insert table";
        contents.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 8, 0, 4) });
        var custom = ActionButton("Insert table…", "Insert table with custom dimensions", double.NaN, delegate
        {
            popup.IsOpen = false;
            string dimensions = AskText("Insert table", "Enter rows × columns, from 1 to 30 for each.", "3 × 3");
            if (dimensions == null) return;
            var values = dimensions.Split('×', 'x', 'X');
            int rows, columns;
            if (values.Length != 2 || !int.TryParse(values[0], out rows) || !int.TryParse(values[1], out columns) || rows < 1 || rows > 30 || columns < 1 || columns > 30)
                ShowNoteDialog("Insert table", "Enter two numbers from 1 to 30, such as 3 × 4.", "OK", null);
            else InsertTable(rows, columns);
        });
        contents.Children.Add(custom);
        if (editMenu != null)
        {
            var edit = ActionButton("Edit table  ›", "Edit table", double.NaN, delegate
            {
                popup.IsOpen = false;
                editMenu.IsOpen = true;
            });
            edit.IsEnabled = NoteTables.Ancestor<TableCell>(current.TextBox.CaretPosition) != null;
            contents.Children.Add(edit);
        }
        var frame = new Border { Child = contents, Background = InkpadStyle.PopupBackground,
            BorderBrush = InkpadStyle.PopupBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(3, 2, 7, 8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = .16 } };
        frame.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape) { popup.IsOpen = false; owner.Focus(); args.Handled = true; }
        };
        KeyboardNavigation.SetDirectionalNavigation(grid, KeyboardNavigationMode.Cycle);
        popup.Child = frame;
        popup.Opened += delegate { owner.Background = SettingsField; InkpadStyle.AnimateIn(frame, -3); };
        popup.Closed += delegate { owner.Background = Brushes.Transparent; };
        popup.IsOpen = true;
    }

    void InsertTable(int rows, int columns)
    {
        if (drawing) SetDrawing(false);
        current.Formatted = true;
        NoteTables.Insert(current.TextBox, rows, columns);
        current.TextBox.Focus();
        MarkDirty(current);
    }

    void AddTableActions(ContextMenu menu)
    {
        var names = new[] { "Insert row above", "Insert row below", "Insert column left", "Insert column right", "Delete row", "Delete column", "Delete table" };
        for (int index = 0; index < names.Length; index++)
        {
            if (index == 4) menu.Items.Add(new Separator());
            var action = (TableAction)index;
            AddItem(menu, names[index], "", delegate
            {
                var cell = NoteTables.Ancestor<TableCell>(current.TextBox.CaretPosition);
                NoteTables.Edit(current.TextBox, cell, action);
                current.TextBox.Focus();
            });
        }
    }

    void AddEditorMenu(Note note)
    {
        AddCheckboxPointer(note);
        var menu = new ContextMenu();
        AddItem(menu, "Cut", "Ctrl+X", note.TextBox.Cut);
        AddItem(menu, "Copy", "Ctrl+C", note.TextBox.Copy);
        AddItem(menu, "Paste", "Ctrl+V", note.TextBox.Paste);
        AddItem(menu, "Select all", "Ctrl+A", note.TextBox.SelectAll);
        var divider = new Separator();
        menu.Items.Add(divider);
        AddTableActions(menu);
        menu.Opened += delegate
        {
            bool inTable = NoteTables.Ancestor<TableCell>(note.TextBox.CaretPosition) != null;
            for (int index = 4; index < menu.Items.Count; index++)
                ((UIElement)menu.Items[index]).Visibility = inTable ? Visibility.Visible : Visibility.Collapsed;
        };
        note.TextBox.PreviewMouseRightButtonDown += delegate(object sender, MouseButtonEventArgs args)
        {
            var location = note.TextBox.GetPositionFromPoint(args.GetPosition(note.TextBox), false);
            if (location != null && !note.TextBox.Selection.Contains(location)) note.TextBox.CaretPosition = location;
        };
        InkpadStyle.Apply(menu);
        note.TextBox.ContextMenu = menu;
        note.TextBox.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
        {
            var point = args.GetPosition(note.TextBox);
            var checkbox = CheckboxAt(note.TextBox, point);
            if (checkbox != null)
            {
                args.Handled = true;
                if (args.ClickCount == 1)
                {
                    note.TextBox.Focus();
                    ToggleCheckbox(note, checkbox);
                }
                return;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            var location = note.TextBox.GetPositionFromPoint(point, false);
            var link = location == null ? null : NoteTables.Ancestor<Hyperlink>(location);
            if (link == null || !IsWebLink(link.NavigateUri)) return;
            args.Handled = true;
            try { Process.Start(new ProcessStartInfo(link.NavigateUri.AbsoluteUri) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception error)
            { MessageBox.Show(this, error.Message, "Could not open link", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
    }

    void InsertLink()
    {
        if (drawing) SetDrawing(false);
        var editor = current.TextBox;
        var selection = editor.Selection;
        var original = NoteTables.Ancestor<Hyperlink>(editor.CaretPosition);
        var dialog = new Window { Owner = this, Title = original == null ? "Insert link" : "Edit link", Width = 425,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Surface, FontFamily = FontFamily, FontSize = 14 };
        var fields = new StackPanel { Margin = new Thickness(20, 15, 20, 17) };
        var title = LinkField(fields, "Text", original == null ? selection.Text.Trim() : new TextRange(original.ContentStart, original.ContentEnd).Text);
        var url = LinkField(fields, "Web address", original == null || original.NavigateUri == null ? "" : original.NavigateUri.ToString());
        var errorLabel = Label("", 12, Brush("#B82940"));
        fields.Children.Add(errorLabel);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 11, 0, 0) };
        Uri destination = null;
        var accept = ActionButton(original == null ? "Insert" : "Save", "Save link", 82, delegate
        {
            string address = url.Text.Trim();
            if (address.IndexOf(":", StringComparison.Ordinal) < 0) address = "https://" + address;
            if (!Uri.TryCreate(address, UriKind.Absolute, out destination) || !IsWebLink(destination))
            { errorLabel.Text = "Enter a valid web address."; url.Focus(); return; }
            dialog.DialogResult = true;
        });
        accept.IsDefault = true;
        accept.Background = Brush("#0067C0");
        accept.Foreground = Brushes.White;
        accept.BorderBrush = Brush("#0067C0");
        accept.Style = InkpadStyle.DialogButton;
        buttons.Children.Add(accept);
        var cancel = ActionButton("Cancel", "Cancel", 79, null);
        cancel.IsCancel = true;
        cancel.Background = Brushes.White;
        cancel.BorderBrush = InkpadStyle.PopupBorder;
        cancel.Style = InkpadStyle.DialogButton;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(cancel);
        fields.Children.Add(buttons);
        dialog.Content = fields;
        dialog.Loaded += delegate { InkpadStyle.AnimateIn(fields, 3); url.Focus(); url.SelectAll(); };
        if (dialog.ShowDialog() != true) return;
        current.Formatted = true;
        WriteLink(editor, original, title.Text.Trim(), destination);
        editor.Focus();
        MarkDirty(current);
    }

    static TextBox LinkField(Panel panel, string name, string value)
    {
        panel.Children.Add(Label(name, 13, Text));
        var box = new TextBox { Text = value, Height = 30, Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 4, 0, 10), BorderBrush = InkpadStyle.PopupBorder };
        AutomationProperties.SetName(box, name);
        panel.Children.Add(box);
        return box;
    }

    static bool IsWebLink(Uri uri)
    {
        return uri != null && uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && uri.Host.Length > 0;
    }

    internal static void WriteLink(RichTextBox editor, Hyperlink original, string title, Uri destination)
    {
        editor.BeginChange();
        try
        {
            var insertion = editor.Selection.Start;
            if (string.IsNullOrWhiteSpace(title)) title = destination.AbsoluteUri;
            if (original == null)
            {
                editor.Selection.Text = "";
                var link = new Hyperlink(new Run(title), insertion) { NavigateUri = destination, ToolTip = "Ctrl+click to open " + destination.Host };
                editor.CaretPosition = link.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
            }
            else
            {
                original.Inlines.Clear();
                original.Inlines.Add(new Run(title));
                original.NavigateUri = destination;
                original.ToolTip = "Ctrl+click to open " + destination.Host;
            }
        }
        finally { editor.EndChange(); }
    }

    void ToggleExplorerMenu()
    {
        try
        {
            bool enable = !InkpadWindows.ExplorerMenuEnabled();
            InkpadWindows.SetExplorerMenu(enable);
            if (enable) MessageBox.Show(this, "Right-click a text file or Inkpad note in File Explorer. If needed, choose Show more options, then Edit in Inkpad.",
                "File Explorer integration", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "File Explorer integration", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
