using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using ComboboxGraphWindow = LcdMod.Client.Terminal.Controls.Generic.ComboboxGraphWindow;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using ComboboxPowerLinkType = LcdMod.Client.Terminal.Controls.Generic.ComboboxPowerLinkType;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(EnergyDashboardApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class EnergyDashboardSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<ComboboxGraphWindow>,
        IUsesTerminalControl<ComboboxPowerLinkType>
    {
        public const string ID = MOD_PREFIX + "EnergyDashboard";
        public const string TITLE = MOD_PREFIX + "EnergyDashboard";
        protected override string DefaultTitle => TITLE;
        public override IApp App => _app;
        EnergyDashboardApp _app;

        public EnergyDashboardSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public override CursorType CursorType { get; protected set; }

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
                _app = new EnergyDashboardApp(this);

            EnsureRetainedAppView(_app);
            UpdateViewBox();
            _app.Update();
            if (_app.IsDirty || InteractiveVisualsDirty)
                RenderSprites();
        }

        protected override bool RendersInParallel => true;


        public override List<MySprite> GetSprites()
        {
            var sprites = BeginFrameSprites();
            AddBackground(sprites);
            if (_app != null)
                DrawRetainedAppView(sprites);
            return sprites;
        }
    }
}
