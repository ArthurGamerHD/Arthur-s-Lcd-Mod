using System.Collections.Generic;
using Generated;
using IAutoScroll = LcdMod.Client.Terminal.Controls.Generic.IAutoScroll;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using CheckboxHideEmpty = LcdMod.Client.Terminal.Controls.Generic.CheckboxHideEmpty;
using ComboboxSorting = LcdMod.Client.Terminal.Controls.Generic.ComboboxSorting;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using ComboboxItemDisplayMode = LcdMod.Client.Terminal.Controls.Generic.ComboboxItemDisplayMode;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(InventoryApp))]
    [MyTextSurfaceScript(ID, "Inventory")]
    public partial class InventoryLcdSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<ComboboxItemDisplayMode>,
        IUsesTerminalControl<CheckboxHideEmpty>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>,
        IUsesTerminalControlGroup<ItemsFilterTerminalControlGroup>,
        IUsesTerminalControl<ComboboxSorting>,
        IAutoScroll
    {
        public const string ID = "InventoryCharts";
        public const string NAME = InventoryApp.NAME;


        InventoryApp _app;

        public override IApp App => _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override List<Control> InteractiveList => _app?.VisualChildren as List<Control>;
        public override string Title => _app != null ? _app.Title : base.Title;
        protected override string DefaultTitle => NAME;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

        public InventoryLcdSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface,
            block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        protected override IApp DetachGridBoundApp()
        {
            var app = _app;
            _app = null;
            return app;
        }

        public override void SafeRun()
        {
            base.SafeRun();

            if (_app == null)
                _app = new InventoryApp(this);

            _app.Update();

            if (_app.IsDirty || InteractiveVisualsDirty)
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

                _app?.CompleteHostRender();
                return sprites;
            }

            AddBackground(sprites);
            DrawTitle(sprites);
            sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
