using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.Apps
{
    internal sealed class ProjectorApp : ItemsAppBase
    {
        public const string TITLE = "DisplayName_Block_Projector";

        public string[] AllowedTypes = { "Component" };
        new ScreenConfigProjector AppConfig => (ScreenConfigProjector)base.AppConfig;

        protected override string DefaultTitle => _customTitle ?? TITLE;

        string _customTitle;

        IMyProjector _projector;
        readonly List<IMyCubeGrid> _projectorGrids = new List<IMyCubeGrid>();
        readonly List<IMySlimBlock> _projectorBlocks = new List<IMySlimBlock>();

        public override Dictionary<MyItemType, double> ItemSource => _missing;

        // Active view (components or ore-bars/ingots, depending on _showIngots).
        readonly Dictionary<MyItemType, double> _missing = new Dictionary<MyItemType, double>();
        readonly Dictionary<MyItemType, double> _needed = new Dictionary<MyItemType, double>();

        // Always tracked as components, independent of the active view (used by "Craft all").
        readonly Dictionary<MyItemType, double> _componentNeeded = new Dictionary<MyItemType, double>();
        readonly Dictionary<MyItemType, double> _componentMissing = new Dictionary<MyItemType, double>();
        readonly Dictionary<MyItemType, double> _ingotNeeded = new Dictionary<MyItemType, double>();

        int _totalBlocks = 1;
        int _remainingBlocks;

        int _totalComponents;
        int _missingComponents;
        int _componentMissingTotal;
        bool _showIngots;

        string _required = "Req";
        string _available = "Ava";

        float _requiredX;
        float _availableX;
        bool _projectorDataInitialized;
#if EXPERIMENTAL
        string _projectorDiagKey;
#endif
        Button _craftAllButton;
        ControlStyle _craftAllButtonStyle;
        ControlStyle _craftAllDisabledButtonStyle;
        Button _toggleViewButton;
        ControlStyle _toggleViewButtonStyle;

        const float PIE_RADIUS = 40;
        const string CRAFT_ALL_TEXT = "Craft all";
        const string INGOT_TYPE_ID = "MyObjectBuilder_Ingot";
        const string LOC_INGOTS_LABEL = "LcdMod_Projector_Ingots";
        const string LOC_COMPONENTS_LABEL = "DisplayName_InventoryConstraint_Components";

        public bool IsLoading { get; private set; }

        public ProjectorApp(ScreenConfigProjector config, IAppHost host) : base(config, host)
        {
        }

        struct ProjectorFooterLayout
        {
            public float Height;
            public float Top;
            public float ContentTop;
            public float ContentLeft;
            public float TextRight;
            public Vector2 PieCenter;
            public RectangleF ButtonRect;
            public RectangleF ToggleRect;
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            _projectorDataInitialized = false;
            _customTitle = _projector?.CustomName;

            var raA = MyTexts.Get(MyStringId.GetOrCompute("ScreenTerminalProduction_RequiredAndAvailable")).ToString()
                .Split('/');
            if (raA.Length == 2)
            {
                _required = raA.First().Trim();
                _available = raA.Last().Trim();
            }

            _requiredX = Surface.MeasureStringInPixels(new StringBuilder(_required), "White", 1).X;
            _availableX = Surface.MeasureStringInPixels(new StringBuilder(_available), "White", 1).X;
        }

        protected override void DrawFooter(List<MySprite> frame)
        {
            if (_projector?.CustomName != _customTitle)
                LayoutChanged();

            if (_projector == null)
                return;

            // Guard on the component requirement (always tracked) so the footer — and its toggle
            // button — stays visible even when the active ore-bar view computes to zero.
            if (_totalBlocks == 0 || _componentNeeded.Count == 0)
                return;

            int built = Math.Max(_totalBlocks - _remainingBlocks, 0);
            float textScale = Scale * 0.9f * FontScale;
            var lineSpacer = GetFooterLineSpacer();
            var legendSize = GetFooterLegendSize();
            var pieSize = GetFooterPieSize();
            var layout = CreateFooterLayout();
            var pos = new Vector2(layout.ContentLeft, layout.ContentTop);

            FooterHeight = layout.Height;
            pos.X += pieSize.X;

            var footerTop = layout.Top;

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(ViewBox.X + ViewBox.Width * 0.5f, footerTop + FooterHeight * 0.5f),
                Size = new Vector2(ViewBox.Width, FooterHeight),
                Color = new Color(BackgroundColor.MulValue(0.8f), 0.5f),
                Alignment = TextAlignment.CENTER
            });

            float legendTextSpacing = GetFooterLegendTextSpacing();
            float pieToTextGap = 10f * Scale;

            var blocksString = MyTexts.GetString("TerminalTab_Info_Blocks");

            pos.X += legendSize.X + legendTextSpacing + pieToTextGap;

            var blocksPct = built / (float)_totalBlocks;
            var componentsPct = _totalComponents > 0 ? 1 - (float)_missingComponents / _totalComponents : 1f;

            StringBuilder sb = new StringBuilder($"{blocksString}{blocksPct:P2}  ({built}/{_totalBlocks} )");

            TrimText(ref sb, layout.TextRight - pos.X, 0.9f);

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = sb.ToString(),
                Position = pos,
                RotationOrScale = textScale,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            pos.Y += lineSpacer;

            var components = GetMaterialLabel();

            sb.Clear();
            sb.Append(
                $"{components}: {componentsPct:P2}  ({FormatingHelper.FormatItemQty(_totalComponents - _missingComponents)}" +
                $"/{FormatingHelper.FormatItemQty(_totalComponents)})");


            TrimText(ref sb, layout.TextRight - pos.X, 0.9f);

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = sb.ToString(),
                Position = pos,
                RotationOrScale = textScale,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            pos.X -= legendSize.X + legendTextSpacing;

            pos.Y -= lineSpacer - (legendSize.Y + legendSize.Y / 2);

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = pos,
                Size = legendSize,
                Color = AppConfig.HeaderColor,
                Alignment = TextAlignment.CENTER,
            });

            pos.Y += lineSpacer;

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = pos,
                Size = legendSize,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
            });

            PieDualChartPanel.CreateSprites(
                frame,
                "",
                (IMyTextSurface)Surface,
                ToScreenMargin(layout.PieCenter),
                pieSize,
                componentsPct,
                blocksPct,
                AppConfig.HeaderColor,
                true,
                false);

            DrawToggleViewButton(frame, layout);
            DrawCraftAllButton(frame, layout, _componentMissingTotal > 0);
        }

        string GetMaterialLabel()
        {
            return MyTexts.GetString(_showIngots ? LOC_INGOTS_LABEL : LOC_COMPONENTS_LABEL);
        }

        string GetToggleViewButtonText()
        {
            return MyTexts.GetString(_showIngots ? LOC_COMPONENTS_LABEL : LOC_INGOTS_LABEL);
        }

        public override void Update()
        {
            IsLoading = false;
            EnsureData();

            if (!_projectorDataInitialized && AppConfig != null && AppConfig.ReferenceBlock != 0 && _projector == null)
            {
                _projectorDataInitialized = true;
                IsLoading = true;
                return;
            }

            _projectorDataInitialized = true;
            base.Update();
        }

        protected override List<KeyValuePair<MyItemType, double>> ReadItems(IMyTerminalBlock lcd)
        {
            if (lcd == null || ItemSource == null)
                return new List<KeyValuePair<MyItemType, double>>();

            var list = ItemSource.ToList();
            switch (SortMethod)
            {
                case SortMethod.Type:
                    list.Sort((a, b) =>
                    {
                        var typeCmp = string.Compare(a.Key.TypeId, b.Key.TypeId, StringComparison.CurrentCulture);
                        if (typeCmp != 0)
                            return typeCmp;
                        return string.Compare(a.Key.SubtypeId, b.Key.SubtypeId, StringComparison.CurrentCulture);
                    });
                    break;
                default:
                    list.Sort((a, b) => b.Value.CompareTo(a.Value));
                    break;
            }

            return list;
        }

        protected override ItemViewModel GetOrCreateItemViewModel(KeyValuePair<MyItemType, double> item)
        {
            var viewModel = base.GetOrCreateItemViewModel(item);
            var shortageColor = GetShortageColor(item.Key, item.Value);
            var rowColor = shortageColor ?? Surface.ScriptForegroundColor;
            var useAlertText = shortageColor.HasValue && AppConfig.DrawLines;
            var panelColor = AppConfig.HeaderColor;
            var panelTextColor = Surface.ScriptForegroundColor;
            var neededText = FormatingHelper.FormatItemQty(GetNeededQty(item.Key));
            var availableText = FormatingHelper.FormatItemQty(GetAvailableQty(item.Key, item.Value));

            viewModel.PrimaryAmountText = neededText;
            viewModel.SecondaryAmountText = availableText;
            viewModel.AmountText = availableText + "/" + neededText;
            viewModel.ListTextColor = rowColor;
            viewModel.ListIconColor = Color.White;
            viewModel.IconBackgroundColor = shortageColor.HasValue && shortageColor.Value.Equals(AppConfig.ErrorColor)
                ? AppConfig.ErrorColor
                : Color.White;
            viewModel.GridTextColor = useAlertText ? shortageColor.Value : panelTextColor;
            viewModel.GridIconColor = Color.White;
            viewModel.PanelColor = shortageColor ?? panelColor;
            return viewModel;
        }

        protected override double GetDefaultCraftAmount(ItemViewModel item)
        {
            return item == null ? 1d : Math.Max(1d, Math.Ceiling(item.Amount));
        }

        protected override void DrawListItemContent(List<MySprite> frame, ItemViewModel item, RectangleF bounds)
        {
            var margin = 0f;
            var xStart = bounds.X + margin;
            var xEnd = bounds.Right - margin;
            Vector2 position = bounds.Position;
            position.X = xStart;

            bool drawSeparatorLine = AppConfig.SortMethod == (int)SortMethod.Type && PreviousType != item.TypeId;

            if (AppConfig.DrawLines || drawSeparatorLine)
            {
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = new Vector2((xStart + xEnd) / 2f, position.Y),
                    Size = new Vector2(xEnd - xStart, 1),
                    Color = drawSeparatorLine ? AppConfig.HeaderColor : Surface.ScriptForegroundColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            PreviousType = item.TypeId;

            DrawItemIcon(frame,
                item.Icon,
                position + new Vector2(20f, 15) * Scale,
                new Vector2(LINE_HEIGHT * Scale),
                TextAlignment.CENTER,
                item.IconBackgroundColor);
            position.X += (xEnd - xStart) / 8f;
            var quantityColumnsWidth = 2f * GetQuantityColumnWidth() + GetQuantityColumnGap();

            var clip = new Rectangle((int)position.X, (int)position.Y,
                (int)(xEnd - position.X - quantityColumnsWidth),
                (int)(position.Y + (LINE_HEIGHT + 5) * Scale));

            frame.Add(MySprite.CreateClipRect(clip));

            var localizedName = TrimText(item.DisplayName, clip.Width);

            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = localizedName,
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = item.ListTextColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });
            frame.Add(MySprite.CreateClearClipRect());
            position.X = xEnd;
            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = item.PrimaryAmountText,
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = item.ListTextColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });
            position.X -= GetQuantityColumnWidth() + GetQuantityColumnGap();
            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = item.SecondaryAmountText,
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = item.ListTextColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });
        }

        protected override void DrawCellContent(List<MySprite> frame, ItemViewModel item,
            MyTuple<RectangleF, RectangleF, RectangleF> slots)
        {
            var iconRect = slots.Item1;
            var numberRect = slots.Item2;
            var nameRect = slots.Item3;

            DrawItemIcon(frame,
                item.Icon,
                new Vector2(iconRect.X, iconRect.Y + iconRect.Height / 2f),
                new Vector2(iconRect.Width),
                TextAlignment.LEFT,
                item.IconBackgroundColor);

            var localizedName = TrimText(item.DisplayName, nameRect.Width);

            Vector2 size = FormatingHelper.GetSizeInPixel(localizedName, "White", 1, Surface);
            float minProportion = Math.Min(nameRect.Width / size.X, nameRect.Height / size.Y);
            float fontSize = minProportion;
            float renderedHeight = size.Y * fontSize * FontScale;
            Vector2 pos = nameRect.Center;
            pos.Y -= renderedHeight * 0.5f;
            pos.X = nameRect.Right;

            frame.Add(new MySprite(
                SpriteType.TEXT,
                localizedName,
                pos,
                null,
                item.GridTextColor,
                "White",
                TextAlignment.RIGHT,
                fontSize * .95f * FontScale
            ));

            var qty = item.AmountText;
            size = FormatingHelper.GetSizeInPixel(qty, "White", 1, Surface);
            minProportion = Math.Min(numberRect.Width / size.X, numberRect.Height / size.Y);
            fontSize = minProportion;
            renderedHeight = size.Y * fontSize * FontScale;
            pos = numberRect.Center;
            pos.Y -= renderedHeight * 0.5f;
            pos.X = numberRect.Right;

            frame.Add(new MySprite(
                SpriteType.TEXT,
                qty,
                pos,
                null,
                item.GridTextColor,
                "White",
                TextAlignment.RIGHT,
                fontSize * .95f * FontScale
            ));
        }

        protected override void DrawCellBackground(List<MySprite> frame, ItemViewModel item,
            float xStart, float xEnd, float yStart, float cellHeight, float cellPadding)
        {
            var rl = xStart + cellPadding / 2;
            var rr = xEnd - cellPadding / 2;
            var rt = yStart + cellPadding / 2;
            var rb = yStart + cellHeight - cellPadding / 2;

            var backgroundColor = item.PanelColor;
            var a = backgroundColor.ColorToHSV();
            a.Z *= 0.2f;
            var cellRect = new RectangleF(rl, rt, rr - rl, rb - rt);
            var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
            Border.CreateSpritesFromRect(dropShadow, frame, a.HSVtoColor(), radiusScale: Scale);
            Border.CreateSpritesFromRect(cellRect, frame, backgroundColor, radiusScale: Scale);
        }

        double GetNeededQty(MyItemType itemType)
        {
            double needed;
            return _needed.TryGetValue(itemType, out needed) ? needed : 0d;
        }

        double GetAvailableQty(MyItemType itemType, double missingQty)
        {
            var needed = GetNeededQty(itemType);
            var have = needed - missingQty;
            return have < 0 ? 0 : have;
        }

        Color? GetShortageColor(MyItemType itemType, double missingQty)
        {
            var needed = GetNeededQty(itemType);
            if (needed <= 0)
                return null;

            var available = GetAvailableQty(itemType, missingQty);
            if (available <= 0)
                return AppConfig.ErrorColor;

            if (available < needed)
                return AppConfig.WarningColor;

            return null;
        }

        float GetQuantityColumnWidth()
        {
            var labelWidth = Math.Max(_requiredX, _availableX) * Scale * 1.3f * FontScale + (8f * Scale);
            return Math.Max(100f * Scale, labelWidth);
        }

        float GetQuantityColumnGap()
        {
            return 20f * Scale;
        }

        ProjectorFooterLayout CreateFooterLayout()
        {
            var baseHeight = GetFooterBaseHeight();
            var buttonSize = GetCraftAllButtonSize();
            var toggleSize = GetToggleViewButtonSize();
            var buttonGap = 8f * Scale;
            var buttonsWidth = buttonSize.X + buttonGap + toggleSize.X;
            var buttonsHeight = Math.Max(buttonSize.Y, toggleSize.Y);
            var footerPaddingX = GetFooterPaddingX();
            var footerInnerPaddingX = GetFooterInnerPaddingX();
            var footerContentLeft = ViewBox.X + footerPaddingX + footerInnerPaddingX;
            var footerContentRight = ViewBox.Right - footerPaddingX - footerInnerPaddingX;
            var legendSize = GetFooterLegendSize();
            var textLeft = footerContentLeft + GetFooterPieSize().X + legendSize.X +
                           GetFooterLegendTextSpacing() + 10f * Scale;
            var minTextWidth = Math.Max(170f * Scale, Math.Max(_requiredX, _availableX) * Scale * FontScale * 2f);
            var canUseSideButton = footerContentRight - textLeft >= minTextWidth + buttonGap + buttonsWidth;

            var layout = new ProjectorFooterLayout
            {
                Height = canUseSideButton ? baseHeight : baseHeight + buttonGap + buttonsHeight,
                ContentLeft = footerContentLeft,
                TextRight = footerContentRight
            };

            layout.Top = ViewBox.Bottom - layout.Height;
            layout.ContentTop = layout.Top + GetFooterPaddingY();
            layout.PieCenter = new Vector2(
                ViewBox.X + GetFooterInnerPaddingX() + GetFooterPieSize().X * 0.5f,
                layout.Top + baseHeight * 0.5f);

            if (canUseSideButton)
            {
                var buttonTop = layout.Top + (baseHeight - buttonSize.Y) * 0.5f;
                layout.ButtonRect = new RectangleF(
                    footerContentRight - buttonSize.X,
                    buttonTop,
                    buttonSize.X,
                    buttonSize.Y);
                layout.ToggleRect = new RectangleF(
                    layout.ButtonRect.X - buttonGap - toggleSize.X,
                    layout.Top + (baseHeight - toggleSize.Y) * 0.5f,
                    toggleSize.X,
                    toggleSize.Y);
                layout.TextRight = layout.ToggleRect.X - buttonGap;
            }
            else
            {
                var availableWidth = Math.Max(0f, footerContentRight - footerContentLeft);
                var totalWidth = Math.Min(availableWidth, buttonsWidth);
                var craftWidth = buttonSize.X;
                var toggleWidth = toggleSize.X;
                if (totalWidth < buttonsWidth && buttonsWidth > 0f)
                {
                    var ratio = totalWidth / buttonsWidth;
                    craftWidth *= ratio;
                    toggleWidth *= ratio;
                }

                var rowTop = layout.Top + baseHeight + buttonGap;
                var startX = footerContentLeft + (availableWidth - (toggleWidth + buttonGap + craftWidth)) * 0.5f;
                layout.ToggleRect = new RectangleF(startX, rowTop, toggleWidth, toggleSize.Y);
                layout.ButtonRect = new RectangleF(startX + toggleWidth + buttonGap, rowTop, craftWidth, buttonSize.Y);
            }

            return layout;
        }

        Vector2 GetCraftAllButtonSize()
        {
            var textScale = GetCraftAllButtonTextScale(Scale, FontScale);
            var textSize = FormatingHelper.GetSizeInPixel(CRAFT_ALL_TEXT, "White", textScale, Surface);
            return new Vector2(
                Math.Max(112f * Scale, textSize.X + 24f * Scale),
                Math.Max(28f * Scale, FormatingHelper.LineHeight(textScale, Surface) + 10f * Scale));
        }

        Vector2 GetToggleViewButtonSize()
        {
            var textScale = GetCraftAllButtonTextScale(Scale, FontScale);
            var ingotsSize = FormatingHelper.GetSizeInPixel(MyTexts.GetString(LOC_INGOTS_LABEL), "White", textScale, Surface);
            var componentsSize = FormatingHelper.GetSizeInPixel(MyTexts.GetString(LOC_COMPONENTS_LABEL), "White", textScale, Surface);
            var textWidth = Math.Max(ingotsSize.X, componentsSize.X);
            return new Vector2(
                Math.Max(112f * Scale, textWidth + 24f * Scale),
                GetCraftAllButtonSize().Y);
        }

        static float GetCraftAllButtonTextScale(float scale, float fontScale)
        {
            return 0.58f * scale * fontScale;
        }

        float GetFooterPaddingX()
        {
            return GetFooterLegendSize().X + GetFooterLegendTextSpacing();
        }

        float GetFooterInnerPaddingX()
        {
            return 6f * Scale;
        }

        float GetFooterPaddingY()
        {
            return GetFooterLegendSize().Y;
        }

        Vector2 GetFooterPieSize()
        {
            return new Vector2(PIE_RADIUS * Scale);
        }

        Vector2 GetFooterLegendSize()
        {
            return new Vector2(8f, 8f) * Scale * FontScale;
        }

        float GetFooterLegendTextSpacing()
        {
            return GetFooterLegendSize().X * 0.5f;
        }

        float GetFooterLineSpacer()
        {
            return 25f * LayoutScale;
        }

        float GetFooterTextHeight()
        {
            return 25f * 2f * LayoutScale;
        }

        float GetFooterBaseHeight()
        {
            var pieSize = GetFooterPieSize();
            return Math.Max(GetFooterTextHeight(), pieSize.Y) + GetFooterPaddingY() * 2f;
        }

        void DrawCraftAllButton(List<MySprite> frame, ProjectorFooterLayout layout, bool enabled)
        {
            if (layout.ButtonRect.Width <= 0f || layout.ButtonRect.Height <= 0f)
                return;

            EnsureCraftAllButton(layout.ButtonRect);
            ConfigureCraftAllButton(enabled);

            if (!InteractiveList.Contains(_craftAllButton))
                InteractiveList.Add(_craftAllButton);

            _craftAllButton.Render(CreateItemRenderContext(), frame);
        }

        void DrawToggleViewButton(List<MySprite> frame, ProjectorFooterLayout layout)
        {
            if (layout.ToggleRect.Width <= 0f || layout.ToggleRect.Height <= 0f)
                return;

            if (_toggleViewButton == null)
            {
                _toggleViewButton = new Button(layout.ToggleRect, new ButtonModel
                {
                    Text = GetToggleViewButtonText(),
                    Clicked = OnToggleViewClicked
                });
            }
            else
            {
                _toggleViewButton.SetRect(layout.ToggleRect);
            }

            var model = _toggleViewButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = GetToggleViewButtonText();
                model.Enabled = true;
            }

            _toggleViewButton.SetVisible(true);
            _toggleViewButton.SetCursor(CursorType.Hand);
            _toggleViewButton.SetStyle(GetToggleViewButtonStyle());
            _toggleViewButton.CustomRender = RenderCraftAllButton;

            if (!InteractiveList.Contains(_toggleViewButton))
                InteractiveList.Add(_toggleViewButton);

            _toggleViewButton.Render(CreateItemRenderContext(), frame);
        }

        ControlStyle GetToggleViewButtonStyle()
        {
            if (_toggleViewButtonStyle == null)
                _toggleViewButtonStyle = ControlStyle.FromThemeRoles(
                    Constants.ON_SECONDARY_CONTAINER,
                    Constants.SECONDARY_CONTAINER,
                    Constants.SECONDARY_CONTAINER + Constants.HOVER,
                    Constants.ON_SECONDARY_CONTAINER,
                    Theme);
            else
                _toggleViewButtonStyle.ThemeColors = Theme;

            _toggleViewButtonStyle.BorderRadiusPixels = Border.DEFAULT_RADIUS_PIXELS;
            return _toggleViewButtonStyle;
        }

        void OnToggleViewClicked(ButtonModel model, object sender)
        {
            try
            {
                _showIngots = !_showIngots;
                Update();
                Host.RenderSprites();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }
        }

        void EnsureCraftAllButton(RectangleF rect)
        {
            if (_craftAllButton == null)
            {
                _craftAllButton = new Button(rect, new ButtonModel
                {
                    Text = CRAFT_ALL_TEXT,
                    Clicked = OnCraftAllClicked
                });
            }
            else
            {
                _craftAllButton.SetRect(rect);
            }

            _craftAllButton.SetVisible(true);
        }

        void ConfigureCraftAllButton(bool enabled)
        {
            var model = _craftAllButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = CRAFT_ALL_TEXT;
                model.Enabled = enabled;
            }

            _craftAllButton.SetCursor(enabled ? CursorType.Hand : CursorType.Default);
            _craftAllButton.SetStyle(GetCraftAllButtonStyle(enabled));
            _craftAllButton.CustomRender = RenderCraftAllButton;
        }

        ControlStyle GetCraftAllButtonStyle(bool enabled)
        {
            if (enabled)
            {
                if (_craftAllButtonStyle == null)
                    _craftAllButtonStyle = Button.CreatePrimaryButtonStyle(Theme);
                else
                    _craftAllButtonStyle.ThemeColors = Theme;

                return _craftAllButtonStyle;
            }

            if (_craftAllDisabledButtonStyle == null)
                _craftAllDisabledButtonStyle = Button.CreateDisabledButtonStyle(Theme);
            else
                _craftAllDisabledButtonStyle.ThemeColors = Theme;

            return _craftAllDisabledButtonStyle;
        }

        void RenderCraftAllButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var model = control.DataContext as ButtonModel;
            var enabled = model == null || model.Enabled;
            var rect = control.Bounds;
            var hover = enabled && rect.Contains(context.CursorPosition);
            var buttonColor = context.Style.GetPanelColor(hover);
            var textColor = context.Style.GetTextColor(hover);
            var text = model == null || string.IsNullOrEmpty(model.Text) ? CRAFT_ALL_TEXT : model.Text;
            var textScale = GetCraftAllButtonTextScale(context.Scale, context.FontScale);

            Border.CreateSpritesFromRect(rect, sprites, buttonColor, radiusScale: context.Scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X,
                    rect.Center.Y - FormatingHelper.GetSizeInPixel(text, "White", textScale, context.Surface).Y * 0.5f),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });
        }

        void OnCraftAllClicked(ButtonModel model, object sender)
        {
            if (_componentMissingTotal <= 0)
                return;

            var interactiveHost = Host as InteractiveSurfaceScript;
            if (interactiveHost == null)
                return;

            var requests = BuildCraftAllRequests();
            if (requests.Count == 0)
                return;

            interactiveHost.ShowDialog(new CraftDialog(
                this,
                GridLogic,
                requests,
                delegate(Dialog dialog) { interactiveHost.ShowDialog(dialog); }));
        }

        List<CraftDialog.CraftRequest> BuildCraftAllRequests()
        {
            // "Craft all" always operates on the missing components, regardless of the active view.
            var requests = new List<CraftDialog.CraftRequest>();

            foreach (var item in _componentMissing)
            {
                if (item.Value <= 0d)
                    continue;

                requests.Add(new CraftDialog.CraftRequest(
                    item.Key,
                    ResolveDisplayName(item.Key),
                    ResolveSprite(item.Key),
                    item.Value));
            }

            return requests;
        }

        void EnsureData()
        {
            _missing.Clear();
            _needed.Clear();
            _componentNeeded.Clear();
            _componentMissing.Clear();
            _ingotNeeded.Clear();
            _totalBlocks = 1;
            _remainingBlocks = 0;
            _totalComponents = 0;
            _missingComponents = 0;
            _componentMissingTotal = 0;

            var lcd = Block as IMyTerminalBlock;

            IMyCubeGrid grid = Block?.CubeGrid;

            if (grid == null)
                return;

            FindProjector(grid, ref _projector);

#if EXPERIMENTAL
            if (_projector == null)
                LogHelper.LogOnce("projector_null",
                    "[Projector] nenhum projetor resolvido (configRef=" + AppConfig.ReferenceBlock + ")");
#endif

            if (_projector == null)
                return;

            try
            {
                _totalBlocks = Math.Max(_projector.TotalBlocks, 1);
                _remainingBlocks = Math.Max(_projector.RemainingBlocks, 0);
            }
            catch
            {
                _totalBlocks = 1;
                _remainingBlocks = 0;
            }

            try
            {
#if EXPERIMENTAL
                var diagSignature = _projector.EntityId + ":" + _totalBlocks + ":" + _remainingBlocks;
                StringBuilder diag = null;
                if (diagSignature != _projectorDiagKey)
                {
                    _projectorDiagKey = diagSignature;
                    diag = new StringBuilder();
                    diag.Append("[Projector] '").Append(_projector.CustomName)
                        .Append("' projectorGrid=").Append(_projector.CubeGrid.GridSizeEnum)
                        .Append(" projectedGrid=").Append(_projector.ProjectedGrid != null ? "yes" : "NULL")
                        .Append(" total=").Append(_totalBlocks)
                        .Append(" remaining=").Append(_remainingBlocks)
                        .Append(" types=").Append(_projector.RemainingBlocksPerType.Count).Append('\n');
                }
#endif
                foreach (var block in _projector.RemainingBlocksPerType)
                {
                    var def = block.Key as MyCubeBlockDefinition;
                    if (def == null)
                        continue;

                    AccumulateComponents(def, block.Value);
#if EXPERIMENTAL
                    AppendBlockDiag(diag, def, block.Value);
#endif
                }

                // RemainingBlocksPerType comes back empty in several valid projector states (the
                // projection isn't currently weldable, the projector just loaded the blueprint, etc.).
                // Fall back to the projected hologram grid: its blocks ARE the ones still to build, so the
                // requirements never render blank while a projection is up.
                if (_componentNeeded.Count == 0 && _projector.ProjectedGrid != null)
                {
                    _projectorBlocks.Clear();
                    _projector.ProjectedGrid.GetBlocks(_projectorBlocks);
                    for (int i = 0; i < _projectorBlocks.Count; i++)
                    {
                        var def = _projectorBlocks[i].BlockDefinition as MyCubeBlockDefinition;
                        if (def == null)
                            continue;

                        AccumulateComponents(def, 1);
#if EXPERIMENTAL
                        AppendBlockDiag(diag, def, 1);
#endif
                    }
#if EXPERIMENTAL
                    if (diag != null)
                        diag.Append("  (fallback ProjectedGrid: blocks=").Append(_projectorBlocks.Count).Append(")\n");
#endif
                }

#if EXPERIMENTAL
                if (diag != null)
                    LogHelper.LogInfo(diag.ToString());
#endif
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }

            // Component shortage is always tracked so "Craft all" works in either view.
            var availableComponents = GetAvailableComponents(lcd);
            long componentMissing = 0;
            foreach (var needed in _componentNeeded)
            {
                double available;
                availableComponents.TryGetValue(needed.Key, out available);

                double missing = needed.Value - available;
                if (missing < 0) missing = 0;

                _componentMissing[needed.Key] = missing;
                componentMissing += (long)Math.Round(missing);
            }

            _componentMissingTotal = (int)Math.Max(0, componentMissing);

            if (_showIngots)
            {
                BuildIngotNeeded(_componentNeeded, _ingotNeeded);
                PopulateActiveView(_ingotNeeded, GetAvailableIngots(lcd));
            }
            else
            {
                PopulateActiveView(_componentNeeded, availableComponents);
            }
        }

        void AccumulateComponents(MyCubeBlockDefinition def, int blockCount)
        {
            if (def.Components == null)
                return;

            foreach (var perType in def.Components)
            {
                double qty;
                _componentNeeded.TryGetValue(perType.Definition.Id, out qty);
                _componentNeeded[perType.Definition.Id] = qty + perType.Count * blockCount;
            }
        }

