using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Groups;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using ListboxProjectorSelection = LcdMod.Client.Terminal.Controls.Blueprint.ListboxProjectorSelection;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using SwitchToggleLines = LcdMod.Client.Terminal.Controls.Generic.SwitchToggleLines;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class ProjectorLcdSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<ListboxProjectorSelection>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>
    {
        protected override ConfigKind ConfigKind => ConfigKind.Projector;
        public const string ID = "ProjectorCharts";
        public const string TITLE = ProjectorApp.TITLE;

        ProjectorApp _app;

        public override IApp App => _app;
        public override string Title => _app != null ? _app.Title : base.Title;
        protected override string DefaultTitle => TITLE;

        public ProjectorLcdSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        public override void SafeRun()
        {
            var appConfig = AppConfig as ScreenConfigProjector;
            if (appConfig == null)
                return;

            if (_app == null)
            {
                _app = new ProjectorApp(appConfig, this);
                _app.LayoutChanged();
            }

            _app.Update();

            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            var appConfig = AppConfig as ScreenConfigProjector;
            if (_app == null || appConfig == null)
                return sprites;

            if (_app.IsLoading)
            {
                AddLoadingScreenSprites(sprites, appConfig.Scale);
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
