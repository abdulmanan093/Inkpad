using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;

sealed partial class Inkpad
{
    ContextMenu WritingMenu()
    {
        var menu = new ContextMenu { MinWidth = 220 };
        AddItem(menu, "Write…", "Ctrl+Q", WriteDraft);
        AddItem(menu, "Rewrite", "Ctrl+R", delegate { ApplyWritingTool("Rewrite"); });
        AddItem(menu, "Summarize", "Ctrl+M", delegate { ApplyWritingTool("Summarize"); });
        AddItem(menu, "Make shorter", "", delegate { ApplyWritingTool("Make shorter"); });
        AddItem(menu, "Make longer", "", delegate { ApplyWritingTool("Make longer"); });
        WritingSubmenu(menu, "Change tone", new[] { "Formal", "Casual", "Inspirational", "Humor" });
        WritingSubmenu(menu, "Change format", new[] { "Paragraph", "List", "Business", "Academic", "Marketing", "Poetry" });
        menu.Opened += delegate
        {
            bool text = !string.IsNullOrWhiteSpace(current.TextBox.Selection.Text) || !string.IsNullOrWhiteSpace(current.PlainText);
            foreach (var item in menu.Items.OfType<MenuItem>().Skip(1)) item.IsEnabled = text;
        };
        return menu;
    }

    void WritingSubmenu(ContextMenu menu, string label, IEnumerable<string> options)
    {
        var parent = new MenuItem { Header = label };
        menu.Items.Add(parent);
        foreach (string option in options) AddItem(parent, option, "", delegate { ApplyWritingTool(option); });
    }

    void WriteDraft()
    {
        if (drawing) SetDrawing(false);
        if (markdownPreview != null) SetMarkdownPreview(false);
        var topic = AskText("Write a draft", "Enter a topic to start a local draft template.", "");
        if (string.IsNullOrWhiteSpace(topic)) return;
        current.TextBox.Selection.Text = topic.Trim() + "\r\n\r\nOverview\r\n\r\nKey points\r\n- \r\n\r\nNext steps\r\n- ";
        current.TextBox.Focus();
    }

    void ApplyWritingTool(string action)
    {
        if (drawing) SetDrawing(false);
        if (markdownPreview != null) SetMarkdownPreview(false);
        var editor = current.TextBox;
        bool wholeNote = editor.Selection.IsEmpty;
        if (wholeNote && (current.NeedsInkFile || current.Markdown))
        { ShowNoteDialog(action, "Select the passage you want to edit.", "OK", null); return; }
        string input = wholeNote ? current.PlainText : editor.Selection.Text;
        string result = TransformWriting(input, action);
        if (result == input) return;
        string reviewed = AskText(action, "Review the local edit before applying it.", result, true);
        if (reviewed == null) return;
        if (wholeNote) editor.SelectAll();
        editor.Selection.Text = reviewed;
        editor.Focus();
    }

    internal static string TransformWriting(string input, string action)
    {
        var sentences = Regex.Split(input.Trim(), @"(?<=[.!?])\s+|\r?\n+")
            .Select(line => Regex.Replace(line.Trim(), @"^(?:[-*•]|\d+\.)\s+", "")).Where(line => line.Length != 0).ToArray();
        if (sentences.Length == 0) return input;
        var paragraph = string.Join(" ", sentences);
        switch (action)
        {
            case "Summarize": return string.Join(" ", sentences.Take(Math.Max(1, Math.Min(3, (sentences.Length + 1) / 2))));
            case "Make shorter": return Regex.Replace(paragraph, @"\b(?:very|really|actually|basically|in fact)\s+|\bin order to\b", match => match.Value.Equals("in order to", StringComparison.OrdinalIgnoreCase) ? "to" : "", RegexOptions.IgnoreCase);
            case "Make longer": return paragraph + "\r\n\r\nAdd context: \r\nAdd an example: \r\nNext step: ";
            case "Formal": return Regex.Replace(paragraph, @"\b(?:can't|won't|don't|isn't|it's|I'm|we're|you're)\b", match => FormalWords[match.Value], RegexOptions.IgnoreCase);
            case "Casual": return Regex.Replace(paragraph, @"\b(?:cannot|do not|will not|is not|we are|you are)\b", match => CasualWords[match.Value], RegexOptions.IgnoreCase);
            case "Inspirational": return paragraph + "\r\n\r\nOne small step is enough to get started.";
            case "Humor": return paragraph + "\r\n\r\nAt least the to-do list has a sense of ambition.";
            case "List": return string.Join("\r\n", sentences.Select(sentence => "- " + sentence));
            case "Business": return "Overview\r\n" + sentences[0] + "\r\n\r\nKey points\r\n" + string.Join("\r\n", sentences.Skip(1).Select(sentence => "- " + sentence)) + "\r\n\r\nNext steps\r\n- ";
            case "Academic": return "Abstract\r\n" + sentences[0] + "\r\n\r\nDiscussion\r\n" + string.Join(" ", sentences.Skip(1)) + "\r\n\r\nReferences\r\n";
            case "Marketing": return sentences[0] + "\r\n\r\n" + string.Join("\r\n", sentences.Skip(1).Select(sentence => "- " + sentence)) + "\r\n\r\nCall to action: ";
            case "Poetry": return string.Join("\r\n", sentences.SelectMany(sentence => Regex.Split(sentence, @"(?<=[,;:])\s+")));
            case "Paragraph": return paragraph;
            default: return Regex.Replace(Regex.Replace(paragraph, @"[ \t]+([.,!?;:])", "$1"), @"[ \t]{2,}", " ");
        }
    }

    static readonly Dictionary<string, string> FormalWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { { "can't", "cannot" }, { "won't", "will not" }, { "don't", "do not" }, { "isn't", "is not" }, { "it's", "it is" }, { "I'm", "I am" }, { "we're", "we are" }, { "you're", "you are" } };
    static readonly Dictionary<string, string> CasualWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { { "cannot", "can't" }, { "do not", "don't" }, { "will not", "won't" }, { "is not", "isn't" }, { "we are", "we're" }, { "you are", "you're" } };
}
