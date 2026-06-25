using System.Collections.Generic;
using LcdMod.Common.Config.Components;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Groups;
using LcdMod.Client.Utility;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using ListboxProjectorSelection = LcdMod.Client.Terminal.Controls.Blueprint.ListboxProjectorSelection;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using SwitchToggleLines = LcdMod.Client.Terminal.Controls.Generic.SwitchToggleLines;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.ProjectorApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class ProjectorLcdSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<ListboxProjectorSelection>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>,
        IMultiDisplayMode
    {
        public const string ID = "ProjectorCharts";
        public const string TITLE = ProjectorApp.TITLE;


        ProjectorApp _app;

        public override IApp App => _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override List<Control> InteractiveList => _app?.Children as List<Control>;
        public override string Title => _app != null ? _app.Title : base.Title;
        protected override string DefaultTitle => TITLE;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

        public ProjectorLcdSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.GridAndLegacy;
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        public override void SafeRun()
        {
            base.SafeRun();

            if (_app == null)
            {
                _app = new ProjectorApp(this);
                _app.LayoutChanged();
            }

            _app.Update();

            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            if (_app == null)
                return sprites;

            if (_app.IsLoading)
            {
                AddLoadingScreenSprites(sprites, GeneralComponent.GetScale());
                return sprites;
            }

            if (!_app.HasItems)
            {
                if (_app.HasFilters)
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
