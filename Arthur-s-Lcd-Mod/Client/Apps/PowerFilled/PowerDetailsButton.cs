using Acrux.Controls;
using Acrux.Controls.Primitives;
using Acrux.Core.Input;
using Acrux.Core.PropertySystem;

namespace LcdMod.Client.Apps.PowerFilled
{
    /// <summary>A power entry that opens its details, with any actions such as the battery charge mode, in a flyout.</summary>
    internal sealed class PowerDetailsButton : Button
    {
        public static readonly StyledProperty<bool> HasDetailsProperty =
            StyledProperty.Register<PowerDetailsButton, bool>(nameof(HasDetails), false);

        readonly PowerDetailsView _details = new PowerDetailsView();
        readonly Popup _flyout;

        public PowerDetailsButton()
        {
            _flyout = new Popup
            {
                Child = new Border { StyleClass = "power-details", Child = _details },
                Placement = PlacementMode.Bottom,
                IsLightDismissEnabled = true,
                Opened = OnOpened
            };
        }

        public bool HasDetails
        {
            get { return GetValue(HasDetailsProperty); }
            set { SetValue(HasDetailsProperty, value); }
        }

        protected override void OnPropertyChanged(IStyledProperty property)
        {
            base.OnPropertyChanged(property);
            if (property != HasDetailsProperty)
                return;
            Flyout = HasDetails ? _flyout : null;
            Cursor = HasDetails ? PointerCursor.Hand : PointerCursor.Arrow;
        }

        void OnOpened()
        {
            _details.DataContext = DataContext;
            (DataContext as PowerDisplayEntry)?.NotifyDetailsOpened();
        }
    }
}
