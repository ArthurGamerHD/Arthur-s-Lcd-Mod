# GUI Controls

This layer is intended to be parent-driven: create a container, add children through the container API, and let the container arrange, render, clip, and hit-test its descendants.

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

Controls inherit theme/style context from their render parent unless they set a local style or style override. Prefer configuring styles on parent containers or models instead of recalculating style values every frame.

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
