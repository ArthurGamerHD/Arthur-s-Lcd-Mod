using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.IMyTextSurface;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed class NpcMarketSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderNpcMarketMaxDistance>
    {
        protected override ConfigKind ConfigKind { get { return ConfigKind.NpcMarket; } }
        public const string ID = "LcdMod_MarketApp";
        public const string TITLE = NpcMarketApp.TITLE;

        readonly List<ControlBase> _interactiveListFallback = new List<ControlBase>();
        NpcMarketApp _app;

        public override IApp App { get { return _app; } }
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override List<ControlBase> InteractiveList
        {
            get { return _app != null ? _app.InteractiveList : _interactiveListFallback; }
        }
        protected override string DefaultTitle { get { return TITLE; } }
        protected override bool RendersInteractiveEntriesInGetSprites { get { return true; } }

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
