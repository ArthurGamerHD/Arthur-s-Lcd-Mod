using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Animation;
using LcdMod.Client.Apps.ViewModel;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Lists;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using Sandbox.Definitions;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.Gui.Styling.DataTemplates
{
    public static class ItemDataTemplate
    {
        public const string DRAW_ITEM_LIST_ENTRY_RESOURCE_NAME =
            "ItemDataTemplate.DrawItemListEntry";
        public const string DRAW_ITEM_GRID_ENTRY_RESOURCE_NAME =
            "ItemDataTemplate.DrawItemGridEntry";

        public static readonly ResourceKey<ListBoxItemRenderHandler<ItemEntry>> DrawItemListEntryResource =
            ResourceKey.Register<ListBoxItemRenderHandler<ItemEntry>>(DRAW_ITEM_LIST_ENTRY_RESOURCE_NAME);
        public static readonly ResourceKey<InteractiveRenderHandler> DrawItemGridEntryResource =
            ResourceKey.Register<InteractiveRenderHandler>(DRAW_ITEM_GRID_ENTRY_RESOURCE_NAME);

        const float ITEM_ROW_HEIGHT = 30f;
        const float ITEM_HORIZONTAL_PADDING = 14f;
        const int ITEM_CACHE_LIMIT = 256;
        const string ITEM_WARNING_STYLE_ID = "ItemWarning";
        const string ITEM_ERROR_STYLE_ID = "ItemError";

        static readonly Dictionary<MyItemType, string> ItemIcons = new Dictionary<MyItemType, string>();
        static readonly Dictionary<MyItemType, string> ItemNames = new Dictionary<MyItemType, string>();

        public static readonly StyleTree ItemListStyles = BuildItemListStyles();
        public static readonly StyleTree ItemTableStyles = BuildItemTableStyles();
        public static readonly StyleTree ItemGridCardStyles = BuildItemGridCardStyles();
        public static readonly StyleTree ItemGridLineStyles = BuildItemGridLineStyles();

        public static void AddResources(ResourceTree resources)
        {
            if (resources == null)
                throw new ArgumentNullException(nameof(resources));

            resources.Set(DrawItemListEntryResource, DrawItemListEntry);
            resources.Set(DrawItemGridEntryResource, DrawItemGridEntry);
        }

        public static void DrawItemListEntry(
            ListBoxItem<ItemEntry> control,
            ItemEntry item,
            List<MySprite> sprites)
        {
            if (control == null || item == null || sprites == null)
                return;

            var rect = control.GetViewBox();
            var layoutScale = control.LayoutScale;
            var textScale = layoutScale * control.FontScale;
            var amount = (double)item.Amount;
            var amountText = string.IsNullOrEmpty(item.AmountText)
                ? FormatingHelper.FormatItemQty(amount)
                : item.AmountText;
            var textColor = item.ListTextColor.A == 0 ? control.TextColor : item.ListTextColor;
            var amountColor = item.ListAmountColor.A == 0 ? textColor : item.ListAmountColor;

            if (control.HasStyleClass("ItemRowOdd") &&
                !control.IsMouseOver &&
                !control.IsPressed)
            {
                var alternate = control.ResolveColor(ThemeResources.SurfaceContainerColor);
                sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
                {
                    Position = rect.Center,
                    Size = rect.Size,
                    Color = new Color(alternate, 0.18f),
                    Alignment = TextAlignment.CENTER
                });
            }

            var iconSize = Math.Min(rect.Height, ITEM_ROW_HEIGHT * layoutScale);
            var iconRect = new RectangleF(rect.X, rect.Y, iconSize, iconSize);
            var icon = string.IsNullOrEmpty(item.Icon) ? ResolveItemIcon(item.ItemType, control) : item.Icon;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = string.IsNullOrEmpty(icon) ? "MissingIcon" : icon,
                Position = iconRect.Center,
                Size = iconRect.Size,
                Color = item.ListIconColor.A == 0 ? Color.White : item.ListIconColor,
                Alignment = TextAlignment.CENTER
            });

            var amountWidth = Math.Max(
                105f * layoutScale,
                control.MeasureText(amountText, control.TextFont, textScale).X + 8f * layoutScale);
            var textLeft = iconRect.Right + 8f * layoutScale;
            var contentRight = rect.Right - ITEM_HORIZONTAL_PADDING * layoutScale;
            var nameWidth = Math.Max(0f, contentRight - textLeft - amountWidth);
            var displayName = TrimText(
                control,
                string.IsNullOrEmpty(item.DisplayName) ? ResolveItemName(item.ItemType) : item.DisplayName,
                nameWidth,
                textScale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = displayName,
                Position = new Vector2(textLeft, rect.Y),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = TextAlignment.LEFT,
                FontId = control.TextFont
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = amountText,
                Position = new Vector2(contentRight, rect.Y),
                RotationOrScale = textScale,
                Color = amountColor,
                Alignment = TextAlignment.RIGHT,
                FontId = control.TextFont
            });
        }

        public static void DrawItemGridEntry(ControlTemplate control, List<MySprite> sprites)
        {
            var item = control?.DataContext as ItemEntry;
            if (item == null || sprites == null)
                return;

            var rect = control.GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var layoutScale = control.LayoutScale;
            var textScale = layoutScale * control.FontScale;
            var amount = (double)item.Amount;
            var amountText = string.IsNullOrEmpty(item.AmountText)
                ? FormatingHelper.FormatItemQty(amount)
                : item.AmountText;
            var textColor = item.GridTextColor.A == 0 ? control.TextColor : item.GridTextColor;
            var amountColor = item.GridAmountColor.A == 0 ? textColor : item.GridAmountColor;

            DrawGridEntryBackground(control, sprites, rect);

            var padding = 15f * layoutScale;
            var inner = new RectangleF(
                rect.X + padding,
                rect.Y + padding,
                Math.Max(0f, rect.Width - padding * 2f),
                Math.Max(0f, rect.Height - padding * 2f));
            var iconSize = Math.Min(inner.Height, 60f * layoutScale);
            var iconRect = new RectangleF(inner.X, inner.Y, iconSize, iconSize);
            var icon = string.IsNullOrEmpty(item.Icon) ? ResolveItemIcon(item.ItemType, control) : item.Icon;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = string.IsNullOrEmpty(icon) ? "MissingIcon" : icon,
                Position = iconRect.Center,
                Size = iconRect.Size,
                Color = item.GridIconColor.A == 0 ? Color.White : item.GridIconColor,
                Alignment = TextAlignment.CENTER
            });

            var textLeft = iconRect.Right + 8f * layoutScale;
            var textWidth = Math.Max(0f, inner.Right - textLeft);
            var lineHeight = Math.Max(1f, inner.Height * 0.5f);
            var displayName = TrimText(
                control,
                string.IsNullOrEmpty(item.DisplayName) ? ResolveItemName(item.ItemType) : item.DisplayName,
                textWidth,
                textScale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = amountText,
                Position = new Vector2(inner.Right, inner.Y),
                RotationOrScale = FitTextScale(control, amountText, textWidth, lineHeight, textScale),
                Color = amountColor,
                Alignment = TextAlignment.RIGHT,
                FontId = control.TextFont
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = displayName,
                Position = new Vector2(inner.Right, inner.Y + lineHeight),
                RotationOrScale = FitTextScale(control, displayName, textWidth, lineHeight, textScale),
                Color = textColor,
                Alignment = TextAlignment.RIGHT,
                FontId = control.TextFont
            });
        }

        public static void InvalidateItemAssets()
        {
            ItemIcons.Clear();
            ItemNames.Clear();
        }

        public static string GetItemDisplayName(ItemEntry item)
        {
            if (item == null)
                return string.Empty;

            return string.IsNullOrEmpty(item.DisplayName)
                ? ResolveItemName(item.ItemType)
                : item.DisplayName;
        }

        public static void DrawItemSortHeader(ControlTemplate control, List<MySprite> sprites)
        {
            var model = control?.DataContext as ItemSortHeaderModel;
            if (model == null || sprites == null)
                return;

            var rect = control.Bounds;
            var layoutScale = control.LayoutScale;
            var textScale = 0.58f * layoutScale * control.FontScale;
            var ascending = control.HasStyleClass("SortAscending");
            var descending = control.HasStyleClass("SortDescending");
            var active = ascending || descending;
            var alignment = model.Column == SortMethod.Type
                ? TextAlignment.LEFT
                : TextAlignment.RIGHT;
            var indicatorWidth = active ? 18f * layoutScale : 0f;
            var text = TrimText(
                control,
                model.Text ?? string.Empty,
                Math.Max(0f, rect.Width - 16f * layoutScale - indicatorWidth),
                textScale);
            var textSize = control.MeasureText(text, control.TextFont, textScale);
            var textX = alignment == TextAlignment.LEFT
                ? rect.X + 8f * layoutScale
                : rect.Right - ITEM_HORIZONTAL_PADDING * layoutScale;
            var textY = rect.Center.Y - textSize.Y * 0.5f;
            var textColor = active || control.IsPointerOver
                ? control.ResolveColor(ThemeResources.AccentColor)
                : control.TextColor;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(textX, textY),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = alignment,
                FontId = control.TextFont
            });

            if (!active)
                return;

            var triangleX = alignment == TextAlignment.LEFT
                ? textX + textSize.X + 8f * layoutScale
                : textX - textSize.X - 8f * layoutScale;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Triangle",
                Position = new Vector2(triangleX, rect.Center.Y),
                Size = new Vector2(8f * layoutScale, 6f * layoutScale),
                RotationOrScale = descending ? MathHelper.Pi : 0f,
                Color = textColor,
                Alignment = TextAlignment.CENTER
            });
        }

        public static string GetItemStyleId(ItemEntry item)
        {
            if (item == null)
                return null;

            switch (item.AvailabilityStatus)
            {
                case ItemAvailabilityStatus.Warning:
                    return ITEM_WARNING_STYLE_ID;
                case ItemAvailabilityStatus.Error:
                    return ITEM_ERROR_STYLE_ID;
                default:
                    return null;
            }
        }

        static StyleTree BuildItemListStyles()
        {
            var styles = new StyleTree();
            Style<ListBoxItem<ItemEntry>> item = styles.For<ListBoxItem<ItemEntry>>()
                .Set(ControlTemplate.BackgroundColorProperty, Color.Transparent)
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity)
                .Animate(
                    ControlTemplate.BackgroundColorProperty,
                    0,
                    EasingMode.Linear,
                    AnimationInterpolators.Color);

            item.Id(ITEM_WARNING_STYLE_ID)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.WarningColor);
            item.Id(ITEM_ERROR_STYLE_ID)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.ErrorColor);
            item.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighColor);
            item.State(StyleState.Pressed)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighestColor);
            return styles;
        }

        static StyleTree BuildItemTableStyles()
        {
            var styles = new StyleTree();
            Style<ListBoxItem<ItemEntry>> item = styles.For<ListBoxItem<ItemEntry>>()
                .Set(ControlTemplate.BackgroundColorProperty, Color.Transparent)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity)
                .Animate(
                    ControlTemplate.BackgroundColorProperty,
                    0,
                    EasingMode.Linear,
                    AnimationInterpolators.Color);

            item.Id(ITEM_WARNING_STYLE_ID)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.WarningColor);
            item.Id(ITEM_ERROR_STYLE_ID)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.ErrorColor);
            item.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighColor);
            item.State(StyleState.Pressed)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighestColor);
            return styles;
        }

        static StyleTree BuildItemGridCardStyles()
        {
            var styles = new StyleTree();
            Style<RectangleControl> item = styles.For<RectangleControl>()
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentContainerColor)
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity)
                .Animate(
                    ControlTemplate.BackgroundColorProperty,
                    0,
                    EasingMode.Linear,
                    AnimationInterpolators.Color);

            item.Id(ITEM_WARNING_STYLE_ID)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.WarningColor);
            item.Id(ITEM_ERROR_STYLE_ID)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.ErrorColor);
            item.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighColor);
            item.State(StyleState.Pressed)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighestColor);
            return styles;
        }

        static StyleTree BuildItemGridLineStyles()
        {
            var styles = new StyleTree();
            Style<RectangleControl> item = styles.For<RectangleControl>()
                .Set(ControlTemplate.BackgroundColorProperty, Color.Transparent)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity)
                .Animate(
                    ControlTemplate.BackgroundColorProperty,
                    0,
                    EasingMode.Linear,
                    AnimationInterpolators.Color);

            item.Id(ITEM_WARNING_STYLE_ID)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.WarningColor);
            item.Id(ITEM_ERROR_STYLE_ID)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.ErrorColor);
            item.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighColor);
            item.State(StyleState.Pressed)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighestColor);
            return styles;
        }

        static void DrawGridEntryBackground(
            ControlTemplate control,
            List<MySprite> sprites,
            RectangleF rect)
        {
            if (ReferenceEquals(control.Styles, ItemGridLineStyles))
            {
                var divider = control.ResolveColor(ThemeResources.SurfaceColor);
                var thickness = Math.Max(1f, control.LayoutScale);
                AddLine(sprites, new Vector2(rect.Center.X, rect.Y), new Vector2(rect.Width, thickness), divider);
                AddLine(sprites, new Vector2(rect.Center.X, rect.Bottom), new Vector2(rect.Width, thickness), divider);
                AddLine(sprites, new Vector2(rect.X, rect.Center.Y), new Vector2(thickness, rect.Height), divider);
                AddLine(sprites, new Vector2(rect.Right, rect.Center.Y), new Vector2(thickness, rect.Height), divider);
                return;
            }

            var background = control.BackgroundColor;
            if (background.A == 0)
                return;

            var margin = 7.5f * control.LayoutScale;
            var cardRect = new RectangleF(
                rect.X + margin,
                rect.Y + margin,
                Math.Max(0f, rect.Width - margin * 2f),
                Math.Max(0f, rect.Height - margin * 2f));
            var shadow = new RectangleF(cardRect.Position + new Vector2(2f), cardRect.Size);
            BorderRenderer.CreateSpritesFromRect(
                shadow,
                sprites,
                background.MulValue(0.2f),
                radiusScale: control.LayoutScale);
            BorderRenderer.CreateSpritesFromRect(
                cardRect,
                sprites,
                background,
                radiusScale: control.LayoutScale);
        }

        static void AddLine(List<MySprite> sprites, Vector2 position, Vector2 size, Color color)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = position,
                Size = size,
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        static string ResolveItemIcon(MyItemType itemType, ControlTemplate control)
        {
            string icon;
            if (ItemIcons.TryGetValue(itemType, out icon))
                return icon;

            var definition = MyDefinitionManager.Static != null
                ? MyDefinitionManager.Static.TryGetPhysicalItemDefinition(itemType)
                : null;
            icon = definition != null
                ? TextureHelper.ResolveItemSprite(definition, control.TextSurface)
                : "MissingIcon";
            if (string.IsNullOrEmpty(icon))
                icon = "MissingIcon";

            AddToCache(ItemIcons, itemType, icon);
            return icon;
        }

        static string ResolveItemName(MyItemType itemType)
        {
            string displayName;
            if (ItemNames.TryGetValue(itemType, out displayName))
                return displayName;

            var definition = MyDefinitionManager.Static != null
                ? MyDefinitionManager.Static.TryGetPhysicalItemDefinition(itemType)
                : null;
            var localizationKey = definition != null && definition.DisplayNameEnum.HasValue
                ? definition.DisplayNameEnum.Value.ToString()
                : itemType.SubtypeId;
            displayName = MyTexts.GetString(localizationKey);
            if (string.IsNullOrEmpty(displayName))
                displayName = itemType.SubtypeId;

            AddToCache(ItemNames, itemType, displayName);
            return displayName;
        }

        static string TrimText(
            ControlTemplate control,
            string text,
            float availableWidth,
            float textScale)
        {
            if (string.IsNullOrEmpty(text) || availableWidth <= 0f)
                return string.Empty;
            if (control.MeasureText(text, control.TextFont, textScale).X <= availableWidth)
                return text;

            for (var length = text.Length - 1; length > 0; length--)
            {
                var candidate = FormatingHelper.TrimName(text, length);
                if (control.MeasureText(candidate, control.TextFont, textScale).X <= availableWidth)
                    return candidate;
            }

            return string.Empty;
        }

        static float FitTextScale(
            ControlTemplate control,
            string text,
            float availableWidth,
            float availableHeight,
            float preferredScale)
        {
            if (string.IsNullOrEmpty(text) || availableWidth <= 0f || availableHeight <= 0f)
                return preferredScale;

            var measured = control.MeasureText(text, control.TextFont, preferredScale);
            if (measured.X <= 0f || measured.Y <= 0f)
                return preferredScale;

            return preferredScale * Math.Min(
                1f,
                Math.Min(availableWidth / measured.X, availableHeight / measured.Y));
        }

        static void AddToCache<TValue>(
            Dictionary<MyItemType, TValue> cache,
            MyItemType key,
            TValue value)
        {
            cache[key] = value;
            if (cache.Count > ITEM_CACHE_LIMIT)
                cache.Remove(cache.Keys.First());
        }
    }
}
