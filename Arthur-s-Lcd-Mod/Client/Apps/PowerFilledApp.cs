using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Gui.UserControls.Power;
using LcdMod.Client.Helpers;
using VRage;
using VRage.Game.GUI.TextPanel;
using ScreenConfigPower = LcdMod.Common.Config.Models.Apps.ScreenConfigPower;
using VRageMath;

namespace LcdMod.Client.Apps
{
    internal sealed class PowerFilledApp : AppBase, IAppInteractive
    {
        const float BATTERY_SLOT_W = 100f;
        const float BATTERY_SLOT_H = 100f;
        const float SCROLLER_W = 8f;
        const int SCROLL_TICK = 12;
        const float ICON_TEXTURE_SIZE = 192f;

        readonly IAppHost _surfaceHost;
        readonly InteractiveSurfaceScript _interactiveHost;
        readonly List<PowerCollector> _collectors = new List<PowerCollector>();
        readonly List<PowerEntry> _entries = new List<PowerEntry>();
        readonly List<ControlBase> _interactiveList = new List<ControlBase>();
        readonly Dictionary<long, PowerEntry> _entryById = new Dictionary<long, PowerEntry>();
        readonly Dictionary<long, RectangleControl> _entryHitboxById = new Dictionary<long, RectangleControl>();
        readonly ScrollPanel _scrollPanel;
        ScreenConfigPower _config;
        
        public List<ControlBase> InteractiveList => _interactiveList;

        public PowerFilledApp(ScreenConfigPower config, IAppHost surfaceHost) : base(config, surfaceHost)
        {
            _surfaceHost = surfaceHost;
            _interactiveHost = surfaceHost as InteractiveSurfaceScript;
            if (_interactiveHost == null)
                throw new ArgumentException("PowerFilledApp requires an InteractiveSurfaceScript host.", "surfaceHost");
            _config = config;

            _scrollPanel = new ScrollPanel(CursorType.Default, this);
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _scrollPanel.SetVisible(false);
        }

        public override void LayoutChanged()
        {
            _collectors.Clear();
            _entries.Clear();
            _entryById.Clear();
            ClearPowerEntryHitboxes();
        }

