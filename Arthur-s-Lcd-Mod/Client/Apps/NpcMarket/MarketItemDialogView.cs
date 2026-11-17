using Acrux.Controls;
using Acrux.Core;
using LcdMod.Client.Market;
using VRageMath;

namespace LcdMod.Client.Apps.NpcMarket
{
    internal sealed partial class MarketItemDialogView : Window
    {
        readonly MarketItemDialogViewModel _model;
        readonly ComboBox _mode;

        // Any view model with the bound members works (the IDE preview's clone, for one); the mode picker needs the
        // app's model, so it is there only with the typed constructor.
        public MarketItemDialogView()
        {
            InitializeComponent();
        }

        public MarketItemDialogView(MarketItemDialogViewModel model) : this()
        {
            _model = model;
            DataContext = model;
            _mode = new ComboBox(new[] { NpcMarketMode.Buy, NpcMarketMode.Sell }, item => model.ModeLabel((NpcMarketMode)item),
                item => model.SelectMode((NpcMarketMode)item))
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _mode.SelectedItem = model.Mode;
            ModeHost.Child = _mode;
        }

        void OnBuyNowEntered(object sender, Acrux.Core.Input.PointerEventArgs e)
        {
            var model = DataContext as MarketItemDialogViewModel;
            if (model != null)
                model.PrepareBuy();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_mode != null)
                _mode.TextScale = _model.FontSize;
            if (_model != null)
                Destination.TextScale = _model.FontSize;
            return base.MeasureOverride(availableSize);
        }
    }
}
