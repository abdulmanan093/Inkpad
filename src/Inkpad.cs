using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Inkpad")]
[assembly: System.Reflection.AssemblyDescription("Offline text and drawing editor for Windows")]
[assembly: System.Reflection.AssemblyProduct("Inkpad")]
[assembly: System.Reflection.AssemblyVersion("1.9.1.0")]

sealed partial class Inkpad : Window
{
    static readonly Brush Chrome = new SolidColorBrush(ColorOf("#E7E7E7")), Surface = new SolidColorBrush(ColorOf("#F6F6F6")), Paper = new SolidColorBrush(ColorOf("#F6F6F6")),
        Line = new SolidColorBrush(ColorOf("#E4E4E4")), Text = new SolidColorBrush(ColorOf("#191919")), Muted = new SolidColorBrush(ColorOf("#5C5C5C")),
        Blue = Brush("#124C86"), PaleBlue = new SolidColorBrush(ColorOf("#DCEBFA"));
    static readonly string[] ShapePaths = { "M 3,13 L 13,3", "M 3,13 L 13,3 M 7,3 H 13 V 9", "M 2.5,4 H 13.5 V 12 H 2.5 Z", "M 14,8 A 6,4.5 0 1 1 2,8 A 6,4.5 0 1 1 14,8 Z", "M 8,2.5 L 14,13 H 2 Z" };

    readonly List<Note> notes = new List<Note>();
    readonly DockPanel shell = new DockPanel();
    readonly StackPanel tabs = new StackPanel { Orientation = Orientation.Horizontal };
    readonly ScrollViewer tabScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly ScrollViewer pageScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Background = Paper };
    readonly Border drawingBar = new Border { Visibility = Visibility.Collapsed };
    readonly TextBlock position = Label("", 12, Muted), characters = Label("", 12, Muted),
        fileMode = Label("", 12, Text), zoomLabel = Label("100%", 12, Muted),
        lineEnding = Label("Windows (CRLF)", 12, Muted), encodingLabel = Label("UTF-8", 12, Muted);
    readonly Border findBar = new Border { Visibility = Visibility.Collapsed };
    readonly TextBox findBox = new TextBox();
    readonly TextBlock findFeedback = Label("", 12, Muted);
    readonly Style buttonStyle;
    readonly ScaleTransform pageScale = new ScaleTransform(1, 1);
    readonly ShapeLayer shapeLayer = new ShapeLayer();
    Button drawButton, penButton, eraserButton, shapesButton, undoInkButton, redoInkButton, boldButton, italicButton, strikeButton;
    Grid page;
    Note current;
    bool drawing, erasing, wrap = true;
    ShapeTool? shape;
    int zoom = 100;
    bool wasMaximized;
    readonly string windowPlacementPath;

    [STAThread]
    public static void Main(string[] paths)
    {
#if !STORE
        if (paths.Length == 1 && (paths[0] == "--enable-explorer-menu" || paths[0] == "--disable-explorer-menu"))
        {
            try { InkpadWindows.SetExplorerMenu(paths[0] == "--enable-explorer-menu"); }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
                Environment.ExitCode = 1;
            }
            return;
        }
