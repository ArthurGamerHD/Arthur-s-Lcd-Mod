#if DEBUG
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.VisibleTreeDebug
{
    public sealed partial class ListboxVisibleTreeDebugScreenSelection : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        readonly List<SurfaceScriptBase> _instances = new List<SurfaceScriptBase>();

        public ListboxVisibleTreeDebugScreenSelection()
        {
            var listbox = CreateControl<IMyTerminalControlListbox>("VisibleTreeDebugScreenSelection");
            listbox.ListContent = Getter;
            listbox.ItemSelected = Setter;
            listbox.Visible = Visible;
            listbox.VisibleRowsCount = 8;
            listbox.Multiselect = false;
            listbox.Title = MyStringId.GetOrCompute("Debug screen");
            TerminalControl = listbox;
        }

        void Setter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selection)
        {
            ConfigManager.ModifyComponentForTerminalApp<VisibleTreeDebugConfigComponent>(
                block,
                config => config.ReferenceScreenIndex = (int)ListBoxItemHelper.GetLongUserData(selection.FirstOrDefault()));
        }

        void Getter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> itemList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var app = ConfigManager.GetComponentForTerminalApp<VisibleTreeDebugConfigComponent>(block);
            var reference = ConfigManager.GetComponentForCurrentSurface<BlockReferenceConfigComponent>(
                block,
                Constants.VISIBLE_TREE_REFERENCE);
            if (app == null || reference == null || reference.EntityId == 0L)
                return;

            GetInstances(reference.EntityId);
            for (int i = 0; i < _instances.Count; i++)
            {
                var instance = _instances[i];
                long index = instance.RotationOrSurfaceIndex;
                itemList.Add(new MyTerminalControlListBoxItem(
                    MyStringId.GetOrCompute(GetScreenName(instance)),
                    MyStringId.GetOrCompute(GetScreenTooltip(instance)),
                    index));
            }

            var selection = itemList.FirstOrDefault(a => ListBoxItemHelper.GetLongUserData(a) == app.ReferenceScreenIndex);
            if (selection != null)
                selected.Add(selection);
        }

        void GetInstances(long blockId)
        {
            _instances.Clear();
            foreach (var instance in SurfaceScriptBase.Instances)
            {
                var block = instance?.Block as IMyTerminalBlock;
                if (block == null || block.EntityId != blockId || block.MarkedForClose)
                    continue;

                _instances.Add(instance);
            }

            _instances.Sort((left, right) => left.RotationOrSurfaceIndex.CompareTo(right.RotationOrSurfaceIndex));
        }

        static string GetScreenName(SurfaceScriptBase instance)
        {
            string surfaceName = instance.Surface?.DisplayName;
            if (string.IsNullOrEmpty(surfaceName))
                surfaceName = "Screen " + instance.RotationOrSurfaceIndex;

            string appName = instance.App != null ? instance.App.GetType().Name : instance.GetType().Name;
            return surfaceName + " - " + appName;
        }

        static string GetScreenTooltip(SurfaceScriptBase instance)
        {
            var block = instance.Block as IMyTerminalBlock;
            string blockName = block != null ? block.CustomName : string.Empty;
            return blockName + " / index " + instance.RotationOrSurfaceIndex;
        }
    }
}
#endif
