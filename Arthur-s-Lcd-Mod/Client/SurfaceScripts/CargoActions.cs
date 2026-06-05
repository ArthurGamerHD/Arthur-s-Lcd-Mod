using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Cargo;
using LcdMod.Client.Terminal.Controls.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class CargoActionsSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<ComboboxLinkType>,
        IUsesTerminalControl<SwitchShowConfigButton>
    {
        protected override ConfigKind ConfigKind => ConfigKind.CargoActions;
        public const string ID = "CargoActions";
        public const string TITLE = "LcdMod_CargoActions_Title";
        protected override string DefaultTitle => TITLE;

        public override IApp App => _app;
        readonly List<ControlBase> _interactiveListFallback = new List<ControlBase>();
        CargoActionsApp _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override List<ControlBase> InteractiveList => _app != null ? _app.InteractiveList : _interactiveListFallback;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

        public CargoActionsSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            base.SafeRun();

            if (_app == null)
                _app = new CargoActionsApp(AppConfig, this);

            Scale = GetAutoScaleUniform();
            UpdateViewBox();
            _app.Update();

            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            DrawTitle(sprites);
            if (_app != null)
                sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