#endif
        var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
        var window = new Inkpad();
        window.Loaded += delegate { window.OpenStartup(paths); };
        app.Run(window);
    }

    Inkpad() : this(null) { }

    internal Inkpad(string placementPath)
    {
        windowPlacementPath = placementPath;
        Title = "Inkpad";
        Width = 1120;
        Height = 690;
        MinWidth = 720;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(8) });
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;
        Foreground = Text;
        Background = Surface;
        buttonStyle = (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
          <Setter Property='Background' Value='Transparent'/><Setter Property='BorderBrush' Value='Transparent'/>
          <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
           <Grid>
            <Border x:Name='hover' Background='#10000000' Opacity='0' CornerRadius='5'/>
            <Border x:Name='body' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}' CornerRadius='5'>
             <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='True'/>
            </Border>
           </Grid><ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
             <Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hover' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.08'/></Storyboard></BeginStoryboard></Trigger.EnterActions>
             <Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hover' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.11'/></Storyboard></BeginStoryboard></Trigger.ExitActions>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='body' Property='Background' Value='#1B000000'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='body' Property='BorderBrush' Value='#7896B3'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter TargetName='body' Property='Opacity' Value='0.38'/></Trigger>
           </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
        </Style>");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        workspace = new Grid();
        var chrome = new DockPanel();
        workspace.Children.Add(shell);
        Content = new Border { Background = Surface, BorderBrush = Brush("#BDBDBD"), BorderThickness = new Thickness(1), Child = chrome };
        BuildTitleBar(chrome);
        chrome.Children.Add(workspace);
        BuildToolbar();
        BuildDrawingBar();
        BuildImageBar();
        BuildFindBar();
        BuildStatusBar();
        shapeLayer.Committed += delegate(Stroke stroke) { current.Ink.Strokes.Add(stroke); };
        shell.Children.Add(pageScroll);
        pageScroll.SizeChanged += delegate { ResizePage(); };
        pageScroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs args)
        {
            if (args.ViewportWidthChange != 0 || args.ViewportHeightChange != 0) ResizePage();
        };
        PreviewKeyDown += OnShortcut;
        Closing += OnClosing;
        InkpadWindows.Restore(this, windowPlacementPath);
        wasMaximized = WindowState == WindowState.Maximized;
        StateChanged += delegate { if (WindowState != WindowState.Minimized) wasMaximized = WindowState == WindowState.Maximized; };
        AllowDrop = true;
        PreviewDragOver += delegate(object sender, DragEventArgs args)
        {
            if (!args.Data.GetDataPresent(DataFormats.FileDrop)) return;
            args.Effects = DragDropEffects.Copy;
            args.Handled = true;
        };
        PreviewDrop += delegate(object sender, DragEventArgs args)
        {
            if (!args.Data.GetDataPresent(DataFormats.FileDrop)) return;
            OpenFiles((string[])args.Data.GetData(DataFormats.FileDrop));
            args.Handled = true;
        };
        AddNote(new Note());
        ApplyPreferences();
    }

    void BuildTitleBar(DockPanel chrome)
    {
        titleBar = new DockPanel { Background = Chrome, Height = 34, LastChildFill = true };
        var row = titleBar;
        DockPanel.SetDock(row, Dock.Top);
        chrome.Children.Add(row);
        row.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
        {
            if (args.ClickCount == 2) ToggleMaximized();
            else if (WindowState == WindowState.Normal) DragMove();
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(controls, Dock.Right);
        row.Children.Add(controls);
        var minimize = ActionButton(Symbol("M 3,8 H 13", 13), "Minimize", 44, delegate { SystemCommands.MinimizeWindow(this); });
        var maximize = ActionButton(Symbol("M 3,3 H 13 V 13 H 3 Z", 13), "Maximize", 44, ToggleMaximized);
        var close = ActionButton(null, "Close", 46, Close);
        close.Style = InkpadStyle.CloseButton;
        StateChanged += delegate
        {
            bool enlarged = WindowState == WindowState.Maximized;
            maximize.Content = Symbol(enlarged ? "M 5,4 V 2 H 14 V 11 H 12 M 2,5 H 11 V 14 H 2 Z" : "M 3,3 H 13 V 13 H 3 Z", 13);
            maximize.ToolTip = enlarged ? "Restore down" : "Maximize";
            AutomationProperties.SetName(maximize, (string)maximize.ToolTip);
        };
        foreach (var button in new[] { minimize, maximize, close })
        {
            button.Height = 28;
            controls.Children.Add(button);
        }
        var identity = new StackPanel { Orientation = Orientation.Horizontal };
        settingsBack = ActionButton(Symbol("M 10,3 L 5,8 L 10,13 M 5,8 H 14", 15), "Back to editor", 31, delegate { CloseSettings(); });
        settingsBack.Visibility = Visibility.Collapsed;
        identity.Children.Add(settingsBack);
        var logo = new Image { Width = 22, Height = 22, Source = ReadAppIcon(), VerticalAlignment = VerticalAlignment.Center };
        Icon = logo.Source;
        logo.Margin = new Thickness(13, 0, 5, 0);
        identity.Children.Add(logo);
        settingsTitle = Label("Inkpad", 13, Text);
        settingsTitle.Margin = new Thickness(6, 0, 0, 0);
        settingsTitle.Visibility = Visibility.Collapsed;
        identity.Children.Add(settingsTitle);
        DockPanel.SetDock(identity, Dock.Left);
        row.Children.Add(identity);
        var tabStrip = new DockPanel { Margin = new Thickness(0, 4, 5, 0), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Height = 30 };
        titleTabs = tabStrip;
        row.Children.Add(tabStrip);
        var back = ActionButton(Symbol("M 10,3 L 5,8 L 10,13 Z", 11, Muted), "Scroll tabs left", 29, delegate { tabScroll.ScrollToHorizontalOffset(tabScroll.HorizontalOffset - 140); });
        var forward = ActionButton(Symbol("M 6,3 L 11,8 L 6,13 Z", 11, Muted), "Scroll tabs right", 29, delegate { tabScroll.ScrollToHorizontalOffset(tabScroll.HorizontalOffset + 140); });
        var add = ActionButton(Symbol("M 3,8 H 13 M 8,3 V 13", 15), "New tab (Ctrl+N)", 34, delegate { AddNote(new Note()); });
        back.Height = forward.Height = add.Height = 27;
        DockPanel.SetDock(back, Dock.Left);
        DockPanel.SetDock(forward, Dock.Right);
        DockPanel.SetDock(add, Dock.Right);
        tabStrip.Children.Add(back);
        tabStrip.Children.Add(add);
        tabStrip.Children.Add(forward);
        tabScroll.Content = tabs;
        tabStrip.Children.Add(tabScroll);
        tabScroll.ScrollChanged += delegate
        {
            back.IsEnabled = tabScroll.HorizontalOffset > 0;
            forward.IsEnabled = tabScroll.HorizontalOffset + 1 < tabScroll.ScrollableWidth;
        };
        back.IsEnabled = forward.IsEnabled = false;
    }

    void BuildToolbar()
    {
        var row = new Grid { Background = Surface, Height = 33 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(Rule(row, Surface), Dock.Top);
        var menu = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
        var names = new[] { "File", "Edit", "View" };
        var groups = new[] { FileMenu(), EditMenu(), ViewMenu() };
        for (int index = 0; index < names.Length; index++)
        {
            var button = ActionButton("_" + names[index], names[index] + " menu", 49, null);
            button.FontSize = 15;
            AttachMenu(button, groups[index]);
            menu.Children.Add(button);
        }
        row.Children.Add(menu);

        var formatting = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        editorFormatting = formatting;
        Grid.SetColumn(formatting, 1);
        row.Children.Add(formatting);
        var headings = ActionButton(WithChevron(Label("H1", 15, Text)), "Heading styles", 57, null);
        BuildHeadingMenu(headings);
        var bullets = ActionButton(WithChevron(Symbol("M 2,4 H 2.4 M 5,4 H 14 M 2,8 H 2.4 M 5,8 H 14 M 2,12 H 2.4 M 5,12 H 14", 18)), "List styles", 52, null);
        var lists = new ContextMenu();
        AddItem(lists, "Bulleted list", "", ToggleBullet);
        AddItem(lists, "Numbered list", "", delegate { Format(EditingCommands.ToggleNumbering); });
        AddItem(lists, "Checklist", "Ctrl+Shift+L", StartChecklist);
        lists.Items.Add(new Separator());
        var indent = AddItem(lists, "Increase indent", "", delegate { Format(EditingCommands.IncreaseIndentation); });
        var outdent = AddItem(lists, "Decrease indent", "", delegate { Format(EditingCommands.DecreaseIndentation); });
        lists.Opened += delegate
        {
            indent.IsEnabled = EditingCommands.IncreaseIndentation.CanExecute(null, current.TextBox);
            outdent.IsEnabled = EditingCommands.DecreaseIndentation.CanExecute(null, current.TextBox);
        };
        AttachMenu(bullets, lists);
        var checkbox = BuildCheckboxToolbar();
        boldButton = ActionButton(new TextBlock { Text = "B", FontWeight = FontWeights.Bold, FontSize = 19 }, "Bold (Ctrl+B)", 40, delegate { Format(EditingCommands.ToggleBold); });
        italicButton = ActionButton(new TextBlock { Text = "I", FontFamily = new FontFamily("Georgia"), FontStyle = FontStyles.Italic, FontSize = 20 }, "Italic (Ctrl+I)", 40, delegate { Format(EditingCommands.ToggleItalic); });
        strikeButton = ActionButton(Symbol("M 12,3 C 9,0 3,2 4,5 C 4.5,7 7,7.5 9,8 M 5,8 C 8,8 12,8.5 12,11 C 12,15 5,16 3,12 M 1,8 H 15", 17), "Strikethrough", 40, ToggleStrike);
        var link = ActionButton(Symbol("M 6.5,11 H 5 A 3,3 0 0 1 5,5 H 6.5 M 9.5,5 H 11 A 3,3 0 0 1 11,11 H 9.5 M 5.5,8 H 10.5", 18), "Insert link (Ctrl+K)", 38, InsertLink);
        var table = ActionButton(WithChevron(Symbol("M 4,2 H 12 Q 14,2 14,4 V 12 Q 14,14 12,14 H 4 Q 2,14 2,12 V 4 Q 2,2 4,2 Z M 2,8 H 14 M 8,2 V 14", 19)), "Table", 56, null);
        BuildTableMenu(table);
        var clear = ActionButton(Symbol("M 1,11 L 4,3 L 7,11 M 2,8 H 6 M 10,4 V 6 M 8,5 Q 10,3 12,5 M 7,12 L 11,8 L 15,12 L 12,15 H 10 Z M 9,10 L 13,14", 20), "Clear formatting", 38, ClearFormatting);
        foreach (var control in new UIElement[] { headings, bullets, checkbox, boldButton, italicButton, strikeButton, BuildHighlightToolbar(), link, table, clear }) formatting.Children.Add(control);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        drawButton = ActionButton(WithIcon(Pencil(), "Draw"), "Draw on the note (Ctrl+D)", 71, delegate { SetDrawing(!drawing); });
        actions.Children.Add(drawButton);
        actions.Children.Add(BuildImageButton());
        actions.Children.Add(ActionButton(Symbol("M 3,2 H 11 L 14,5 V 14 H 2 V 2 H 3 M 5,2 V 6 H 11 V 2 M 5,14 V 9 H 11 V 14", 17), "Save (Ctrl+S)", 35, delegate { Save(current, false); }));
        var writing = ActionButton(WithChevron(Label("✧", 18, Text)), "Writing tools", 43, null);
        AttachMenu(writing, WritingMenu());
        writingButton = writing;
        actions.Children.Add(writing);
        var settings = ActionButton(Symbol("M 7,1.4 H 9 L 9.5,3.2 L 11,4 L 12.8,3.5 L 14,5.3 L 12.7,6.8 V 8.5 L 14,10 L 12.8,11.8 L 11,11.3 L 9.5,12.1 L 9,14 H 7 L 6.5,12.1 L 5,11.3 L 3.2,11.8 L 2,10 L 3.3,8.5 V 6.8 L 2,5.3 L 3.2,3.5 L 5,4 L 6.5,3.2 Z M 10.2,7.7 A 2.2,2.2 0 1 1 5.8,7.7 A 2.2,2.2 0 1 1 10.2,7.7 Z", 19), "Settings", 36, delegate { OpenSettings(); });
        actions.Children.Add(settings);
    }

    void BuildDrawingBar()
    {
        drawingBar.Background = Surface;
        drawingBar.BorderBrush = Line;
        drawingBar.BorderThickness = new Thickness(0, 0, 0, 1);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 39, Margin = new Thickness(14, 0, 10, 0) };
        drawingBar.Child = row;
        Place(drawingBar, Dock.Top);
        AddInkTool(row, false);
        AddInkTool(row, true);
        row.Children.Add(Divider(7));
        eraserButton = ActionButton(WithIcon(Symbol("M 2,9 L 8,3 Q 9,2 10,3 L 14,7 Q 15,8 14,9 L 9,14 H 6 Z M 5,6 L 12,12 M 9,14 H 15", 16), "Eraser"), "Erase a stroke", 81, delegate { erasing = true; shape = null; ApplyDrawingMode(); });
        shapesButton = ActionButton(null, "Shapes. Hold Shift while dragging to constrain the shape.", 105, null);
        var shapes = new ContextMenu { MinWidth = 164, Padding = new Thickness(3) };
        foreach (ShapeTool tool in Enum.GetValues(typeof(ShapeTool)))
        {
            ShapeTool selected = tool;
            var entry = AddItem(shapes, tool.ToString(), "", delegate { shape = selected; erasing = false; ApplyDrawingMode(); shapeLayer.Focus(); });
            entry.Icon = Symbol(ShapePaths[(int)tool], 16);
            entry.IsCheckable = true;
            shapes.Opened += delegate { entry.IsChecked = shape == selected; };
        }
        AttachMenu(shapesButton, shapes);
        row.Children.Add(eraserButton);
        row.Children.Add(shapesButton);
        row.Children.Add(Divider(7));
        undoInkButton = ActionButton(Symbol("M 6,3 L 2,7 L 6,11 M 2,7 H 10 Q 15,7 14,13", 17), "Undo ink (Ctrl+Z)", 35, delegate { Undo(false); });
        redoInkButton = ActionButton(Symbol("M 10,3 L 14,7 L 10,11 M 14,7 H 6 Q 1,7 2,13", 17), "Redo ink (Ctrl+Y)", 35, delegate { Undo(true); });
        row.Children.Add(undoInkButton);
        row.Children.Add(redoInkButton);
        row.Children.Add(Divider(7));
        row.Children.Add(ActionButton(WithIcon(Symbol("M 3,8 L 6.5,11.5 L 13,5", 15), "Done"), "Return to typing (Esc)", 68, delegate { SetDrawing(false); }));
    }

    void BuildFindBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(13, 0, 12, 0), Height = 41 };
        findBox.Width = 220;
        findBox.Height = 26;
        findBox.VerticalAlignment = VerticalAlignment.Center;
        findBox.Padding = new Thickness(4, 1, 4, 1);
        AutomationProperties.SetName(findBox, "Find text");
        findBox.TextChanged += delegate { findFeedback.Text = ""; };
        findBox.KeyDown += delegate(object sender, KeyEventArgs args) { if (args.Key == Key.Enter) { FindNext(); args.Handled = true; } };
        row.Children.Add(findBox);
        row.Children.Add(ActionButton("Find next", "Find next (Enter)", 81, FindNext));
        row.Children.Add(ActionButton(Symbol("M 4,4 L 12,12 M 12,4 L 4,12", 14), "Close find (Esc)", 32, HideFind));
        findFeedback.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(findFeedback);
        var content = new StackPanel();
        content.Children.Add(row);
        replaceRow = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(13, 0, 12, 5) };
        replaceBox.Width = 220;
        replaceBox.Height = 26;
        replaceBox.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(replaceBox, "Replace with");
        replaceRow.Children.Add(replaceBox);
        replaceRow.Children.Add(ActionButton("Replace", "Replace", 81, delegate { ReplaceText(false); }));
        replaceRow.Children.Add(ActionButton("Replace all", "Replace all", 87, delegate { ReplaceText(true); }));
        content.Children.Add(replaceRow);
        findBar.Child = Rule(content, Surface);
        Place(findBar, Dock.Top);
    }

    void BuildStatusBar()
    {
        var row = new Grid { Height = 33, Margin = new Thickness(17, 0, 8, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(134) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(89) });
        var fields = new[] { position, characters, fileMode, zoomLabel, lineEnding, encodingLabel };
        for (int index = 0; index < fields.Length; index++)
        {
            var border = new Border { Child = fields[index], BorderBrush = Line, BorderThickness = new Thickness(index == 0 ? 0 : 1, 0, 0, 0), Margin = new Thickness(0, 7, 0, 7), Padding = new Thickness(10, 0, 4, 0) };
            Grid.SetColumn(border, index);
            row.Children.Add(border);
        }
        var area = new Grid();
        area.Children.Add(row);
        area.Children.Add(BuildSaveFeedback());
        editorStatus = new Border { Child = area, Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0) };
        Place(editorStatus, Dock.Bottom);
    }

    void AddNote(Note note)
    {
        notes.Add(note);
        ApplyEditorPreferences(note);
        AddMediaEditing(note);
        AddEditorMenu(note);
        note.PreviousInk = note.Ink.Strokes.ToArray();
        note.Ink.Strokes.StrokesChanged += delegate
        {
            if (note.ReplayingInk) return;
            note.UndoInk.Push(note.PreviousInk);
            note.RedoInk.Clear();
            note.PreviousInk = note.Ink.Strokes.ToArray();
            if (note == current) ResizePage();
            MarkDirty(note);
        };
        note.TextBox.TextChanged += delegate
        {
            note.CharacterCount = null;
            if (note == current && !wrap && note.PaperWidth == 0) ResizePage();
            MarkDirty(note);
        };
        note.TextBox.SelectionChanged += delegate { if (note == current) UpdateSelection(); };
        ShowNote(note);
    }

    void ShowNote(Note note)
    {
        if (note == current) return;
        if (current != null)
        {
            if (markdownPreview != null) SetMarkdownPreview(false);
            shapeLayer.Cancel();
            current.ScrollX = pageScroll.HorizontalOffset;
            current.ScrollY = pageScroll.VerticalOffset;
            CloseInkPicker();
            SelectImage(null, null);
            page.Children.Clear();
        }
        current = note;
        UpdateImageBar();
        drawing = erasing = false;
        shape = null;
        page = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, LayoutTransform = pageScale, Background = Paper };
        page.Children.Add(note.TextBox);
        page.Children.Add(note.Ink);
        page.Children.Add(shapeLayer);
        page.Children.Add(imageOverlay);
        page.LayoutUpdated += delegate { if (imageNote == current) PositionImageOverlay(); };
        pageScroll.Content = page;
        ResizePage();
        pageScroll.ScrollToHorizontalOffset(note.ScrollX);
        pageScroll.ScrollToVerticalOffset(note.ScrollY);
        ApplyDrawingMode();
        UpdateTabs();
        UpdateSelection();
        note.TextBox.Focus();
    }

    void UpdateTabs()
    {
        tabs.Children.Clear();
        Border selected = null;
        foreach (var note in notes)
        {
            bool active = note == current;
            var tab = new DockPanel { LastChildFill = true };
            var end = new Grid { Width = 24, Height = 24, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            DockPanel.SetDock(end, Dock.Right);
            tab.Children.Add(end);
            var close = ActionButton(null, "Close " + note.Name, 24, delegate { CloseNote(note); });
            close.Style = InkpadStyle.TabCloseButton;
            close.Height = 24;
            end.Children.Add(close);
            var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Brush("#818181"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            end.Children.Add(dot);
            var caption = Label(note.Name, 13, active ? Text : Muted);
            if (active) caption.FontWeight = FontWeights.SemiBold;
            caption.Margin = new Thickness(9, 0, 1, 0);
            var switchTab = ActionButton(caption, note.Path ?? "Untitled note", double.NaN, delegate { ShowNote(note); });
            AutomationProperties.SetName(switchTab, note.Name);
            AutomationProperties.SetItemStatus(switchTab, note.Dirty ? "Unsaved changes" : "");
            switchTab.HorizontalContentAlignment = HorizontalAlignment.Left;
            switchTab.Height = 28;
            tab.Children.Add(switchTab);
            caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var frame = new Border { Width = Math.Max(76, Math.Min(120, Math.Ceiling(caption.DesiredSize.Width) + 34)), Child = tab, Background = active ? Surface : Chrome,
                BorderBrush = active ? Brush("#D8D8D8") : Chrome, BorderThickness = new Thickness(1, 1, 1, 0), CornerRadius = new CornerRadius(8, 8, 0, 0) };
            Action updateEnd = delegate
            {
                bool showClose = (active && !note.Dirty) || end.IsMouseOver || close.IsKeyboardFocused || (!note.Dirty && frame.IsMouseOver);
                close.Opacity = showClose ? 1 : 0;
                close.IsHitTestVisible = showClose;
                dot.Visibility = note.Dirty && !showClose ? Visibility.Visible : Visibility.Collapsed;
                if (!active) frame.Background = frame.IsMouseOver ? Brush("#EEEEEE") : Chrome;
            };
            frame.MouseEnter += delegate { updateEnd(); };
            frame.MouseLeave += delegate { updateEnd(); };
            end.MouseEnter += delegate { updateEnd(); };
            end.MouseLeave += delegate { updateEnd(); };
            close.IsKeyboardFocusedChanged += delegate { updateEnd(); };
            updateEnd();
            tab.MouseDown += delegate(object sender, MouseButtonEventArgs args) { if (args.ChangedButton == MouseButton.Middle) CloseNote(note); };
            tabs.Children.Add(frame);
            if (active) selected = frame;
        }
        if (selected != null) selected.BringIntoView();
        Title = current == null ? "Inkpad" : current.Name + (current.Dirty ? " •" : "") + " - Inkpad";
    }

    void ResizePage()
    {
        if (page == null) return;
        double scale = zoom / 100.0;
        double viewport = pageScroll.ViewportWidth > 0 && !double.IsInfinity(pageScroll.ViewportWidth) ? pageScroll.ViewportWidth : Math.Max(300, ActualWidth - 2);
        double width = Math.Max(260, viewport / scale);
        if (current.PaperWidth > 0) width = current.PaperWidth;
        else if (!wrap)
        {
            var font = new Typeface(current.TextBox.Document.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            foreach (string line in current.PlainText.Split('\n'))
            {
                var layout = new FormattedText(line.TrimEnd('\r').Replace("\t", "    "), CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, font, current.TextBox.Document.FontSize, Text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                width = Math.Max(width, Math.Min(50000, Math.Ceiling(layout.WidthIncludingTrailingWhitespace) + 48));
            }
        }
        page.Width = width;
        double height = pageScroll.ViewportHeight > 0 && !double.IsInfinity(pageScroll.ViewportHeight) ? pageScroll.ViewportHeight / scale : 0;
        Rect ink = current.Ink.Strokes.GetBounds();
        page.MinHeight = Math.Max(Math.Floor(height), ink.IsEmpty ? 0 : ink.Bottom + 20);
    }

    void SetDrawing(bool enabled)
    {
        if (!enabled) CloseInkPicker();
        if (enabled) SelectImage(null, null);
        drawing = enabled;
        UpdateImageBar();
        if (enabled)
        {
            HideFind();
            erasing = false;
            shape = null;
        }
        ApplyDrawingMode();
        if (enabled) InkpadStyle.AnimateIn(drawingBar, -3, 125);
        FocusDrawingTool();
    }

    void ApplyDrawingMode()
    {
        if (current == null) return;
        var ink = current.Ink;
        bool shaping = drawing && shape.HasValue;
        shapeLayer.Cancel();
        bool marking = drawing && highlighter && !shaping && !erasing;
        bool inkInput = drawing && !shaping && !TextHighlightActive;
        ink.DefaultDrawingAttributes = ToolAttributes(marking);
        shapeLayer.Attributes = ink.DefaultDrawingAttributes;
        if (shaping) shapeLayer.Tool = shape.Value;
        shapeLayer.Visibility = shaping ? Visibility.Visible : Visibility.Collapsed;
        ink.EditingMode = inkInput ? (erasing ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.Ink) : InkCanvasEditingMode.None;
        ink.EditingModeInverted = inkInput ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.None;
        ink.IsHitTestVisible = inkInput;
        ink.Cursor = erasing ? Cursors.Cross : Cursors.Pen;
        UpdateInkToolButtons(shaping);
        drawingBar.Visibility = drawing ? Visibility.Visible : Visibility.Collapsed;
        drawButton.Background = drawing ? PaleBlue : Brushes.Transparent;
        eraserButton.Background = erasing ? PaleBlue : Brushes.Transparent;
        shapesButton.Background = shaping ? PaleBlue : Brushes.Transparent;
        shapesButton.Content = WithChevron(shaping ? WithIcon(Symbol(ShapePaths[(int)shape.Value], 17), shape.Value.ToString()) :
            WithIcon(Symbol("M 3,6 H 10 V 13 H 3 Z M 7,4 A 3.5,3.5 0 1 1 12,8", 17), "Shapes"));
        UpdateStatus();
    }

    void Undo(bool redo)
    {
        if (!drawing || TextHighlightActive)
        {
            if (redo) current.TextBox.Redo();
            else current.TextBox.Undo();
            current.TextBox.Focus();
            return;
        }
        var from = redo ? current.RedoInk : current.UndoInk;
        var to = redo ? current.UndoInk : current.RedoInk;
        if (from.Count == 0) return;
        to.Push(current.PreviousInk);
        current.PreviousInk = from.Pop();
        current.ReplayingInk = true;
        try
        {
            current.Ink.Strokes.Clear();
            foreach (Stroke stroke in current.PreviousInk) current.Ink.Strokes.Add(stroke);
        }
        finally { current.ReplayingInk = false; }
        ResizePage();
        MarkDirty(current);
    }

    void MarkDirty(Note note)
    {
        if (note.HasInk && note.PaperWidth == 0 && note == current && page != null && page.Width > 0)
            note.PaperWidth = page.Width;
        if (!note.Dirty)
        {
            note.Dirty = true;
            UpdateTabs();
        }
        if (note == current) UpdateStatus();
    }

    void UpdateSelection()
    {
        if (current == null) return;
        var selection = current.TextBox.Selection;
        boldButton.Background = Equals(selection.GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold) ? PaleBlue : Brushes.Transparent;
        italicButton.Background = Equals(selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic) ? PaleBlue : Brushes.Transparent;
        var decoration = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        strikeButton.Background = decoration != null && decoration.Any(item => item.Location == TextDecorationLocation.Strikethrough) ? PaleBlue : Brushes.Transparent;
        UpdateStatus();
    }

    void UpdateStatus()
    {
        var editor = current.TextBox;
        string prefix = new TextRange(editor.Document.ContentStart, editor.CaretPosition).Text;
        int lines = prefix.Count(character => character == '\n');
        position.Text = string.Format(CultureInfo.CurrentCulture, "Ln {0}, Col {1}", lines + 1, prefix.Length - prefix.LastIndexOf('\n'));
        if (current.CharacterCount == null) current.CharacterCount = current.PlainText.Replace("\r\n", "\n").Length;
        int count = current.CharacterCount.Value;
        characters.Text = count.ToString("N0", CultureInfo.CurrentCulture) + (count == 1 ? " character" : " characters");
        fileMode.Text = drawing ? (shape.HasValue ? shape.Value.ToString() : erasing ? "Eraser" : highlighter ? (textOnlyHighlight ? "Text highlighter" : "Highlighter") : "Pencil") : current.HasInk ? "Text + ink" : current.Formatted ? "Formatted" : current.Markdown ? "Markdown" : "Plain text";
        bool inkFile = current.NeedsInkFile || (current.Path != null && NoteFiles.IsInkFile(current.Path));
        lineEnding.Text = inkFile ? "Inkpad note" : current.Newline == "\n" ? "Unix (LF)" : current.Newline == "\r" ? "Mac (CR)" : "Windows (CRLF)";
        encodingLabel.Text = inkFile ? "" : current.Encoding.CodePage == 65001 ? "UTF-8" : current.Encoding.WebName.ToUpperInvariant();
        undoInkButton.IsEnabled = TextHighlightActive ? editor.CanUndo : current.UndoInk.Count > 0;
        redoInkButton.IsEnabled = TextHighlightActive ? editor.CanRedo : current.RedoInk.Count > 0;
    }

    void Format(RoutedUICommand command)
    {
        if (drawing) SetDrawing(false);
        current.TextBox.Focus();
        current.Formatted = true;
        command.Execute(null, current.TextBox);
        MarkDirty(current);
        UpdateSelection();
    }

    void ToggleBullet() { Format(EditingCommands.ToggleBullets); }

    void ToggleStrike()
    {
        if (drawing) SetDrawing(false);
        var selection = current.TextBox.Selection;
        var decoration = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        bool enabled = decoration != null && decoration.Any(item => item.Location == TextDecorationLocation.Strikethrough);
        selection.ApplyPropertyValue(Inline.TextDecorationsProperty, enabled ? new TextDecorationCollection() : TextDecorations.Strikethrough);
        current.Formatted = true;
        current.TextBox.Focus();
        MarkDirty(current);
        UpdateSelection();
    }

    void ClearFormatting()
    {
        if (drawing) SetDrawing(false);
        current.TextBox.Selection.ClearAllProperties();
        current.TextBox.Focus();
        MarkDirty(current);
        UpdateSelection();
    }

    void SetZoom(int value)
    {
        zoom = Math.Max(50, Math.Min(250, value));
        pageScale.ScaleX = pageScale.ScaleY = zoom / 100.0;
        zoomLabel.Text = zoom + "%";
        ResizePage();
    }

    void ShowFind()
    {
        if (markdownPreview != null) SetMarkdownPreview(false);
        if (drawing) SetDrawing(false);
        findBar.Visibility = Visibility.Visible;
        InkpadStyle.AnimateIn(findBar, -3, 125);
        string selected = current.TextBox.Selection.Text;
        if (selected.Length > 0 && selected.IndexOfAny(new[] { '\r', '\n' }) < 0) findBox.Text = selected;
        findBox.Focus();
        findBox.SelectAll();
    }

    void HideFind()
    {
        if (findBar.Visibility != Visibility.Visible) return;
        findBar.Visibility = Visibility.Collapsed;
        replaceRow.Visibility = Visibility.Collapsed;
        findFeedback.Text = "";
        if (!drawing) current.TextBox.Focus();
    }

    void FindNext()
    {
        FindInDirection(false);
    }

    internal static TextPointer TextLocation(FlowDocument document, int characters)
    {
        TextPointer origin = document.ContentStart;
        int low = 0, high = origin.GetOffsetToPosition(document.ContentEnd);
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (new TextRange(origin, origin.GetPositionAtOffset(middle)).Text.Length < characters) low = middle + 1;
            else high = middle;
        }
        return origin.GetPositionAtOffset(low).GetInsertionPosition(LogicalDirection.Forward);
    }

    void Open()
    {
        var dialog = new OpenFileDialog { Filter = "Notes and text files|*.inknote;*.txt;*.md;*.log;*.csv;*.json;*.ini|All files|*.*", Multiselect = true, Title = "Open notes" };
        if (dialog.ShowDialog(this) == true) OpenFiles(dialog.FileNames);
    }

    void OpenPath(string path)
    {
        try
        {
            path = System.IO.Path.GetFullPath(path);
            var existing = notes.Find(note => string.Equals(note.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { ShowNote(existing); return; }
            var loaded = new Note();
            NoteFiles.Load(loaded, path);
            if (notes.Count == 1 && current.Path == null && !current.Dirty) notes.Remove(current);
            AddNote(loaded);
            RememberFile(path);
        }
        catch (Exception error) { FileError("open", error); }
    }

    bool Save(Note note, bool askPath)
    {
        return SaveNote(note, askPath, true);
    }

    bool SaveNote(Note note, bool askPath, bool offerAnotherFolder)
    {
        string path = note.Path;
        bool useInk = note.NeedsInkFile || (path != null && NoteFiles.IsInkFile(path));
        if (askPath || path == null || (useInk && !NoteFiles.IsInkFile(path)))
        {
            var dialog = new SaveFileDialog
            {
                Filter = useInk ? "Inkpad note (*.inknote)|*.inknote" : note.Markdown ? "Markdown (*.md)|*.md|Text document (*.txt)|*.txt|Inkpad note (*.inknote)|*.inknote" : "Text document (*.txt)|*.txt|Inkpad note (*.inknote)|*.inknote|Markdown (*.md)|*.md",
                DefaultExt = useInk ? ".inknote" : note.Markdown ? ".md" : ".txt",
                FileName = path == null ? "Untitled" : System.IO.Path.GetFileNameWithoutExtension(path),
                InitialDirectory = path == null ? null : System.IO.Path.GetDirectoryName(path),
                Title = "Save as"
            };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
            if (useInk && !NoteFiles.IsInkFile(path))
            {
                MessageBox.Show(this, "Use the .inknote extension to save the complete note.", "Save note", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
        }
        if (notes.Exists(other => other != note && string.Equals(other.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "That file is open in another tab. Choose a different filename.", "Save note", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        try
        {
            NoteFiles.Save(note, path, false);
            note.Path = path;
            note.Markdown = IsMarkdownPath(path);
            note.Dirty = false;
            RememberFile(path);
            UpdateTabs();
            UpdateStatus();
            ShowSaveFeedback("Saved", path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            const string title = "Windows blocked the save";
            string description = "Inkpad couldn’t write to “" + path + "”. Your edits are still in this tab. You can save a copy in another folder, such as Downloads.";
            if (!offerAnotherFolder)
            {
                ShowNoteDialog(title, description, "Keep editing", null);
                return false;
            }
            var choice = ShowNoteDialog(title, description, "Save as…", "Keep editing", false);
            return choice == MessageBoxResult.Yes && SaveNote(note, true, false);
        }
        catch (Exception error) { FileError("save", error); return false; }
    }

    void ExportText()
    {
        var dialog = new SaveFileDialog { Filter = "Text document (*.txt)|*.txt", DefaultExt = ".txt", FileName = System.IO.Path.GetFileNameWithoutExtension(current.Name), Title = "Export text only" };
        if (dialog.ShowDialog(this) != true) return;
        if (NoteFiles.IsInkFile(dialog.FileName) || notes.Exists(note => string.Equals(note.Path, dialog.FileName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Choose a new text filename for the export.", "Export text", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { NoteFiles.Save(current, dialog.FileName, true); ShowSaveFeedback("Text exported", dialog.FileName); }
        catch (Exception error) { FileError("export", error); }
    }

    void FileError(string action, Exception error)
    {
        ShowNoteDialog("Couldn’t " + action + " the note", error.Message, "OK", null);
    }

    bool CanClose(Note note)
    {
        if (!note.Dirty) return true;
        var choice = ShowNoteDialog("Save changes?", "Save your changes to “" + note.Name + "” before closing.", "Save", "Don’t save");
        return choice == MessageBoxResult.No || (choice == MessageBoxResult.Yes && Save(note, false));
    }

    void CloseNote(Note note)
    {
        if (!CanClose(note)) return;
        int index = notes.IndexOf(note);
        notes.RemoveAt(index);
        if (notes.Count == 0) AddNote(new Note());
        else if (note == current) ShowNote(notes[Math.Min(index, notes.Count - 1)]);
        else UpdateTabs();
    }

    void OnClosing(object sender, CancelEventArgs args)
    {
        foreach (var note in notes)
        {
            if (!CanClose(note)) { args.Cancel = true; return; }
        }
        if (Preferences.Restore)
        {
            Preferences.OpenFiles.Clear();
            Preferences.OpenFiles.AddRange(notes.Where(note => note.Path != null).Select(note => note.Path));
            Preferences.ActiveFile = current.Path;
            Preferences.Save();
        }
        InkpadWindows.SavePlacement(RestoreBounds, wasMaximized, windowPlacementPath);
    }

    void OnShortcut(object sender, KeyEventArgs args)
    {
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (settingsPage != null)
        {
            if (args.Key == Key.Escape) { CloseSettings(); args.Handled = true; }
            return;
        }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && (args.Key == Key.S || (args.Key == Key.System && args.SystemKey == Key.S)))
        { SaveAll(); args.Handled = true; return; }
        if (!drawing && current.TextBox.IsKeyboardFocusWithin && args.Key == Key.Enter &&
            modifiers == ModifierKeys.None && ContinueChecklist(current))
        {
            args.Handled = true;
            return;
        }
        if (!drawing && current.TextBox.IsKeyboardFocusWithin && args.Key == Key.Tab &&
            (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift) &&
            NoteTables.Navigate(current.TextBox, modifiers == ModifierKeys.Shift))
        {
            args.Handled = true;
            return;
        }
        if (args.Key == Key.Escape)
        {
            if (inkPicker != null)
            {
                var owner = inkPickerOwner;
                CloseInkPicker();
                if (drawing) FocusDrawingTool(); else owner.Focus();
                args.Handled = true;
                return;
            }
            if (imageCrop.HasValue) { StopImageCrop(); args.Handled = true; return; }
            if (drawing && shapeLayer.Cancel()) { args.Handled = true; return; }
            if (drawing) SetDrawing(false);
            else if (imageBar.Visibility == Visibility.Visible) SelectImage(null, null);
            else if (findBar.Visibility == Visibility.Visible) HideFind();
            else return;
        }
        else if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == ModifierKeys.Control)
        {
            bool shift = (modifiers & ModifierKeys.Shift) != 0;
            switch (args.Key)
            {
                case Key.Space:
                    if (findBox.IsKeyboardFocusWithin) return;
                    if (shift) InsertCheckbox();
                    else if (!ToggleCheckboxAtCaret()) return;
                    break;
                case Key.N: if (shift) NewWindow(null); else AddNote(new Note()); break;
                case Key.O: Open(); break;
                case Key.S: Save(current, shift); break;
                case Key.W: if (shift) Close(); else CloseNote(current); break;
                case Key.D: SetDrawing(!drawing); break;
                case Key.F: ShowFind(); break;
                case Key.H: ShowReplace(); break;
                case Key.G: GoToLine(); break;
                case Key.E: ShowSpelling(); break;
                case Key.P: Print(); break;
                case Key.Q: if (!Preferences.Writing) return; WriteDraft(); break;
                case Key.R: if (!Preferences.Writing) return; ApplyWritingTool("Rewrite"); break;
                case Key.M: if (!Preferences.Writing) return; ApplyWritingTool("Summarize"); break;
                case Key.Tab: ShowNote(notes[(notes.IndexOf(current) + (shift ? notes.Count - 1 : 1)) % notes.Count]); break;
                case Key.Z: if (findBox.IsKeyboardFocused || !drawing) return; Undo(shift); break;
                case Key.Y: if (findBox.IsKeyboardFocused || !drawing) return; Undo(true); break;
                case Key.B: if (findBox.IsKeyboardFocused) return; Format(EditingCommands.ToggleBold); break;
                case Key.I: if (findBox.IsKeyboardFocused) return; Format(EditingCommands.ToggleItalic); break;
                case Key.K: if (findBox.IsKeyboardFocused) return; InsertLink(); break;
                case Key.L: if (!shift || findBox.IsKeyboardFocusWithin) return; StartChecklist(); break;
                case Key.U: if (findBox.IsKeyboardFocused) return; Format(EditingCommands.ToggleUnderline); break;
                case Key.Add:
                case Key.OemPlus: SetZoom(zoom + 10); break;
                case Key.Subtract:
                case Key.OemMinus: SetZoom(zoom - 10); break;
                case Key.NumPad0:
                case Key.D0: SetZoom(100); break;
                default: return;
            }
        }
        else if (args.Key == Key.F3) FindInDirection(modifiers == ModifierKeys.Shift);
        else if (args.Key == Key.F5 && modifiers == ModifierKeys.None) InsertDate();
        else return;
        args.Handled = true;
    }

    void ToggleMaximized()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    Button ActionButton(object content, string name, double width, Action action)
    {
        var button = new Button { Style = buttonStyle, Content = content, Width = width, Height = 31, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Text, ToolTip = name, BorderThickness = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, name);
        if (action != null) button.Click += delegate { action(); };
        return button;
    }

    void Place(UIElement element, Dock dock) { DockPanel.SetDock(element, dock); shell.Children.Add(element); }

    static void AttachMenu(Button button, ContextMenu menu, Func<bool> showMenu = null, bool headings = false)
    {
        InkpadStyle.Apply(menu, headings);
        button.ContextMenu = menu;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.HorizontalOffset = headings ? -54 : -6;
        menu.VerticalOffset = 0;
        menu.Opened += delegate { button.BorderBrush = Line; };
        menu.Closed += delegate { button.BorderBrush = Brushes.Transparent; };
        button.Click += delegate
        {
            if (showMenu != null && !showMenu()) return;
            if (menu.IsOpen) { menu.IsOpen = false; return; }
            menu.IsOpen = true;
        };
    }

    static MenuItem AddItem(ItemsControl parent, string title, string shortcut, Action action)
    {
        var item = new MenuItem { Header = title, InputGestureText = shortcut };
        item.Click += delegate { action(); };
        parent.Items.Add(item);
        return item;
    }

    static TextBlock Label(string value, double size, Brush color)
    {
        return new TextBlock { Text = value, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    }

    static Border Rule(UIElement child, Brush color)
    {
        return new Border { Child = child, Background = color, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    static Border Divider(double margin)
    {
        return new Border { Height = 23, Width = 1, Background = Line, Margin = new Thickness(margin, 0, margin, 0), VerticalAlignment = VerticalAlignment.Center };
    }

    static StackPanel WithIcon(UIElement icon, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        var text = Label(label, 13, Text);
        text.Margin = new Thickness(7, 0, 0, 0);
        row.Children.Add(text);
        return row;
    }

    static StackPanel WithChevron(UIElement content)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(content);
        var arrow = Symbol("M 3,6 L 8,11 L 13,6", 11);
        arrow.Margin = new Thickness(7, 0, 0, 0);
        row.Children.Add(arrow);
        return row;
    }

    static Viewbox Pencil() { return Symbol("M 2,14 L 3,10 L 11,2 Q 12,1 13,2 L 14,3 Q 15,4 14,5 L 6,13 Z M 9.5,3.5 L 12.5,6.5 M 3,10 L 6,13", 17); }

    static Viewbox Symbol(string geometry, double size, Brush fill = null, Brush stroke = null)
    {
        var grid = new Grid { Width = 16, Height = 16 };
        grid.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Fill = fill, Stroke = stroke ?? fill ?? Text, StrokeThickness = 1.25, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
        return new Viewbox { Width = size, Height = size, Child = grid, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    }

    static bool IsMarkdownPath(string path) { return string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase); }

    static Color ColorOf(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

    static Brush Brush(string hex)
    {
        var color = new SolidColorBrush(ColorOf(hex));
        color.Freeze();
        return color;
    }
}
