# Envoy UI Library Research: Cyberpunk Visual Polish

## 1. MaterialDesignInXAML (MaterialDesignThemes)

**NuGet:** `MaterialDesignThemes` | **Version:** 5.3.2 (released 2026-05-01) | **Target:** net8.0-windows7.0 + net462 | **Stars:** 16.1k
**Depends on:** MaterialDesignColors (>=5.3.2), Microsoft.Xaml.Behaviors.Wpf (>=1.1.77)

### Customizability
- **Highly customizable.** Uses `BundledTheme` markup extension with `BaseTheme` (Light/Dark/Inherit), `PrimaryColor`, `SecondaryColor` parameters. You can override the entire palette at runtime.
- Color system uses named swatches. You can set `PrimaryColor="Cyan"` as a starting point then override individual brushes.
- You can also use `<materialDesign:BundledTheme BaseTheme="Dark" PrimaryColor="Cyan" SecondaryColor="Lime" />` and then layer your `CyberpunkTheme.xaml` **after** it in `MergedDictionaries` to override specific brushes.
- Material Design 3 variant is available (`MaterialDesign3.Defaults.xaml`).

### Animations, Transitions, Effects
- **Ripple effects** on buttons/inputs (Material Design signature)
- **Transitions:** `Transitioner` and `TransitioningContent` controls for slide/fade/wipe between views
- **DialogHost:** Built-in modal dialog system with backdrop blur
- **Snackbar:** Toast notifications
- **Card:** Elevated card container with shadows
- **Clock:** Analog/digital clock control
- **ColorZone:** Regions with distinct color treatment (primary/secondary/accent)
- All controls have entrance animations, hover state transitions built in

### Selective Use
- **Yes, you can use selectively.** Include `MaterialDesignThemes` in MergedDictionaries, then override colors with your `CyberpunkTheme.xaml` loaded **last** (last wins in WPF resource resolution).
- You can use just `Transitioner`/`DialogHost`/`Snackbar` without applying Material styles globally (use `x:Key` styles instead of default styles).
- **Risk:** If you include the default theme globally, ALL controls get Material styles unless you explicitly override. This will fight your cyberpunk aesthetic.

### Verdict for Envoy
- **Pros:** Mature, actively maintained (release 2 days ago), great transition/animation system, DialogHost is excellent
- **Cons:** Material Design aesthetic fundamentally conflicts with cyberpunk. Overriding every color is a lot of work. Ripple effects feel "mobile Material", not "cyberpunk terminal".
- **Best selective use:** Just `Transitioner`, `DialogHost`, `Snackbar` — skip the rest of the styling.

---

## 2. HandyControl

**NuGet:** `HandyControl` | **Version:** 3.5.1 (stable, 2024-03-05) / 3.6.0-rc3 (pre-release, 2025-09-28) | **Target:** net8.0 + net5-8 + netcoreapp3+ + net40-481 | **Stars:** 7k
**Dependencies:** None (zero!)

### Custom Theming / Dark Themes
- Has built-in dark theme via `SkinDark.xaml`:
  ```xml
  <ResourceDictionary Source="pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml"/>
  <ResourceDictionary Source="pack://application:,,,/HandyControl;component/Themes/Theme.xaml"/>
  ```
- Color resources can be overridden by defining same-key brushes after loading HandyControl dictionaries.
- The theming istoken-based, so you can swap `PrimaryColor`, `SecondaryColor`, etc. at runtime.

### Unique Visual Features (highly relevant to cyberpunk)
- **GlowWindow** — Window with neon-like border glow around the entire window frame (!!!)
- **BlurWindow** — Acrylic/blur background window
- **GradientStart/GradientEnd color resources** — Built-in gradient theming
- **TransitioningContentControl** — Animated content transitions (slide, fade)
- **Effects** — Built-in visual effects including shadow, glow
- **Growl** — Toast notification system
- **Card** — Elevated card container
- **Dialog** — Modal dialogs
- **Drawer** — Slide-in panels
- **Loading** — Loading spinners (circular)
- **CircleProgressBar** — Circular progress indicators
- **OutlineText** — Text with outline/stroke effect
- **AnimationPath** — Animated path rendering
- **GeometryAnimation** — Animated geometry transitions
- **FloatingBlock** — Floating notification blocks
- **RunningBlock** — Marquee/scrolling text
- **GooeyEffect** — Morphing blob effect
- **SideMenu** — Navigation sidebar
- **StepBar** — Step/wizard progress indicator
- **Badge, Tag, Shield** — Labeling controls
- **DateTimePicker, SearchBar, PinBox** — Input controls
- **WaveProgressBar** — Wave-fill progress bar
- **FlipClock** — Flip-style clock display

