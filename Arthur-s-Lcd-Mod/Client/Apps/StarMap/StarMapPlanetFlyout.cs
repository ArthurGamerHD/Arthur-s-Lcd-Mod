using Adk.Gui.Controls.Primitives;
using Adk.Gui.Core.PropertySystem;
using VRageMath;

namespace LcdMod.Client.Apps.StarMap
{
    /// <summary>
    /// A user-controlled info button. Selection/texture refreshes update an open
    /// panel, but never open it; its anchor stays at the top-right map toolbar.
    /// </summary>
    internal sealed class StarMapPlanetFlyout : ToggleButton
    {
        public static readonly StyledProperty<StarMapPlanetInfo> InfoProperty =
            StyledProperty.Register<StarMapPlanetFlyout, StarMapPlanetInfo>(nameof(Info), null);
        public static readonly StyledProperty<int> VersionProperty =
            StyledProperty.Register<StarMapPlanetFlyout, int>(nameof(Version), 0);

        const float GAP = 8f;

        readonly StarMapPlanetInfoView _view = new StarMapPlanetInfoView();
        readonly Popup _popup;
        bool _closingQuietly;

        public StarMapPlanetFlyout()
        {
            _popup = new Popup
            {
                Child = _view,
                Placement = PlacementMode.BottomEdgeAlignedRight,
                VerticalOffset = GAP,
                IsLightDismissEnabled = true,
                OverlayDismissEventPassThrough = false,
                Opened = () => Info?.SetOpen(true),
                Closed = OnClosed
            };
            Flyout = _popup;
        }

        public StarMapPlanetInfo Info
        {
            get { return GetValue(InfoProperty); }
            set { SetValue(InfoProperty, value); }
        }

        public int Version
        {
            get { return GetValue(VersionProperty); }
            set { SetValue(VersionProperty, value); }
        }

        internal Popup Popup => _popup;

        protected override void InvokeClick()
        {
            if (!IsEffectivelyEnabled)
                return;
            // Button's default click always opens its Flyout. A checked info
            // button must instead close it, including keyboard activation.
            if (IsChecked == true)
                IsChecked = false;
            else
                base.InvokeClick();
        }

        protected override void OnPropertyChanged(IStyledProperty property)
        {
            base.OnPropertyChanged(property);
            if (property == InfoProperty)
                _view.DataContext = Info;
            if (property == InfoProperty || property == VersionProperty || property == IsCheckedProperty)
                Refresh();
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            base.ArrangeChildren(bounds);
            Refresh();
        }

        void Refresh()
        {
            StarMapPlanetInfo info = Info;
            Enabled = info != null && info.IsVisible;
            if (IsChecked != true || info == null || !info.IsVisible || Bounds.Width <= 0f || Bounds.Height <= 0f)
            {
                CloseQuietly();
                if (info == null || !info.IsVisible)
                    IsChecked = false;
                return;
            }
            _popup.PlacementTarget = this;
            _popup.Open();
        }

        void CloseQuietly()
        {
            _closingQuietly = true;
            try
            {
                _popup.Close();
                Info?.SetOpen(false);
            }
            finally
            {
                _closingQuietly = false;
            }
        }

        void OnClosed()
        {
            if (_closingQuietly)
                return;
            IsChecked = false;
            Info?.SetOpen(false);
            Info?.Dismiss();
        }
    }
}