        public override void Update()
        {
            if (_config == null)
                return;

            if (_collectors.Count == 0)
                BuildCollectors();

            var gridLogic = _surfaceHost.GridLogic;
            if (gridLogic == null)
                return;

            _entries.Clear();
            _entryById.Clear();

            for (int i = 0; i < _collectors.Count; i++)
                _collectors[i].Collect(gridLogic, _entries);

            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry != null)
                    _entryById[entry.EntryId] = entry;
            }
        }

        public bool HasVisibleItems()
        {
            for (int i = 0; i < _collectors.Count; i++)
            {
                if (_collectors[i] != null && _collectors[i].HasVisibleItems)
                    return true;
            }

            return false;
        }

        public void OnMouseScroll(int delta, ref bool handled)
        {
        }

        public PowerEntry GetPowerEntry(long entryId)
        {
            PowerEntry entry;
            return _entryById.TryGetValue(entryId, out entry) ? entry : null;
        }

        public override List<MySprite> GetSprites()
        {
            BeginPowerEntryHitboxFrame();
            var sprites = new List<MySprite>();
            DrawFooter(_surfaceHost, sprites);
            DrawBatteries(_surfaceHost, sprites);
            return sprites;
        }

        void BuildCollectors()
        {
            _collectors.Clear();

            var labelCharging = MyTexts.GetString("HudEnergyGroupCharging");
            var labelDischarging = MyTexts.GetString("BlockActionTitle_Discharge");
            var labelJumpNotReady = MyTexts.GetString("ScreenMedicals_RespawnShipNotReady");
            var labelJumpReady = MyTexts.GetString("ScreenMedicals_RespawnShipReady");
            var labelFull = MyTexts.GetString("RadialMenuAction_Signal_Full");

            _collectors.Add(new BatteryPowerCollector(_config)
            {
                ChargingLabel = labelCharging,
                DischargingLabel = labelDischarging,
                FullLabel = labelFull
            });

            _collectors.Add(new JumpDrivePowerCollector(_config)
            {
                ChargingLabel = labelCharging,
                ReadyLabel = labelJumpReady,
                NotReadyLabel = labelJumpNotReady
            });
        }

        void DrawBatteries(IAppHost owner, List<MySprite> sprites)
        {
            float minW = BATTERY_SLOT_W * owner.Scale;
            float minH = BATTERY_SLOT_H * owner.Scale;
            float contentTop = GetContentTop(owner) + 6f * owner.Scale;
            float footerHeight = GetFooterHeight(owner);
            float availW = owner.ViewBox.Width;
            float xLeft = owner.ViewBox.X;
            float xRight = owner.ViewBox.X + owner.ViewBox.Width;

            int count = _entries.Count;
            if (count <= 0)
                return;

            int cols = Math.Min(count, Math.Max(1, (int)Math.Floor(availW / minW)));
            int totalRows = (int)Math.Ceiling(count / (float)cols);
            ConfigurePowerScrollPanel(owner, contentTop, footerHeight, minH, totalRows);

            if (_scrollPanel.IsScrollable)
            {
                xRight -= SCROLLER_W * owner.Scale;
                availW = xRight - xLeft;
                cols = Math.Min(count, Math.Max(1, (int)Math.Floor(availW / minW)));
                totalRows = (int)Math.Ceiling(count / (float)cols);
                ConfigurePowerScrollPanel(owner, contentTop, footerHeight, minH, totalRows);
            }

            float slotW = availW / cols;
            float slotH = minH;
            int startIdx = _scrollPanel.GetStartIndex(cols);
            int renderRows = _scrollPanel.VisibleRows + (_scrollPanel.IsScrollable ? 1 : 0);
            int show = Math.Min(renderRows * cols, count - startIdx);

            BeginScrollPanelClip(sprites);
            var renderContext = CreateRenderContext(owner);

            for (int i = 0; i < show; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float xStart = xLeft + col * slotW;
                float yStart = _scrollPanel.ContentBounds.Y + row * slotH;
                var bounds = new RectangleF(xStart, yStart, slotW, slotH);
                var control = RegisterPowerEntryHitbox(_entries[startIdx + i], bounds);
                control?.Render(renderContext, sprites);
            }

            EndScrollPanelClip(sprites);
            RenderScrollPanelBar(owner, sprites);
        }

        void ConfigurePowerScrollPanel(IAppHost owner, float contentTop, float footerHeight, float rowHeight, int totalRows)
        {
            _scrollPanel.Configure(owner.ViewBox, contentTop, footerHeight, rowHeight, totalRows, SCROLLER_W * owner.Scale, SCROLL_TICK / 6f);
            _scrollPanel.SetVisible(true);
            if (!_interactiveList.Contains(_scrollPanel))
                _interactiveList.Add(_scrollPanel);
        }

        void RenderScrollPanelBar(IAppHost owner, List<MySprite> sprites)
        {
            _scrollPanel.RenderScrollBar(
                sprites,
                new Color(owner.Surface.ScriptForegroundColor.R, owner.Surface.ScriptForegroundColor.G, owner.Surface.ScriptForegroundColor.B, 127),
                new Color(_config.HeaderColor.R, _config.HeaderColor.G, _config.HeaderColor.B, 250));
        }

        void BeginPowerEntryHitboxFrame()
        {
            _interactiveList.Clear();
            _scrollPanel.ClearChildren();
            _scrollPanel.SetVisible(false);
            foreach (var kv in _entryHitboxById)
                kv.Value?.SetVisible(false);
        }

        void ClearPowerEntryHitboxes()
        {
            foreach (var kv in _entryHitboxById)
                kv.Value?.SetVisible(false);
            _entryHitboxById.Clear();
            _interactiveList.Clear();
        }


        void BeginScrollPanelClip(List<MySprite> sprites)
        {
            if (sprites == null)
                return;

            var bounds = _scrollPanel.ContentViewportBounds;
            if (bounds.Width <= 0f || bounds.Height <= 0f)
                return;

            int x = (int)Math.Floor(bounds.X);
            int y = (int)Math.Floor(bounds.Y);
            int right = (int)Math.Ceiling(bounds.Right);
            int bottom = (int)Math.Ceiling(bounds.Bottom);
            sprites.Add(MySprite.CreateClipRect(new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y))));
        }

        static void EndScrollPanelClip(List<MySprite> sprites)
        {
            if (sprites != null)
                sprites.Add(MySprite.CreateClearClipRect());
        }

        ControlRenderContext CreateRenderContext(IAppHost owner)
        {
            return CreateControlRenderContext(
                owner.Surface,
                owner.Scale,
                owner.Surface.FontSize,
                new Vector2(float.NaN, float.NaN));
        }

        RectangleControl RegisterPowerEntryHitbox(PowerEntry entry, RectangleF bounds)
        {
            if (entry == null)
                return null;

            RectangleControl hitbox;
            if (!_entryHitboxById.TryGetValue(entry.EntryId, out hitbox) || hitbox == null)
            {
                hitbox = new RectangleControl(bounds, CursorType.Hand, entry, null, BuildPowerEntryTooltip(entry.EntryId))
                {
                    ClickSound = AudioHelper.HudClick,
                    CustomRender = RenderPowerEntryHitbox
                };
                _entryHitboxById[entry.EntryId] = hitbox;
            }
            else
            {
                hitbox.SetRect(bounds);
                hitbox.SetDataContext(entry);
                hitbox.SetCursor(CursorType.Hand);
                hitbox.SetTooltip(BuildPowerEntryTooltip(entry.EntryId));
                hitbox.CustomRender = RenderPowerEntryHitbox;
            }

            hitbox.SetVisible(true);
            _scrollPanel.AddChild(hitbox);
            return hitbox;
        }

        void OnScrollPanelChanged(ScrollPanel panel)
        {
            _interactiveHost.RenderSprites();
        }

        void RenderPowerEntryHitbox(ControlBase hitbox, ControlRenderContext context, List<MySprite> sprites)
        {
            if (hitbox == null)
                return;

            var entry = hitbox.DataContext as PowerEntry;
            if (entry == null)
                return;

            entry = GetPowerEntry(entry.EntryId);
            if (entry == null)
                return;

            DrawPowerSlotVisual(_surfaceHost, sprites, entry, hitbox.Bounds);
        }

        InteractiveTooltip BuildPowerEntryTooltip(long entryId)
        {
            return new InteractiveTooltip(
                delegate
                {
                    var entry = GetPowerEntry(entryId);
                    if (entry != null && entry.Entity != null && !string.IsNullOrEmpty(entry.Entity.CustomName))
                        return entry.Entity.CustomName;
                    return entry != null ? entry.PercentText : string.Empty;
                },
                delegate
                {
                    var entry = GetPowerEntry(entryId);
                    return entry != null ? entry.GetDetails() : new List<ITooltipLine>();
                },
                null,
                null,
                TooltipActivationMode.Click,
                TooltipActivationMode.Click,
                delegate
                {
                    var entry = GetPowerEntry(entryId);
                    return entry != null ? entry.Icon : string.Empty;
                });
        }

        void DrawFooter(IAppHost owner, List<MySprite> sprites)
        {
            int rows = 0;
            for (int i = 0; i < _collectors.Count; i++)
                if (_collectors[i] != null && _collectors[i].HasVisibleItems)
                    rows++;

            if (rows == 0)
            {
                return;
            }

            float rowHeight = 40f * owner.Scale * owner.Surface.FontSize;
            float footerHeight = rowHeight * rows;
            float footerTop = owner.ViewBox.Bottom - footerHeight;
            float footerLeft = owner.ViewBox.X;
            float footerWidth = Math.Max(1f, owner.ViewBox.Width);
            float footerPad = 6f * owner.Scale;
            float contentLeft = footerLeft + footerPad;
            float contentRight = footerLeft + footerWidth - footerPad;
            Color fg = owner.Surface.ScriptForegroundColor;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(owner.ViewBox.X + owner.ViewBox.Width * 0.5f, footerTop + footerHeight * 0.5f),
                Size = new Vector2(owner.ViewBox.Width, footerHeight),
                Color = new Color(owner.BackgroundColor.MulValue(0.8f), 0.5f),
                Alignment = TextAlignment.CENTER
            });

            float iconLeft = contentLeft;
            float textScale = owner.Scale * 0.75f * owner.Surface.FontSize;
            float textLeftPad = Math.Max(2f, owner.Scale * 2f);
            int rowIndex = 0;

            for (int i = 0; i < _collectors.Count; i++)
            {
                var collector = _collectors[i];
                if (collector == null || !collector.HasVisibleItems)
                    continue;
                DrawFooterRow(owner, sprites, collector, footerTop, rowHeight, rowIndex, iconLeft, contentRight, textScale, textLeftPad, fg);
                rowIndex++;
            }
        }

        void DrawFooterRow(IAppHost owner, List<MySprite> sprites, PowerCollector collector, float footerTop, float rowHeight,
            int rowIndex, float iconLeft, float contentRight, float textScale, float textLeftPad, Color fg)
        {
            float bandCy = footerTop + rowHeight * (rowIndex + 0.5f);
            float iconSize = rowHeight * 0.55f;
            var iconCenter = new Vector2(iconLeft + iconSize / 2f, bandCy);
            float displayedRatio = (float)Math.Round(collector.AverageCharge * 100f, MidpointRounding.AwayFromZero) / 100f;

            DrawFillableTexture(sprites, collector.FillableTexture, iconCenter, iconSize, displayedRatio, collector.StatusColor, fg,
                collector.DrawCenterIcon, 0f, collector.CenterIconScale);

            string avgText = FormatingHelper.PercentageToString(displayedRatio) + " " + collector.FooterPrefix;
            float textLeft = iconLeft + iconSize + textLeftPad;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = avgText,
                Position = new Vector2(textLeft, bandCy - FormatingHelper.GetSizeInPixel(avgText, "White", textScale, owner.Surface).Y / 2f),
                RotationOrScale = textScale,
                Color = fg,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            if (!string.IsNullOrEmpty(collector.StatusText))
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = collector.StatusText,
                    Position = new Vector2(owner.ViewBox.Center.X, bandCy - FormatingHelper.GetSizeInPixel(collector.StatusText, "White", textScale, owner.Surface).Y / 2f),
                    RotationOrScale = textScale,
                    Color = collector.StatusColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }

            if (!collector.HasRightSideText)
                return;

            var rightText = collector.RightSideText;
            var size = FormatingHelper.GetSizeInPixel(rightText, "White", textScale, owner.Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = rightText,
                Position = new Vector2(contentRight, bandCy - size.Y / 2f),
                RotationOrScale = textScale,
                Color = collector.RightSideColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });
        }

        public void DrawPowerSlotVisual(IAppHost owner, List<MySprite> sprites, PowerEntry slot, RectangleF bounds)
        {
            float width = bounds.Width;
            float height = bounds.Height;
            float labelGap = Math.Max(1f, owner.Scale * 2f);
            Vector2 pctRef = FormatingHelper.GetSizeInPixel(slot.PercentText, "White", 1f, owner.Surface);
            float pctScale = Math.Min((width * 0.6f) / Math.Max(1f, pctRef.X), (height * 0.22f) / Math.Max(1f, pctRef.Y)) * Math.Min(owner.Surface.FontSize, 1f);
            float pctH = pctRef.Y * pctScale;
            float iconSize = Math.Max(0f, Math.Min(width, height - pctH - labelGap));
            float centerX = bounds.X + width / 2f;
            float centerY = bounds.Y + iconSize / 2f;

            DrawFillableTexture(sprites, slot.FillableTexture, new Vector2(centerX, centerY), iconSize, slot.Ratio, slot.FillColor,
                owner.Surface.ScriptForegroundColor, slot.DrawCenterIcon, slot.CenterIconRotation, slot.CenterIconScale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = slot.PercentText,
                Position = new Vector2(centerX, bounds.Y + iconSize + labelGap),
                RotationOrScale = pctScale,
                Color = owner.Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });
        }

        static void DrawFillableTexture(List<MySprite> sprites, FillableTexture texture, Vector2 center, float iconSize, float ratio, Color fillColor, Color iconColor,
            bool drawCenterIcon = true, float centerIconRotation = 0f, float centerIconScale = 1f)
        {
            ratio = MathHelper.Clamp(ratio, 0f, 1f);
            float texScale = iconSize / ICON_TEXTURE_SIZE;
            float spriteLeft = center.X - iconSize / 2f;
            float spriteTop = center.Y - iconSize / 2f;
            float innerLeft = spriteLeft + (texture.Left + texture.Margin) * texScale;
            float innerTop = spriteTop + (texture.Top + texture.Margin) * texScale;
            float innerRight = center.X + iconSize / 2f - (texture.Right + texture.Margin) * texScale;
            float innerBottom = center.Y + iconSize / 2f - (texture.Bottom + texture.Margin) * texScale;
            float innerW = Math.Max(0f, innerRight - innerLeft);
            float innerH = Math.Max(0f, innerBottom - innerTop);

            if (ratio > 0.005f && innerW > 0f && innerH > 0f)
            {
                float fillH = innerH * ratio;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2((innerLeft + innerRight) / 2f, innerBottom - fillH / 2f),
                    Size = new Vector2(innerW, fillH),
                    Color = fillColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = texture.Name, Position = center, Size = new Vector2(iconSize), Color = iconColor, Alignment = TextAlignment.CENTER });

            if (!drawCenterIcon || string.IsNullOrEmpty(texture.CenterIconTexture))
                return;

            float innerMin = Math.Min(innerW, innerH);
            float centerIconSize = innerMin > 0f ? innerMin * centerIconScale : iconSize * centerIconScale;
            Vector2 centerIconPos = innerMin > 0f ? new Vector2((innerLeft + innerRight) / 2f, (innerTop + innerBottom) / 2f) : center;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = texture.CenterIconTexture,
                Position = centerIconPos,
                Size = new Vector2(centerIconSize),
                RotationOrScale = centerIconRotation,
                Color = iconColor,
                Alignment = TextAlignment.CENTER
            });
        }

        float GetContentTop(IAppHost owner)
        {
            return owner.TitleVisible ? owner.ViewBox.Y + (40f * owner.Scale * owner.Surface.FontSize) : owner.ViewBox.Y;
        }

        float GetFooterHeight(IAppHost owner)
        {
            int rows = 0;
            for (int i = 0; i < _collectors.Count; i++)
                if (_collectors[i] != null && _collectors[i].HasVisibleItems)
                    rows++;
            if (rows == 0)
                return 0f;
            return (40f * owner.Scale * owner.Surface.FontSize) * rows;
        }
    }
}
