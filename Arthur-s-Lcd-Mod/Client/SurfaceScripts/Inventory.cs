using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Groups;
using LcdMod.Client.Utility;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using CheckboxHideEmpty = LcdMod.Client.Terminal.Controls.Generic.CheckboxHideEmpty;
using ComboboxSorting = LcdMod.Client.Terminal.Controls.Generic.ComboboxSorting;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using SwitchToggleLines = LcdMod.Client.Terminal.Controls.Generic.SwitchToggleLines;
using ScreenConfigWithItems = LcdMod.Common.Config.Models.Apps.ScreenConfigWithItems;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, "Inventory")]
    public partial class InventoryLcdSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<CheckboxHideEmpty>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>,
        IUsesTerminalControlGroup<ItemsFilterTerminalControlGroup>,
        IUsesTerminalControl<ComboboxSorting>,
        IMultiDisplayMode
    {
        protected override ConfigKind ConfigKind => ConfigKind.WithItems;
        public const string ID = "InventoryCharts";
        public const string NAME = InventoryApp.NAME;

        InventoryApp _app;

        public override IApp App => _app;
        public override string Title => _app != null ? _app.Title : base.Title;
        protected override string DefaultTitle => NAME;

        public InventoryLcdSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface,
            block, size)
        {
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.GridAndLegacy;
        }

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            if (_app == null)
                _app = new InventoryApp((ScreenConfigWithItems)AppConfig, this);

            _app.Update();

            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            if (_app == null || !_app.HasItems)
            {
                if (_app != null && _app.HasFilters)
                    AddEmptyWithFiltersSprites(sprites);
                else
                    AddEmptySprites(sprites);
                return sprites;
            }

            AddBackground(sprites);
            DrawTitle(sprites);
            sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
