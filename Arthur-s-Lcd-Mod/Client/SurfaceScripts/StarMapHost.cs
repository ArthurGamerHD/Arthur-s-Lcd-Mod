using LcdMod.Common.Config.Components;
using Generated;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Generic;
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
    [LcdSurface(typeof(LcdMod.Client.Apps.StarMapApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed partial class StarMapSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderFov>,
        IMultiDisplayMode,
        IUsesTerminalControl<ComboboxReferenceMode>
    {
        public const string ID = MOD_PREFIX + "StarMapSurface";
        public const string TITLE = MOD_PREFIX + "StarMapSurface";
        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        protected override bool RendersInteractiveEntriesInGetSprites => true;
        public override IApp App => _app;
        StarMapApp _app;

        public override List<Control> InteractiveList => _app.Children as List<Control>;

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
                CursorType = _app.RequestedCursorType;
            }
        }

        public override void SafeRun()
        {
            if (_app == null)
            {
                _app = new StarMapApp(this);
                _app.LayoutChanged();
            }

            _app.Update();
            CursorType = _app.RequestedCursorType;
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            DrawTitle(sprites);
            if (_app != null)
            {
                sprites.AddRange(_app.GetSprites());
                RenderInteractiveEntryVisuals(sprites);
            }
            CursorType = _app?.RequestedCursorType ?? CursorType.Default;
            return sprites;
        }

        protected override void OnLookAt(Vector2 onScreenCoordinates)
        {
            if (_app != null)
                _app.OnLookAt(onScreenCoordinates);
            base.OnLookAt(onScreenCoordinates);
        }

        protected override void OnMouseScroll(int delta, ref bool handled)
        {
            base.OnMouseScroll(delta, ref handled);
            if (_app != null)
                _app.OnMouseScroll(delta, ref handled);
        }
    }
}
