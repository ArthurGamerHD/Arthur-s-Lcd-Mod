using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Snapshots;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using SliderRotation = LcdMod.Client.Terminal.Controls.Generic.SliderRotation;

using LcdMod.Common.Config.Generation;
using static LcdMod.Common.Helpers.Constants;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(TargetInfoApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class TargetInfoSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderRotation>,
        IMultiDisplayMode,
        IGridSnapshotScreen
    {
        public const string ID = MOD_PREFIX + "TargetInfo";
        public const string TITLE = TargetInfoApp.TITLE;

        TargetInfoApp _app;

        public override IApp App => _app;
        public override string Title => _app != null ? _app.Title : base.Title;
        protected override string DefaultTitle => TITLE;

        public TargetInfoSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base((Sandbox.ModAPI.IMyTextSurface)surface, block, size)
        {
        }

        public bool HasGridSnapshot => _app != null && _app.HasSnapshot;
        public void ClearGridSnapshot()
        {
            _app?.ClearSnapshot();
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return PreviewGridApp.IntegrityAxes;
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
                _app = new TargetInfoApp(this);
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

        protected override void OnMouseScroll(int delta, ref bool handled)
        {
            base.OnMouseScroll(delta, ref handled);
            if (_app != null)
                _app.OnMouseScroll(delta, ref handled);
        }
    }
}
