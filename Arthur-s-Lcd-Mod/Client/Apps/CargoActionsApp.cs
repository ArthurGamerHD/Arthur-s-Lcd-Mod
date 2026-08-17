using Generated;
using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;

namespace LcdMod.Client.Apps
{
    /// <summary>
    /// Standalone button-pad screen that exposes the cargo Sorter / sort-mode / fill actions that used
    /// to live in the <see cref="CargoFilledApp"/> footer. Grid-wide: it operates on every reachable
    /// container/target on the (mechanically linked) construct, with no per-block selection.
    /// Server-authoritative for the inventory mutations, like the original footer.
    /// </summary>
    [LcdApp(18)]
    [ConfigComponent(APP, typeof(CargoActionsConfigComponent), PropertyName = "CargoActionsComponent")]
    public sealed partial class CargoActionsApp : App, IApp
    {
        const int ACTION_CONFIG = 0;
        const int ACTION_SORTER = 1;
        const int ACTION_WEAPONS = 2;
        const int ACTION_REACTORS = 3;
        const int ACTION_COUNT = 4;

        const float GAP_FRACTION = 0.12f;          
        const float GAP_MIN = 6f;                 
        const float GAP_MAX = 24f;
        const float SMALL_HEIGHT_FRACTION = 0.7f;  
        const float SMALL_SURFACE_SCALE = 0.4f; 
        const long ACTION_THROTTLE_FRAMES = 60L;
        const long ADOPT_RETRY_FRAMES = 30L;
        const long ADOPT_RECHECK_FRAMES = 300L;    // periodic revision re-check (~5s): late edits on sibling screens must still propagate
        const int STATUS_MESSAGE_FRAMES = 240;

        const string LOC_CONFIG = MOD_PREFIX + "CargoActions_Config";
        const string LOC_SORTER = MOD_PREFIX + "Cargo_Sorter";
        const string LOC_SORT_DONE = MOD_PREFIX + "Cargo_SortDone";
        const string LOC_SORT_REQUESTED = MOD_PREFIX + "Cargo_SortRequested";
        const string LOC_FILL_REQUESTED = MOD_PREFIX + "Cargo_FillRequested";
        const string LOC_FILL_WEAPONS = MOD_PREFIX + "Cargo_FillWeapons";
        const string LOC_FILL_REACTORS = MOD_PREFIX + "Cargo_FillReactors";
        const string LOC_FILL_DONE = MOD_PREFIX + "Cargo_FillDone";

        const string ICON_CONFIG = "SettingsIcon";
        const string ICON_SORTER = "SortIcon";
        const string ICON_AMMO = "AmmoIcon";
        const string ICON_REFUEL = "RefuelIcon";

        readonly List<Control> _children = new List<Control>();
        readonly List<IMyTerminalBlock> _sources = new List<IMyTerminalBlock>();
        readonly List<IMyTerminalBlock> _targets = new List<IMyTerminalBlock>();
        readonly Button[] _buttons = new Button[ACTION_COUNT];
        readonly List<int> _visible = new List<int>(ACTION_COUNT);
        readonly HashSet<long> _constructGridIds = new HashSet<long>();
        readonly List<IMyTerminalBlock> _pendingSyncBlocks = new List<IMyTerminalBlock>();
        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly LinkedTypedBlockSourceSet<IMyTerminalBlock> _terminalSources =
            new LinkedTypedBlockSourceSet<IMyTerminalBlock>(blocks => blocks.TerminalBlocks);
        readonly InteractiveSurfaceScript _interactiveHost;

        long _lastActionFrame = long.MinValue;
        string _statusMessage;
        long _statusUntilFrame;
        long _nextAdoptAttemptFrame;

        public CargoActionsApp(IAppHost host) : base(host)
        {
            _interactiveHost = host as InteractiveSurfaceScript;
        }

        public override IReadOnlyList<Control> VisualChildren => _children;


        public override void Update()
        {
            _terminalSources.Bind(Host.GridLogic, (GridLinkTypeEnum)CargoActionsComponent.GridLinkTypeInternal);
            TryAdoptSharedSettings();
        }