### Can it coexist with CyberpunkTheme.xaml?
- **Yes.** Load HandyControl's `SkinDark.xaml` + `Theme.xaml` first, then load `CyberpunkTheme.xaml` last. Your brushes will override HandyControl's.
- The `GlowWindow`, `BlurWindow`, `TransitioningContentControl`, `Growl`, `Dialog`, `OutlineText`, `Loading` spinner are all usable without adopting HandyControl's visual style on every control.
- **Important:** HandyControl's zero-dependency design means it's lightweight.

### Verdict for Envoy
- **Pros:** `GlowWindow` is *exactly* what a cyberpunk app needs. `OutlineText` is perfect for neon headings. `TransitioningContentControl` for view switching. `Growl` for toast notifications. All with zero extra dependencies.
- **Cons:** Last stable release was March 2024 (2 years old). RC version available. Less maintained than MaterialDesign. Chinese-centric community/docs.
- **Best use:** `GlowWindow`, `OutlineText`, `TransitioningContentControl`, `Growl`, `CircleProgressBar`, `Loading` — cherry-pick controls, override all colors.

---

## 3. AdonisUI

**NuGet:** `AdonisUI` + `AdonisUI.ClassicTheme` | **Version:** 1.17.1 (2021-09-05) | **Target:** net5.0-windows7.0 + netcoreapp3.1 + net45+ | **Stars:** ~1.5k
**Dependencies:** `System.Drawing.Common` (>=4.6.0)

### Dark Theme
- Has a well-crafted dark theme that actually looks good:
  ```xml
  <ResourceDictionary Source="pack://application:,,,/AdonisUI;component/ColorSchemes/Dark.xaml"/>
  <ResourceDictionary Source="pack://application:,,,/AdonisUI.ClassicTheme;component/Resources.xaml"/>
  ```
- The dark theme uses proper contrast ratios and subtle elevation differences.

### Custom Accent Colors
- Yes, supports custom accent colors via resource overrides. Define your own accent brush after loading AdonisUI dictionaries.
- Has `AccentColor`, `AccentHighlightColor`, etc. resource keys.

### What It Adds Beyond Styling
- **Cursor six-line toggle** validation UI
- **ScrollViewer** improvements with smooth scrolling
- **Window** chrome customization (custom title bars)
- **FadeIn/FadeOut** animations on select controls
- **DialogHost** equivalent
- **Very lightweight** — minimal footprint, just better-looking defaults
- Does NOT add a massive control library. It's mainly a theme system.

### Verdict for Envoy
- **Pros:** Lightweight, clean dark theme, minimal footprint
- **Cons:** Last release was 2021 (nearly 5 years ago!). No development activity. Very few controls. No glow effects, no transitions, no animation system. Doesn't add much beyond basic styling that you've already done yourself.
- **Verdict:** Skip. It doesn't offer enough beyond what you've already built, and it's unmaintained.

---

## 4. Pure XAML/Code-Behind Visual Effects (No Packages)

### 4a. Glow Effects — DropShadowEffect on Borders

```xml
<!-- Neon border glow on a card -->
<Border Background="#111827" CornerRadius="3" Padding="20" Margin="0,0,0,16">
    <Border.Effect>
        <DropShadowEffect Color="#00F0FF" BlurRadius="12" ShadowDepth="0" Opacity="0.4" Direction="0"/>
    </Border.Effect>
    <Border.BorderBrush>
        <SolidColorBrush Color="#00F0FF" Opacity="0.3"/>
    </Border.BorderBrush>
    <!-- content -->
</Border>
```

For **inner glow**, use an overlappingGrid approach:

