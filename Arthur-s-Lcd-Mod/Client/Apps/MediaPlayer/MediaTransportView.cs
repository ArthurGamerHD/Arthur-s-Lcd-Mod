using Acrux.Controls;

namespace LcdMod.Client.Apps.MediaPlayer
{
    internal sealed partial class MediaTransportView : UserControl
    {
        public MediaTransportView()
        {
            InitializeComponent();
            TransportBackground.PreviousButton = PreviousButton;
            TransportBackground.PlayButton = PlayButton;
            TransportBackground.NextButton = NextButton;
        }
    }
}
