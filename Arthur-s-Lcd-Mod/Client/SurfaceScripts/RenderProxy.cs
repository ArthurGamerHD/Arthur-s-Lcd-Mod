using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Proxy;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class RenderProxySurfaceScript : InteractiveSurfaceScript, 
        IUsesTerminalControl<SliderProxyX>,
        IUsesTerminalControl<SliderProxyY>
    {
        public const string ID = "LcdMod_RenderProxy";
        public const string TITLE = "LcdMod_RenderProxy";
        
        List<MySprite> _sprites = new List<MySprite>();

        protected override ConfigKind ConfigKind => ConfigKind.RenderProxy;

        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;

        readonly List<InteractiveEntry> _interactiveListFallback = new List<InteractiveEntry>();

        SurfaceScriptBase _parent;

        public override IApp App => _parent?.App;

        IAppInteractive AppInteractive => App as IAppInteractive;

        public override List<InteractiveEntry> InteractiveList =>
            AppInteractive != null ? AppInteractive.InteractiveList : _interactiveListFallback;

        public RenderProxySurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block,
            size)
        {
        }

        public void SetParent(SurfaceScriptBase parent)
        {
            if(parent.Block.BlockDefinition.SubtypeName.Equals(Block.BlockDefinition.SubtypeName))
                _parent = parent;
        }

        public override void SafeRun()
        {
            if (AppConfig == null || App == null)
                return;

            App.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();
            foreach (var sprite in _parent.GetSprites())
            {
                _sprites.Add(new MySprite(
                    sprite.Type, 
                    sprite.Data, 
                    sprite.Position + new Vector2(512 * AppConfig.XAxisOffset, 512 * AppConfig.YAxisOffset), 
                    sprite.Size, 
                    sprite.Color,
                    sprite.FontId, 
                    sprite.Alignment, 
                    sprite.RotationOrScale));
            }
            return _sprites;
        }

        protected override void OnMouseScroll(int delta, ref bool handled)
        {
            base.OnMouseScroll(delta, ref handled);
            AppInteractive?.OnMouseScroll(delta, ref handled);
        }
    }
}
