using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
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
        readonly EnergyDashboardApp _app = new EnergyDashboardApp();

        public EnergyDashboardSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            Scale = GetAutoScaleUniform();
            UpdateViewBox();
            _app.Update(this);

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                _app.Draw(this, sprites);
                frame.AddRange(sprites);
            }
        }

        internal ScreenConfigPower ConfigPower => AppConfig;
        internal LcdMod.Client.Grid.GridLogic GridLogicInternal => GridLogic;
        internal float CaretYInternal => CaretY;
        internal float FontScaleInternal => FontScale;
    }
}
