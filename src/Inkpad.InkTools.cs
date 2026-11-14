using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

sealed partial class Inkpad
{
    const string MarkerIcon = "M 3,2 H 13 V 5 L 11,8 H 5 L 3,5 Z M 6,8 V 13 L 10,11 V 8 M 2,15 H 14";
    static readonly string[] PencilPalette = {
        "#000000", "#555759", "#808284", "#A9ABAC", "#D0D2D3", "#FFFFFF",
        "#B51062", "#E41319", "#FF5500", "#FFAA00", "#FFCD00", "#FFE900",
        "#A4E51C", "#24D900", "#007957", "#00AAC6", "#0649DB", "#3705B5",
        "#6900B9", "#650080", "#F8DAC9", "#BD946A", "#88522E", "#5D3B2E",
        "#F780F1", "#FFC980", "#FFFF86", "#82FF9F", "#82D5F5", "#BBAFFA"
    };
    static readonly string[] MarkerPalette = { "#FFEB3B", "#70EB4F", "#7DDAF2", "#F481C2", "#F34A4A" };
    static readonly ControlTemplate InkThicknessTemplate = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>
        <Grid Margin='4,0' Height='24'>
            <Track x:Name='PART_Track'>
                <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False' Background='#858585'>
                    <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Grid Background='Transparent'><Border Background='{TemplateBinding Background}' Height='4' CornerRadius='2' /></Grid></ControlTemplate></RepeatButton.Template>
                </RepeatButton></Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False' Background='#BABABA'>
                    <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Grid Background='Transparent'><Border Background='{TemplateBinding Background}' Height='4' CornerRadius='2' /></Grid></ControlTemplate></RepeatButton.Template>
                </RepeatButton></Track.IncreaseRepeatButton>
                <Track.Thumb><Thumb Width='17' Height='17' Cursor='Hand'>
                    <Thumb.Template><ControlTemplate TargetType='Thumb'><Ellipse Fill='{Binding Foreground, RelativeSource={RelativeSource AncestorType=Slider}}' Stroke='#999999' StrokeThickness='.5' /></ControlTemplate></Thumb.Template>
                </Thumb></Track.Thumb>
            </Track>
        </Grid></ControlTemplate>");
    Color pencilColor = ColorOf("#202020");
    readonly SolidColorBrush markerIndicator = new SolidColorBrush(ColorOf("#FFEB3B"));
    Color markerColor { get { return markerIndicator.Color; } set { markerIndicator.Color = value; } }
    double pencilWidth = 2.8, markerWidth = 18;
    bool highlighter, textOnlyHighlight;
    Button markerButton, pencilOptions, markerOptions;
    Popup inkPicker;
    Button inkPickerOwner;
    TextPointer highlightAnchor;

    bool TextHighlightActive
    {
        get { return drawing && highlighter && textOnlyHighlight && !erasing && !shape.HasValue; }
    }

    DrawingAttributes ToolAttributes(bool marker)
    {
        return new DrawingAttributes
        {
            Color = marker ? markerColor : pencilColor,
            Width = marker ? Math.Max(2, markerWidth * .45) : pencilWidth,
            Height = marker ? markerWidth : pencilWidth,
            StylusTip = marker ? StylusTip.Rectangle : StylusTip.Ellipse,
            IsHighlighter = marker, IgnorePressure = marker, FitToCurve = true
        };
    }

    void AddInkTool(StackPanel row, bool marker)
    {
        string name = marker ? "Highlighter" : "Pencil";
        var tool = ActionButton(null, name, marker ? 105 : 77, delegate
        {
            CloseInkPicker();
            ActivateInkTool(marker);
            FocusDrawingTool();
        });
        var options = ActionButton(Symbol("M 4,6 L 8,10 L 12,6", 13), name + " colors and thickness", 27, null);
        options.Click += delegate { OpenInkPicker(options, marker, false); };
        row.Children.Add(tool);
        row.Children.Add(options);
        if (marker) { markerButton = tool; markerOptions = options; }
        else { penButton = tool; pencilOptions = options; }
    }

    void ActivateInkTool(bool marker)
    {
        highlighter = marker;
        erasing = false;
        shape = null;
        ApplyDrawingMode();
    }

    void FocusDrawingTool()
    {
        if (!drawing || TextHighlightActive) current.TextBox.Focus();
        else if (shape.HasValue) shapeLayer.Focus();
        else current.Ink.Focus();
    }

