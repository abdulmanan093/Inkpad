using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;

sealed partial class Inkpad
{
    static readonly string[] HeadingNames = { "Title", "Subtitle", "Heading", "Subheading", "Section", "Subsection", "Body" };
    static readonly double[] HeadingSizes = { 40, 34, 27, 23, 20, 18, 17 };
    Border saveFeedback;
    TextBlock saveFeedbackText;
    DispatcherTimer saveFeedbackTimer;

    void BuildHeadingMenu(Button owner)
    {
        var menu = new ContextMenu();
        double[] previewSizes = { 26, 23, 20, 18, 16, 15, 14 };
        double[] rowHeights = { 38, 35, 33, 31, 30, 29, 28 };
        var items = new MenuItem[HeadingNames.Length];
        for (int index = 0; index < HeadingNames.Length; index++)
        {
            int level = index;
            var item = AddItem(menu, HeadingNames[index], "", delegate { SetHeading(level); });
            item.FontSize = previewSizes[index];
            item.Height = rowHeights[index];
            item.IsCheckable = true;
            items[index] = item;
        }
        menu.Opened += delegate
        {
            var paragraph = current.TextBox.CaretPosition.Paragraph;
            int selection = paragraph == null ? -1 : Array.FindIndex(HeadingSizes, size => Math.Abs(size - paragraph.FontSize) <= .4);
            if (selection < 0) selection = HeadingNames.Length - 1;
            for (int index = 0; index < items.Length; index++) items[index].IsChecked = index == selection;
        };
        AttachMenu(owner, menu, headings: true);
    }

    void SetHeading(int level)
    {
        if (drawing) SetDrawing(false);
        var editor = current.TextBox;
        var start = editor.Selection.Start;
        var end = editor.Selection.End;
        var first = start.Paragraph;
        if (first == null) return;
        bool body = level == HeadingNames.Length - 1;
        if (!body) current.Formatted = true;
        editor.BeginChange();
        try
        {
            for (var paragraph = first; paragraph != null && paragraph.ContentStart.CompareTo(end) <= 0;)
            {
                if (body)
                {
                    paragraph.ClearValue(TextElement.FontSizeProperty);
                    paragraph.ClearValue(TextElement.FontWeightProperty);
                }
                else
                {
                    paragraph.FontSize = HeadingSizes[level];
                    paragraph.FontWeight = FontWeights.Normal;
                }
                if (paragraph == end.Paragraph) break;
                var next = paragraph.ElementEnd.GetNextInsertionPosition(LogicalDirection.Forward);
                paragraph = next == null ? null : next.Paragraph;
            }
        }
        finally { editor.EndChange(); }
        editor.Focus();
        UpdateSelection();
    }

    Border BuildSaveFeedback()
    {
        saveFeedbackText = Label("", 12, Brush("#145F45"));
        saveFeedback = new Border
        {
            Child = WithIcon(Symbol("M 3,8 L 6.5,11.5 L 13,5", 13), saveFeedbackText),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = Brush("#E8F4ED"), BorderBrush = Brush("#CFE5D9"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 9, 0),
            Visibility = Visibility.Collapsed, IsHitTestVisible = false
        };
        saveFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        saveFeedbackTimer.Tick += delegate
        {
            saveFeedbackTimer.Stop();
            if (!SystemParameters.ClientAreaAnimation) { saveFeedback.Visibility = Visibility.Collapsed; return; }
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop };
            fade.Completed += delegate { if (!saveFeedbackTimer.IsEnabled) saveFeedback.Visibility = Visibility.Collapsed; };
            saveFeedback.BeginAnimation(OpacityProperty, fade);
        };
        Closed += delegate { saveFeedbackTimer.Stop(); };
        return saveFeedback;
    }

    void ShowSaveFeedback(string message, string path)
    {
        saveFeedbackTimer.Stop();
        saveFeedbackText.Text = message;
        saveFeedback.ToolTip = path;
        saveFeedback.Visibility = Visibility.Visible;
        InkpadStyle.AnimateIn(saveFeedback, 4, 160);
        saveFeedbackTimer.Start();
    }

    static StackPanel WithIcon(UIElement icon, TextBlock label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        label.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(label);
        return row;
    }

    MessageBoxResult ShowNoteDialog(string title, string description, string acceptLabel, string discardLabel)
    {
        var dialog = new Window
        {
            Title = title, Owner = this, Width = 416, SizeToContent = SizeToContent.Height, FontFamily = FontFamily,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, Background = Surface, UseLayoutRounding = true
        };
        WindowChrome.SetWindowChrome(dialog, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(8) });
        var layout = new StackPanel();
        var header = new StackPanel { Margin = new Thickness(23, 20, 23, 22) };
        var heading = new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Text, TextWrapping = TextWrapping.Wrap };
        header.Children.Add(heading);
        header.Children.Add(new TextBlock { Text = description, FontSize = 14, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MaxHeight = 150 });
        layout.Children.Add(header);
        var actions = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(18, 13, 18, 13) };
        var result = MessageBoxResult.Cancel;
        Action<string, MessageBoxResult, bool> button = delegate(string name, MessageBoxResult response, bool primary)
        {
            var control = ActionButton(name, name, double.NaN, delegate { result = response; dialog.DialogResult = true; });
            control.Background = primary ? Brush("#0067C0") : Brushes.White;
            control.Foreground = primary ? Brushes.White : Text;
            control.BorderBrush = primary ? Brush("#0067C0") : Brush("#D7D7D7");
            control.Style = InkpadStyle.DialogButton;
            control.Margin = new Thickness(4, 0, 4, 0);
            control.Height = 34;
            control.IsDefault = primary;
            actions.Children.Add(control);
        };
        button(acceptLabel, MessageBoxResult.Yes, true);
        if (discardLabel != null)
        {
            button(discardLabel, MessageBoxResult.No, false);
            button("Cancel", MessageBoxResult.Cancel, false);
        }
        layout.Children.Add(new Border { Child = actions, Background = Brush("#EEEEEE"), BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(0, 0, 8, 8) });
        dialog.Content = new Border { Child = layout, BorderBrush = Brush("#CBCBCB"), BorderThickness = new Thickness(1), Background = Surface, CornerRadius = new CornerRadius(8) };
        dialog.Loaded += delegate { InkpadStyle.AnimateIn(layout, 3, 140); Keyboard.Focus((Button)actions.Children[0]); };
        dialog.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape) { dialog.Close(); args.Handled = true; }
        };
        dialog.ShowDialog();
        return result;
    }

}
