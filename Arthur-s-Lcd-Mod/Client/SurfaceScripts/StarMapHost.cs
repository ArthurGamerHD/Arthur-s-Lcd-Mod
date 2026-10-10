using Generated;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Gui;
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
    [LcdSurface(typeof(StarMapApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed partial class StarMapSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderFov>,
        IMultiDisplayMode,
        IUsesTerminalControl<ComboboxReferenceMode>,
        IUsesTerminalControl<SwitchDisplayMyGps>,
        IUsesTerminalControl<SwitchIncludeRadioSignals>,
        IUsesTerminalControlGroup<GpsAlwaysDisplayTerminalControlGroup>
    {
        public const string ID = MOD_PREFIX + "StarMapSurface";
        public const string TITLE = MOD_PREFIX + "StarMapSurface";
        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override IApp App => _app;
        public override bool AsyncRender => false;
        StarMapApp _app;

        public StarMapSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return _app != null ? _app.GetDisplayModes() : StarMapApp.StarMapDisplayModes;
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
            {
                _app.LayoutChanged();
                CursorType = ResolveRetainedCursorType();
            }
        }

        public override void SafeRun()
        {
            if (_app == null)
            {
                _app = new StarMapApp(this);
                _app.LayoutChanged();
            }

            EnsureRetainedAppView(_app);
            _app.Update();
            CursorType = ResolveRetainedCursorType();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = BeginFrameSprites();
            AddBackground(sprites);
            if (_app != null)
            {
                DrawRetainedAppView(sprites);
            }
            CursorType = ResolveRetainedCursorType();
            return sprites;
        }

        protected override CursorType ResolveRetainedCursorType()
        {
            return _app != null && _app.RequestedCursorType == CursorType.None
                ? CursorType.None
                : base.ResolveRetainedCursorType();
        }

    }
}
