# 🎨 Making loading themes

The loading window is what you see while Vizstrap starts Roblox. Besides its built-in styles, Vizstrap draws **XML
themes** - the same format as Bloxstrap's custom themes, so themes made for Bloxstrap work in Vizstrap as they are.

---

## 🚀 Quick start

1. Open **Appearance → Style → XML theme**.
2. **New theme** creates one from a starter template and opens it in Notepad.
3. Change it, save, and press **Preview** on the Appearance page to see it.
4. A theme is a folder with a `Theme.xml` and its pictures. To share it, zip the folder - others use **Import ZIP** -
   or put it in a [package](making-a-package.md) under `loading-themes\`.

Vizstrap's own themes (Neon, Minimal, Split, Terminal) and the example **Synthwave** theme
([`examples/synthwave-pack/loading-themes/Synthwave/Theme.xml`](../examples/synthwave-pack/loading-themes/Synthwave/Theme.xml))
are good to learn from.

## 🧱 The basics

```xml
<BloxstrapCustomBootstrapper Version="1" Width="520" Height="320" Theme="Dark" IgnoreTitleBarInset="True" Background="#15122A">
  <TitleBar Title="" ShowMinimize="False" />

  <Image Source="{Icon}" Width="72" Height="72" HorizontalAlignment="Center" Margin="0,40,0,0" />
  <TextBlock Name="StatusText" FontSize="18" Foreground="#FFFFFF" HorizontalAlignment="Center" Margin="0,150,0,0" />
  <ProgressBar Name="PrimaryProgressBar" Width="320" Height="6" HorizontalAlignment="Center" Margin="0,190,0,0" />
  <Button Name="CancelButton" Content="{Common.Cancel}" Width="110" Height="32" HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,0,0,24" />
</BloxstrapCustomBootstrapper>
```

Elements are placed on top of each other in the window: move them with `HorizontalAlignment`, `VerticalAlignment` and
`Margin` (left, top, right, bottom). Without an alignment an element sits in the **top left** corner.

### The three named elements

Vizstrap finds these by their `Name` and keeps them up to date:

| Name | What Vizstrap does with it |
|---|---|
| `StatusText` (a `TextBlock`) | Shows what's happening: "Connecting to Roblox…", "Downloading…" |
| `PrimaryProgressBar` (a `ProgressBar` or `ProgressRing`) | Shows the download progress |
| `CancelButton` (a `Button`) | Cancels the launch |

### Placeholders

| Write | You get |
|---|---|
| `Source="{Icon}"` | The icon picked in Appearance (Vizstrap's, a classic Roblox one or the player's own) |
| `Content="{Common.Cancel}"` / `{Common.Close}` | "Cancel" / "Close" in the player's language |
| `Text="{Version}"` | Vizstrap's version |
| `Foreground="{NeonAccentBrush}"` | A colour of Vizstrap's own theme - follows the player's accent colour |

## 🪟 The window

| Attribute of `<BloxstrapCustomBootstrapper>` | What it does |
|---|---|
| `Version="1"` | The format's version - always `1` |
| `Width`, `Height` | The window's size |
| `Theme` | `Dark`, `Light` or `Default` (follows Vizstrap) |
| `Background` | A colour, or a gradient/picture with `<BloxstrapCustomBootstrapper.Background>` |
| `IgnoreTitleBarInset="True"` | Content starts at the very top, under the title bar |
| `Margin` | Space around all the content |
| `WindowCornerPreference` | `Round`, `RoundSmall` or `DoNotRound` |

`<TitleBar>` takes `Title`, `ShowMinimize`, `ShowClose` and `Visibility`.

## 🧩 Elements

| Element | For | Main attributes |
|---|---|---|
| `TextBlock` | Text | `Text`, `Foreground`, `FontSize`, `FontWeight`, `FontStyle`, `TextAlignment`, `TextWrapping` |
| `MarkdownTextBlock` | Text with **bold** and *italics* | `Text` |
| `Image` | A picture from the theme's folder, or `{Icon}` | `Source`, `Stretch`, `IsAnimated="True"` for GIFs |
| `ProgressBar` / `ProgressRing` | Progress | `Foreground`, `Background`, `IsIndeterminate` |
| `Button` | A button | `Content`, `Background`, `Foreground`, `FontSize` |
| `Border` | A box around one element | `Background`, `BorderBrush`, `BorderThickness`, `CornerRadius`, `Padding` |
| `Grid` / `StackPanel` | Arranging several elements | `Grid.RowDefinitions`, `Grid.ColumnDefinitions` / `Orientation` |
| `Rectangle`, `Ellipse`, `Line` | Shapes | `Fill`, `Stroke`, `StrokeThickness`, `RadiusX`/`RadiusY`, `X1`,`Y1`,`X2`,`Y2` |

Every element also takes `Width`, `Height`, `Margin`, `Opacity`, `Visibility`, `HorizontalAlignment`,
`VerticalAlignment`, `Panel.ZIndex` (0-1000, higher is on top) and, inside a `Grid`, `Grid.Row` / `Grid.Column`.

> [!TIP]
> Shapes stretch to fill their space by default. For a `Line` drawn with coordinates, add `Stretch="None"`.

## 🌈 Colours, gradients and pictures

A colour is `#RRGGBB`, `#AARRGGBB` (with transparency) or a name like `White`. For more, use a property element:

```xml
<Rectangle Width="200" Height="80" RadiusX="12" RadiusY="12">
  <Rectangle.Fill>
    <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
      <GradientStop Color="#7F77DD" Offset="0" />
      <GradientStop Color="#5EE6FF" Offset="1" />
    </LinearGradientBrush>
  </Rectangle.Fill>
</Rectangle>
```

A picture as a background: `<ImageBrush ImageSource="background.png" Stretch="UniformToFill" />`. Pictures must be
in the theme's folder - a theme can't load anything from the internet.

## ✨ Effects and transforms

```xml
<Image Source="{Icon}" Width="72" Height="72">
  <Image.Effect>
    <DropShadowEffect Color="#7F77DD" BlurRadius="28" ShadowDepth="0" Opacity="0.9" />
  </Image.Effect>
</Image>

<TextBlock Text="VIZSTRAP" RenderTransformOrigin="0.5,0.5">
  <TextBlock.RenderTransform>
    <RotateTransform Angle="-8" />
  </TextBlock.RenderTransform>
</TextBlock>
```

| Effects | Transforms |
|---|---|
| `BlurEffect` (`Radius`), `DropShadowEffect` (`Color`, `BlurRadius`, `ShadowDepth`, `Opacity`) | `RotateTransform`, `ScaleTransform`, `SkewTransform`, `TranslateTransform` |

A glow like the Neon theme's: an `Ellipse` in the accent colour with a big `BlurEffect` behind the icon.

## ❓ When something's wrong

If a theme has a mistake, Vizstrap says what and where when you press **Preview**, and uses its own loading window
instead - a broken theme never stops Roblox from starting.
