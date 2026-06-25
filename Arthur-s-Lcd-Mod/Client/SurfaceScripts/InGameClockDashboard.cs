using LcdMod.Common.Config.Components;
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.ClockDashboard;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.IMyTextSurface;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.InGameClockDashboardApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed partial class InGameClockDashboardSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<ComboboxClockDashboardTemperatureMode>
    {
        public const string ID = MOD_PREFIX + "InGameClockDashboard";
        public const string TITLE = ClockDashboardLocalization.TITLE_KEY;

        InGameClockDashboardApp _app;
        protected override string DefaultTitle => TITLE;
        protected override bool RendersInteractiveEntriesInGetSprites => true;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override IApp App => _app;
        public override List<Control> InteractiveList => _app?.Children as List<Control>;

        public InGameClockDashboardSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
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
            base.SafeRun();

            if (_app == null)
                _app = new InGameClockDashboardApp(this);

            UpdateViewBox();
            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            if (_app == null)
                return sprites;

            AddBackground(sprites);
            DrawTitle(sprites);
            sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
