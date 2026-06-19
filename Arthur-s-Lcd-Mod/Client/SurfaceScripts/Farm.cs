using System.Collections.Generic;
using Generated;
using IAutoScroll = LcdMod.Client.Terminal.Controls.Generic.IAutoScroll;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using IMyTextSurface = Sandbox.ModAPI.IMyTextSurface;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class FarmSurfaceScript : InteractiveSurfaceScript,
        IAutoScroll,
        IUsesTerminalControl<ComboboxLinkType>
    {
        protected override ConfigKind ConfigKind => ConfigKind.Power;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public const string ID = MOD_PREFIX + "Farm";
        public const string TITLE = MOD_PREFIX + "Farm";

        protected override string DefaultTitle => TITLE;

        public override IApp App => _app;
        IApp _app;
        public override List<Control> InteractiveList => _app.Children as List<Control>;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

        public FarmSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
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
            if (AppConfig == null)
                return;

            base.SafeRun();

            if (_app == null)
                _app = new FarmApp(AppConfig, this);

            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
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
