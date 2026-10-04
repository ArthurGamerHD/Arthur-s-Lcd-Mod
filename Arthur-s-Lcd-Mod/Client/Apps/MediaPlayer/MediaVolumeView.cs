using Adk.Gui.Controls;
using VRageMath;

namespace LcdMod.Client.Apps.MediaPlayer
{
    internal sealed partial class MediaVolumeView : UserControl
    {
        public MediaVolumeView()
        {
            InitializeComponent();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var viewModel = DataContext as MediaPlayerViewModel;
            if (viewModel != null && Slider.ValueChanged == null)
                Slider.ValueChanged = viewModel.App.MediaSetVolume;
            return base.MeasureOverride(availableSize);
        }
    }
}
