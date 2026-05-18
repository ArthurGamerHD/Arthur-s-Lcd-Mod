using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using ComboboxGraphWindow = LcdMod.Client.Terminal.Controls.Generic.ComboboxGraphWindow;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using ScreenConfigPower = LcdMod.Common.Config.Models.Apps.ScreenConfigPower;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class EnergyDashboardSurfaceScript : SurfaceScriptBase, IUsesTerminalControl<ComboboxGraphWindow>
    {
        protected override ConfigKind ConfigKind => ConfigKind.Power;
        public const string ID = "LcdMod_EnergyDashboard";
        public const string TITLE = "LcdMod_EnergyDashboard";
        protected override string DefaultTitle => TITLE;
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

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                sprites.AddRange(_app.GetSprites());
                frame.AddRange(sprites);
            }
        }
    }
}
