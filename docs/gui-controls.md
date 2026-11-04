# GUI Controls

This layer is intended to be parent-driven: create a container, add children through the container API, and let the container arrange, render, clip, and hit-test its descendants.

## Renderer-independent windows

Use `Window` as the root of top-level ADK XAML. A window contains no LCD or HUD logic; its `Platform` creates the backend-specific `WindowImpl`.

```xml
<Window xmlns="https://github.com/ArthurGamerHD/AcruxUI"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="Example.StatusWindow">
  <TextBlock Text="Hello" />
</Window>
```

The screen-space backend can own its draw/input lifecycle and open from a key:

```csharp
window.Platform = ScreenSpaceWindowPlatform.Instance;
window.BindOpen(Key.F11);
```

The same window instance and XAML can instead target an LCD:

```csharp
window.Platform = new LcdWindowPlatform(surface);
window.Open();
```

For an LCD whose existing surface script owns the sprite frame, construct `LcdWindowPlatform` with `ownsSurfaceFrame: false` and append it through `Draw(window, bounds, sprites)`. `AppView` uses this integrated mode so global LCD chrome and input continue to share one frame.

`Window.RenderScaling` reports the backend's pixels-per-logical-unit scale. Supply a positive finite scale through `LcdWindowPlatform.RenderScalingProvider`, `ScreenSpaceWindowPlatform.RenderScalingProvider`, or `LcdGuiHost.RenderScaling` for directly hosted trees. This is a window DPI scale, not a per-child render transform: layout gets the physical viewport size divided by the scale, drawing scales all primitives, and input receives inverse-mapped coordinates. Leave scale at `1` for legacy views that still bake scaling into their layout.

The app theme assigns the native text `FontSize` from `{DynamicResource fontScale}`. Leave ordinary text without a local size so it inherits this default. An intentional fractional or enlarged size must be relative to that native size through a converter, rather than a fixed font literal. Reusable template controls can apply relative sizes with standard converter syntax:

```xml
<TextBlock FontSize="{TemplateBinding FontSize, Converter={StaticResource FontSizeMultiplier}, ConverterParameter=0.46, ConverterCulture=en-US}" />
```

Register `FontSizeMultiplierConverter` in the view's `Resources` before `InitializeComponent`. The same converter options work on compiled `{Binding ...}` expressions. Dynamic resource changes invalidate measurement and refresh template bindings, including descendants with their own data contexts; explicit local font sizes keep precedence.

## Fixed Grid

```csharp
var grid = new Grid(parent, cols: 3, rows: 2);
grid.Set(title, col: 0, row: 0, colSpan: 3);
grid.Set(cancelButton, col: 0, row: 1);
grid.Set(confirmButton, col: 2, row: 1);
```

## Stack Panel

Use `StackPanel` for ordinary lists with a modest number of rows.

```csharp
var scroll = new ScrollPanel(parent);
var panel = new StackPanel(scroll)
{
    RowHeight = 30f * scale,
    Gap = 0f,
};

panel.AddChildren(rowControls);
scroll.ConfigureAutomatic(bounds, scrollerWidth, panel.RowHeight, autoScrollSecondsPerStep);
```

## Wrap Panel

Use `WrapPanel` for ordinary grids that can afford one control per item.

```csharp
var scroll = new ScrollPanel(parent);
var panel = new WrapPanel(scroll)
{
    RowHeight = 96f * scale,
    MinimumColumnWidth = 120f * scale,
};

panel.AddChildren(cellControls);
scroll.ConfigureAutomatic(bounds, scrollerWidth, panel.RowHeight, autoScrollSecondsPerStep);
```

## Automatic Scrolling

`ScrollPanel` owns viewport clipping and scrollbar rendering. App code should not clip ordinary scrolling content manually. If a child needs local clipping, restore the scroll clip before rendering later siblings.

```csharp
scroll.SetContent(panel);
scroll.ConfigureAutomatic(bounds, scrollerWidth, scrollStepPixels, autoScrollSecondsPerStep);
scroll.Render(context, sprites);
```

## Style Inheritance

Controls inherit theme/style context from their render parent. Local view styles are
layered after inherited styles, so view-specific selectors override the shared theme
without replacing it.

Views can request their own stylesheet with Avalonia-style property syntax:

```xml
<UserControl xmlns="https://github.com/ArthurGamerHD/AcruxUI"
             xmlns:styling="using:Acrux.Styling">
  <UserControl.Styles>
    <styling:StyleInclude Source="/Data/Gui/Styles/MyViewStyle.xaml" />
  </UserControl.Styles>
</UserControl>
```

The application must configure `StyleSheet.SourceLoader` before constructing views.
Prefer importing the shared theme on a root `Window`; descendants then inherit it
automatically and only need to include their local additions.

## Virtualized Panels

Use virtualized panels for large or dynamically growing collections. They measure the full content height, but only create/bind/render visible pooled controls.

```csharp
var panel = new VirtualizedWrapPanel<int>(scroll)
{
    RowHeight = rowHeight,
    MinimumColumnWidth = minimumColumnWidth,
    ItemsSource = itemIndexes,
    CreateControl = CreateButton,
    BindControl = BindButton,
};

scroll.SetContent(panel);
scroll.ConfigureAutomatic(bounds, scrollerWidth, rowHeight, 0f);
```

Use `VirtualizedStackPanel<T>` for long lists and `VirtualizedWrapPanel<T>` for button grids or card grids. Inactive pooled controls are hidden, so hit testing ignores recycled controls.

## Migration Notes

Do not mutate `Children` directly. Use `AddChild`, `AddChildren`, `RemoveChild`, `ClearChildren`, and `MoveChild` so parent ownership and layout invalidation remain correct.

Avoid app-level visible-range math. If an app still needs `StartRow`, `RenderRows`, or manual row loops, it should usually be moved to a virtualized panel.
