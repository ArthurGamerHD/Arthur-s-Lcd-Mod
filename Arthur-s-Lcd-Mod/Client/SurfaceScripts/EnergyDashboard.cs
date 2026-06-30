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
using ComboboxLinkType = LcdMod.Client.Terminal.Controls.Generic.ComboboxLinkType;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.EnergyDashboardApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class EnergyDashboardSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<ComboboxGraphWindow>,
        IUsesTerminalControl<ComboboxLinkType>
    {
        public const string ID = MOD_PREFIX + "EnergyDashboard";
        public const string TITLE = MOD_PREFIX + "EnergyDashboard";
        protected override string DefaultTitle => TITLE;
        public override IApp App => _app;
        IApp _app;

        public override List<Control> InteractiveList => _app.VisualChildren as List<Control>;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

        public EnergyDashboardSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public override CursorType CursorType { get; protected set; }

        public override void SafeRun()
        {
            if (_app == null)
                _app = new EnergyDashboardApp(this);

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