        public override List<MySprite> GetSprites()
        {
            _children.Clear();
            _sprites.Clear();
            DrawButtons(_sprites);
            DrawStatusMessage(_sprites);
            ClearDirtyAfterRender();
            return _sprites;
        }

        void DrawButtons(List<MySprite> sprites)
        {
            var contentTop = GetContentTop();
            var area = new RectangleF(
                Host.ViewBox.X,
                contentTop,
                Host.ViewBox.Width,
                Math.Max(0f, Host.ViewBox.Bottom - contentTop));
            if (area.Width <= 0f || area.Height <= 0f)
                return;

            bool small = GeneralComponent.GetScale() < SMALL_SURFACE_SCALE;
            _visible.Clear();
            if (!small && CargoActionsComponent.ShowConfigButton)
                _visible.Add(ACTION_CONFIG);
            _visible.Add(ACTION_SORTER);
            _visible.Add(ACTION_WEAPONS);
            _visible.Add(ACTION_REACTORS);
            int count = _visible.Count;

            var aspect = area.Width / Math.Max(1f, area.Height);
            int columns = aspect >= 2f ? count : (aspect >= 0.6f ? Math.Min(2, count) : 1);
            if (columns < 1)
                columns = 1;
            int rows = (count + columns - 1) / columns;

            var cellMin = Math.Min(area.Width / columns, area.Height / rows);
            var gap = MathHelper.Clamp(cellMin * GAP_FRACTION, GAP_MIN, GAP_MAX);

            var cap = small ? Math.Max(32f, area.Height * SMALL_HEIGHT_FRACTION) : 150f * GeneralComponent.GetScale();
            var maxTileWidth = cap * (small ? 2f : 1.4f);
            var buttonWidth = Math.Min((area.Width - (columns - 1) * gap) / columns, maxTileWidth);
            var buttonHeight = Math.Min((area.Height - (rows - 1) * gap) / rows, cap);
            if (buttonWidth <= 0f)
                buttonWidth = Math.Max(1f, area.Width / columns);
            if (buttonHeight <= 0f)
                buttonHeight = Math.Max(1f, area.Height / rows);

            var gridHeight = rows * buttonHeight + (rows - 1) * gap;
            var startY = area.Y + Math.Max(0f, (area.Height - gridHeight) * 0.5f);
            for (int i = 0; i < count; i++)
            {
                int row = i / columns;
                int column = i % columns;
                int itemsInRow = Math.Min(columns, count - row * columns);
                var rowWidth = itemsInRow * buttonWidth + (itemsInRow - 1) * gap;
                var rowStartX = area.X + (area.Width - rowWidth) * 0.5f;

                var rect = new RectangleF(
                    rowStartX + column * (buttonWidth + gap),
                    startY + row * (buttonHeight + gap),
                    buttonWidth,
                    buttonHeight);

                DrawActionButton(_visible[i], rect, sprites);
            }
        }

        void DrawActionButton(int index, RectangleF rect, List<MySprite> sprites)
        {
            var button = _buttons[index];
            if (button == null)
            {
                button = AddLogicalChild(new Button(rect, new PadTileModel
                {
                    Text = GetButtonText(index),
                    SpriteName = GetButtonSprite(index),
                    Clicked = GetButtonAction(index)
                }));
                _buttons[index] = button;
            }
            else
            {
                button.SetRect(rect);
            }

            // Text and icon are fixed at creation; the sprite always wins in PadButtonStyle,
            // so refreshing the (never rendered) label per frame would be wasted allocation.
            var model = button.DataContext as PadTileModel;
            if (model != null)
                model.Enabled = true;

            button.SetVisible(true);
            button.SetCursor(CursorType.Hand);
            button.SetStyleId("Primary");
            button.CustomRender = PadButtonStyle.RenderLabeled;

            _children.Add(button);
            button.Render(sprites);
        }

        string GetButtonText(int index)
        {
            switch (index)
            {
                case ACTION_CONFIG:
                    return MyTexts.GetString(LOC_CONFIG);
                case ACTION_SORTER:
                    return MyTexts.GetString(LOC_SORTER);
                case ACTION_WEAPONS:
                    return MyTexts.GetString(LOC_FILL_WEAPONS);
                default:
                    return MyTexts.GetString(LOC_FILL_REACTORS);
            }
        }