```xml
<Grid>
    <Border x:Name="GlowBorder" Background="Transparent" CornerRadius="3"
            BorderBrush="#00F0FF" BorderThickness="1">
        <Border.Effect>
            <DropShadowEffect Color="#00F0FF" BlurRadius="16" ShadowDepth="0" Opacity="0.5"/>
        </Border.Effect>
    </Border>
    <Border Background="#111827" CornerRadius="3" Margin="1" Padding="20">
        <!-- actual content sits here, on top -->
    </Border>
</Grid>
```

### 4b. Panel Entrance Animation (Fade + Slide)

```xml
<!-- Add to UserControl.Resources or Window.Resources -->
<Storyboard x:Key="SlideInFromRight">
    <DoubleAnimation Storyboard.TargetProperty="Opacity" From="0" To="1" Duration="0:0:0.3"/>
    <DoubleAnimation Storyboard.TargetProperty="(RenderTransform).(TranslateTransform.X)"
                      From="40" To="0" Duration="0:0:0.3">
        <DoubleAnimation.EasingFunction>
            <CubicEase EasingMode="EaseOut"/>
        </DoubleAnimation.EasingFunction>
    </DoubleAnimation>
</Storyboard>

<Storyboard x:Key="SlideInFromBottom">
    <DoubleAnimation Storyboard.TargetProperty="Opacity" From="0" To="1" Duration="0:0:0.25"/>
    <DoubleAnimation Storyboard.TargetProperty="(RenderTransform).(TranslateTransform.Y)"
                      From="20" To="0" Duration="0:0:0.25">
        <DoubleAnimation.EasingFunction>
            <CubicEase EasingMode="EaseOut"/>
        </DoubleAnimation.EasingFunction>
    </DoubleAnimation>
</Storyboard>
```

Apply to panels:
```xml
<Border x:Name="CardPanel" RenderTransformOrigin="0.5,0.5">
    <Border.RenderTransform>
        <TranslateTransform X="40"/>
    </Border.RenderTransform>
    <!-- content -->
</Border>
```

```csharp
// In code-behind on Loaded
var sb = (Storyboard)FindResource("SlideInFromRight");
sb.Begin(CardPanel);
```

### 4c. Button Hover Glow Pulse

```xml
<Style TargetType="Button" x:Key="CyberButtonGlow" BasedOn="{StaticResource CyberButton}">
    <Style.Triggers>
        <EventTrigger RoutedEvent="MouseEnter">
            <BeginStoryboard>
                <Storyboard>
                    <ColorAnimation Storyboard.TargetProperty="(Border.BorderBrush).(SolidColorBrush.Color)"
                                    To="#00F0FF" Duration="0:0:0.15"/>
                    <DoubleAnimation Storyboard.TargetProperty="(Effect).(DropShadowEffect.Opacity)"
                                    To="0.6" Duration="0:0:0.15"/>
                    <ColorAnimation Storyboard.TargetProperty="(Effect).(DropShadowEffect.Color)"
                                    To="#00F0FF" Duration="0:0:0.0"/>
                </Storyboard>
            </Storyboard>
        </EventTrigger>
        <EventTrigger RoutedEvent="MouseLeave">
            <BeginStoryboard>
                <Storyboard>
                    <DoubleAnimation Storyboard.TargetProperty="(Effect).(DropShadowEffect.Opacity)"
                                    To="0" Duration="0:0:0.3"/>
                </Storyboard>
            </Storyboard>
        </EventTrigger>
    </Style.Triggers>
    <Setter Property="Effect">
        <Setter.Value>
            <DropShadowEffect Color="#00F0FF" BlurRadius="8" ShadowDepth="0" Opacity="0"/>
        </Setter.Value>
    </Setter>
</Style>
```

### 4d. Neon Pulsing Border Glow

```xml
<Storyboard x:Key="NeonPulse" RepeatBehavior="Forever">
    <DoubleAnimation Storyboard.TargetProperty="(Effect).(DropShadowEffect.Opacity)"
                     From="0.2" To="0.7" Duration="0:0:1.5"
                     AutoReverse="True">
        <DoubleAnimation.EasingFunction>
            <SineEase EasingMode="EaseInOut"/>
        </DoubleAnimation.EasingFunction>
    </DoubleAnimation>
</Storyboard>
```

