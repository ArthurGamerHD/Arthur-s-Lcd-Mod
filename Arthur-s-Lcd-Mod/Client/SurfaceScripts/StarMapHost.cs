using Generated;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using SliderFov = LcdMod.Client.Terminal.Controls.Generic.SliderFov;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed class StarMapSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderFov>,
        IMultiDisplayMode,
        IUsesTerminalControl<ComboboxReferenceMode>
    {
        public const string ID = "LcdMod_StarMapSurface";
        public const string TITLE = "LcdMod_StarMapSurface";

        protected override ConfigKind ConfigKind => ConfigKind.StarMap;
        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

        readonly List<InteractiveEntry> _interactiveListFallback = new List<InteractiveEntry>();
        public override IApp App => _app;
        StarMapApp _app;

        public override List<InteractiveEntry> InteractiveList => _app != null ? _app.InteractiveList : _interactiveListFallback;

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
            var appConfig = Config as ScreenConfigStarMap;
            if (appConfig == null)
                return;

            if (_app == null)
            {
                _app = new StarMapApp(appConfig, this);
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
            CursorType = _app != null ? _app.RequestedCursorType : CursorType.Default;
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
