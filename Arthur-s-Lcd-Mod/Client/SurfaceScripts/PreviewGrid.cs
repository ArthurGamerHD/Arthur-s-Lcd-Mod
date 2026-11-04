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
using ListboxProjectorSelection = LcdMod.Client.Terminal.Controls.Blueprint.ListboxProjectorSelection;
using SliderRotation = LcdMod.Client.Terminal.Controls.Generic.SliderRotation;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(PreviewGridApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class PreviewGridSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderRotation>,
        IUsesTerminalControl<ListboxProjectorSelection>,
        IMultiDisplayMode,
        IGridSnapshotTaker
    {
        public const string ID = "PreviewCharts";
        public const string TITLE = PreviewGridApp.TITLE;

        PreviewGridApp _app;

        public override IApp App => _app;
        public override string Title => _app != null ? _app.Title : base.Title;
        protected override string DefaultTitle => TITLE;

        public PreviewGridSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base((Sandbox.ModAPI.IMyTextSurface)surface, block, size)
        {
        }

        public bool HasGridSnapshot => _app != null && _app.HasSnapshot;
        public bool CanTakeGridSnapshot => _app != null && _app.CanTakeSnapshot;

        public void TakeGridSnapshot()
        {
            _app?.TakeSnapshot();
        }

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
                _app = new PreviewGridApp(this);
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
