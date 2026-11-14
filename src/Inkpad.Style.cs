using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

static class InkpadStyle
{
    const string Namespace = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";
    static readonly string[] Light = { "#1B1B1B", "#686868", "#777777", "#EEEEEE", "#F9F9F9", "#DBDBDB", "#E4E4E4", "#E9E9E9", "#191919", "#272727" };
    static readonly string[] Dark = { "#F0F0F0", "#B9B9B9", "#B9B9B9", "#3D3D3D", "#292929", "#4B4B4B", "#444444", "#3D3D3D", "#F0F0F0", "#F0F0F0" };

    static object Parse(string xaml)
    {
        for (int i = 0; i < Light.Length; i++) xaml = xaml.Replace("'" + Light[i] + "'", "'{DynamicResource InkpadBrush" + i + "}'");
        return XamlReader.Parse(xaml);
    }

    internal static void ApplyTheme(bool dark)
    {
        var resources = Application.Current.Resources;
        for (int i = 0; i < Light.Length; i++)
        {
            string name = "InkpadBrush" + i;
            var color = (Color)ColorConverter.ConvertFromString(dark ? Dark[i] : Light[i]);
            var previous = resources[name] as SolidColorBrush;
            if (previous != null && previous.Color == color) continue;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[name] = brush;
        }
        PopupBackground.Color = (Color)ColorConverter.ConvertFromString(dark ? Dark[4] : "#FCFCFC");
        PopupBorder.Color = (Color)ColorConverter.ConvertFromString(dark ? Dark[5] : "#DBDBDB");
    }

