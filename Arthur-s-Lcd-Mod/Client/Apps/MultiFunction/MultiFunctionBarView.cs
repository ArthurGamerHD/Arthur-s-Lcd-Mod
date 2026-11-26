using Acrux.Controls;

namespace LcdMod.Client.Apps.MultiFunction
{
    internal sealed partial class MultiFunctionBarView : Window
    {
        // The display keeps this much of its side for the bar: a bar button (40) and the bar's padding (5) on both sides,
        // as MultiFunction.xaml styles them.
        internal const float THICKNESS = 50f;

        public MultiFunctionBarView()
        {
            InitializeComponent();
        }
    }
}
