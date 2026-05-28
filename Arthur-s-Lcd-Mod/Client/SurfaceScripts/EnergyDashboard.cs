using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using ComboboxGraphWindow = LcdMod.Client.Terminal.Controls.Generic.ComboboxGraphWindow;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using ComboboxLinkType = LcdMod.Client.Terminal.Controls.Generic.ComboboxLinkType;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class EnergyDashboardSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControl<ComboboxGraphWindow>,
        IUsesTerminalControl<ComboboxLinkType>
    {
        protected override ConfigKind ConfigKind => ConfigKind.Power;
        public const string ID = "LcdMod_EnergyDashboard";
        public const string TITLE = "LcdMod_EnergyDashboard";
        protected override string DefaultTitle => TITLE;
        public override IApp App => _app;
        IApp _app;

        public EnergyDashboardSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            if (_app == null)
                _app = new EnergyDashboardApp(AppConfig, this);

            Scale = GetAutoScaleUniform();
            UpdateViewBox();
            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            DrawTitle(sprites);
            if (_app != null)
                sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