#if EXPERIMENTAL
        void AppendBlockDiag(StringBuilder diag, MyCubeBlockDefinition def, int blockCount)
        {
            if (diag == null || def.Components == null)
                return;

            diag.Append("  ").Append(def.Id.SubtypeName)
                .Append(" [").Append(def.CubeSize).Append("] x").Append(blockCount);
            for (int c = 0; c < def.Components.Length; c++)
                diag.Append(" | ").Append(def.Components[c].Definition.Id.SubtypeName)
                    .Append('=').Append(def.Components[c].Count);
            diag.Append('\n');
        }
#endif

        void PopulateActiveView(Dictionary<MyItemType, double> neededByType, Dictionary<MyItemType, double> availableByType)
        {
            long totalNeeded = 0;
            long totalMissing = 0;

            foreach (var needed in neededByType)
            {
                double available;
                availableByType.TryGetValue(needed.Key, out available);

                double missing = needed.Value - available;
                if (missing < 0) missing = 0;

                _needed[needed.Key] = needed.Value;
                _missing[needed.Key] = missing;

                totalNeeded += (long)Math.Round(needed.Value);
                totalMissing += (long)Math.Round(missing);
            }

            _totalComponents = (int)Math.Max(0, totalNeeded);
            _missingComponents = (int)Math.Max(0, totalMissing);
        }

        // Estimates the ore-bars (ingots) consumed by the still-needed components, expanding each
        // component through its primary blueprint. Components that are only a secondary blueprint
        // result (absent from PrimaryBlueprintByCreatedItem) are skipped, so the total is a lower-bound
        // estimate. Only "MyObjectBuilder_Ingot" prerequisites are counted.
        void BuildIngotNeeded(Dictionary<MyItemType, double> componentNeeded, Dictionary<MyItemType, double> ingotNeeded)
        {
            ingotNeeded.Clear();
            if (componentNeeded.Count == 0)
                return;

            try
            {
                LcdMod.Client.Grid.GridLogic.EnsureBlueprintResultDatabase();

                foreach (var component in componentNeeded)
                {
                    if (component.Value <= 0d)
                        continue;

                    MyDefinitionId componentId = component.Key;
                    MyBlueprintDefinitionBase blueprint;
                    if (!LcdMod.Client.Grid.GridLogic.PrimaryBlueprintByCreatedItem.TryGetValue(componentId, out blueprint) ||
                        blueprint == null)
                        continue;

                    double resultAmount = GetBlueprintResultAmount(blueprint, componentId);
                    if (resultAmount <= 0d)
                        resultAmount = 1d;

                    double cycles = component.Value / resultAmount;

                    var prerequisites = blueprint.Prerequisites;
                    if (prerequisites == null)
                        continue;

                    for (int i = 0; i < prerequisites.Length; i++)
                    {
                        MyItemType ingotType = prerequisites[i].Id;
                        if (ingotType.TypeId != INGOT_TYPE_ID)
                            continue;

                        double amount = (double)prerequisites[i].Amount * cycles;
                        double current;
                        ingotNeeded.TryGetValue(ingotType, out current);
                        ingotNeeded[ingotType] = current + amount;
                    }
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }
        }

        static double GetBlueprintResultAmount(MyBlueprintDefinitionBase blueprint, MyDefinitionId itemId)
        {
            var results = blueprint.Results;
            if (results == null)
                return 1d;

            for (int i = 0; i < results.Length; i++)
                if (results[i].Id.Equals(itemId))
                    return (double)results[i].Amount;

            return 1d;
        }

        Dictionary<MyItemType, double> GetAvailableIngots(IMyTerminalBlock referenceBlock)
        {
            try
            {
                var hasFilter = AppConfig.SelectedBlocks.Length > 0 || AppConfig.SelectedGroups.Length > 0;
                return hasFilter ? GridLogic.GetIngots(AppConfig, referenceBlock) : GridLogic.Ingots;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }

            return new Dictionary<MyItemType, double>();
        }

        Dictionary<MyItemType, double> GetAvailableComponents(IMyTerminalBlock referenceBlock)
        {
            try
            {
                var hasFilter = AppConfig.SelectedBlocks.Length > 0 || AppConfig.SelectedGroups.Length > 0;
                return hasFilter ? GridLogic.GetItems(AppConfig, referenceBlock, AllowedTypes) : GridLogic.Components;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }

            return new Dictionary<MyItemType, double>();
        }

        void FindProjector(IMyCubeGrid grid, ref IMyProjector projector)
        {
            if (AppConfig.ReferenceBlock == 0)
            {
                projector = ResolveSingleLoadedProjector(grid);
                return;
            }

            if (projector != null && projector.EntityId == AppConfig.ReferenceBlock)
                return;

            var entity = MyAPIGateway.Entities.GetEntityById(AppConfig.ReferenceBlock) as IMyProjector;
            projector = entity?.CubeGrid.IsInSameLogicalGroupAs(grid) ?? false ? entity : null;
        }

        IMyProjector ResolveSingleLoadedProjector(IMyCubeGrid rootGrid)
        {
            if (rootGrid == null)
                return null;

            IMyProjector found = null;
            _projectorGrids.Clear();

            MyAPIGateway.GridGroups.GetGroup(rootGrid, GridLinkTypeEnum.Logical, _projectorGrids);
            if (_projectorGrids.Count == 0 || !_projectorGrids.Contains(rootGrid))
                _projectorGrids.Add(rootGrid);

            for (int i = 0; i < _projectorGrids.Count; i++)
            {
                var grid = _projectorGrids[i];
                if (grid == null)
                    continue;

                _projectorBlocks.Clear();
                grid.GetBlocks(_projectorBlocks);

                for (int j = 0; j < _projectorBlocks.Count; j++)
                {
                    var candidate = _projectorBlocks[j].FatBlock as IMyProjector;
                    if (candidate == null || candidate.Closed || candidate.ProjectedGrid == null)
                        continue;

                    if (found != null)
                        return null;

                    found = candidate;
                }
            }

            return found;
        }
    }
}
