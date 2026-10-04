using Adk.Gui.Controls;
using Adk.Gui.Core;
using LcdMod.Client.Market;
using VRageMath;

namespace LcdMod.Client.Apps.NpcMarket
{
    internal sealed partial class MarketItemDialogView : Window
    {
        readonly MarketItemDialogViewModel _model;
        readonly ComboBox<NpcMarketMode> _mode;

        public MarketItemDialogView(MarketItemDialogViewModel model)
        {
            _model = model;
            DataContext = model;
            InitializeComponent();
            _mode = new ComboBox<NpcMarketMode>(new[] { NpcMarketMode.Buy, NpcMarketMode.Sell }, mode => model.ModeLabel(mode),
                model.SelectMode)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _mode.SetSelectedValue(model.Mode);
            ModeHost.Child = _mode;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _mode.TextScale = _model.FontSize;
            return base.MeasureOverride(availableSize);
        }
    }
}
