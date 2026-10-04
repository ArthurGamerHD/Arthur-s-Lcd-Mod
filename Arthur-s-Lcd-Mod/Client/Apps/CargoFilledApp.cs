using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.CargoFilled;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Helpers;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(2)]
    [ConfigComponent(Constants.BLOCKS, typeof(BlockSelectionConfigComponent), PropertyName = "BlockSelectionComponent")]
    public sealed partial class CargoFilledApp : App, IApp
    {
        private const int STATUS_MESSAGE_FRAMES = 240;
        private readonly HashSet<long> _activeEntryIds = new HashSet<long>();

        private readonly List<CargoEntry> _entries = new List<CargoEntry>();
        private readonly LinkedTypedBlockSourceSet<IMyCargoContainer> _cargoContainers =
            new LinkedTypedBlockSourceSet<IMyCargoContainer>(delegate(TypedBlockCollection blocks)
            {
                return blocks.CargoContainers;
            });
        private readonly LinkedTypedBlockSourceSet<IMyRefinery> _refineries =
            new LinkedTypedBlockSourceSet<IMyRefinery>(delegate(TypedBlockCollection blocks)
            {
                return blocks.Refineries;
            });
        private readonly LinkedTypedBlockSourceSet<IMyShipConnector> _shipConnectors =
            new LinkedTypedBlockSourceSet<IMyShipConnector>(delegate(TypedBlockCollection blocks)
            {
                return blocks.ShipConnectors;
            });
        private readonly List<long> _entriesToRemove = new List<long>();
        private readonly Dictionary<long, CargoEntry> _entryModels = new Dictionary<long, CargoEntry>();
        private readonly InteractiveSurfaceScript _interactiveHost;
        private string _statusMessage;
        private long _statusUntilFrame;
        readonly CargoFilled.CargoFilledViewModel _nativeViewModel;

        public CargoFilledApp(IAppHost host) : base(host)
        {
            _nativeViewModel = new CargoFilled.CargoFilledViewModel(this);
            SetContent(new CargoFilled.CargoFilledView { DataContext = _nativeViewModel });
            _interactiveHost = host as InteractiveSurfaceScript;
            if (_interactiveHost == null)
                throw new ArgumentException("CargoFilledApp requires an InteractiveSurfaceScript host.", "host");

        }
        public bool HasEntries => _entries.Count > 0;
        internal IReadOnlyList<CargoEntry> NativeEntries => _entries;
        internal bool NativeGridMode => GeneralComponent.DisplayMode == (int)DisplayMode.Grid;
        internal override float WindowRenderScaling => CardScale;
        internal string NativeEntryClasses => (NativeGridMode ? "card" : "row") + (GeneralComponent.DrawLines ? " lines" : string.Empty);

        // The usage color while the entry is critical, otherwise the card fill; readable against what is behind it.
        internal Color NativeFill(float percentage)
        {
            bool lines = GeneralComponent.DrawLines;
            Color behind = lines ? Host.BackgroundColor : GetHeaderColor();
            Color fill = GetEntryUsageColor(percentage) ??
                         ThemeColor(lines ? LcdMod.Client.Gui.Styling.ThemeResources.AccentColor
                             : LcdMod.Client.Gui.Styling.ThemeResources.CardFillColor);
            return fill.EnsureMinimalContrast(behind);
        }

        public override void Update()
        {
            _entries.Clear();
            _activeEntryIds.Clear();
            AggregateAllContainersFromGridLogic(Host.GridLogic, _entries);
            RemoveInactiveEntryModels();
            _entries.Sort((a, b) =>
            {
                var fa = a.Cap > 0 ? a.Used / a.Cap : 0;
                var fb = b.Cap > 0 ? b.Used / b.Cap : 0;
                var cmp = fb.CompareTo(fa);
                if (cmp != 0) return cmp;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < _entries.Count; i++)
            {
                CargoEntry entry = _entries[i];
                entry.Clicked = delegate { OnEntryClicked(entry, null); };
            }
            _nativeViewModel.Refresh();
            if (_statusMessage != null && CurrentFrame > _statusUntilFrame)
            {
                _statusMessage = null;
                _nativeViewModel.Message.Hide();
            }
        }

        static long CurrentFrame => MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        public override void Close()
        {
            _cargoContainers.Dispose();
            _refineries.Dispose();
            _shipConnectors.Dispose();
            base.Close();
        }

        public override bool HasVisibleItems() => HasEntries;


        private Color? GetEntryUsageColor(float pct)
        {
            if (pct >= .99f)
                return ColorComponent.ResolveErrorColor();
            if (pct > .90f)
                return ColorComponent.ResolveWarningColor();
            return null;
        }

        private void OnEntryClicked(object dataContext, object sender)
        {
            var entry = dataContext as CargoEntry;
            if (entry == null)
                return;

            var entityId = entry.EntryId;
            LcdModClientComponent.RunNextFrame.Add(delegate { OpenActionDialog(entityId); });
        }

        private void OpenActionDialog(long entityId)
        {
            try
            {
                var source = MyAPIGateway.Entities != null
                    ? MyAPIGateway.Entities.GetEntityById(entityId) as IMyTerminalBlock
                    : null;
                if (source == null || !source.HasInventory)
                    return;

                var candidates = new List<IMyTerminalBlock>();
                CollectContainerCandidates(candidates);

                CargoFilled.ContainerActionLauncher.Show(this, source, candidates,
                    BlockSelectionComponent.SortFilterKeys,
                    BlockSelectionComponent.SortFilterCategories,
                    SaveSortFilter,
                    SetStatusMessage);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        private void SaveSortFilter(List<string> keys, List<string> categories)
        {
            try
            {
                BlockSelectionComponent.SortFilterKeys = keys != null ? keys.ToArray() : Array.Empty<string>();
                BlockSelectionComponent.SortFilterCategories = categories != null ? categories.ToArray() : Array.Empty<string>();
                var block = Host.Block as IMyTerminalBlock;
                if (block != null)
                    ConfigManager.Sync(block);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        private void SetStatusMessage(string message)
        {
            _statusMessage = message;
            _statusUntilFrame = CurrentFrame + STATUS_MESSAGE_FRAMES;
            _nativeViewModel.Message.Show(message, null, Host.Surface.ScriptForegroundColor);
            _interactiveHost.RenderSprites();
        }

        private void CollectContainerCandidates(List<IMyTerminalBlock> result)
        {
            var gridLogic = Host.GridLogic;
            if (gridLogic == null)
                return;

            var linkType = (GridLinkTypeEnum)BlockSelectionComponent.GridLinkTypeInternal;
            _cargoContainers.Bind(gridLogic, linkType);
            _shipConnectors.Bind(gridLogic, linkType);

            var cargoSources = _cargoContainers.Sources;
            for (var i = 0; i < cargoSources.Count; i++)
                AddContainerCandidates(cargoSources[i], result);

            var connectorSources = _shipConnectors.Sources;
            for (var i = 0; i < connectorSources.Count; i++)
                AddContainerCandidates(connectorSources[i], result);
        }

        private void AddContainerCandidates<T>(IList<T> blocks, List<IMyTerminalBlock> result)
            where T : class, IMyTerminalBlock
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null || !block.HasInventory)
                    continue;
                if (BlockSelectionComponent.SelectedBlocks.Length > 0 &&
                    Array.IndexOf(BlockSelectionComponent.SelectedBlocks, block.EntityId) < 0)
                    continue;

                result.Add(block);
            }
        }

        private void AggregateAllContainersFromGridLogic(GridLogic gridLogic, List<CargoEntry> details)
        {
            if (gridLogic == null)
                return;

            var linkType = (GridLinkTypeEnum)BlockSelectionComponent.GridLinkTypeInternal;
            _cargoContainers.Bind(gridLogic, linkType);
            _refineries.Bind(gridLogic, linkType);

            var cargoSources = _cargoContainers.Sources;
            for (var sourceIndex = 0; sourceIndex < cargoSources.Count; sourceIndex++)
            {
                var cargoContainers = cargoSources[sourceIndex];
                for (var i = 0; i < cargoContainers.Count; i++)
                    AggregateContainer(cargoContainers[i], false, details);
            }

            var refinerySources = _refineries.Sources;
            for (var sourceIndex = 0; sourceIndex < refinerySources.Count; sourceIndex++)
            {
                var refineries = refinerySources[sourceIndex];
                for (var i = 0; i < refineries.Count; i++)
                    AggregateContainer(refineries[i], true, details);
            }
        }

        private void AggregateContainer(IMyTerminalBlock fat, bool isRefinery, List<CargoEntry> details)
        {
            if (fat == null)
                return;

            if (BlockSelectionComponent.SelectedBlocks.Length > 0 &&
                Array.IndexOf(BlockSelectionComponent.SelectedBlocks, fat.EntityId) < 0)
                return;

            if (!fat.HasInventory)
                return;

            double localUsed = 0;
            double localCap = 0;
            if (isRefinery)
            {
                var inv = fat.GetInventory(0);
                if (inv != null)
                    try
                    {
                        localUsed = (double)inv.CurrentVolume;
                        localCap = (double)inv.MaxVolume;
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, Host);
                    }
            }
            else
            {
                var invCount = 0;
                try
                {
                    invCount = fat.InventoryCount;
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, Host);
                }

                for (var k = 0; k < invCount; k++)
                {
                    var inv = fat.GetInventory(k);
                    if (inv == null)
                        continue;
                    try
                    {
                        localUsed += (double)inv.CurrentVolume;
                        localCap += (double)inv.MaxVolume;
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, Host);
                    }
                }
            }

            if (localCap <= 0)
                return;

            string name;
            try
            {
                name = fat.CustomName;
                if (string.IsNullOrEmpty(name))
                    name = fat.DisplayNameText;
                if (string.IsNullOrEmpty(name))
                    name = fat.BlockDefinition.SubtypeName;
                if (string.IsNullOrEmpty(name))
                    name = "Container";
            }
            catch
            {
                name = "Container";
            }

            var entry = GetOrCreateEntry(fat.EntityId);
            var cubeBlock = fat as MyCubeBlock;
            var icon = cubeBlock == null || cubeBlock.BlockDefinition == null
                ? "MissingIcon"
                : TextureHelper.GetOrAddTextureForBlock(cubeBlock.BlockDefinition);
            entry.Update(icon, name, localUsed, localCap);
            details.Add(entry);
        }

        private CargoEntry GetOrCreateEntry(long entryId)
        {
            _activeEntryIds.Add(entryId);

            CargoEntry entry;
            if (!_entryModels.TryGetValue(entryId, out entry) || entry == null)
            {
                entry = new CargoEntry(entryId);
                _entryModels[entryId] = entry;
            }

            return entry;
        }

        private void RemoveInactiveEntryModels()
        {
            _entriesToRemove.Clear();
            foreach (var kv in _entryModels)
                if (!_activeEntryIds.Contains(kv.Key))
                    _entriesToRemove.Add(kv.Key);

            for (var i = 0; i < _entriesToRemove.Count; i++)
            {
                var entryId = _entriesToRemove[i];
                _entryModels.Remove(entryId);

            }
        }
    }
}
