using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.IMyTextSurface;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed class NpcMarketSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderNpcMarketMaxDistance>,
        IUsesTerminalControl<SliderNpcMarketPageSwitchDelay>
    {
        protected override ConfigKind ConfigKind => ConfigKind.NpcMarket;
        public const string ID = MOD_PREFIX + "MarketApp";
        public const string TITLE = NpcMarketApp.TITLE;


        NpcMarketApp _app;

        public override IApp App => _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override List<Control> InteractiveList => _app?.Children as List<Control>;
        protected override string DefaultTitle => TITLE;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

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
            if (Config == null)
                return;

            base.SafeRun();

            if (_app == null)
                _app = new NpcMarketApp((ScreenConfigNpcMarket)Config, this);

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
