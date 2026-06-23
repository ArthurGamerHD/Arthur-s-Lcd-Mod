using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts;
using LcdMod.Common.Helpers;
using ManagedDoom.SE;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    /// <summary>
    /// Selects the cockpit whose live controls are forwarded to the Doom app on
    /// the currently selected LCD surface.
    /// </summary>
    public sealed class ListboxDoomCockpitSelection : TerminalControlsWrapper
    {
        readonly List<IMyTerminalBlock> _cockpits = new List<IMyTerminalBlock>();
        readonly List<IMySlimBlock> _blocks = new List<IMySlimBlock>();
        readonly List<IMyCubeGrid> _grids = new List<IMyCubeGrid>();

        public override IMyTerminalControl TerminalControl { get; }

        public ListboxDoomCockpitSelection()
        {
            var listbox = CreateControl<IMyTerminalControlListbox>("DoomCockpitSelection");
            listbox.ListContent = Getter;
            listbox.ItemSelected = Setter;
            listbox.Visible = Visible;
            listbox.VisibleRowsCount = 6;
            listbox.Multiselect = false;
            listbox.Title = MyStringId.GetOrCompute("Doom input cockpit");
            listbox.Tooltip = MyStringId.GetOrCompute(
                "Cockpit or seat that enables the local classic keyboard controls for Doom on this surface.");
            TerminalControl = listbox;
        }

        public override bool VisibleForScript(string script)
        {
            return script == ManagedDoomSurfaceScript.ID;
        }

        void Setter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selection)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            long entityId = 0L;
            if (selection != null && selection.Count > 0)
                entityId = GetEntityId(selection[0]);

            DoomInputSettings.SetCockpitEntityId(config, entityId);
            RemapHelper.PinBlock(entityId);
            ConfigManager.Sync(block);
        }

        void Getter(
            IMyTerminalBlock block,
            List<MyTerminalControlListBoxItem> blockList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            long configuredId = DoomInputSettings.GetCockpitEntityId(config);
            var none = CreateItem("(none)", string.Empty, 0L);
            blockList.Add(none);
            if (configuredId == 0L)
                selected.Add(none);

            GetCockpits(block == null ? null : block.CubeGrid);
            for (int i = 0; i < _cockpits.Count; i++)
            {
                var cockpit = _cockpits[i];
                var item = CreateCockpitItem(cockpit);
                blockList.Add(item);

                if (cockpit.EntityId == configuredId)
                    selected.Add(item);
            }

            if (configuredId != 0L && !ContainsEntity(blockList, configuredId))
            {
                IMyEntity entity;
                if (MyAPIGateway.Entities != null &&
                    MyAPIGateway.Entities.TryGetEntityById(configuredId, out entity))
                {
                    var cockpit = entity as IMyCockpit;
                    if (cockpit != null && !cockpit.MarkedForClose)
                    {
                        var item = CreateCockpitItem(cockpit);
                        blockList.Add(item);
                        selected.Add(item);
                    }
                }
            }
        }

        void GetCockpits(IMyCubeGrid rootGrid)
        {
            _cockpits.Clear();
            _grids.Clear();

            if (rootGrid == null || MyAPIGateway.GridGroups == null)
                return;

            MyAPIGateway.GridGroups.GetGroup(rootGrid, GridLinkTypeEnum.Logical, _grids);
            if (_grids.Count == 0 || !_grids.Contains(rootGrid))
                _grids.Add(rootGrid);

            for (int i = 0; i < _grids.Count; i++)
            {
                var grid = _grids[i];
                if (grid == null)
                    continue;

                _blocks.Clear();
                grid.GetBlocks(_blocks);

                for (int j = 0; j < _blocks.Count; j++)
                {
                    var cockpit = _blocks[j].FatBlock as IMyCockpit;
                    if (cockpit != null && !cockpit.MarkedForClose)
                        _cockpits.Add(cockpit);
                }
            }
        }

        static MyTerminalControlListBoxItem CreateCockpitItem(IMyTerminalBlock cockpit)
        {
            string gridName = cockpit.CubeGrid == null
                ? string.Empty
                : cockpit.CubeGrid.DisplayName;

            return CreateItem(cockpit.CustomName, gridName, cockpit.EntityId);
        }

        static MyTerminalControlListBoxItem CreateItem(string text, string tooltip, long entityId)
        {
            return new MyTerminalControlListBoxItem(
                MyStringId.GetOrCompute(text ?? string.Empty),
                MyStringId.GetOrCompute(tooltip ?? string.Empty),
                entityId);
        }

        static bool ContainsEntity(List<MyTerminalControlListBoxItem> items, long entityId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (GetEntityId(items[i]) == entityId)
                    return true;
            }

            return false;
        }

        static long GetEntityId(MyTerminalControlListBoxItem item)
        {
            if (item == null || !(item.UserData is long))
                return 0L;

            return (long)item.UserData;
        }
    }
}
