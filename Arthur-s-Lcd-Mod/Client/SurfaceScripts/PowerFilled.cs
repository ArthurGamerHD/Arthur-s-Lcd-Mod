using System.Collections.Generic;
using Generated;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.Models.Power;
using LcdMod.Client.Helpers;
using LcdMod.Client.Utility;
using LcdMod.Client.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.Apps.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class PowerFilledSurfaceScript : InteractiveSurfaceScript
    {
        protected override ConfigKind ConfigKind => ConfigKind.Power;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public const string ID = "BatteryGraph";
        public const string TITLE = "LcdMod_PowerFilled";

        protected override string DefaultTitle => TITLE;

        readonly PowerFilledApp _app;
        readonly List<InteractiveEntry> _interactiveListFallback = new List<InteractiveEntry>();
        public override List<InteractiveEntry> InteractiveList => _app != null ? _app.InteractiveList : _interactiveListFallback;

        public PowerFilledSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
            _app = new PowerFilledApp(this);
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _app.LayoutChanged();
        }

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            _app.SetConfig(AppConfig);
            _app.Update(GridLogic);
            RenderSprites();
        }

        protected override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            DrawTitle(sprites);
            if (!_app.HasVisibleItems())
                DrawMessage(sprites, LocHelper.Empty, "Warning", AppConfig.WarningColor, AppConfig.Scale);
            else
                sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
