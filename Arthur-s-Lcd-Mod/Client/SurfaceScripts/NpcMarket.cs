using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.IMyTextSurface;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(NpcMarketApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed partial class NpcMarketSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderNpcMarketMaxDistance>,
        IUsesTerminalControl<SliderNpcMarketPageSwitchDelay>
    {
        public const string ID = MOD_PREFIX + "MarketApp";
        public const string TITLE = NpcMarketApp.TITLE;


        NpcMarketApp _app;

        public override IApp App => _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override List<Control> InteractiveList => _app?.VisualChildren as List<Control>;
        protected override string DefaultTitle => TITLE;
        protected override bool RendersInteractiveEntriesInGetSprites => true;
        protected override bool RenderContinuouslyWhileLookedAt => false;

        public NpcMarketSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        public override void Dispose()
        {
            if (_app != null)
                _app.Close();

            base.Dispose();
        }

        public override void SafeRun()
        {
            base.SafeRun();

            if (_app == null)
                _app = new NpcMarketApp(this);

            _app.Update();
            if (_app.IsDirty || InteractiveVisualsDirty)
                RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            if (_app == null)
                return sprites;

            AddBackground(sprites);
            DrawTitle(sprites);
            sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
