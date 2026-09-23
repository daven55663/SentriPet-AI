using System;
using System.Windows;
using System.Windows.Markup;

namespace SentriPet
{
    /// <summary>Dark, rounded styles for menus and the settings window (loose XAML parsed at start-up).</summary>
    static class Styles
    {
        public const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='MenuBg' Color='#F61C1E27'/>
  <SolidColorBrush x:Key='MenuBorder' Color='#30FFFFFF'/>
  <SolidColorBrush x:Key='MenuHover' Color='#26FFFFFF'/>
  <SolidColorBrush x:Key='MenuText' Color='#F2F4F8'/>
  <SolidColorBrush x:Key='MenuSub' Color='#8F98A8'/>
  <SolidColorBrush x:Key='Accent' Color='#7C9CFF'/>

  <Style x:Key='Plain' TargetType='{x:Type RepeatButton}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='FillRepeat' TargetType='{x:Type RepeatButton}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type RepeatButton}'>
          <Grid Background='Transparent'><Border Height='4' CornerRadius='2' Background='#6D8BFF' VerticalAlignment='Center'/></Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='{x:Type Separator}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Separator}'><Border Height='1' Margin='10,5' Background='#22FFFFFF'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='PetMenuItem' TargetType='{x:Type MenuItem}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{StaticResource MenuText}'/>
    <Setter Property='FontFamily' Value='Microsoft JhengHei UI'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type MenuItem}'>
          <Grid SnapsToDevicePixels='True'>
            <Border x:Name='Bd' Background='Transparent' CornerRadius='7' Padding='8,6,10,6' MinWidth='190'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='26'/>
                  <ColumnDefinition Width='*'/>
                  <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>
                <ContentPresenter x:Name='Icon' ContentSource='Icon' VerticalAlignment='Center' HorizontalAlignment='Left'/>
                <TextBlock x:Name='Check' Text='&#xE73E;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='12' Foreground='{StaticResource Accent}' VerticalAlignment='Center' Visibility='Collapsed'/>
                <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
                <TextBlock x:Name='Gesture' Grid.Column='2' Text='{TemplateBinding InputGestureText}' Foreground='{StaticResource MenuSub}' FontSize='11.5' Margin='18,0,0,0' VerticalAlignment='Center'/>
                <TextBlock x:Name='Arrow' Grid.Column='2' Text='&#xE76C;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='10' Foreground='{StaticResource MenuSub}' Margin='18,0,0,0' VerticalAlignment='Center' Visibility='Collapsed'/>
              </Grid>
            </Border>
            <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-6' VerticalOffset='-15' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'
                   IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
              <Border Margin='10' Background='{StaticResource MenuBg}' BorderBrush='{StaticResource MenuBorder}' BorderThickness='1' CornerRadius='12' Padding='5'>
                <Border.Effect><DropShadowEffect BlurRadius='16' ShadowDepth='3' Opacity='0.45'/></Border.Effect>
                <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='{StaticResource MenuHover}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Check' Property='Visibility' Value='Visible'/>
              <Setter TargetName='Icon' Property='Visibility' Value='Collapsed'/>
            </Trigger>
            <Trigger Property='HasItems' Value='True'>
              <Setter TargetName='Arrow' Property='Visibility' Value='Visible'/>
              <Setter TargetName='Gesture' Property='Visibility' Value='Collapsed'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.55'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='PetMenu' TargetType='{x:Type ContextMenu}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Foreground' Value='{StaticResource MenuText}'/>
    <Setter Property='FontFamily' Value='Microsoft JhengHei UI'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ContextMenu}'>
          <Border Margin='10' Background='{StaticResource MenuBg}' BorderBrush='{StaticResource MenuBorder}' BorderThickness='1' CornerRadius='12' Padding='5'>
            <Border.Effect><DropShadowEffect BlurRadius='18' ShadowDepth='4' Opacity='0.45'/></Border.Effect>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Resources>
      <Style TargetType='{x:Type MenuItem}' BasedOn='{StaticResource PetMenuItem}'/>
    </Style.Resources>
  </Style>

  <Style x:Key='Toggle' TargetType='{x:Type CheckBox}'>
    <Setter Property='Foreground' Value='#E8EAF0'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type CheckBox}'>
          <Grid Background='Transparent'>
            <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
            <ContentPresenter VerticalAlignment='Center' Margin='0,0,14,0'/>
            <Border x:Name='Track' Grid.Column='1' Width='42' Height='23' CornerRadius='11.5' Background='#343946' BorderBrush='#4E5563' BorderThickness='1'>
              <Ellipse x:Name='Knob' Width='16' Height='16' Fill='#C3C9D4' HorizontalAlignment='Left' Margin='3,0,0,0'/>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Track' Property='Background' Value='#6D8BFF'/>
              <Setter TargetName='Track' Property='BorderBrush' Value='#6D8BFF'/>
              <Setter TargetName='Knob' Property='HorizontalAlignment' Value='Right'/>
              <Setter TargetName='Knob' Property='Margin' Value='0,0,3,0'/>
              <Setter TargetName='Knob' Property='Fill' Value='White'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Track' Property='Opacity' Value='0.88'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Slim' TargetType='{x:Type Slider}'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Slider}'>
          <Grid Height='24' Background='Transparent'>
            <Border Height='4' CornerRadius='2' Background='#3A3F4B' VerticalAlignment='Center'/>
            <Track x:Name='PART_Track'>
              <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource FillRepeat}' Command='Slider.DecreaseLarge'/></Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource Plain}' Command='Slider.IncreaseLarge'/></Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType='{x:Type Thumb}'><Ellipse Width='17' Height='17' Fill='#6D8BFF' Stroke='White' StrokeThickness='2.5'/></ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Btn' TargetType='{x:Type Button}'>
    <Setter Property='Foreground' Value='#E8EAF0'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Padding' Value='14,7'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Button}'>
          <Border x:Name='B' Background='#2A2E39' BorderBrush='#3C4250' BorderThickness='1' CornerRadius='9' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='#363B48'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Background' Value='#1F222A'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Card' TargetType='{x:Type Button}'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Button}'>
          <Border x:Name='B' Background='#1F222B' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='2' CornerRadius='14' Padding='8'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='#282C37'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Input' TargetType='{x:Type TextBox}'>
    <Setter Property='Foreground' Value='#F2F4F8'/>
    <Setter Property='Background' Value='#262A34'/>
    <Setter Property='BorderBrush' Value='#3C4250'/>
    <Setter Property='CaretBrush' Value='#F2F4F8'/>
    <Setter Property='Padding' Value='8,5'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type TextBox}'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='SlimScroll' TargetType='{x:Type ScrollBar}'>
    <Setter Property='Width' Value='8'/>
    <Setter Property='MinWidth' Value='8'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ScrollBar}'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource Plain}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource Plain}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='{x:Type Thumb}'><Border CornerRadius='4' Background='#40FFFFFF' Margin='1,0'/></ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        public static void Load(Application app)
        {
            try
            {
                var dict = (ResourceDictionary)XamlReader.Parse(Xaml);
                app.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex)
            {
                Log.Error("styles", ex);
            }
        }
    }
}
