using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.ViewModel;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(17)]
    [ConfigComponent(PROJECTOR_REFERENCE, typeof(BlockReferenceConfigComponent), PropertyName = "ProjectorReferenceComponent")]
    [ConfigComponent(ITEM_DISPLAY, typeof(ItemDisplayConfigComponent), PropertyName = "ItemDisplayComponent")]
    internal sealed partial class ProjectorApp : ItemsApp
    {
        public const string TITLE = "DisplayName_Block_Projector";

        protected override string DefaultTitle => _customTitle ?? TITLE;
        protected override ItemDisplayMode PresentationMode =>
            ItemDisplayComponent.ResolveDisplayMode(GeneralComponent);

        string _customTitle;
        readonly ProjectorViewModel _projectorViewModel;
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
        const string LOC_INGOTS_LABEL = MOD_PREFIX + "Projector_Ingots";
        const string LOC_COMPONENTS_LABEL = "DisplayName_InventoryConstraint_Components";

        public bool IsLoading { get; private set; }

        public ProjectorApp(IAppHost host) : base(host, CreateViewModel)
        {
            _projectorViewModel = (ProjectorViewModel)ViewModel;

            if (!ItemDisplayComponent.MigrateLegacyDisplayMode(GeneralComponent))
                return;

            var block = Host.Block as IMyTerminalBlock;
            var provider = Host.ProviderConfig;
            if (block == null || provider == null || !provider.CanWrite)
                return;

            LcdModClientComponent.RunNextFrame.Add(delegate
            {
                ConfigManager.Sync(block, provider);
            });
        }

        static IItemsAppViewModel CreateViewModel(
            GridLogic gridLogic,
            ItemSelectionConfigComponent selection,
            BlockSelectionConfigComponent blockSelection)
        {
            return new ProjectorViewModel(gridLogic, selection, blockSelection);
        }

        IMyProjector Projector => _projectorViewModel.Projector;

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
            _customTitle = Projector?.CustomName;

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
            if (Projector?.CustomName != _customTitle)
                LayoutChanged();

            if (Projector == null)
                return;

            // Guard on the component requirement (always tracked) so the footer — and its toggle
            // button — stays visible even when the active ore-bar view computes to zero.
            if (_projectorViewModel.TotalBlocks == 0 || _projectorViewModel.ComponentMissing.Count == 0)
                return;

            int built = Math.Max(_projectorViewModel.TotalBlocks - _projectorViewModel.RemainingBlocks, 0);
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

            var blocksPct = built / (float)_projectorViewModel.TotalBlocks;
            var componentsPct = _projectorViewModel.TotalMaterials > 0
                ? 1 - (float)_projectorViewModel.MissingMaterials / _projectorViewModel.TotalMaterials
                : 1f;

            StringBuilder sb = new StringBuilder(
                $"{blocksString}{blocksPct:P2}  ({built}/{_projectorViewModel.TotalBlocks} )");

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
                $"{components}: {componentsPct:P2}  ({FormatingHelper.FormatItemQty(_projectorViewModel.TotalMaterials - _projectorViewModel.MissingMaterials)}" +
                $"/{FormatingHelper.FormatItemQty(_projectorViewModel.TotalMaterials)})");


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

            DonutDualPanel.CreateSprites(
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
            DrawCraftAllButton(frame, layout, _projectorViewModel.MissingComponents > 0);
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
            base.Update();
            var changed = _projectorViewModel.UpdateProjector(
                Block?.CubeGrid,
                ProjectorReferenceComponent.EntityId,
                _showIngots);
            if (Projector?.CustomName != _customTitle)
                LayoutChanged();

            if (!_projectorDataInitialized && ProjectorReferenceComponent.EntityId != 0 && Projector == null)
            {
                _projectorDataInitialized = true;
                IsLoading = true;
                return;
            }

            _projectorDataInitialized = true;
            if (changed)
                InvalidateContentAndFooterSprites();
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

            if (!VisualChildren.Contains(_craftAllButton))
                _children.Add(_craftAllButton);

            _craftAllButton.Render(frame);
        }

        void DrawToggleViewButton(List<MySprite> frame, ProjectorFooterLayout layout)
        {
            if (layout.ToggleRect.Width <= 0f || layout.ToggleRect.Height <= 0f)
                return;

            if (_toggleViewButton == null)
            {
                _toggleViewButton = AddLogicalChild(new Button(layout.ToggleRect, new ButtonModel
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

            if (!VisualChildren.Contains(_toggleViewButton))
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
                _craftAllButton = AddLogicalChild(new Button(rect, new ButtonModel
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
            var rect = control.Bounds;
            var button = control as Button;
            var buttonColor = button?.BackgroundColor ?? control.BackgroundColor;
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
            if (_projectorViewModel.MissingComponents <= 0)
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

            foreach (var item in _projectorViewModel.ComponentMissing)
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

    }
}
