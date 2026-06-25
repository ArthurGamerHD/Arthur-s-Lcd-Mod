using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
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
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(17)]
    [ConfigComponent(PROJECTOR_REFERENCE, typeof(BlockReferenceConfigComponent), PropertyName = "ProjectorReferenceComponent")]
    internal sealed partial class ProjectorApp : ItemsApp
    {
        public const string TITLE = "DisplayName_Block_Projector";

        public string[] AllowedTypes = { "Component" };

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
        Button _craftAllButton;
        Button _toggleViewButton;

        const float PIE_RADIUS = 40;
        const string CRAFT_ALL_TEXT = "Craft all";
        const string INGOT_TYPE_ID = "MyObjectBuilder_Ingot";
        const string LOC_INGOTS_LABEL = MOD_PREFIX + "Projector_Ingots";
        const string LOC_COMPONENTS_LABEL = "DisplayName_InventoryConstraint_Components";

        public bool IsLoading { get; private set; }

        public ProjectorApp(IAppHost host) : base(host)
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

            _requiredX = Surface.MeasureStringInPixels(new StringBuilder(_required), TextFont, 1).X;
            _availableX = Surface.MeasureStringInPixels(new StringBuilder(_available), TextFont, 1).X;
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
                FontId = TextFont
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
                FontId = TextFont
            });

            pos.X -= legendSize.X + legendTextSpacing;

            pos.Y -= lineSpacer - (legendSize.Y + legendSize.Y / 2);

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = pos,
                Size = legendSize,
                Color = GetHeaderColor(),
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
                GetHeaderColor(),
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

            if (!_projectorDataInitialized && ProjectorReferenceComponent.EntityId != 0 && _projector == null)
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
            var useAlertText = shortageColor.HasValue && GeneralComponent.DrawLines;
            var panelColor = GetHeaderColor();
            var panelTextColor = Surface.ScriptForegroundColor;
            var neededText = FormatingHelper.FormatItemQty(GetNeededQty(item.Key));
            var availableText = FormatingHelper.FormatItemQty(GetAvailableQty(item.Key, item.Value));

            viewModel.SetQuotaAmount(availableText, neededText);
            viewModel.ListTextColor = rowColor;
            viewModel.ListAmountColor = rowColor;
            viewModel.ListIconColor = Color.White;
            viewModel.IconBackgroundColor = shortageColor.HasValue && shortageColor.Value.Equals(ColorComponent.ResolveErrorColor())
                ? ColorComponent.ResolveErrorColor()
                : Color.White;
            viewModel.GridTextColor = useAlertText ? shortageColor.Value : panelTextColor;
            viewModel.GridAmountColor = viewModel.GridTextColor;
            viewModel.GridIconColor = Color.White;
            viewModel.PanelColor = shortageColor ?? panelColor;
            return viewModel;
        }

        protected override double GetDefaultCraftAmount(ItemViewModel item)
        {
            return item == null ? 1d : Math.Max(1d, Math.Ceiling(item.Amount));
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
            BorderRenderer.CreateSpritesFromRect(dropShadow, frame, a.HSVtoColor(), radiusScale: Scale);
            BorderRenderer.CreateSpritesFromRect(cellRect, frame, backgroundColor, radiusScale: Scale);
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
                return ColorComponent.ResolveErrorColor();

            if (available < needed)
                return ColorComponent.ResolveWarningColor();

            return null;
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
            var textSize = FormatingHelper.GetSizeInPixel(CRAFT_ALL_TEXT, TextFont, textScale, Surface);
            return new Vector2(
                Math.Max(112f * Scale, textSize.X + 24f * Scale),
                Math.Max(28f * Scale, MeasureLineHeight(textScale) + 10f * Scale));
        }

        Vector2 GetToggleViewButtonSize()
        {
            var textScale = GetCraftAllButtonTextScale(Scale, FontScale);
            var ingotsSize = FormatingHelper.GetSizeInPixel(MyTexts.GetString(LOC_INGOTS_LABEL), TextFont, textScale, Surface);
            var componentsSize = FormatingHelper.GetSizeInPixel(MyTexts.GetString(LOC_COMPONENTS_LABEL), TextFont, textScale, Surface);
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

            if (!Children.Contains(_craftAllButton))
                _children.Add(_craftAllButton);

            _craftAllButton.Render(frame);
        }

        void DrawToggleViewButton(List<MySprite> frame, ProjectorFooterLayout layout)
        {
            if (layout.ToggleRect.Width <= 0f || layout.ToggleRect.Height <= 0f)
                return;

            if (_toggleViewButton == null)
            {
                _toggleViewButton = AddChild(new Button(layout.ToggleRect, new ButtonModel
                {
                    Text = GetToggleViewButtonText(),
                    Clicked = OnToggleViewClicked
                }));
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
            _toggleViewButton.SetStyleId("Primary");
            _toggleViewButton.CustomRender = RenderCraftAllButton;

            if (!Children.Contains(_toggleViewButton))
                _children.Add(_toggleViewButton);

            _toggleViewButton.Render(frame);
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
                _craftAllButton = AddChild(new Button(rect, new ButtonModel
                {
                    Text = CRAFT_ALL_TEXT,
                    Clicked = OnCraftAllClicked
                }));
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
            _craftAllButton.SetStyleId(enabled ? "Primary" : "Disabled");
            _craftAllButton.SetEnabled(enabled);
            _craftAllButton.CustomRender = RenderCraftAllButton;
        }

        void RenderCraftAllButton(ControlTemplate control, List<MySprite> sprites)
        {
            var model = control.DataContext as ButtonModel;
            var enabled = model == null || model.Enabled;
            var rect = control.Bounds;
            var hover = enabled && rect.Contains(new Vector2(float.NaN, float.NaN));
            var button = control as Button;
            var defaultButtonColor = button?.BackgroundColor ?? control.BackgroundColor;
            var buttonColor = hover
                ? control.GetResourceColor(ThemeResources.AccentColor, defaultButtonColor)
                : defaultButtonColor;
            var textColor = control.TextColor;
            var text = model == null || string.IsNullOrEmpty(model.Text) ? CRAFT_ALL_TEXT : model.Text;
            var textScale = GetCraftAllButtonTextScale(control.LayoutScale, control.FontScale);

            BorderRenderer.CreateSpritesFromRect(rect, sprites, buttonColor, radiusScale: control.LayoutScale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X,
                    rect.Center.Y - FormatingHelper.GetSizeInPixel(text, control, textScale, control.TextSurface).Y * 0.5f),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = TextAlignment.CENTER,
                FontId = control.TextFont
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
                foreach (var block in _projector.RemainingBlocksPerType)
                {
                    var def = block.Key as MyCubeBlockDefinition;
                    if (def == null)
                        continue;

                    AccumulateComponents(def, block.Value);
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
                    }
                }
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
        // component through its blueprint (primary, or any producer as a fallback - see GridLogic). A
        // component that no blueprint produces as a primary result is skipped, so the total is a
        // lower-bound estimate. Only "MyObjectBuilder_Ingot" prerequisites are counted (gravel is the
        // Stone ingot, so reactor components now contribute it correctly).
        void BuildIngotNeeded(Dictionary<MyItemType, double> componentNeeded, Dictionary<MyItemType, double> ingotNeeded)
        {
            ingotNeeded.Clear();
            if (componentNeeded.Count == 0)
                return;

            try
            {
                GridData.GridLogic.EnsureBlueprintResultDatabase();

                foreach (var component in componentNeeded)
                {
                    if (component.Value <= 0d)
                        continue;

                    MyDefinitionId componentId = component.Key;
                    MyBlueprintDefinitionBase blueprint;
                    if (!GridData.GridLogic.PrimaryBlueprintByCreatedItem.TryGetValue(componentId, out blueprint) ||
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
                var hasFilter = BlockSelectionComponent.SelectedBlocks.Length > 0 || BlockSelectionComponent.SelectedGroups.Length > 0;
                return hasFilter ? GridLogic.GetIngots(BlockSelectionComponent, ItemSelectionComponent, referenceBlock) : GridLogic.Ingots;
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
                var hasFilter = BlockSelectionComponent.SelectedBlocks.Length > 0 || BlockSelectionComponent.SelectedGroups.Length > 0;
                return hasFilter ? GridLogic.GetItems(BlockSelectionComponent, ItemSelectionComponent, referenceBlock, AllowedTypes) : GridLogic.Components;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }

            return new Dictionary<MyItemType, double>();
        }

        void FindProjector(IMyCubeGrid grid, ref IMyProjector projector)
        {
            if (ProjectorReferenceComponent.EntityId == 0)
            {
                projector = ResolveSingleLoadedProjector(grid);
                return;
            }

            if (projector != null && projector.EntityId == ProjectorReferenceComponent.EntityId)
                return;

            var entity = MyAPIGateway.Entities.GetEntityById(ProjectorReferenceComponent.EntityId) as IMyProjector;
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