    void UpdateInkToolButtons(bool shaping)
    {
        bool mark = highlighter && !erasing && !shaping;
        bool pen = !highlighter && !erasing && !shaping;
        penButton.Content = WithIcon(Symbol("M 4,2 H 12 V 6 L 9,10 V 13 L 7,15 V 10 L 4,6 Z M 4,5 H 12", 18), "Pencil");
        ((StackPanel)penButton.Content).Children.Add(new Ellipse { Width = 6, Height = 6, Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(pencilColor), Stroke = Muted, StrokeThickness = .5 });
        markerButton.Content = WithIcon(Symbol(MarkerIcon, 18, Text), "Highlighter");
        ((StackPanel)markerButton.Content).Children.Add(new Ellipse { Width = 6, Height = 6, Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Fill = markerIndicator });
        penButton.Background = pen ? PaleBlue : Brushes.Transparent;
        markerButton.Background = mark ? PaleBlue : Brushes.Transparent;
        pencilOptions.Background = pen ? PaleBlue : Brushes.Transparent;
        markerOptions.Background = mark ? PaleBlue : Brushes.Transparent;
    }

    void CloseInkPicker()
    {
        if (inkPicker != null) inkPicker.IsOpen = false;
    }

    void OpenInkPicker(Button owner, bool marker, bool formatText)
    {
        bool same = inkPicker != null && inkPickerOwner == owner;
        CloseInkPicker();
        if (same) { if (!formatText) FocusDrawingTool(); return; }
        if (!formatText) ActivateInkTool(marker);
        var popup = new Popup { PlacementTarget = owner, Placement = PlacementMode.Bottom, HorizontalOffset = -8,
            AllowsTransparency = true, StaysOpen = false };
        inkPicker = popup;
        inkPickerOwner = owner;
        var panel = new StackPanel { Width = marker ? 226 : 272, Margin = new Thickness(15, 12, 15, 12) };
        var colors = Label("Colors", 15, Text);
        colors.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(colors);
        var palette = new UniformGrid { Columns = marker ? 5 : 6, Margin = new Thickness(-3, 6, -3, 0) };
        var samples = new List<Tuple<Color, Border>>();
        panel.Children.Add(palette);

        Path stroke = null;
        if (!formatText)
        {
            var preview = new Viewbox { Height = 55, Margin = new Thickness(3, 4, 3, 5), Stretch = Stretch.Fill, IsHitTestVisible = false };
            var track = new Canvas { Width = 230, Height = 55 };
            stroke = new Path { Data = Geometry.Parse("M 14,34 C 52,4 75,8 113,26 C 152,46 179,42 216,16"),
                StrokeStartLineCap = marker ? PenLineCap.Flat : PenLineCap.Round,
                StrokeEndLineCap = marker ? PenLineCap.Flat : PenLineCap.Round };
            track.Children.Add(stroke);
            preview.Child = track;
            panel.Children.Add(preview);
        }

        Action update = delegate
        {
            Color chosen = marker ? markerColor : pencilColor;
            foreach (var sample in samples) sample.Item2.BorderBrush = sample.Item1 == chosen ? Brush("#0078D4") : Brushes.Transparent;
            if (stroke != null)
            {
                stroke.Stroke = new SolidColorBrush(chosen);
                stroke.Opacity = marker ? .58 : 1;
                stroke.StrokeThickness = marker ? markerWidth : pencilWidth;
            }
        };
        Action<Color> choose = delegate(Color color)
        {
            if (marker) markerColor = color;
            else pencilColor = color;
            if (formatText)
            {
                popup.IsOpen = false;
                if (drawing) ApplyDrawingMode();
                FocusDrawingTool();
            }
            else
            {
                ApplyDrawingMode();
                update();
            }
        };
        Button selectedSwatch = null;
        foreach (var hex in marker ? MarkerPalette : PencilPalette)
        {
            Color color = ColorOf(hex);
            var outer = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(2),
                Child = new Border { Margin = new Thickness(2), CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(color), BorderBrush = Brush("#55000000"), BorderThickness = new Thickness(1) } };
            samples.Add(Tuple.Create(color, outer));
            var chip = ActionButton(outer, "Color " + hex, 37, delegate { choose(color); });
            chip.Height = 37;
            palette.Children.Add(chip);
            if (color == (marker ? markerColor : pencilColor)) selectedSwatch = chip;
        }
        var custom = ActionButton("More colors…", "Choose a custom color", 106, delegate
        {
            popup.IsOpen = false;
            var color = PickColor(marker ? markerColor : pencilColor);
            if (color.HasValue) choose(color.Value);
            else if (formatText) current.TextBox.Focus();
            if (!formatText) FocusDrawingTool();
        });
        custom.HorizontalAlignment = formatText ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        if (formatText)
        {
            custom.Width = double.NaN;
            custom.HorizontalContentAlignment = HorizontalAlignment.Left;
            custom.Padding = new Thickness(8, 0, 0, 0);
            custom.Margin = new Thickness(0, 7, 0, 0);
        }