    static readonly Style MenuItems = (Style)Parse("<Style " + Namespace + @" TargetType='MenuItem'>
      <Setter Property='Foreground' Value='#1B1B1B'/><Setter Property='FontSize' Value='13.5'/>
      <Setter Property='Height' Value='32'/><Setter Property='MinWidth' Value='158'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
        <Border x:Name='row' Margin='0,1' Padding='8,0,9,0' CornerRadius='4' Background='Transparent'>
          <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='24'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
            <ContentPresenter x:Name='icon' ContentSource='Icon' VerticalAlignment='Center' HorizontalAlignment='Left'/>
            <Path x:Name='check' Visibility='Collapsed' Data='M 1,6 L 4,9 L 10,2' Stroke='#1765A7' StrokeThickness='1.6' StrokeLineJoin='Round' VerticalAlignment='Center'/>
            <ContentPresenter Grid.Column='1' ContentSource='Header' VerticalAlignment='Center' RecognizesAccessKey='True'/>
            <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' Foreground='#686868' Margin='15,0,0,0' VerticalAlignment='Center'/>
            <Path x:Name='next' Grid.Column='2' Visibility='Collapsed' Data='M 0,0 L 4,4 L 0,8' Stroke='#777777' StrokeThickness='1.1' Margin='14,0,3,0' VerticalAlignment='Center'/>
            <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-3' PlacementTarget='{Binding ElementName=row}' IsOpen='{TemplateBinding IsSubmenuOpen}' AllowsTransparency='True' Focusable='False'>
              <Border Background='#F9F9F9' BorderBrush='#DBDBDB' BorderThickness='1' CornerRadius='8' Padding='4' Margin='0,1,10,12'>
                <Border.Effect><DropShadowEffect BlurRadius='10' ShadowDepth='2' Opacity='.16'/></Border.Effect>
                <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
              </Border>
            </Popup>
          </Grid>
        </Border><ControlTemplate.Triggers>
          <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='row' Property='Background' Value='#EEEEEE'/></Trigger>
          <Trigger Property='IsChecked' Value='True'><Setter TargetName='check' Property='Visibility' Value='Visible'/><Setter TargetName='icon' Property='Visibility' Value='Hidden'/></Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter TargetName='row' Property='Opacity' Value='0.42'/></Trigger>
          <Trigger Property='HasItems' Value='True'><Setter TargetName='next' Property='Visibility' Value='Visible'/></Trigger>
        </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

    static readonly Style HeadingItems = (Style)Parse("<Style " + Namespace + @" TargetType='MenuItem'>
      <Setter Property='Foreground' Value='#1B1B1B'/><Setter Property='MinWidth' Value='155'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
        <Border x:Name='row' Margin='0,1' CornerRadius='5' Background='Transparent'>
          <Grid>
            <Border x:Name='marker' Width='3' Height='18' HorizontalAlignment='Left' VerticalAlignment='Center' CornerRadius='2' Background='#0067C0' Visibility='Collapsed'/>
            <ContentPresenter Margin='14,0,10,0' ContentSource='Header' VerticalAlignment='Center'/>
          </Grid>
        </Border><ControlTemplate.Triggers>
          <Trigger Property='IsChecked' Value='True'><Setter TargetName='row' Property='Background' Value='#EEEEEE'/><Setter TargetName='marker' Property='Visibility' Value='Visible'/></Trigger>
          <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='row' Property='Background' Value='#E9E9E9'/></Trigger>
        </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

    internal static readonly Style CloseButton = (Style)Parse("<Style " + Namespace + @" TargetType='Button'>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
        <Grid Background='Transparent'>
          <Border x:Name='red' Background='#C42B1C' Opacity='0'/>
          <Path x:Name='cross' Data='M 1,1 L 11,11 M 11,1 L 1,11' Stroke='#191919' StrokeThickness='1.1' StrokeStartLineCap='Round' StrokeEndLineCap='Round' HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Grid><ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'>
            <Setter TargetName='cross' Property='Stroke' Value='White'/>
            <Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='red' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.09'/></Storyboard></BeginStoryboard></Trigger.EnterActions>
            <Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='red' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.12'/></Storyboard></BeginStoryboard></Trigger.ExitActions>
          </Trigger>
          <Trigger Property='IsPressed' Value='True'><Setter TargetName='red' Property='Background' Value='#A42619'/></Trigger>
        </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

    internal static readonly Style TabCloseButton = (Style)Parse("<Style " + Namespace + @" TargetType='Button'>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
        <Border x:Name='hit' Background='Transparent' CornerRadius='4'>
          <Path x:Name='cross' Data='M 1,1 L 9,9 M 9,1 L 1,9' Stroke='#272727' StrokeThickness='1.2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border><ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='hit' Property='Background' Value='#F8DDD9'/><Setter TargetName='cross' Property='Stroke' Value='#A42619'/></Trigger>
          <Trigger Property='IsPressed' Value='True'><Setter TargetName='hit' Property='Background' Value='#F4C8C1'/></Trigger>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='hit' Property='Background' Value='#E4EBF4'/></Trigger>
        </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

    internal static readonly Style DialogButton = (Style)XamlReader.Parse("<Style " + Namespace + @" TargetType='Button'>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
        <Grid>
          <Border x:Name='face' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='4'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <Border x:Name='outline' BorderBrush='#4B80B4' BorderThickness='1' CornerRadius='5' Margin='-2' Visibility='Collapsed'/>
        </Grid><ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='face' Property='Opacity' Value='.86'/></Trigger>
          <Trigger Property='IsPressed' Value='True'><Setter TargetName='face' Property='Opacity' Value='.74'/></Trigger>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='outline' Property='Visibility' Value='Visible'/></Trigger>
        </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

    static readonly Style SeparatorStyle = (Style)Parse("<Style " + Namespace + @" TargetType='Separator'>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'>
        <Border Height='1' Background='#E4E4E4' Margin='5,4'/>
      </ControlTemplate></Setter.Value></Setter></Style>");

    const string MenuItemsPresenter = "<ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>";
    static readonly ControlTemplate MenuChrome = MenuTemplate("<ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>" + MenuItemsPresenter + "</ScrollViewer>");
    static readonly ControlTemplate HeadingChrome = MenuTemplate(MenuItemsPresenter);

    static ControlTemplate MenuTemplate(string contents)
    {
        return (ControlTemplate)Parse("<ControlTemplate " + Namespace + @" TargetType='ContextMenu'>
          <Border x:Name='panel' Margin='6,7,13,14' Background='#F9F9F9' BorderBrush='#DBDBDB' BorderThickness='1' CornerRadius='8' Padding='4'>
            <Border.Effect><DropShadowEffect BlurRadius='10' ShadowDepth='2' Opacity='.16'/></Border.Effect>" + contents + "</Border></ControlTemplate>");
    }

    internal static void Apply(ContextMenu menu, bool headings = false)
    {
        menu.Template = headings ? HeadingChrome : MenuChrome;
        var items = headings ? HeadingItems : MenuItems;
        menu.ItemContainerStyle = items;
        menu.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        menu.HasDropShadow = false;
        StyleItems(menu, items);
        menu.Opened += delegate
        {
            menu.ApplyTemplate();
            var panel = menu.Template.FindName("panel", menu) as FrameworkElement;
            if (panel != null) AnimateIn(panel, -5, 150);
        };
    }

    internal static void StyleItems(ItemsControl menu) { StyleItems(menu, MenuItems); }

    static void StyleItems(ItemsControl menu, Style style)
    {
        foreach (var item in menu.Items)
        {
            var command = item as MenuItem;
            var divider = item as Separator;
            if (command != null)
            {
                command.Style = style;
                if (command.HasItems) StyleItems(command, MenuItems);
            }
            if (divider != null) divider.Style = SeparatorStyle;
        }
    }

    internal static void AnimateIn(FrameworkElement element, double offset, int milliseconds = 145)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
        if (offset == 0) return;
        var slide = new TranslateTransform();
        element.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
    }

    internal static readonly SolidColorBrush PopupBackground = new SolidColorBrush(Color.FromRgb(252, 252, 252));
    internal static readonly SolidColorBrush PopupBorder = new SolidColorBrush(Color.FromRgb(219, 219, 219));
}
