using System;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Markdown;
using LcdMod.Client.Markdown;
using VRageMath;

namespace LcdMod.Client.Ftue
{
    internal enum HintPlacement
    {
        Top,
        Bottom
    }

    /// <summary>A tutorial hint: a non-modal window over the app with its Markdown text and a close button.</summary>
    internal sealed class FtueHintWindow : Window
    {
        readonly MarkdownPresenter _header;
        readonly MarkdownPresenter _body;
        MarkdownDocument _document;
        bool _closingQuietly;

        public FtueHintWindow(HintPlacement placement)
        {
            StyleClass = "ftue-hint";
            WindowState = WindowState.Normal;
            CanMaximize = false;
            CanMinimize = false;
            SizeToContent = SizeToContent.Height;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = placement == HintPlacement.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            Styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/Controls/Ftue.xaml");
            ExtendClientAreaToDecorationsHint = true;
            _header = new MarkdownPresenter { VerticalAlignment = VerticalAlignment.Center };
            _header.SetDynamicResource(MarkdownPresenter.FontSizeProperty, "fontScale");
            _body = new MarkdownPresenter { Margin = new Vector4(0f, 4f, 0f, 0f) };
            _body.SetDynamicResource(MarkdownPresenter.FontSizeProperty, "fontScale");
            bool captionsOnLeft = WindowPlatforms.CaptionButtons == CaptionButtonsPlacement.Left;
            var layout = new Grid();
            layout.SetColumnDefinitions(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            layout.SetRowDefinitions(GridLength.Auto, GridLength.Auto);
            layout.Set(new Border { StyleClass = "ftue-hint-caption-space" }, captionsOnLeft ? 0 : 2, 0);
            layout.Set(_header, 1, 0);
            layout.Set(_body, 0, 1);
            _body.GridColumnSpan = 3;
            Content = layout;
            Closed += OnClosed;
        }

        /// <summary>Raised when the user closes the hint, not when it is hidden by the tutorial service.</summary>
        public Action Dismissed { get; set; }

        public MarkdownDocument Document
        {
            get { return _document; }
            set
            {
                if (ReferenceEquals(_document, value))
                    return;
                _document = value;
                var header = new MarkdownDocument();
                var body = new MarkdownDocument();
                if (value != null)
                    for (int i = 0; i < value.Blocks.Count; i++)
                        (i == 0 ? header : body).Blocks.Add(value.Blocks[i]);
                _header.Document = header;
                _body.Document = body;
                _body.Visible = body.Blocks.Count > 0;
            }
        }

        public void ShowOver(Window owner)
        {
            if (IsOpen)
                return;
            Show(owner);
        }

        public void Hide()
        {
            if (!IsOpen)
                return;
            _closingQuietly = true;
            try
            {
                Close();
            }
            finally
            {
                _closingQuietly = false;
            }
        }

        /// <summary>Blends the background toward the theme's success colour as the tip completes.</summary>
        public void SetSuccess(float progress)
        {
            if (progress <= 0f)
            {
                ClearValue(BackgroundProperty);
                return;
            }
            Color normal = ResolveDynamicResource("surfaceContainerHighColor", new Color(40, 44, 52));
            Color success = ResolveDynamicResource("successColor", new Color(41, 171, 79));
            Background = new SolidColorBrush(Color.Lerp(normal, success, MathHelper.Clamp(progress, 0f, 1f)));
        }

        void OnClosed()
        {
            if (!_closingQuietly && Dismissed != null)
                Dismissed();
        }
    }
}
