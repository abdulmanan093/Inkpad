using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

static class NoteTables
{
    internal static T Ancestor<T>(TextPointer position) where T : DependencyObject
    {
        for (DependencyObject item = position.Parent; item != null; item = LogicalTreeHelper.GetParent(item))
        {
            var found = item as T;
            if (found != null) return found;
        }
        return null;
    }

    internal static Table Insert(RichTextBox editor, int rows, int columns)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 8) };
        for (int index = 0; index < columns; index++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        for (int index = 0; index < rows; index++) group.Rows.Add(NewRow(columns));

        var document = editor.Document;
        Block before = editor.CaretPosition.Paragraph;
        while (before != null && before.Parent != document)
        {
            DependencyObject ancestor = before.Parent;
            while (ancestor != null && !(ancestor is Block)) ancestor = LogicalTreeHelper.GetParent(ancestor);
            before = ancestor as Block;
        }
        editor.BeginChange();
        try
        {
            var paragraph = before as Paragraph;
            if (paragraph != null && new TextRange(paragraph.ContentStart, paragraph.ContentEnd).IsEmpty)
                document.Blocks.InsertBefore(paragraph, table);
            else if (before != null) document.Blocks.InsertAfter(before, table);
            else document.Blocks.Add(table);
            if (document.Blocks.LastBlock == table) document.Blocks.Add(new Paragraph());
            MoveTo(editor, group.Rows[0].Cells[0]);
        }
        finally { editor.EndChange(); }
        return table;
    }

    internal static void Edit(RichTextBox editor, TableCell cell, TableAction action)
    {
        if (cell == null) return;
        var row = (TableRow)cell.Parent;
        var group = (TableRowGroup)row.Parent;
        var table = (Table)group.Parent;
        int column = row.Cells.IndexOf(cell), rowIndex = group.Rows.IndexOf(row);
        editor.BeginChange();
        try
        {
            switch (action)
            {
                case TableAction.AddRowAbove:
                case TableAction.AddRowBelow:
                    int insertRow = rowIndex + (action == TableAction.AddRowBelow ? 1 : 0);
                    var added = NewRow(row.Cells.Count);
                    group.Rows.Insert(insertRow, added);
                    MoveTo(editor, added.Cells[column]);
                    break;
                case TableAction.AddColumnLeft:
                case TableAction.AddColumnRight:
                    int insertColumn = column + (action == TableAction.AddColumnRight ? 1 : 0);
                    foreach (var each in Rows(table)) each.Cells.Insert(Math.Min(insertColumn, each.Cells.Count), NewCell());
                    table.Columns.Insert(Math.Min(insertColumn, table.Columns.Count), new TableColumn());
                    MoveTo(editor, row.Cells[insertColumn]);
                    break;
                case TableAction.DeleteRow:
                    if (Rows(table).Count() == 1) DeleteTable(editor, table);
                    else
                    {
                        group.Rows.Remove(row);
                        var target = group.Rows.Count == 0 ? Rows(table).First() : group.Rows[Math.Min(rowIndex, group.Rows.Count - 1)];
                        MoveTo(editor, target.Cells[Math.Min(column, target.Cells.Count - 1)]);
                    }
                    break;
                case TableAction.DeleteColumn:
                    if (Rows(table).Max(each => each.Cells.Count) == 1) DeleteTable(editor, table);
                    else
                    {
                        foreach (var each in Rows(table)) if (each.Cells.Count > column) each.Cells.RemoveAt(column);
                        if (table.Columns.Count > column) table.Columns.RemoveAt(column);
                        MoveTo(editor, row.Cells[Math.Min(column, row.Cells.Count - 1)]);
                    }
                    break;
                case TableAction.DeleteTable:
                    DeleteTable(editor, table);
                    break;
            }
        }
        finally { editor.EndChange(); }
    }

    internal static bool Navigate(RichTextBox editor, bool backwards)
    {
        var cell = Ancestor<TableCell>(editor.CaretPosition);
        if (cell == null) return false;
        var row = (TableRow)cell.Parent;
        var table = (Table)((TableRowGroup)row.Parent).Parent;
        var cells = Rows(table).SelectMany(each => each.Cells).ToList();
        int index = cells.IndexOf(cell) + (backwards ? -1 : 1);
        if (index >= 0 && index < cells.Count) MoveTo(editor, cells[index]);
        else
        {
            var adjacent = backwards ? table.PreviousBlock : table.NextBlock;
            if (adjacent == null && !backwards && table.Parent == editor.Document)
            {
                adjacent = new Paragraph();
                editor.Document.Blocks.InsertAfter(table, adjacent);
            }
            var next = adjacent == null ? null : adjacent.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            if (next != null) editor.CaretPosition = next;
        }
        return true;
    }

    internal static void StyleLoaded(FlowDocument document)
    {
        foreach (var table in Tables(document.Blocks))
        {
            table.CellSpacing = 0;
            table.Margin = new Thickness(0, 8, 0, 8);
            foreach (var cell in Rows(table).SelectMany(row => row.Cells)) StyleCell(cell);
        }
    }

    static IEnumerable<Table> Tables(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            var table = block as Table;
            if (table != null) yield return table;
            var section = block as Section;
            if (section != null) foreach (var child in Tables(section.Blocks)) yield return child;
        }
    }

    static IEnumerable<TableRow> Rows(Table table) { return table.RowGroups.SelectMany(group => group.Rows); }

    static void DeleteTable(RichTextBox editor, Table table)
    {
        var parent = table.Parent as FlowDocument;
        var section = table.Parent as Section;
        var blocks = parent != null ? parent.Blocks : section != null ? section.Blocks : null;
        if (blocks == null) return;
        var after = table.NextBlock as Paragraph;
        if (after == null)
        {
            after = new Paragraph();
            blocks.InsertAfter(table, after);
        }
        blocks.Remove(table);
        editor.CaretPosition = after.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
    }

    static TableRow NewRow(int columns)
    {
        var row = new TableRow();
        for (int index = 0; index < columns; index++) row.Cells.Add(NewCell());
        return row;
    }

    static TableCell NewCell()
    {
        var cell = new TableCell(new Paragraph());
        StyleCell(cell);
        return cell;
    }

    static readonly Brush CellBorder = new SolidColorBrush(Color.FromRgb(198, 198, 198));

    static void StyleCell(TableCell cell)
    {
        cell.BorderBrush = CellBorder;
        cell.BorderThickness = new Thickness(.5);
        cell.Padding = new Thickness(9, 5, 9, 5);
    }

    static void MoveTo(RichTextBox editor, TableCell cell)
    {
        editor.CaretPosition = cell.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
    }
}

enum TableAction { AddRowAbove, AddRowBelow, AddColumnLeft, AddColumnRight, DeleteRow, DeleteColumn, DeleteTable }
