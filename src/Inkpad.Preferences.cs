using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;
using Microsoft.Win32;

sealed class InkpadPreferences
{
    const int RecentColorLimit = 4;
    internal string Theme = "System";
    internal string Font = "Consolas";
    internal string FontStyle = "Regular";
    internal double FontSize = 17;
    internal bool Wrap = true, Formatting = true, Recent = true, Status = true, Writing = true;
    internal bool NewWindow, Restore, Spelling, Autocorrect;
    internal readonly List<string> RecentFiles = new List<string>();
    internal readonly List<string> OpenFiles = new List<string>();
    internal readonly List<int> CheckboxColors = new List<int>();
    internal string ActiveFile;
    static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Inkpad", "preferences.xml");

    internal static InkpadPreferences Load(string path = null)
    {
        var value = new InkpadPreferences();
        try
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(path ?? PathName, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 100000 }))
                doc.Load(reader);
            var root = doc.DocumentElement;
            if (root == null || root.Name != "inkpad") return value;
            value.Theme = root.GetAttribute("theme");
            if (value.Theme != "Dark" && value.Theme != "Light") value.Theme = "System";
            if (root.GetAttribute("font").Length > 0) value.Font = root.GetAttribute("font");
            var style = root.GetAttribute("fontStyle");
            if (style == "Bold" || style == "Italic" || style == "Bold Italic") value.FontStyle = style;
            double size;
            if (double.TryParse(root.GetAttribute("fontSize"), NumberStyles.Float, CultureInfo.InvariantCulture, out size) && size >= 8 && size <= 72) value.FontSize = size;
            value.Wrap = Read(root, "wrap", true);
            value.Formatting = Read(root, "formatting", true);
            value.Recent = Read(root, "recent", true);
            value.Status = Read(root, "status", true);
            value.Writing = Read(root, "writing", true);
            value.NewWindow = Read(root, "newWindow", false);
            value.Restore = Read(root, "restore", false);
            value.Spelling = Read(root, "spelling", false);
            value.Autocorrect = Read(root, "autocorrect", false);
            value.ActiveFile = root.GetAttribute("activeFile");
            if (value.Recent) value.RecentFiles.AddRange(root.SelectNodes("recent/file").Cast<XmlNode>().Select(node => node.InnerText).Take(10));
            if (value.Restore) value.OpenFiles.AddRange(root.SelectNodes("open/file").Cast<XmlNode>().Select(node => node.InnerText).Take(60));
            foreach (XmlNode node in root.SelectNodes("colors/color").Cast<XmlNode>())
            {
                if (value.CheckboxColors.Count == RecentColorLimit) break;
                int rgb;
                if (int.TryParse(node.InnerText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb) && rgb >= 0 && rgb <= 0xffffff && !value.CheckboxColors.Contains(rgb)) value.CheckboxColors.Add(rgb);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (XmlException) { }
        return value;
    }

    static bool Read(XmlElement root, string name, bool fallback)
    {
        bool value;
        return bool.TryParse(root.GetAttribute(name), out value) ? value : fallback;
    }

    internal void Save(string path = null)
    {
        path = path ?? PathName;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var xml = XmlWriter.Create(temporary, new XmlWriterSettings { Indent = true }))
            {
                xml.WriteStartElement("inkpad");
                xml.WriteAttributeString("theme", Theme);
                xml.WriteAttributeString("font", Font);
                xml.WriteAttributeString("fontStyle", FontStyle);
                xml.WriteAttributeString("fontSize", FontSize.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("wrap", Wrap.ToString());
                xml.WriteAttributeString("formatting", Formatting.ToString());
                xml.WriteAttributeString("recent", Recent.ToString());
                xml.WriteAttributeString("status", Status.ToString());
                xml.WriteAttributeString("writing", Writing.ToString());
                xml.WriteAttributeString("newWindow", NewWindow.ToString());
                xml.WriteAttributeString("restore", Restore.ToString());
                xml.WriteAttributeString("spelling", Spelling.ToString());
                xml.WriteAttributeString("autocorrect", Autocorrect.ToString());
                if (Restore) xml.WriteAttributeString("activeFile", ActiveFile ?? "");
                WriteFiles(xml, "recent", Recent ? RecentFiles : Enumerable.Empty<string>());
                WriteFiles(xml, "open", Restore ? OpenFiles : Enumerable.Empty<string>());
                xml.WriteStartElement("colors");
                foreach (int rgb in CheckboxColors) xml.WriteElementString("color", rgb.ToString("X6"));
                xml.WriteEndElement();
                xml.WriteEndElement();
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal void RememberColor(Color color)
    {
        int rgb = color.R << 16 | color.G << 8 | color.B;
        CheckboxColors.Remove(rgb);
        CheckboxColors.Insert(0, rgb);
        if (CheckboxColors.Count > RecentColorLimit) CheckboxColors.RemoveAt(RecentColorLimit);
    }

    static void WriteFiles(XmlWriter xml, string name, IEnumerable<string> paths)
    {
        xml.WriteStartElement(name);
        foreach (string path in paths) xml.WriteElementString("file", path);
        xml.WriteEndElement();
    }
}

sealed partial class Inkpad
{
    static readonly InkpadPreferences Preferences = InkpadPreferences.Load();
    static readonly SolidColorBrush SettingsBackground = new SolidColorBrush();
    static readonly SolidColorBrush SettingsCard = new SolidColorBrush();
    static readonly SolidColorBrush SettingsField = new SolidColorBrush();
    Grid workspace;
    ScrollViewer settingsPage;
    Button settingsBack;
    DockPanel titleBar, titleTabs;
    TextBlock settingsTitle;
    FrameworkElement editorFormatting, editorStatus, writingButton;

    void OpenSettings(bool focusFont = false)
    {
        if (drawing) SetDrawing(false);
        if (settingsPage != null) CloseSettings(false);
        var grid = new Grid { Margin = new Thickness(42, 28, 39, 34) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(234) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = Label("Settings", 36, Text);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 0, 0, 19);
        grid.Children.Add(title);
        var sections = new StackPanel();
        Grid.SetRow(sections, 1);
        grid.Children.Add(sections);
        var about = new StackPanel { Margin = new Thickness(32, 14, 0, 0) };
        Grid.SetColumn(about, 1);
        Grid.SetRow(about, 1);
        grid.Children.Add(about);
        var aboutTitle = Label("About this app", 15, Text);
        aboutTitle.FontWeight = FontWeights.SemiBold;
        about.Children.Add(aboutTitle);
        about.Children.Add(new TextBlock { Text = "Inkpad " + typeof(Inkpad).Assembly.GetName().Version.ToString(3), Foreground = Text, Margin = new Thickness(0, 9, 0, 0) });
        about.Children.Add(new TextBlock { Text = "Free, offline Windows editor", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 14) });
        about.Children.Add(SettingsButton("Help", delegate
        {
            ShowNoteDialog("Inkpad help", "Ctrl+N creates a tab. Ctrl+O opens a local file. Ctrl+S saves it. Use Draw to sketch, or insert a checkbox from the toolbar. Save as .inknote to preserve drawings, tables, and rich formatting.", "OK", null);
        }));

        Section(sections, "Appearance");
        var theme = SettingChoice("App theme", new[] { "Light", "Dark", "System" }, Preferences.Theme,
            value => { Preferences.Theme = value; PreferencesChanged(); });
        SettingExpander(sections, "App theme", "Select which app theme to display", "◐", theme, false);

        Section(sections, "Text formatting");
        var fonts = Fonts.SystemFontFamilies.Select(font => font.Source).OrderBy(name => name).ToArray();
        var fontPanel = new StackPanel();
        fontPanel.Children.Add(SettingLine("Family", SettingChoice("Font family", fonts, Preferences.Font,
            value => { Preferences.Font = value; PreferencesChanged(); })));
        fontPanel.Children.Add(SettingLine("Style", SettingChoice("Font style", new[] { "Regular", "Bold", "Italic", "Bold Italic" }, Preferences.FontStyle,
            value => { Preferences.FontStyle = value; PreferencesChanged(); })));
        var size = new TextBox { Text = Preferences.FontSize.ToString(CultureInfo.CurrentCulture), Width = 165, Padding = new Thickness(8, 4, 8, 4) };
        AutomationProperties.SetName(size, "Font size");
        Action updateSize = delegate
        {
            double value;
            if (double.TryParse(size.Text, out value) && value >= 8 && value <= 72)
            { Preferences.FontSize = value; PreferencesChanged(); }
            else size.Text = Preferences.FontSize.ToString(CultureInfo.CurrentCulture);
        };
        size.LostKeyboardFocus += delegate { updateSize(); };
        size.KeyDown += delegate(object sender, KeyEventArgs args) { if (args.Key == Key.Enter) { updateSize(); args.Handled = true; } };
        fontPanel.Children.Add(SettingLine("Size", size));
        var fontHeader = SettingExpander(sections, "Font", "", "Aa", fontPanel, focusFont);
        SettingCard(sections, "Word wrap", "Fit text within the window by default", "↵", SettingSwitch("Word wrap", Preferences.Wrap, SetWrap));
        SettingCard(sections, "Formatting", "Show the formatting toolbar", "A", SettingSwitch("Formatting", Preferences.Formatting, value => { Preferences.Formatting = value; PreferencesChanged(); }));

        Section(sections, "Opening Inkpad");
        SettingCard(sections, "Opening files", "Choose where your files are opened", "↗", SettingChoice("Opening files",
            new[] { "Open in a new tab", "Open in a new window" }, Preferences.NewWindow ? "Open in a new window" : "Open in a new tab",
            value => { Preferences.NewWindow = value == "Open in a new window"; PreferencesChanged(); }));
        SettingExpander(sections, "When Inkpad starts", "Choose which tabs to open", "▣", SettingChoice("When Inkpad starts",
            new[] { "Start with a new tab", "Reopen saved tabs" }, Preferences.Restore ? "Reopen saved tabs" : "Start with a new tab",
            value => { Preferences.Restore = value == "Reopen saved tabs"; PreferencesChanged(); }), false);
        SettingCard(sections, "Recent files", "Show recently opened and saved files in the File menu", "◷", SettingSwitch("Recent files", Preferences.Recent,
            value => { Preferences.Recent = value; if (!value) Preferences.RecentFiles.Clear(); PreferencesChanged(); }));

        Section(sections, "Spelling");
        SettingCard(sections, "Spell check", "Underline misspellings using the local Windows dictionary", "✓", SettingSwitch("Spell check", Preferences.Spelling,
            value => { Preferences.Spelling = value; PreferencesChanged(); }));
        SettingCard(sections, "Autocorrect", "Correct common typos when spell check is on", "A✓", SettingSwitch("Autocorrect", Preferences.Autocorrect,
            value => { Preferences.Autocorrect = value; PreferencesChanged(); }));

        Section(sections, "Advanced features");
        SettingCard(sections, "Writing tools", "Show tools that run locally on this device", "✧", SettingSwitch("Writing tools", Preferences.Writing,
            value => { Preferences.Writing = value; PreferencesChanged(); }));
#if !STORE
        var explorer = SettingSwitch("Edit in File Explorer", InkpadWindows.ExplorerMenuEnabled(), delegate { ToggleExplorerMenu(); });
        SettingCard(sections, "Edit in File Explorer", "Add an Inkpad command to the classic right-click menu", "↗", explorer);
#endif
        settingsPage = new ScrollViewer { Content = grid, Background = SettingsBackground, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        shell.Visibility = Visibility.Collapsed;
        workspace.Children.Add(settingsPage);
        settingsBack.Visibility = Visibility.Visible;
        settingsTitle.Visibility = Visibility.Visible;
        titleTabs.Visibility = Visibility.Collapsed;
        titleBar.Background = SettingsBackground;
        if (focusFont) Dispatcher.BeginInvoke(new Action(delegate { fontHeader.BringIntoView(); fontHeader.Focus(); }));
        else settingsBack.Focus();
    }

    void CloseSettings(bool restoreFocus = true)
    {
        if (settingsPage == null) return;
        workspace.Children.Remove(settingsPage);
        settingsPage = null;
        shell.Visibility = Visibility.Visible;
        settingsBack.Visibility = Visibility.Collapsed;
        settingsTitle.Visibility = Visibility.Collapsed;
        titleTabs.Visibility = Visibility.Visible;
        titleBar.Background = Chrome;
        if (restoreFocus) current.TextBox.Focus();
    }

    static void Section(Panel panel, string name)
    {
        var text = Label(name, 14, Text);
        text.Margin = new Thickness(0, 12, 0, 10);
        panel.Children.Add(text);
    }

    Border SettingCard(Panel panel, string name, string description, string icon, UIElement option)
    {
        var card = new Border { Background = SettingsCard, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 0, 5) };
        card.Child = SettingRow(name, description, icon, option);
        panel.Children.Add(card);
        return card;
    }

    Button SettingExpander(Panel panel, string name, string description, string icon, UIElement option, bool expanded)
    {
        var body = new StackPanel();
        var header = ActionButton(SettingRow(name, description, icon, Symbol("M 4,6 L 8,10 L 12,6", 14)), name, double.NaN, null);
        header.Height = double.NaN;
        header.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        body.Children.Add(header);
        var details = new Border { Child = option, Padding = new Thickness(16, 8, 15, 12), BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Visibility = expanded ? Visibility.Visible : Visibility.Collapsed };
        body.Children.Add(details);
        header.Click += delegate { details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; };
        panel.Children.Add(new Border { Child = body, Background = SettingsCard, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 0, 5) });
        return header;
    }

    static DockPanel SettingRow(string name, string description, string icon, UIElement option)
    {
        var row = new DockPanel { MinHeight = 66, Margin = new Thickness(15, 0, 14, 0) };
        var mark = Label(icon, 17, Text);
        mark.Width = 34;
        DockPanel.SetDock(mark, Dock.Left);
        row.Children.Add(mark);
        if (option != null) { DockPanel.SetDock(option, Dock.Right); row.Children.Add(option); }
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 10, 3) };
        copy.Children.Add(Label(name, 15, Text));
        if (description.Length > 0) copy.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(copy);
        return row;
    }

    static FrameworkElement SettingLine(string name, FrameworkElement choice)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), MinHeight = 30 };
        DockPanel.SetDock(choice, Dock.Right);
        row.Children.Add(choice);
        row.Children.Add(Label(name, 13, Text));
        return row;
    }

    ComboBox SettingChoice(string name, IEnumerable<string> choices, string value, Action<string> changed)
    {
        var box = new ComboBox { ItemsSource = choices, SelectedItem = value, MinWidth = 165, Height = 31, VerticalAlignment = VerticalAlignment.Center,
            Template = ChoiceTemplate, Foreground = Text, Background = SettingsField, BorderBrush = Line, MaxDropDownHeight = 300 };
        AutomationProperties.SetName(box, name);
        box.SelectionChanged += delegate { if (box.SelectedItem is string) changed((string)box.SelectedItem); };
        return box;
    }

    static readonly ControlTemplate ChoiceTemplate = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'>
      <Grid><ToggleButton IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}' Focusable='False' ClickMode='Press'>
      <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='{Binding Background, RelativeSource={RelativeSource AncestorType=ComboBox}}' BorderBrush='{Binding BorderBrush, RelativeSource={RelativeSource AncestorType=ComboBox}}' BorderThickness='1' CornerRadius='5'><Path HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,11,0' Data='M 0,0 L 4,4 L 8,0' Stroke='#777777' StrokeThickness='1.2'/></Border></ControlTemplate></ToggleButton.Template></ToggleButton>
      <ContentPresenter IsHitTestVisible='False' Margin='10,0,26,0' VerticalAlignment='Center' Content='{TemplateBinding SelectionBoxItem}'/>
      <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
      <Border MinWidth='{TemplateBinding ActualWidth}' MaxHeight='{TemplateBinding MaxDropDownHeight}' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='5' Padding='3'><ScrollViewer><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate>");

    static readonly ControlTemplate SwitchTemplate = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ToggleButton'>
      <StackPanel Orientation='Horizontal'><Border x:Name='Track' Width='40' Height='20' CornerRadius='10' Background='#777777'><Ellipse x:Name='Thumb' Width='12' Height='12' Fill='White' Margin='4,0' HorizontalAlignment='Left'/></Border><TextBlock x:Name='State' Margin='10,0,0,0' Width='22' Text='Off' VerticalAlignment='Center'/></StackPanel>
      <ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Track' Property='Background' Value='#0067B8'/><Setter TargetName='Thumb' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='State' Property='Text' Value='On'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");

    static ToggleButton SettingSwitch(string name, bool enabled, Action<bool> changed)
    {
        var control = new ToggleButton { Template = SwitchTemplate, IsChecked = enabled, Foreground = Text, Height = 26, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(control, name);
        control.Click += delegate { changed(control.IsChecked == true); };
        return control;
    }

    Button SettingsButton(string title, Action action)
    {
        var button = ActionButton(title, title, 120, action);
        button.Background = SettingsField;
        button.BorderBrush = Line;
        button.Style = InkpadStyle.DialogButton;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        return button;
    }

    void SetWrap(bool value) { Preferences.Wrap = value; PreferencesChanged(); }

    void PreferencesChanged()
    {
        Preferences.Save();
        foreach (var window in Application.Current.Windows.OfType<Inkpad>()) window.ApplyPreferences();
    }

    void ApplyPreferences()
    {
        ApplyTheme();
        wrap = Preferences.Wrap;
        if (editorFormatting != null) editorFormatting.Visibility = Preferences.Formatting ? Visibility.Visible : Visibility.Collapsed;
        if (editorStatus != null) editorStatus.Visibility = Preferences.Status ? Visibility.Visible : Visibility.Collapsed;
        if (writingButton != null) writingButton.Visibility = Preferences.Writing ? Visibility.Visible : Visibility.Collapsed;
        foreach (var note in notes) ApplyEditorPreferences(note);
        ResizePage();
    }

    static void ApplyEditorPreferences(Note note)
    {
        var editor = note.TextBox;
        var document = editor.Document;
        document.FontFamily = new FontFamily(Preferences.Font);
        document.FontSize = Preferences.FontSize;
        document.FontWeight = Preferences.FontStyle.Contains("Bold") ? FontWeights.Bold : FontWeights.Normal;
        document.FontStyle = Preferences.FontStyle.Contains("Italic") ? FontStyles.Italic : FontStyles.Normal;
        editor.SpellCheck.IsEnabled = Preferences.Spelling;
    }

    static void ApplyTheme()
    {
        bool dark = Preferences.Theme == "Dark";
        if (Preferences.Theme == "System")
        {
            try { dark = Equals(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1), 0); }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }
        SetThemeBrush(Chrome, dark, "#202020", "#E7E7E7");
        SetThemeBrush(Surface, dark, "#282828", "#F6F6F6");
        SetThemeBrush(Paper, dark, "#202020", "#F6F6F6");
        SetThemeBrush(Line, dark, "#454545", "#E4E4E4");
        SetThemeBrush(Text, dark, "#F2F2F2", "#191919");
        SetThemeBrush(Muted, dark, "#BBBBBB", "#5C5C5C");
        SetThemeBrush(PaleBlue, dark, "#29455F", "#DCEBFA");
        SetThemeBrush(SettingsBackground, dark, "#202020", "#F0F0F0");
        SetThemeBrush(SettingsCard, dark, "#2B2B2B", "#F8F8F8");
        SetThemeBrush(SettingsField, dark, "#353535", "#FCFCFC");
        InkpadStyle.ApplyTheme(dark);
    }

    static void SetThemeBrush(Brush brush, bool dark, string darkValue, string lightValue)
    {
        ((SolidColorBrush)brush).Color = ColorOf(dark ? darkValue : lightValue);
    }
}
