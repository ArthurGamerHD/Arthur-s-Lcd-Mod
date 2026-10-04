using System.Collections.Generic;
using System;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
// ReSharper disable PartialTypeWithSinglePart
namespace LcdMod.Client.Apps
{
    /// <summary>
    /// Persisted identity and schema owner for RenderProxySurfaceScript. Rendering behavior remains
    /// on the surface during this migration.
    /// </summary>
    [LcdApp(25)]
    [ConfigComponent(Constants.APP, typeof(RenderProxyConfigComponent), PropertyName = "RenderProxyComponent")]
    [ConfigComponent(Constants.RENDER_PROXY_REFERENCE, typeof(BlockReferenceConfigComponent), PropertyName = "RenderProxyReferenceComponent")]
    internal sealed partial class RenderProxyApp : App
    {
        public RenderProxyApp(IAppHost host) : base(host)
        {
            SetContent(new RenderProxy.RenderProxyView
            {
                DataContext = new RenderProxy.RenderProxyViewModel(this)
            });
        }
        public override void Update() { }
        public override List<MySprite> GetSprites() { return new List<MySprite>(); }
    }

    /// <summary>
    /// Persisted identity for the shared games surface. Individual game objects remain outside the
    /// normal app factory/lifecycle until the nested-app work is designed.
    /// </summary>
    [LcdApp(26, Name = "Games")]

    internal sealed partial class GamesConfigApp : App
    {
        public GamesConfigApp(IAppHost host) : base(host)
        {
            ViewModel = new Games.GamesViewModel(this);
            SetContent(new Games.GamesMenuView { DataContext = ViewModel });
            SetContentLayout(!host.TitleVisible);
        }
        internal Games.GamesViewModel ViewModel { get; }
        internal VRageMath.RectangleF GameBounds => RetainedContentBounds;
        internal Func<List<MySprite>> SpriteProvider { get; set; }
        public override void Update() { }
        public override List<MySprite> GetSprites()
        {
            return SpriteProvider != null ? SpriteProvider() : new List<MySprite>();
        }
    }

    /// <summary>Reserved persisted identity for the deliberate BSoD test surface.</summary>
    [LcdApp(27, Name = "BSoDTest")]
    internal sealed partial class BSoDTestApp : App
    {
        public BSoDTestApp(IAppHost host) : base(host)
        {
        }
        public override void Update() { }
        public override List<MySprite> GetSprites() { return new List<MySprite>(); }
    }
}
