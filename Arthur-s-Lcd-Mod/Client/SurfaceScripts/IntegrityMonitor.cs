using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using ListboxProjectorSelection = LcdMod.Client.Terminal.Controls.Blueprint.ListboxProjectorSelection;
using SliderRotation = LcdMod.Client.Terminal.Controls.Generic.SliderRotation;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(IntegrityMonitorApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class IntegrityMonitorSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControl<SliderRotation>,
        IUsesTerminalControl<ListboxProjectorSelection>,
        IMultiDisplayMode
    {
        public const string ID = "PreviewCharts";
        public const string TITLE = IntegrityMonitorApp.TITLE;

        IntegrityMonitorApp _app;

        public override IApp App => _app;
        public override string Title => _app != null ? _app.Title : base.Title;
        protected override string DefaultTitle => TITLE;

        public IntegrityMonitorSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface,
            block, size)
        {
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return IntegrityMonitorApp.IntegrityAxes;
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
                _app = new IntegrityMonitorApp(this);
                _app.LayoutChanged();
            }

            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            return _app != null ? _app.GetSprites() : new List<MySprite>();
        }
    }
}
