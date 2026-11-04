using System.Collections.Generic;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Layout;
using Acrux.Lcd;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps.Views
{
    public class SpriteAppView : UserControl
    {
        static readonly IReadOnlyList<MySprite> EmptySprites = new MySprite[0];

        readonly Grid _layers = new Grid();

        public SpriteAppView()
        {
            _layers.AddChild(new LcdSpriteCanvas { SpriteProvider = RenderSprites });
            Content = _layers;
        }

        /// <summary>Adds a layer above the sprites, such as one taking input for what they draw.</summary>
        protected void AddOverlay(VisualElement overlay)
        {
            _layers.AddChild(overlay);
        }

        static IReadOnlyList<MySprite> RenderSprites(object dataContext, RectangleF bounds)
        {
            var viewModel = dataContext as SpriteAppViewModel;
            return viewModel == null ? EmptySprites : viewModel.Render(bounds);
        }
    }
}
