using Adk.Gui.Styling;
using LcdMod.Client.Apps.Views;

namespace LcdMod.Client.Apps.Games
{
    internal sealed partial class GamesView : SpriteAppView
    {
        public GamesView()
        {
            InitializeComponent();
            Styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/Controls/Games.xaml");
            AddOverlay(new DoomFrameView());
            AddOverlay(new GameInputLayer());
        }
    }
}