        if (!formatText)
        {
            var thickness = Label("Thickness", 15, Text);
            thickness.FontWeight = FontWeights.SemiBold;
            panel.Children.Add(thickness);
            var slider = new Slider { Minimum = marker ? 5 : 1, Maximum = marker ? 34 : 14,
                Value = marker ? markerWidth : pencilWidth, SmallChange = .5, LargeChange = 2,
                Margin = new Thickness(0, 8, 0, 0), IsMoveToPointEnabled = true, Template = InkThicknessTemplate,
                Foreground = Text, AutoToolTipPlacement = AutoToolTipPlacement.TopLeft, AutoToolTipPrecision = 1 };
            AutomationProperties.SetName(slider, (marker ? "Highlighter" : "Pencil") + " thickness");
            slider.ValueChanged += delegate
            {
                if (marker) markerWidth = slider.Value;
                else pencilWidth = slider.Value;
                ApplyDrawingMode();
                update();
            };
            panel.Children.Add(slider);
            var bounds = new DockPanel { Margin = new Thickness(0, 1, 0, 5) };
            var thick = Label("Thick", 11, Muted);
            DockPanel.SetDock(thick, Dock.Right);
            bounds.Children.Add(thick);
            bounds.Children.Add(Label("Thin", 11, Muted));
            panel.Children.Add(bounds);
        }
        panel.Children.Add(custom);
        if (formatText)
        {
            var remove = ActionButton("Remove highlight", "Remove highlight", double.NaN, delegate
            {
                popup.IsOpen = false;
                HighlightText(Brushes.Transparent);
            });
            remove.HorizontalContentAlignment = HorizontalAlignment.Left;
            remove.Padding = new Thickness(8, 0, 0, 0);
            panel.Children.Add(remove);
        }
        else if (marker)
        {
            var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var toggle = SettingSwitch("Text only highlight", textOnlyHighlight, delegate(bool enabled)
            { textOnlyHighlight = enabled; ApplyDrawingMode(); });
            toggle.Cursor = Cursors.Hand;
            DockPanel.SetDock(toggle, Dock.Right);
            footer.Children.Add(toggle);
            footer.Children.Add(Label("Text only highlight", 13, Text));
            panel.Children.Add(new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 5, 0, 0), Padding = new Thickness(0, 3, 0, 0), Child = footer });
        }

        var frame = new Border { Child = panel, Background = InkpadStyle.PopupBackground, BorderBrush = InkpadStyle.PopupBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(4, 2, 12, 12),
            Effect = new DropShadowEffect { BlurRadius = 15, ShadowDepth = 3, Opacity = .22 } };
        popup.Child = frame;
        frame.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
        {
            if (args.Key != Key.Escape) return;
            popup.IsOpen = false;
            if (formatText) owner.Focus(); else FocusDrawingTool();
            args.Handled = true;
        };
        popup.Opened += delegate
        {
            InkpadStyle.AnimateIn(frame, -3);
            if (Mouse.LeftButton == MouseButtonState.Released) (selectedSwatch ?? (Button)palette.Children[0]).Focus();
        };
        popup.Closed += delegate
        {
            if (inkPicker == popup) { inkPicker = null; inkPickerOwner = null; }
            if (!formatText && frame.IsKeyboardFocusWithin) FocusDrawingTool();
        };
        update();
        popup.IsOpen = true;
    }

    static Brush TextHighlightBrush(Color color)
    {
        return new SolidColorBrush(Color.FromArgb(126, color.R, color.G, color.B));
    }

    bool BeginTextHighlight(Note note, Point point)
    {
        if (note != current || !TextHighlightActive) return false;
        var editor = note.TextBox;
        editor.Focus();
        highlightAnchor = editor.GetPositionFromPoint(point, true);
        if (highlightAnchor != null && editor.CaptureMouse()) editor.Selection.Select(highlightAnchor, highlightAnchor);
        else highlightAnchor = null;
        return true;
    }

    void DragTextHighlight(Note note, Point point)
    {
        if (highlightAnchor == null || note != current || !TextHighlightActive || !note.TextBox.IsMouseCaptured) return;
        var at = note.TextBox.GetPositionFromPoint(point, true);
        if (at != null) note.TextBox.Selection.Select(highlightAnchor, at);
    }

    bool EndTextHighlight(Note note, Point point)
    {
        if (highlightAnchor == null || note != current) return false;
        DragTextHighlight(note, point);
        highlightAnchor = null;
        note.TextBox.ReleaseMouseCapture();
        PaintSelectedText(note);
        return true;
    }

    void PaintSelectedText(Note note)
    {
        var selection = note.TextBox.Selection;
        if (!selection.IsEmpty)
        {
            var end = selection.End;
            ApplyTextHighlight(note, selection.Start, end, TextHighlightBrush(markerColor));
            note.TextBox.Selection.Select(end, end);
        }
    }
}
