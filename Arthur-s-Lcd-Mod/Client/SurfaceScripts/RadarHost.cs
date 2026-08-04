using Generated;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Terminal.Controls.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using SliderRadarRange = LcdMod.Client.Terminal.Controls.Generic.SliderRadarRange;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(RadarApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed partial class RadarSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderRadarRange>,
        IUsesTerminalControl<ComboboxReferenceMode>
    {
        public const string ID = MOD_PREFIX + "Radar";
        public const string TITLE = MOD_PREFIX + "Radar";
        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

        public override IApp App => _app;       
        readonly List<Control> _emptyInteractive = new List<Control>();
        RadarApp _app;

        public override List<Control> InteractiveList
        {
            get
            {
                return _app != null
                    ? _app.VisualChildren as List<Control> ?? _emptyInteractive
                    : _emptyInteractive;
            }
        }

        public RadarSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
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
            if (_app == null)
                _app = new RadarApp(this);

            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            if (_app != null)
            {
                sprites.AddRange(_app.GetSprites());
                RenderInteractiveEntryVisuals(sprites);
            }
            DrawTitle(sprites);
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
