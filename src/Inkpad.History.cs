using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Ink;

sealed class NoteHistory
{
    sealed class Edit
    {
        internal Action Undo, Redo;
        internal bool Document;
    }

    readonly RichTextBox editor;
    readonly InkCanvas ink;
    readonly Stack<Edit> undo = new Stack<Edit>();
    readonly Stack<Edit> redo = new Stack<Edit>();
    internal bool Replaying { get; private set; }

    internal NoteHistory(RichTextBox editor, InkCanvas ink)
    {
        this.editor = editor;
        this.ink = ink;
    }

    internal void Start()
    {
        // Keep the native document history aligned with the order of document and ink edits.
        editor.UndoLimit = -1;
        editor.TextChanged += delegate(object sender, TextChangedEventArgs args)
        {
            if (Replaying) return;
            switch (args.UndoAction)
            {
                case UndoAction.Create:
                    Record(new Edit { Document = true });
                    break;
                case UndoAction.Clear:
                    undo.Clear();
                    redo.Clear();
                    break;
            }
        };
        TrackInk(ink.Strokes);
    }

    internal void TrackInk(StrokeCollection strokes)
    {
        var previous = strokes.ToArray();
        strokes.StrokesChanged += delegate
        {
            if (Replaying) return;
            var before = previous;
            var after = strokes.ToArray();
            Record(delegate { ReplaceStrokes(strokes, before); previous = before; },
                delegate { ReplaceStrokes(strokes, after); previous = after; });
            previous = after;
        };
    }

    static void ReplaceStrokes(StrokeCollection strokes, Stroke[] snapshot)
    {
        strokes.Clear();
        foreach (var stroke in snapshot) strokes.Add(stroke);
    }

    internal void Record(Action undoAction, Action redoAction)
    {
        editor.LockCurrentUndoUnit();
        Record(new Edit { Undo = undoAction, Redo = redoAction });
    }

    internal void AttachToDocumentEdit(Action undoAction, Action redoAction)
    {
        editor.LockCurrentUndoUnit();
        if (undo.Count == 0 || !undo.Peek().Document)
        {
            Record(undoAction, redoAction);
            return;
        }
        var edit = undo.Peek();
        edit.Undo = undoAction;
        edit.Redo = redoAction;
    }

    void Record(Edit edit)
    {
        undo.Push(edit);
        redo.Clear();
    }

    internal bool CanReplay(bool forward)
    {
        var from = forward ? redo : undo;
        return from.Count != 0 && (!from.Peek().Document || (forward ? editor.CanRedo : editor.CanUndo));
    }

    internal bool Replay(bool forward)
    {
        if (!CanReplay(forward)) return false;
        var from = forward ? redo : undo;
        var to = forward ? undo : redo;
        var edit = from.Peek();
        Replaying = true;
        try
        {
            if (edit.Document)
            {
                if (!(forward ? editor.Redo() : editor.Undo())) return false;
            }
            var action = forward ? edit.Redo : edit.Undo;
            if (action != null) action();
            from.Pop();
            to.Push(edit);
            return true;
        }
        finally { Replaying = false; }
    }
}
