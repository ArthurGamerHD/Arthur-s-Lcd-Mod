using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.ClockDashboard;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.IMyTextSurface;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed partial class InGameClockDashboardSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SwitchClockDashboard24Hour>,
        IUsesTerminalControl<ComboboxClockDashboardTemperatureMode>
    {
        public const string ID = MOD_PREFIX + "InGameClockDashboard";
        public const string TITLE = ClockDashboardLocalization.TITLE_KEY;

        InGameClockDashboardApp _app;

        protected override ConfigKind ConfigKind => ConfigKind.ClockDashboard;
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
            if (AppConfig == null)
                return;

            base.SafeRun();

            if (_app == null)
                _app = new InGameClockDashboardApp((ScreenConfigClockDashboard)AppConfig, this);

            UpdateViewBox();
            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            if (_app == null || AppConfig == null)
                return sprites;

            AddBackground(sprites);
            DrawTitle(sprites);
            sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
