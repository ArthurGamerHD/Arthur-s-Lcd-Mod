using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Generated;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.Controls;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Groups;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using ItemsSurfaceScriptBase = LcdMod.Client.Apps.Abstract.ItemsSurfaceScriptBase;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using ListboxProjectorSelection = LcdMod.Client.Terminal.Controls.Blueprint.ListboxProjectorSelection;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using SwitchToggleLines = LcdMod.Client.Terminal.Controls.Generic.SwitchToggleLines;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class ProjectorLcdSurfaceScript : ItemsSurfaceScriptBase,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<ListboxProjectorSelection>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>
    {
        protected override ConfigKind ConfigKind => ConfigKind.Projector;
        public const string ID = "ProjectorCharts";
        public const string TITLE = "DisplayName_Block_Projector";

        public string[] AllowedTypes = { "Component" };

        protected override string DefaultTitle => _customTitle ?? TITLE;

        string _customTitle;

        IMyProjector _projector;
        readonly List<IMyCubeGrid> _projectorGrids = new List<IMyCubeGrid>();
        readonly List<IMySlimBlock> _projectorBlocks = new List<IMySlimBlock>();

        public override Dictionary<MyItemType, double> ItemSource => _missing;

        readonly Dictionary<MyItemType, double> _missing = new Dictionary<MyItemType, double>();
        readonly Dictionary<MyItemType, int> _needed = new Dictionary<MyItemType, int>();

        int _totalBlocks = 1;
        int _remainingBlocks;

        int _totalComponents;
        int _missingComponents;

        string _required = "Req";
        string _available = "Ava";

        float _requiredX;
        float _availableX;
        bool _projectorDataInitialized;

        const float PIE_RADIUS = 40;

        public ProjectorLcdSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        protected override void LayoutChanged()
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

        protected override void DrawTitle(List<MySprite> frame)
        {
            var margin = 0f;
            float headerScale = LayoutScale;
            float titleBarHeight = TITLE_BAR_HEIGHT_BASE * headerScale;

            Vector2 position = ViewBox.Position;
            position.X += margin;
            position.Y += 0f;

            CaretY = position.Y;

            if (!TitleVisible)
                return;

            AddHeaderSprite(frame, new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = Icon,
                Position = position + new Vector2(20f) * headerScale,
                Size = new Vector2(40f * headerScale),
                Color = AppConfig.HeaderColor,
                Alignment = TextAlignment.CENTER
            });
            position.X += ViewBox.Width / 8f;

            var numberWidth = GetQuantityColumnWidth();
            var headerSeparatorPadding = 10f * Scale;

            var availableSize = new Rectangle((int)position.X, (int)position.Y,
                (int)(ViewBox.Width - position.X + (ViewBox.X) - (2 * margin) - (2 * numberWidth) -
                      (2 * headerSeparatorPadding)),
                (int)(position.Y + TITLE_HEIGHT * headerScale));
            frame.Add(MySprite.CreateClipRect(availableSize));


            var displayName = GetCachedTitleText(availableSize.Width);

            AddHeaderSprite(frame, new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = displayName,
                Position = position,
                RotationOrScale = Scale * 1.3f * FontScale,
                Color = AppConfig.HeaderColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            frame.Add(MySprite.CreateClearClipRect());
            var requiredRight = ViewBox.Width + ViewBox.X - margin - SCROLLER_WIDTH * Scale;
            var availableRight = requiredRight - numberWidth - (2f * headerSeparatorPadding);
            var separatorX = requiredRight - numberWidth - headerSeparatorPadding;

            position.X = requiredRight;
            AddHeaderSprite(frame, new MySprite
            {
                Type = SpriteType.TEXT,
                Data = _required,
                Position = position,
                RotationOrScale = Scale * 1.3f * FontScale,
                Color = AppConfig.HeaderColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });

            AddHeaderSprite(frame, new MySprite
            {
                Type = SpriteType.TEXT,
                Data = "/",
                Position = new Vector2(separatorX, position.Y),
                RotationOrScale = Scale * 1.3f * FontScale,
                Color = AppConfig.HeaderColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            position.X = availableRight;
            AddHeaderSprite(frame, new MySprite
            {
                Type = SpriteType.TEXT,
                Data = _available,
                Position = position,
                RotationOrScale = Scale * 1.3f * FontScale,
                Color = AppConfig.HeaderColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });

            CaretY += titleBarHeight;
        }

        protected override void DrawFooter(List<MySprite> frame)
        {
            if (_projector?.CustomName != _customTitle)
                LayoutChanged();

            if (_projector == null)
                return;

            if (_totalBlocks == 0 || _totalComponents == 0)
                return;
            
            var pos = ViewBox.Position;
            var footerPaddingX = GetFooterPaddingX();
            var footerInnerPaddingX = GetFooterInnerPaddingX();
            var footerContentLeft = ViewBox.X + footerPaddingX + footerInnerPaddingX;
            var footerContentRight = ViewBox.Right - footerPaddingX - footerInnerPaddingX;
            pos.X = footerContentLeft;

            int built = Math.Max(_totalBlocks - _remainingBlocks, 0);
            float textScale = Scale * 0.9f * FontScale;
            var lineSpacer = GetFooterLineSpacer();
            var legendSize = GetFooterLegendSize();
            var pieSize = GetFooterPieSize();

            FooterHeight = GetFooterHeight();
            pos.X += pieSize.X;

            var footerTop = ViewBox.Bottom - FooterHeight;
            pos.Y = GetFooterContentTop();

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
            var componentsPct = 1 - (float)_missingComponents / _totalComponents;

            StringBuilder sb = new StringBuilder($"{blocksString}{blocksPct:P2}  ({built}/{_totalBlocks} )");

            TrimText(ref sb, footerContentRight - pos.X, 0.9f);

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

            var components = MyTexts.GetString("DisplayName_InventoryConstraint_Components");

            sb.Clear();
            sb.Append(
                $"{components}: {componentsPct:P2}  ({FormatingHelper.FormatItemQty(_totalComponents - _missingComponents)}" +
                $"/{FormatingHelper.FormatItemQty(_totalComponents)})");


            TrimText(ref sb, footerContentRight - pos.X, 0.9f);

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
                ToScreenMargin(GetFooterPieCenter()),
                pieSize,
                componentsPct,
                blocksPct,
                AppConfig.HeaderColor,
                true,
                false);
        }

        public override void DrawItems()
        {
            EnsureData();

            if (!_projectorDataInitialized && AppConfig != null && AppConfig.ReferenceBlock != 0 && _projector == null)
            {
                _projectorDataInitialized = true;
                DrawLoadingScreen(AppConfig.Scale);
                return;
            }

            _projectorDataInitialized = true;
            base.DrawItems();
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

        protected override void DrawRow(List<MySprite> frame, KeyValuePair<MyItemType, double> item, bool showScrollBar)
        {
            string sprite;
            string localizedName;

            if (!SpriteCache.TryGetValue(item.Key, out sprite))
            {
                var reference = new List<string>();
                var color = "ColorfulIcons_" + item.Key.ToString().Substring(16);
                const string notFound = "Textures\\FactionLogo\\Unknown.dds";

                Surface.GetSprites(reference);
                if (reference.Contains(color))
                    sprite = color;
                else if (reference.Contains(item.Key.ToString()))
                    sprite = item.Key.ToString();
                else sprite = notFound;

                AddToSpriteCache(item.Key, sprite);
            }

            var margin = 0f;
            Vector2 position = ViewBox.Position;
            position.X += margin;
            position.Y = CaretY;

            bool drawSeparatorLine = AppConfig.SortMethod == (int)SortMethod.Type && PreviousType != item.Key.TypeId;

            if (AppConfig.DrawLines || drawSeparatorLine)
            {
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = new Vector2(ViewBox.Center.X, position.Y),
                    Size = new Vector2(ViewBox.Width - 2 * margin, 1),
                    Color = drawSeparatorLine ? AppConfig.HeaderColor : Surface.ScriptForegroundColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            PreviousType = item.Key.TypeId;
            var shortageColor = GetShortageColor(item.Key, item.Value);
            var rowColor = shortageColor ?? Surface.ScriptForegroundColor;

            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = sprite,
                Position = position + new Vector2(20f, 15) * Scale,
                Size = new Vector2(LINE_HEIGHT * Scale),
                Color = rowColor,
                Alignment = TextAlignment.CENTER
            });
            position.X += ViewBox.Width / 8f;
            var quantityColumnsWidth = 2f * GetQuantityColumnWidth() + GetQuantityColumnGap();

            var clip = new Rectangle((int)position.X, (int)position.Y,
                (int)(ViewBox.Width - position.X + (ViewBox.X) - quantityColumnsWidth - margin),
                (int)(position.Y + (LINE_HEIGHT + 5) * Scale));

            frame.Add(MySprite.CreateClipRect(clip));

            if (!LocKeysCache.TryGetValue(item.Key, out localizedName))
            {
                var key =
                    MyDefinitionManager.Static.TryGetPhysicalItemDefinition(item.Key).DisplayNameEnum?.ToString() ??
                    item.Key.SubtypeId;
                var sb = new StringBuilder(MyTexts.GetString(key));
                TrimText(ref sb, clip.Width);
                localizedName = sb.ToString();
                LocKeysCache[item.Key] = sb.ToString();
            }

            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = localizedName,
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = rowColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });
            frame.Add(MySprite.CreateClearClipRect());
            position.X = ViewBox.Width + ViewBox.X - margin;
            if (showScrollBar) position.X -= SCROLLER_WIDTH * Scale;
            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = FormatingHelper.FormatItemQty(GetNeededQty(item.Key)),
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = rowColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });
            position.X -= GetQuantityColumnWidth() + GetQuantityColumnGap();
            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = FormatingHelper.FormatItemQty(GetAvailableQty(item.Key, item.Value)),
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = rowColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });

            CaretY += LINE_HEIGHT * Scale;
        }

        protected override void DrawCellContent(List<MySprite> frame, KeyValuePair<MyItemType, double> item,
            string sprite, Color foreground, MyTuple<RectangleF, RectangleF, RectangleF> slots)
        {
            string localizedName;
            var iconRect = slots.Item1;
            var numberRect = slots.Item2;
            var nameRect = slots.Item3;
            var shortageColor = GetShortageColor(item.Key, item.Value);
            var useAlertText = shortageColor.HasValue && AppConfig.DrawLines;
            var color = useAlertText ? shortageColor.Value : foreground;

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = sprite,
                Position = new Vector2(iconRect.X, iconRect.Y + iconRect.Height / 2f),
                Size = new Vector2(iconRect.Width),
                Alignment = TextAlignment.LEFT,
                Color = useAlertText ? shortageColor.Value : Color.White
            });

            if (!LocKeysCache.TryGetValue(item.Key, out localizedName))
            {
                var key =
                    MyDefinitionManager.Static.TryGetPhysicalItemDefinition(item.Key).DisplayNameEnum?.ToString() ??
                    item.Key.SubtypeId;
                var sb = new StringBuilder(MyTexts.GetString(key));
                TrimText(ref sb, nameRect.Width);
                localizedName = sb.ToString();
                LocKeysCache[item.Key] = sb.ToString();
            }

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
                color,
                "White",
                TextAlignment.RIGHT,
                fontSize * .95f * FontScale
            ));

            var qty = FormatingHelper.FormatItemQty(GetAvailableQty(item.Key, item.Value)) + "/" +
                      FormatingHelper.FormatItemQty(GetNeededQty(item.Key));
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
                color,
                "White",
                TextAlignment.RIGHT,
                fontSize * .95f * FontScale
            ));
        }

        protected override void DrawCellBackground(List<MySprite> frame, KeyValuePair<MyItemType, double> item,
            float xStart, float xEnd, float yStart, float cellHeight, float cellPadding)
        {
            var rl = xStart + cellPadding / 2;
            var rr = xEnd - cellPadding / 2;
            var rt = yStart + cellPadding / 2;
            var rb = yStart + cellHeight - cellPadding / 2;

            var shortageColor = GetShortageColor(item.Key, item.Value);
            var backgroundColor = shortageColor ?? AppConfig.HeaderColor;
            var a = backgroundColor.ColorToHSV();
            a.Z *= 0.2f;
            var cellRect = new RectangleF(rl, rt, rr - rl, rb - rt);
            var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
            RectanglePanel.CreateSpritesFromRect(dropShadow, frame, a.HSVtoColor(), .2f);
            RectanglePanel.CreateSpritesFromRect(cellRect, frame, backgroundColor, .2f);
        }

        int GetNeededQty(MyItemType itemType)
        {
            int needed;
            return _needed.TryGetValue(itemType, out needed) ? needed : 0;
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

        Vector2 GetFooterPieCenter()
        {
            var footerLeft = ViewBox.X;
            var pieCenterX = footerLeft + GetFooterInnerPaddingX() + GetFooterPieSize().X * 0.5f;
            var footerHeight = GetFooterHeight();
            var footerTop = ViewBox.Bottom - footerHeight;
            var pieCenterY = footerTop + footerHeight * 0.5f;
            return new Vector2(pieCenterX, pieCenterY);
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

        float GetFooterContentTop()
        {
            return ViewBox.Bottom - GetFooterHeight() + GetFooterPaddingY();
        }

        float GetFooterHeight()
        {
            var pieSize = GetFooterPieSize();
            return Math.Max(GetFooterTextHeight(), pieSize.Y) + GetFooterPaddingY() * 2f;
        }

        void EnsureData()
        {
            _missing.Clear();
            _totalBlocks = 1;
            _remainingBlocks = 0;
            _totalComponents = 0;
            _missingComponents = 0;

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
                _needed.Clear();

                foreach (var block in _projector.RemainingBlocksPerType)
                {
                    var def = (MyCubeBlockDefinition)block.Key;

                    foreach (var perType in def.Components)
                    {
                        int qty;
                        _needed.TryGetValue(perType.Definition.Id, out qty);
                        _needed[perType.Definition.Id] = qty + perType.Count * block.Value;
                    }
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }

            var availableByType = GetAvailableComponents(lcd);

            long totalNeeded = 0;
            long totalMissing = 0;

            foreach (var needed in _needed)
            {
                double available;
                availableByType.TryGetValue(needed.Key, out available);

                double missing = needed.Value - available;
                if (missing < 0) missing = 0;

                _missing[needed.Key] = Math.Max(0, missing);

                totalNeeded += needed.Value;
                totalMissing += (long)Math.Round(missing);
            }

            _totalComponents = (int)Math.Max(0, totalNeeded);
            _missingComponents = (int)Math.Max(0, totalMissing);
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
