using System;
using Acrux.Animation;
using Acrux.Controls;
using Acrux.Controls.Documents;
using Acrux.Core;
using Acrux.Styling;
using LcdMod.Client.Video;
using VRageMath;

namespace LcdMod.Client.Apps.MediaPlayer
{
    internal sealed partial class MediaPlayerView : UserControl
    {
        const int SlideFrames = 12;
        const string SlideChannel = "media-video-slide";

        MediaPlayerViewModel _viewModel;
        bool _chromeShown = true;
        readonly SubtitleSizeReference _captionSizes = new SubtitleSizeReference();
        string _captionMarkup;
        float _captionFontSize;
        object _captionVideo;

        public MediaPlayerView()
        {
            InitializeComponent();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var viewModel = DataContext as MediaPlayerViewModel;
            if (viewModel != null)
            {
                if (!ReferenceEquals(viewModel, _viewModel))
                {
                    if (_viewModel != null)
                        _viewModel.PropertyChanged -= OnViewModelChanged;
                    _viewModel = viewModel;
                    _viewModel.PropertyChanged += OnViewModelChanged;
                }
                viewModel.SetViewport(availableSize);
                if (DockProgress.SeekRequested == null)
                    DockProgress.SeekRequested = viewModel.App.MediaSeek;
                if (DockVolume.VolumeChanged == null)
                {
                    DockVolume.VolumeChanged = viewModel.App.MediaSetVolume;
                    DockVolume.FlyoutToggled = viewModel.SetVolumeFlyoutOpen;
                }
            }
            return base.MeasureOverride(availableSize);
        }

        internal override void PointerHovered(Vector2 point)
        {
            (DataContext as MediaPlayerViewModel)?.NotifyPointer(point);
        }

        void OnViewModelChanged(Adk.Mvvm.ObservableObject sender, string property)
        {
            if (property.Length == 0 || property == nameof(MediaPlayerViewModel.Subtitle))
                UpdateCaption();
            if (property.Length != 0 && property != nameof(MediaPlayerViewModel.DockVisible))
                return;
            bool shown = _viewModel.DockVisible;
            if (shown == _chromeShown)
                return;
            _chromeShown = shown;
            Slide(VideoDockHost, Dock, _viewModel.DockSlideDistance, shown);
            Slide(VideoTitle, VideoTitle, -(Math.Max(VideoTitle.Bounds.Bottom - Bounds.Y, _viewModel.VideoTitleIconSize) + 4f), shown);
        }

        void UpdateCaption()
        {
            string markup = _viewModel.Subtitle;
            float fontSize = _viewModel.SubtitleFontSize;
            object video = _viewModel.VideoKey;
            if (!ReferenceEquals(video, _captionVideo))
            {
                _captionVideo = video;
                _captionSizes.Reset();
            }
            if (markup == _captionMarkup && fontSize == _captionFontSize)
                return;
            _captionMarkup = markup;
            _captionFontSize = fontSize;

            var inlines = new InlineCollection();
            foreach (SubtitleSpan span in SubtitleMarkup.Parse(markup))
            {
                if (span.IsLineBreak)
                {
                    inlines.Add(new LineBreak());
                    continue;
                }
                var run = new Run(span.Text)
                {
                    FontFamily = _viewModel.App.MediaResolveFont(span.Face),
                    Foreground = span.Color,
                    FontWeight = span.Bold ? FontWeight.Bold : (FontWeight?)null,
                    FontStyle = span.Italic ? FontStyle.Italic : (FontStyle?)null
                };
                if (span.SizeUnit == SubtitleSizeUnit.Relative)
                    run.FontSize = fontSize * span.Size;
                else if (span.SizeUnit == SubtitleSizeUnit.Pixels)
                    run.FontSize = fontSize * _captionSizes.Factor(span.Size);
                if (span.Underline || span.Strikethrough)
                {
                    run.TextDecorations = new TextDecorationCollection();
                    if (span.Underline)
                        run.TextDecorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
                    if (span.Strikethrough)
                        run.TextDecorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
                }
                inlines.Add(run);
            }
            Caption.Inlines = inlines;
        }

        // Slides host by distance while part hides, or back as it shows.
        void Slide(VisualElement host, VisualElement part, float distance, bool shown)
        {
            var hidden = new TranslateTransform(0f, distance);
            var rest = new TranslateTransform();
            if (AnimationController == null)
            {
                part.IsVisible = shown;
                host.RenderTransform = null;
                return;
            }

            if (shown)
            {
                if (!part.IsVisible)
                    host.RenderTransform = hidden;
                part.IsVisible = true;
            }

            host.RunAnimation(SlideChannel, AnimationConflict.Replace,
                new Keyframe<TranslateTransform>(
                    () => host.RenderTransform as TranslateTransform ?? rest,
                    value => host.RenderTransform = value,
                    shown ? rest : hidden,
                    SlideFrames,
                    shown ? EasingMode.EaseOutCubic : EasingMode.EaseInCubic,
                    AnimationInterpolators.TranslateTransform),
                new ActionKeyframe(() =>
                {
                    part.IsVisible = shown;
                    host.RenderTransform = null;
                }));
        }
    }
}
