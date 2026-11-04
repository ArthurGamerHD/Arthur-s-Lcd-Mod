using Acrux.Controls;
using VRageMath;

namespace LcdMod.Client.Apps.MediaPlayer
{
    internal sealed partial class MediaProgressView : UserControl
    {
        public MediaProgressView()
        {
            InitializeComponent();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var viewModel = DataContext as MediaPlayerViewModel;
            if (viewModel != null && Progress.SeekRequested == null)
                Progress.SeekRequested = viewModel.App.MediaSeek;
            return base.MeasureOverride(availableSize);
        }
    }
}