Apply to any border:
```xml
<Border.Effect>
    <DropShadowEffect Color="#00F0FF" BlurRadius="10" ShadowDepth="0" Opacity="0.2"/>
</Border.Effect>
```

### 4e. Progress Bar Shimmer

```xml
<Style TargetType="ProgressBar" x:Key="CyberProgressShimmer" BasedOn="{StaticResource CyberProgress}">
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="ProgressBar">
                <Border Background="{TemplateBinding Background}" CornerRadius="2">
                    <Border x:Name="PART_Track" Background="Transparent"/>
                    <Grid>
                        <Rectangle x:Name="PART_Indicator"
                                   Fill="{TemplateBinding Foreground}"
                                   HorizontalAlignment="Left"
                                   RadiusX="2" RadiusY="2">
                            <Rectangle.OpacityMask>
                                <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                                    <GradientStop Offset="0" Color="Transparent"/>
                                    <GradientStop Offset="0.3" Color="#66FFFFFF"/>
                                    <GradientStop Offset="0.5" Color="White"/>
                                    <GradientStop Offset="0.7" Color="#66FFFFFF"/>
                                    <GradientStop Offset="1" Color="Transparent"/>
                                </LinearGradientBrush>
                            </Rectangle.OpacityMask>
                        </Rectangle>
                    </Grid>
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

For a shimmer animation on the progress indicator:
```xml
<Storyboard x:Key="ShimmerAnimation" RepeatBehavior="Forever">
    <DoubleAnimation Storyboard.TargetProperty="(Rectangle.OpacityMask).(LinearGradientBrush.StartPoint).(Point.X)"
                     From="-0.5" To="1.5" Duration="0:0:2"/>
</Storyboard>
```

Simpler shimmer approach (animated gradient overlay):
```xml
<!-- Place this border over the progress bar -->
<Border CornerRadius="2" ClipToBounds="True">
    <Border.Background>
        <LinearGradientBrush x:Name="ShimmerBrush" StartPoint="0,0" EndPoint="1,0">
            <GradientStop Offset="0" Color="Transparent"/>
            <GradientStop Offset="0.4" Color="Transparent"/>
            <GradientStop Offset="0.5" Color="#33FFFFFF"/>
            <GradientStop Offset="0.6" Color="Transparent"/>
            <GradientStop Offset="1" Color="Transparent"/>
        </LinearGradientBrush>
    </Border.Background>
</Border>
```
Animate the gradient offsets in code-behind or with a `DoubleAnimation` on `GradientStop.Offset`.

### 4f. Scanline / CRT Overlay

```xml
<!-- Add as the LAST child of your root Grid/Border in MainWindow -->
<Rectangle Grid.RowSpan="3" Grid.ColumnSpan="3"
           IsHitTestVisible="False" Opacity="0.04" PointerPressed="IGNORE">
    <Rectangle.Fill>
        <DrawingBrush TileMode="Tile" Viewport="0,0,4,4" ViewportUnits="Absolute">
            <DrawingBrush.Drawing>
                <DrawingGroup>
                    <GeometryDrawing>
                        <GeometryDrawing.Pen>
                            <Pen Brush="#000000" Thickness="1"/>
                        </GeometryDrawing.Pen>
                        <GeometryDrawing.Geometry>
                            <LineGeometry StartPoint="0,2" EndPoint="4,2"/>
                        </GeometryDrawing.Geometry>
                    </GeometryDrawing>
                </DrawingGroup>
            </DrawingBrush.Drawing>
        </DrawingBrush>
    </Rectangle.Fill>
</Rectangle>
```

For a more dramatic CRT effect, add a vignette overlay too:
```xml
<Border Grid.RowSpan="3" Grid.ColumnSpan="3" IsHitTestVisible="False">
    <Border.Background>
        <RadialGradientBrush Center="0.5,0.5" RadiusX="0.7" RadiusY="0.7">
            <GradientStop Offset="0" Color="Transparent"/>
            <GradientStop Offset="0.7" Color="Transparent"/>
            <GradientStop Offset="1" Color="#33000000"/>
        </RadialGradientBrush>
    </Border.Background>
