using Generated;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using Acrux.Core.Input;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Client.Terminal.Controls.Gps;
using LcdMod.Client.Terminal.Controls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using SliderFov = LcdMod.Client.Terminal.Controls.Generic.SliderFov;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(ExternalSensorsApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed partial class ExternalSensorsSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderFov>,
        IMultiDisplayMode,
        IUsesTerminalControl<ListboxCameraSelection>,
        IUsesTerminalControl<ComboboxReferenceMode>,
        IUsesTerminalControl<SwitchDisplayMyGps>,
        IUsesTerminalControl<SwitchIncludeRadioSignals>,
        IUsesTerminalControlGroup<GpsAlwaysDisplayTerminalControlGroup>
    {
        public const string ID = MOD_PREFIX + "ExternalSensors";
        public const string TITLE = ExternalSensorsApp.TITLE;
        protected override string DefaultTitle => TITLE;
        public override IApp App => _app;
        public override bool AsyncRender => false;
        ExternalSensorsApp _app;

        public ExternalSensorsSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return ExternalSensorsApp.DisplayModes;
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        public override void SafeRun()
        {
            if (_app == null)
            {
                _app = new ExternalSensorsApp(this);
                _app.LayoutChanged();
            }

            EnsureRetainedAppView(_app);
            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = BeginFrameSprites();
            AddBackground(sprites);
            if (_app != null)
                DrawRetainedAppView(sprites);
            return sprites;
        }

        protected override PointerCursor CursorOverride =>
            _app != null ? _app.RequestedCursor : base.CursorOverride;
    }
}
