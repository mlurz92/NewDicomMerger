import os
import re

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml', 'r', encoding='utf-8') as f:
    xaml = f.read()

# 1. Update Accents to Gold
xaml = re.sub(r'<SolidColorBrush x:Key="Accent1" Color=".*?"/>', '<SolidColorBrush x:Key="Accent1" Color="#FFD700"/>', xaml)
xaml = re.sub(r'<SolidColorBrush x:Key="Accent2" Color=".*?"/>', '<SolidColorBrush x:Key="Accent2" Color="#D4AF37"/>', xaml)
xaml = re.sub(r'<SolidColorBrush x:Key="Accent3" Color=".*?"/>', '<SolidColorBrush x:Key="Accent3" Color="#AA8C27"/>', xaml)

# 2. Change Corner Radii
xaml = xaml.replace('CornerRadius="12"', 'CornerRadius="6"')
xaml = xaml.replace('CornerRadius="8"', 'CornerRadius="4"')

# 3. Add LineDrawBtn Style
line_draw_style = '''        <Style TargetType="Button" x:Key="LineDrawBtn">
            <Setter Property="Foreground" Value="#E2E8F0"/>
            <Setter Property="FontSize" Value="14"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Grid>
                            <Border x:Name="Bd" Background="#18181B" CornerRadius="6" BorderBrush="#27272A" BorderThickness="1">
                                <Border.Effect>
                                    <DropShadowEffect x:Name="Glow" Color="#D4AF37" BlurRadius="0" ShadowDepth="0" Opacity="0"/>
                                </Border.Effect>
                            </Border>
                            <Border x:Name="HoverOverlay" Background="#27272A" CornerRadius="6" Opacity="0"/>
                            
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="24,12"/>
                            
                            <!-- Animated Lines -->
                            <Rectangle x:Name="TopLine" Fill="{DynamicResource Accent1}" Height="2" VerticalAlignment="Top" HorizontalAlignment="Stretch" RenderTransformOrigin="0,0.5">
                                <Rectangle.RenderTransform><ScaleTransform ScaleX="0"/></Rectangle.RenderTransform>
                            </Rectangle>
                            <Rectangle x:Name="RightLine" Fill="{DynamicResource Accent1}" Width="2" HorizontalAlignment="Right" VerticalAlignment="Stretch" RenderTransformOrigin="0.5,0">
                                <Rectangle.RenderTransform><ScaleTransform ScaleY="0"/></Rectangle.RenderTransform>
                            </Rectangle>
                            <Rectangle x:Name="BottomLine" Fill="{DynamicResource Accent1}" Height="2" VerticalAlignment="Bottom" HorizontalAlignment="Stretch" RenderTransformOrigin="1,0.5">
                                <Rectangle.RenderTransform><ScaleTransform ScaleX="0"/></Rectangle.RenderTransform>
                            </Rectangle>
                            <Rectangle x:Name="LeftLine" Fill="{DynamicResource Accent1}" Width="2" HorizontalAlignment="Left" VerticalAlignment="Stretch" RenderTransformOrigin="0.5,1">
                                <Rectangle.RenderTransform><ScaleTransform ScaleY="0"/></Rectangle.RenderTransform>
                            </Rectangle>
                        </Grid>
                        <ControlTemplate.Triggers>
                            <EventTrigger RoutedEvent="MouseEnter">
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="HoverOverlay" Storyboard.TargetProperty="Opacity" To="1" Duration="0:0:0.3"/>
                                        <DoubleAnimation Storyboard.TargetName="Glow" Storyboard.TargetProperty="BlurRadius" To="15" Duration="0:0:0.6"/>
                                        <DoubleAnimation Storyboard.TargetName="Glow" Storyboard.TargetProperty="Opacity" To="0.3" Duration="0:0:0.6"/>
                                        
                                        <DoubleAnimation Storyboard.TargetName="TopLine" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)" To="1" Duration="0:0:0.15"/>
                                        <DoubleAnimation Storyboard.TargetName="RightLine" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)" To="1" Duration="0:0:0.15" BeginTime="0:0:0.15"/>
                                        <DoubleAnimation Storyboard.TargetName="BottomLine" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)" To="1" Duration="0:0:0.15" BeginTime="0:0:0.3"/>
                                        <DoubleAnimation Storyboard.TargetName="LeftLine" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)" To="1" Duration="0:0:0.15" BeginTime="0:0:0.45"/>
                                    </Storyboard>
                                </BeginStoryboard>
                            </EventTrigger>
                            <EventTrigger RoutedEvent="MouseLeave">
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="HoverOverlay" Storyboard.TargetProperty="Opacity" To="0" Duration="0:0:0.3"/>
                                        <DoubleAnimation Storyboard.TargetName="Glow" Storyboard.TargetProperty="Opacity" To="0" Duration="0:0:0.3"/>
                                        
                                        <!-- Fast retreat -->
                                        <DoubleAnimation Storyboard.TargetName="TopLine" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)" To="0" Duration="0:0:0.2"/>
                                        <DoubleAnimation Storyboard.TargetName="RightLine" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)" To="0" Duration="0:0:0.2"/>
                                        <DoubleAnimation Storyboard.TargetName="BottomLine" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)" To="0" Duration="0:0:0.2"/>
                                        <DoubleAnimation Storyboard.TargetName="LeftLine" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)" To="0" Duration="0:0:0.2"/>
                                    </Storyboard>
                                </BeginStoryboard>
                            </EventTrigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>'''
xaml = xaml.replace('<Style TargetType="Button" x:Key="MainActionBtn">', line_draw_style + '\n        <Style TargetType="Button" x:Key="MainActionBtn">')

# 4. Use LineDrawBtn for StartProcessButton and ChangeOutputDir
xaml = xaml.replace('Style="{StaticResource MainActionBtn}"', 'Style="{StaticResource LineDrawBtn}"')
xaml = xaml.replace('Style="{StaticResource GhostBtn}"', 'Style="{StaticResource LineDrawBtn}"')

# Let's also update ToolBtn to have a subtle LineDraw version or just flat gold highlight
tool_btn_hover = '''
                                <Setter TargetName="Bd" Property="Background" Value="#27272A"/>
                                <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Accent2}"/>
                                <Setter Property="Foreground" Value="#FFD700"/>'''
xaml = re.sub(r'<Setter TargetName="Bd" Property="Background" Value="#1A2244"/>\s*<Setter Property="Foreground" Value="White"/>', tool_btn_hover, xaml)

# 5. Remove Theme Pickers
xaml = re.sub(r'<StackPanel Orientation="Horizontal" Grid\.Column="1" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,12,0" x:Name="ThemePickers">.*?</StackPanel>', '', xaml, flags=re.DOTALL)

# 6. Change App Backgrounds from Blues to Blacks/Greys
xaml = re.sub(r'<RadialGradientBrush.*?</RadialGradientBrush>', '<SolidColorBrush Color="#09090B"/>', xaml, flags=re.DOTALL)
xaml = xaml.replace('#050711', '#09090B')
xaml = xaml.replace('#0A0E1A', '#111111')
xaml = xaml.replace('#1C2545', '#27272A')
xaml = xaml.replace('#0C1024', '#18181B')
xaml = xaml.replace('#161D38', '#1F1F22')
xaml = xaml.replace('#0A0E20', '#111111')
xaml = xaml.replace('#070A16', '#0A0A0A')

# 7. Update Text Colors for maximum readability
xaml = xaml.replace('#94A3B8', '#A1A1AA')

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml', 'w', encoding='utf-8') as f:
    f.write(xaml)

print('XAML patched')