</Border>
```

### 4g. Glitch Text Effect (Flicker on Headings)

```xml
<Style TargetType="TextBlock" x:Key="CyberHeadingGlitch" BasedOn="{StaticResource CyberHeading}">
    <Style.Triggers>
        <EventTrigger RoutedEvent="Loaded">
            <BeginStoryboard>
                <Storyboard RepeatBehavior="Forever">
                    <DoubleAnimationUsingKeyFrames Storyboard.TargetProperty="Opacity" Duration="0:0:4">
                        <DiscreteDoubleKeyFrame Value="1" KeyTime="0:0:0"/>
                        <DiscreteDoubleKeyFrame Value="1" KeyTime="0:0:2.1"/>
                        <DiscreteDoubleKeyFrame Value="0.2" KeyTime="0:0:2.12"/>
                        <DiscreteDoubleKeyFrame Value="0.9" KeyTime="0:0:2.14"/>
                        <DiscreteDoubleKeyFrame Value="0.3" KeyTime="0:0:2.15"/>
                        <DiscreteDoubleKeyFrame Value="1" KeyTime="0:0:2.17"/>
                        <DiscreteDoubleKeyFrame Value="1" KeyTime="0:0:3.5"/>
                        <DiscreteDoubleKeyFrame Value="0.1" KeyTime="0:0:3.51"/>
                        <DiscreteDoubleKeyFrame Value="1" KeyTime="0:0:3.53"/>
                    </DoubleAnimationUsingKeyFrames>
                    <ColorAnimationUsingKeyFrames Storyboard.TargetProperty="(Foreground).(SolidColorBrush.Color)">
                        <DiscreteColorKeyFrame Value="#00F0FF" KeyTime="0:0:0"/>
                        <DiscreteColorKeyFrame Value="#00F0FF" KeyTime="0:0:2.1"/>
                        <DiscreteColorKeyFrame Value="#FF00FF" KeyTime="0:0:2.12"/>
                        <DiscreteColorKeyFrame Value="#00F0FF" KeyTime="0:0:2.14"/>
                    </ColorAnimationUsingKeyFrames>
                </Storyboard>
            </BeginStoryboard>
        </EventTrigger>
    </Style.Triggers>
</Style>
```

For a horizontal glitch-offset version, you'd need code-behind with a `DispatcherTimer`:
```csharp
private DispatcherTimer _glitchTimer;
private Random _rng = new();

private void StartGlitch(TextBlock target)
{
    _glitchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
    _glitchTimer.Tick += (s, e) =>
    {
        if (_rng.NextDouble() < 0.3) // 30% chance per tick
        {
            var transform = new TranslateTransform { X = _rng.Next(-3, 4) };
            target.RenderTransform = transform;
            target.Foreground = new SolidColorBrush(_rng.NextDouble() < 0.5 ? Colors.Cyan : Colors.Magenta);
            
            Task.Delay(80).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                target.RenderTransform = new TranslateTransform();
                target.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00F0FF"));
            }));
        }
    };
    _glitchTimer.Start();
}
```

### 4h. View Transition (Content Swap with Animation)

```csharp
// In MainWindow.xaml.cs
private async Task TransitionContent(UserControl newView)
{
    var oldContent = ContentArea.Children.OfType<UserControl>().FirstOrDefault();
    if (oldContent != null)
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += async (_, _) =>
        {
            ContentArea.Children.Clear();
            newView.Opacity = 0;
            newView.RenderTransform = new TranslateTransform(30, 0);
            ContentArea.Children.Add(newView);
            
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var slideIn = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(200));
            slideIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            
            newView.BeginAnimation(FrameworkElement.OpacityProperty, fadeIn);
            ((TranslateTransform)newView.RenderTransform).BeginAnimation(TranslateTransform.XProperty, slideIn);
        };
        oldContent.BeginAnimation(FrameworkElement.OpacityProperty, fadeOut);
    }
    else
    {
        newView.RenderTransform = new TranslateTransform(0, 0);
        ContentArea.Children.Add(newView);
    }
}
```

### 4i. Neon Loading Spinner

```xml
<!-- Spinning circle with neon glow -->
<Grid Width="32" Height="32" x:Name="NeonSpinner">
    <Ellipse Stroke="#00F0FF" StrokeThickness="3" Opacity="0.2"/>
    <Ellipse Stroke="#00F0FF" StrokeThickness="3" StrokeDashArray="20,80"
             RenderTransformOrigin="0.5,0.5">
        <Ellipse.Effect>
            <DropShadowEffect Color="#00F0FF" BlurRadius="6" ShadowDepth="0" Opacity="0.6"/>
        </Ellipse.Effect>
        <Ellipse.RenderTransform>
            <RotateTransform/>
        </Ellipse.RenderTransform>
    </Ellipse>
