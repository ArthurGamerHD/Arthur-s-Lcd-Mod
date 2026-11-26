using System.Collections.Generic;
using Acrux.Controls;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using Generated;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Common.Config.Generation;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(MultiFunctionDisplayApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class MultiFunctionDisplaySurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<ComboboxMultiFunctionMode>,
        IUsesTerminalControl<ComboboxMenuPlacement>
    {
        public const string ID = MOD_PREFIX + "MultiFunctionDisplay";
        public const string TITLE = MOD_PREFIX + "MultiFunctionDisplay";
        protected override string DefaultTitle => TITLE;
        public override IApp App => _app;
        MultiFunctionDisplayApp _app;

        public MultiFunctionDisplaySurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        // The hosted apps subscribe to the grid they were created on; a new display re-creates them for the current grid.
        protected override IApp DetachGridBoundApp()
        {
            var app = _app;
            _app = null;
            return app;
        }

        protected override void OnMouseScroll(int delta, ref bool handled)
        {
            base.OnMouseScroll(delta, ref handled);
            if (_app != null)
                _app.OnMouseScroll(delta, ref handled);
        }

        public override void SafeRun()
        {
            base.SafeRun();
            if (_app == null)
                _app = new MultiFunctionDisplayApp(this);

            EnsureRetainedAppView(_app);
            UpdateViewBox();
            var host = RetainedAppGuiHost;
            _app.Attach(host == null ? null : host.Root as Window);
            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = BeginFrameSprites();
            AddBackground(sprites);
            if (_app != null)
            {
                DrawRetainedAppView(sprites);
                _app.CompleteHostedRender();
            }
            return sprites;
        }

        protected override void OnLookAt(Vector2 onScreenCoordinates)
        {
            base.OnLookAt(onScreenCoordinates);
            if (_app != null)
                _app.PointerMoved(onScreenCoordinates);
        }

        public override void SetPointerButtonState(ControlClickButton button, bool pressed)
        {
            base.SetPointerButtonState(button, pressed);
            if (button == ControlClickButton.Primary && _app != null)
                _app.PrimaryButton(pressed);
        }

        public override void ResetPointerButtonInput()
        {
            base.ResetPointerButtonInput();
            if (_app != null)
                _app.PrimaryButton(false);
        }
    }
}