        static string GetButtonSprite(int index)
        {
            switch (index)
            {
                case ACTION_CONFIG:
                    return ICON_CONFIG;
                case ACTION_SORTER:
                    return ICON_SORTER;
                case ACTION_WEAPONS:
                    return ICON_AMMO;
                default:
                    return ICON_REFUEL;
            }
        }

        Action<ButtonModel, object> GetButtonAction(int index)
        {
            switch (index)
            {
                case ACTION_CONFIG:
                    return OnConfigClicked;
                case ACTION_SORTER:
                    return OnSorterClicked;
                case ACTION_WEAPONS:
                    return OnWeaponsClicked;
                default:
                    return OnReactorsClicked;
            }
        }

        float GetContentTop()
        {
            return Host.TitleVisible ? Host.ViewBox.Y + (40f * GeneralComponent.GetScale() * Host.Surface.FontSize) : Host.ViewBox.Y;
        }

        void OnConfigClicked(ButtonModel model, object sender)
        {
            try
            {
                if (_interactiveHost == null)
                    return;

                _interactiveHost.ShowDialog(new CargoActionsSettingsDialog(this, () => CargoActionsComponent, OnSettingsSaved,
                    Host.RenderSprites, delegate(Dialog d) { _interactiveHost.ShowDialog(d); }, CollectWeaponTypes()));
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        void OnSettingsSaved()
        {
            try
            {
                var config = CargoActionsComponent;
                config.SettingsRevision = config.SettingsRevision + 1;
                PropagateSettingsToConstruct(config);

                var block = Host.Block as IMyTerminalBlock;
                if (block != null)
                    ConfigManager.Sync(block);
                Host.RenderSprites();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        void TryAdoptSharedSettings()
        {
            long frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
            try
            {
                if (frame < _nextAdoptAttemptFrame)
                    return;

                var blocks = GetGridBlocks();
                if (!blocks.Any())
                {
                    _nextAdoptAttemptFrame = frame + ADOPT_RETRY_FRAMES;
                    return;
                }

                // Re-check periodically instead of only once: a sibling screen can bump
                // SettingsRevision at any time and there is no server-side rebroadcast to rely on.
                _nextAdoptAttemptFrame = frame + ADOPT_RECHECK_FRAMES;

                var mine = CargoActionsComponent;
                CollectConstructGridIds(blocks);

                CargoActionsConfigComponent newest = null;
                foreach (var screen in SurfaceScriptBase.Instances)
                {
                    var otherSurface = screen.Config;
                    var other = otherSurface?.TryGetComponent<CargoActionsConfigComponent>();
                    if (other == null || otherSurface.AppTypeId != (int)AppType.CargoActions || ReferenceEquals(other, mine) || screen.Block == null)
                        continue;
                    if (!_constructGridIds.Contains(screen.Block.CubeGrid.EntityId))
                        continue;
                    if (other.SettingsRevision > mine.SettingsRevision &&
                        (newest == null || other.SettingsRevision > newest.SettingsRevision))
                        newest = other;
                }

                if (newest == null)
                    return;

                mine.CopyActionSettingsFrom(newest);

                var block = Host.Block as IMyTerminalBlock;
                if (block != null)
                    LcdModClientComponent.RunNextFrame.Add(delegate { ConfigManager.Sync(block); });
            }
            catch (Exception e)
            {
                _nextAdoptAttemptFrame = frame + ADOPT_RECHECK_FRAMES;
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        void PropagateSettingsToConstruct(CargoActionsConfigComponent source)
        {
            var blocks = GetGridBlocks();
            if (!blocks.Any())
                return;

            CollectConstructGridIds(blocks);
            _pendingSyncBlocks.Clear();

            var ownBlock = Host.Block;
            foreach (var screen in SurfaceScriptBase.Instances)
            {
                var otherSurface = screen.Config;
                var other = otherSurface?.TryGetComponent<CargoActionsConfigComponent>();
                if (other == null || otherSurface.AppTypeId != (int)AppType.CargoActions || ReferenceEquals(other, source) || screen.Block == null)
                    continue;
                if (!_constructGridIds.Contains(screen.Block.CubeGrid.EntityId))
                    continue;

                other.CopyActionSettingsFrom(source);

                var otherBlock = screen.Block as IMyTerminalBlock;
                if (otherBlock == null || (ownBlock != null && otherBlock.EntityId == ownBlock.EntityId))
                    continue;

                if (!ContainsBlock(_pendingSyncBlocks, otherBlock.EntityId))
                    _pendingSyncBlocks.Add(otherBlock);
            }

            // Defer the sibling syncs to the next frame (same pattern as TryAdoptSharedSettings):
            // each Sync triggers a synchronous redraw + transmit, too heavy for one click tick.
            for (var i = 0; i < _pendingSyncBlocks.Count; i++)
            {
                var syncBlock = _pendingSyncBlocks[i];
                LcdModClientComponent.RunNextFrame.Add(delegate { ConfigManager.Sync(syncBlock); });
            }
            _pendingSyncBlocks.Clear();
        }

        void CollectConstructGridIds(IEnumerable<IMyTerminalBlock> blocks)
        {
            _constructGridIds.Clear();
            foreach (var block in blocks)
            {
                var cubeGrid = block != null ? block.CubeGrid : null;
                if (cubeGrid != null)
                    _constructGridIds.Add(cubeGrid.EntityId);
            }
        }

        static bool ContainsBlock(List<IMyTerminalBlock> blocks, long entityId)
        {
            for (var i = 0; i < blocks.Count; i++)
                if (blocks[i].EntityId == entityId)
                    return true;

            return false;
        }

        void OnSorterClicked(ButtonModel model, object sender)
        {
            try
            {
                if (IsThrottled())
                    return;

                _sources.Clear();
                CollectSorterSources(_sources);
                if (_sources.Count < 2)
                    return;

                var sortMode = CargoActionsComponent.SortMode;
                if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer)
                {
                    var moved = InventorySorterCommon.Consolidate(_sources, (InventorySortMode)sortMode);
                    SetStatusMessage(string.Format(MyTexts.GetString(LOC_SORT_DONE), moved));
                }
                else
                {
                    LcdModSessionComponent.NetworkManager.TransmitToServer(
                        new PacketSortInventory(ToEntityIds(_sources), sortMode), false);
                    SetStatusMessage(MyTexts.GetString(LOC_SORT_REQUESTED));
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        void OnWeaponsClicked(ButtonModel model, object sender)
        {
            RunFill(FillKind.Weapons);
        }

        void OnReactorsClicked(ButtonModel model, object sender)
        {
            RunFill(FillKind.Reactors);
        }

        void RunFill(FillKind kind)
        {
            try
            {
                if (IsThrottled())
                    return;

                _sources.Clear();
                CollectFillSources(_sources);
                if (_sources.Count == 0)
                    return;

                _targets.Clear();
                if (kind == FillKind.Weapons)
                    CollectFillTargets<IMyUserControllableGun>(_targets);
                else
                    CollectFillTargets<IMyReactor>(_targets);

                if (_targets.Count == 0)
                    return;

                var settings = CargoActionsComponent.ToFillSettings();
                if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer)
                {
                    var moved = BlockFillerCommon.Execute(_sources, _targets, kind, settings);
                    SetStatusMessage(string.Format(MyTexts.GetString(LOC_FILL_DONE), moved));
                }
                else
                {
                    LcdModSessionComponent.NetworkManager.TransmitToServer(
                        new PacketFillBlocks(ToEntityIds(_sources), ToEntityIds(_targets), (int)kind, settings), false);
                    SetStatusMessage(MyTexts.GetString(LOC_FILL_REQUESTED));
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        bool IsThrottled()
        {
            var frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
            if (_lastActionFrame != long.MinValue && frame - _lastActionFrame < ACTION_THROTTLE_FRAMES)
                return true;
            _lastActionFrame = frame;
            return false;
        }

        void CollectSorterSources(List<IMyTerminalBlock> result)
        {
            var blocks = GetGridBlocks();
            foreach (var fat in blocks)
            {
                if (fat == null || !fat.HasInventory)
                    continue;

                if (fat is IMyCargoContainer || fat is IMyShipConnector || fat is IMyAssembler
                    || fat is IMyRefinery || fat is IMyShipToolBase)
                    result.Add(fat);
            }
        }

        void CollectFillSources(List<IMyTerminalBlock> result)
        {
            var blocks = GetGridBlocks();
            foreach (var fat in blocks)
            {
                if (fat == null || !fat.HasInventory)
                    continue;

                if (fat is IMyCargoContainer || fat is IMyShipConnector)
                    result.Add(fat);
            }
        }

        void CollectFillTargets<T>(List<IMyTerminalBlock> result) where T : class
        {
            var blocks = GetGridBlocks();
            foreach (var fat in blocks)
            {
                if (fat == null || !fat.HasInventory)
                    continue;

                if (fat is T)
                    result.Add(fat);
            }
        }

        IEnumerable<IMyTerminalBlock> GetGridBlocks()
        {
            _terminalSources.Bind(Host.GridLogic, (GridLinkTypeEnum)CargoActionsComponent.GridLinkTypeInternal);
            var sources = _terminalSources.Sources;
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var source = sources[sourceIndex];
                for (var blockIndex = 0; blockIndex < source.Count; blockIndex++)
                    yield return source[blockIndex];
            }
        }

        static long[] ToEntityIds(List<IMyTerminalBlock> blocks)
        {
            var ids = new long[blocks.Count];
            for (var i = 0; i < blocks.Count; i++)
                ids[i] = blocks[i].EntityId;
            return ids;
        }

        List<WeaponOption> CollectWeaponTypes()
        {
            var result = new List<WeaponOption>();
            var seen = new HashSet<string>();
            var blocks = GetGridBlocks();
            if (blocks != null)
            {
                foreach (var fat in blocks)
                {
                    if (fat == null || !fat.HasInventory || !(fat is IMyUserControllableGun))
                        continue;

                    var subtype = fat.BlockDefinition.SubtypeName;
                    if (string.IsNullOrEmpty(subtype) || seen.Contains(subtype))
                        continue;

                    seen.Add(subtype);
                    var name = fat.DefinitionDisplayNameText;
                    if (string.IsNullOrEmpty(name))
                        name = subtype;
                    result.Add(new WeaponOption(subtype, name));
                }
            }

            result.Sort(delegate(WeaponOption a, WeaponOption b)
            {
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        public override void Close()
        {
            _terminalSources.Dispose();
            base.Close();
        }

        void SetStatusMessage(string message)
        {
            _statusMessage = message;
            var frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
            _statusUntilFrame = frame + STATUS_MESSAGE_FRAMES;
            Host.RenderSprites();
        }

        void DrawStatusMessage(List<MySprite> sprites)
        {
            if (string.IsNullOrEmpty(_statusMessage))
                return;

            var frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
            if (frame >= _statusUntilFrame)
            {
                _statusMessage = null;
                return;
            }

            var textScale = PadButtonStyle.TextScaleForHeight(
                MathHelper.Clamp(Host.ViewBox.Height * 0.05f, 12f, 20f), Host.Surface);
            var textSize = FormatingHelper.GetSizeInPixel(_statusMessage, TextFont, textScale, Host.Surface);
            var padX = 20f * GeneralComponent.GetScale();
            var padY = 12f * GeneralComponent.GetScale();
            var rect = new RectangleF(
                Host.ViewBox.Center.X - (textSize.X * 0.5f + padX),
                Host.ViewBox.Center.Y - (textSize.Y * 0.5f + padY),
                textSize.X + 2f * padX,
                textSize.Y + 2f * padY);

            BorderRenderer.CreateSpritesFromRect(rect, sprites,
                Host.BackgroundColor.MulValue(0.2f), radiusScale: GeneralComponent.GetScale());
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = _statusMessage,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textSize.Y * 0.5f),
                RotationOrScale = textScale,
                Color = Host.Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = TextFont
            });
        }
    }
}
