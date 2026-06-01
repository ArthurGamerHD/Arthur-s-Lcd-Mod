using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Groups;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed class DigitalPictureFramesSurfaceScript : InteractiveSurfaceScript,
        IMultiDisplayMode,
        IUsesTerminalControlGroup<SpriteSelectorTerminalControlGroup>
    {
        static readonly List<MyTerminalControlComboBoxItem> PictureFrameDisplayModes =
            new List<MyTerminalControlComboBoxItem>
            {
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)DigitalPictureFramesApp.PictureFrameDisplayMode.Center,
                    Value = MyStringId.GetOrCompute("Center")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)DigitalPictureFramesApp.PictureFrameDisplayMode.Fit,
                    Value = MyStringId.GetOrCompute("Fit")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)DigitalPictureFramesApp.PictureFrameDisplayMode.Fill,
                    Value = MyStringId.GetOrCompute("Fill")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)DigitalPictureFramesApp.PictureFrameDisplayMode.Stretch,
                    Value = MyStringId.GetOrCompute("Stretch")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)DigitalPictureFramesApp.PictureFrameDisplayMode.Tile,
                    Value = MyStringId.GetOrCompute("Tile")
                }
            };

        public const string ID = "LcdMod_DigitalPictureFrames";
        public const string TITLE = "LcdMod_DigitalPictureFrames";

        readonly List<ControlBase> _interactiveListFallback = new List<ControlBase>();
        DigitalPictureFramesApp _app;

        protected override ConfigKind ConfigKind => ConfigKind.DigitalPictureFrames;
        protected override string DefaultTitle => TITLE;
        public override IApp App => _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override List<ControlBase> InteractiveList => _app != null ? _app.InteractiveList : _interactiveListFallback;

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return PictureFrameDisplayModes;
        }

        public DigitalPictureFramesSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _app?.LayoutChanged();
        }

        public override void SafeRun()
        {
            var appConfig = Config as ScreenConfigDigitalPictureFrames;
            if (appConfig == null)
                return;

            if (_app == null)
                _app = new DigitalPictureFramesApp(appConfig, this);

            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            if (_app == null || Config == null)
                return sprites;

            AddBackground(sprites);
            DrawTitle(sprites);
            sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
