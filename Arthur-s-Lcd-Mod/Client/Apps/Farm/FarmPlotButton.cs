using Adk.Gui.Controls;
using Adk.Gui.Controls.Primitives;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Layout;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Markdown;
using LcdMod.Client.Markdown;

namespace LcdMod.Client.Apps.Farm
{
    internal interface IFarmPlotDetailsSource
    {
        void NotifyDetailsOpened();
    }

    /// <summary>A farm plot that opens its Markdown details in a light-dismiss flyout when clicked.</summary>
    internal sealed class FarmPlotButton : Button
    {
        public static readonly StyledProperty<string> TitleProperty =
            StyledProperty.Register<FarmPlotButton, string>(nameof(Title), null, UiInvalidation.Measure);
        public static readonly StyledProperty<string> TitleIconProperty =
            StyledProperty.Register<FarmPlotButton, string>(nameof(TitleIcon), null, UiInvalidation.Measure);
        public static readonly StyledProperty<MarkdownDocument> DetailsProperty =
            StyledProperty.Register<FarmPlotButton, MarkdownDocument>(nameof(Details), null, UiInvalidation.Measure);

        readonly TextBlock _title;
        readonly Border _icon;
        readonly MarkdownPresenter _details;

        public FarmPlotButton()
        {
            _title = new TextBlock { StyleClass = "farm-details-title", TextWrapping = TextWrapping.Wrap };
            _icon = new Border { StyleClass = "farm-details-icon" };
            _details = new MarkdownPresenter();
            _details.SetDynamicResource(MarkdownPresenter.FontSizeProperty, "fontScale");
            var header = new Grid();
            header.SetColumnDefinitions(GridLength.Auto, new GridLength(1, GridUnitType.Star));
            header.Set(_icon, 0, 0);
            header.Set(_title, 1, 0);
            var body = new StackPanel { StyleClass = "farm-details-body" };
            body.AddChild(header);
            body.AddChild(new ScrollViewer
            {
                StyleClass = "farm-details-scroll",
                Child = _details,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            });
            Flyout = new Popup
            {
                Child = new Border { StyleClass = "farm-details", Child = body },
                Placement = PlacementMode.Bottom,
                IsLightDismissEnabled = true,
                Opened = () => (DataContext as IFarmPlotDetailsSource)?.NotifyDetailsOpened()
            };
        }

        public string Title
        {
            get { return GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public string TitleIcon
        {
            get { return GetValue(TitleIconProperty); }
            set { SetValue(TitleIconProperty, value); }
        }

        public MarkdownDocument Details
        {
            get { return GetValue(DetailsProperty); }
            set { SetValue(DetailsProperty, value); }
        }

        protected override void OnPropertyChanged(IStyledProperty property)
        {
            base.OnPropertyChanged(property);
            if (property == TitleProperty)
                _title.Text = Title ?? string.Empty;
            else if (property == TitleIconProperty)
                _icon.Background = string.IsNullOrEmpty(TitleIcon) ? null : new ImageBrush(TitleIcon) { Stretch = Stretch.Uniform };
            else if (property == DetailsProperty)
                _details.Document = Details;
        }
    }
}
