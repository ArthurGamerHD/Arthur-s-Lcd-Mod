using Adk.Gui.Controls;
using VRageMath;

namespace LcdMod.Client.Apps.MediaPlayer
{
    internal sealed partial class MediaPlayerView : UserControl
    {
        public MediaPlayerView()
        {
            InitializeComponent();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            (DataContext as MediaPlayerViewModel)?.SetViewport(availableSize);
            return base.MeasureOverride(availableSize);
        }
    }
}