</Grid>

<!-- Animation (in Resources or code-behind) -->
<Storyboard x:Key="SpinnerRotate" RepeatBehavior="Forever">
    <DoubleAnimation Storyboard.TargetProperty="(Ellipse.RenderTransform).(RotateTransform.Angle)"
                     From="0" To="360" Duration="0:0:1"/>
</Storyboard>
```

### 4j. Neon Typing Cursor (caret for text inputs)

```xml
<Style TargetType="TextBox" x:Key="CyberTextBoxNeon" BasedOn="{StaticResource CyberTextBox}">
    <Setter Property="CaretBrush">
        <Setter.Value>
            <SolidColorBrush Color="#00F0FF"/>
        </Setter.Value>
    </Setter>
    <Style.Triggers>
        <Trigger Property="IsFocused" Value="True">
            <Setter Property="BorderBrush" Value="#00F0FF"/>
            <Setter Property="Effect">
                <Setter.Value>
                    <DropShadowEffect Color="#00F0FF" BlurRadius="6" ShadowDepth="0" Opacity="0.3"/>
                </Setter.Value>
            </Setter>
        </Trigger>
    </Style.Triggers>
</Style>
```

---

## 5. Font Options

### Recommended Cyberpunk Fonts

| Font | Type | Open Source | Best For | Cyberpunk Rating |
|------|------|------------|----------|------------------|
| **JetBrains Mono** | Monospace | Yes (Apache 2.0) | Body text, code, inputs | ★★★★★ |
| **Fira Code** | Monospace | Yes (OFL) | Body text, ligatures | ★★★★☆ |
| **Cascadia Code** | Monospace | Yes (OFL) | Body text, terminals | ★★★★☆ |
| **Space Mono** | Monospace | Yes (OFL) | Body text, labels | ★★★★☆ |
| **Share Tech Mono** | Monospace | Yes (OFL) | Headings, accents | ★★★★☆ |
| **Orbitron** | Display | Yes (OFL) | Headings, titles, logo | ★★★★★ |

### Recommended Combination
- **Headings:** `Orbitron` (geometric, futuristic, very cyberpunk)
- **Body/UI text:** `JetBrains Mono` (excellent readability, ligatures, modern tech feel)
- **Fallthrough:** `Consolas` (already in use, safe system font)

### How to Embed Fonts in WPF

1. Add `.ttf` or `.otf` files to your project (e.g., `Fonts/Orbitron-Regular.ttf`, `Fonts/JetBrainsMono-Regular.ttf`)
2. Set Build Action to **Resource** (not EmbeddedResource)
3. Reference in XAML:

```xml
<!-- In CyberpunkTheme.xaml or App.xaml -->
<FontFamily x:Key="CyberFontHeading">pack://application:,,,/Fonts/#Orbitron</FontFamily>
<FontFamily x:Key="CyberFontBody">pack://application:,,,/Fonts/#JetBrains Mono</FontFamily>

<!-- Usage -->
<TextBlock Text="◈ DASHBOARD" FontFamily="{StaticResource CyberFontHeading}" FontSize="20"/>
```

**Note:** The font family name after `#` must match the **internal font name**, not the filename. Open the `.ttf` in Windows Font Viewer to see the correct name.

Download sources:
- Orbitron: https://fonts.google.com/specimen/Orbitron
- JetBrains Mono: https://www.jetbrains.com/lp/mono/
- Fira Code: https://github.com/tonsky/FiraCode
- Cascadia Code: https://github.com/microsoft/cascadia-code
- Space Mono: https://fonts.google.com/specimen/Space+Mono
- Share Tech Mono: https://fonts.google.com/specimen/Share+Tech+Mono

---

## 6. Recommendation

### Evaluation Matrix

| Factor | MaterialDesign (A) | HandyControl (B) | Pure Custom (C) | Hybrid (D) |
|--------|--------------------|--------------------|-----------------|-----------|
| Visual match to cyberpunk | Low (fighting MD aesthetic) | Medium (GlowWindow fits) | High (full control) | High |
| Effort to implement | High (override everything) | Medium | Very High | Medium |
| Risk to existing code | Medium (global style override) | Low-Medium | None | Low |
| Animation/transition support | High (Transitioner) | Medium (TransitioningContent) | Low (hand-roll all) | High |
| Glow/neon effects | None built-in | High (GlowWindow!) | Manual | High |
| Long-term maintenance | High (active, 16k stars) | Low (last stable 2024) | N/A (yours) | Mixed |
| Dependency count | 2 (MD + Behaviors) | 0 | 0 | 0-2 |

### **Recommended: Option D (Hybrid) — HandyControl controls + Pure custom effects**

Specifically:

1. **Install HandyControl** for: `GlowWindow`, `TransitioningContentControl`, `Growl` (toast), `OutlineText`, `CircleProgressBar`, `Loading` spinner. These give you immediate visual impact with near-zero effort.

2. **Keep your `CyberpunkTheme.xaml`** as the authoritative style dictionary. Load HandyControl first, then CyberpunkTheme overrides on top.

3. **Add pure XAML effects** from Section 4 above:
   - `DropShadowEffect` glow on all card borders
   - Neon pulse animation on the window border and active nav item
   - Scanline overlay on the main grid
   - Glitch text effect on the "ENVOY" title
   - Fade+slide transitions for content area view switching
   - Button hover glow pulse

4. **Embed Orbitron + JetBrains Mono** fonts for the heading/body combination.

5. **Do NOT install MaterialDesignInXAML.** Its aesthetic fundamentally conflicts with cyberpunk, and you'd spend more time overriding its styles than building your own.

6. **Do NOT install AdonisUI.** It's unmaintained and doesn't add anything beyond basic theming you already have.

### Implementation Priority

| Priority | Effect | Effort | Impact |
|----------|--------|--------|--------|
| P0 | DropShadowEffect neon glow on cards | 5 min | Huge |
| P0 | GlowWindow (HandyControl) | 15 min | Huge |
| P0 | Orbitron + JetBrains Mono fonts | 20 min | Huge |
| P1 | Button hover glow pulse | 15 min | Medium |
| P1 | Scanline overlay | 10 min | Medium |
| P1 | View transition animation | 30 min | High |
| P2 | Glitch text on title | 20 min | Medium |
| P2 | Loading spinner (neon) | 15 min | Medium |
| P2 | Growl notifications (HandyControl) | 20 min | Medium |
| P3 | Progress bar shimmer | 20 min | Low |
| P3 | CRT vignette overlay | 10 min | Low |

### Minimal App.xaml Integration

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <!-- HandyControl (loaded first, we override what we don't want) -->
            <ResourceDictionary Source="pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml"/>
            <ResourceDictionary Source="pack://application:,,,/HandyControl;component/Themes/Theme.xaml"/>
            <!-- Our cyberpunk overrides last (wins all conflicts) -->
            <ResourceDictionary Source="Themes/CyberpunkTheme.xaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

### MainWindow Update for GlowWindow

```xml
<!-- Replace Window with hc:GlowWindow -->
<hc:GlowWindow x:Class="Envoy.UI.MainWindow"
        xmlns:hc="https://handyorg.github.io/handycontrol"
        ...>
    <hc:GlowWindow.GlowColor>#00F0FF</hc:GlowWindow.GlowColor>
    ...
</hc:GlowWindow>
```

This gives you an animated glowing border around the entire window frame — the single highest-impact visual change you can make.