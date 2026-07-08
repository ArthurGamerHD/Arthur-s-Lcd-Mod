#if EXPERIMENTAL
using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Audio;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Extensions;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
// todo: this app is just an "stub" to test in game, a real UI for it should be created later 
namespace LcdMod.Client.Apps
{
    [LcdApp(28)]
    [ConfigComponent(APP, typeof(MediaPlayerConfigComponent), PropertyName = "MediaPlayerComponent")]
    public sealed partial class MediaPlayerApp : App, IApp
    {
        const float MIN_BUTTON_HEIGHT = 34f;
        const float MAX_BUTTON_HEIGHT = 56f;
        const float SIDE_PADDING = 12f;
        const float LINE_GAP = 5f;
        const int DEFAULT_VISUALIZER_BARS = 32;
        const string PICK_ICON = "Folder";


        static readonly object LibraryLock = new object();
        static MediaItem[] _cachedLibrary;

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<Control> _children = new List<Control>();
        readonly Button _pickButton;
        readonly Button _previousButton;
        readonly Button _rewindButton;
        readonly ToggleButton _playButton;
        readonly Button _skipButton;
        readonly Button _nextButton;
        readonly Button _stopButton;
        readonly AudioProgressModel _audioProgressModel;
        readonly AudioProgress _audioProgress;
        readonly AudioVisualizerModel _audioVisualizerModel;
        readonly AudioVisualizer _audioVisualizer;
        readonly VerticalSliderModel _volumeSliderModel;
        readonly VerticalSlider _volumeSlider;
        readonly float[] _visualizerLevels = new float[DEFAULT_VISUALIZER_BARS];
        readonly float[] _visualizerTargetLevels = new float[DEFAULT_VISUALIZER_BARS];
        bool _visualizerFrameScheduled;

        MediaItem[] _library = EmptyLibrary;
        int _selectedIndex;
        GridMediaPlayer _player;
        MediaAudioFileReference _pickedAudio;
        readonly InteractiveSurfaceScript _interactiveHost;

        static readonly MediaItem[] EmptyLibrary = new MediaItem[0];

        public string Title
        {
            get
            {
                var title = GetCurrentSongName();
                return string.IsNullOrEmpty(title) ? MediaPlayerSurfaceTitleFallback : title;
            }
        }

        const string MediaPlayerSurfaceTitleFallback = "Media Player";

        sealed class MediaItem
        {
            public string Subtype;
            public string DisplayName;
            public string WavePath;
            public GameAudioContainerKind ContainerKind;
        }

        enum MediaButtonShape
        {
            Rounded,
            Circle,
            LeftRounded,
            RightRounded,
            Transparent
        }

        enum MediaButtonContent
        {
            Text,
            Folder,
            StopSquare,
            PreviousTrack,
            NextTrack,
            PlayToggle
        }

        sealed class MediaButtonModel : ButtonModel
        {
            public MediaButtonContent Content;
            public string DisplayText;
        }

        public MediaPlayerApp(IAppHost host) : base(host)
        {
            _interactiveHost = host as InteractiveSurfaceScript;
            _pickButton = CreateButton("Pick", PickAudio);
            _previousButton = CreateButton("Prev", Previous);
            _rewindButton = CreateButton("-10s", RewindTenSeconds);
            _playButton = AddLogicalChild(new ToggleButton(default(RectangleF), new MediaButtonModel
            {
                Text = "Play",
                DisplayText = "Play",
                Content = MediaButtonContent.Text,
                Clicked = TogglePlay,
                Enabled = true
            })
            {
                GetState = IsPlayToggleActive
            });
            _skipButton = CreateButton("+10s", SkipTenSeconds);
            _nextButton = CreateButton("Next", Next);
            _stopButton = CreateButton("Stop", Stop);
            _audioProgressModel = new AudioProgressModel
            {
                SeekRequested = SeekToPosition
            };
            _audioProgress = AddLogicalChild(new AudioProgress(default(RectangleF), _audioProgressModel));
            _audioVisualizerModel = new AudioVisualizerModel
            {
                BarCount = DEFAULT_VISUALIZER_BARS,
                BarLevels = _visualizerLevels
            };
            _audioVisualizer = AddLogicalChild(new AudioVisualizer(default(RectangleF), _audioVisualizerModel));
            _volumeSliderModel = new VerticalSliderModel
            {
                Value = 1f,
                ValueChanged = SetPlaybackVolume
            };
            _volumeSlider = AddLogicalChild(new VerticalSlider(default(RectangleF), _volumeSliderModel));
        }

        public override IReadOnlyList<Control> VisualChildren => _children;

        public override void Update()
        {
            EnsureLibrary();
            _player = Host.GridLogic == null ? null : Host.GridLogic.MediaPlayer;
            if (_player != null && _volumeSliderModel != null)
                _player.Volume = _volumeSliderModel.Value;
            NormalizeSelectionFromConfig();
        }

        public override List<MySprite> GetSprites()
        {
            _children.Clear();
            _sprites.Clear();

            var area = GetContentArea();
            var scale = GeneralComponent.GetScale();
            var foreground = Host.ForegroundColor;

            var y = area.Y + Math.Max(0f, 6f * scale);
            var textX = area.X + SIDE_PADDING * scale;
            var textWidth = Math.Max(1f, area.Width - 2f * SIDE_PADDING * scale);
            var detailScale = Math.Max(.45f, .65f * scale);

            var songPath = GetCurrentSongPath();
            if (string.IsNullOrEmpty(songPath))
                songPath = "No supported Space Engineers WAV/XWM sounds were found.";

            DrawTrimmedText(_sprites, songPath, textX, y, textWidth, detailScale, foreground);
            y += Math.Max(12f, MeasureLineHeight(detailScale)) + LINE_GAP * scale;

            DrawMediaControls(_sprites, area, y);

            ClearDirtyAfterRender();
            return _sprites;
        }

        Button CreateButton(string text, Action<ButtonModel, object> clicked)
        {
            return AddLogicalChild(new Button(default(RectangleF), new MediaButtonModel
            {
                Text = text,
                DisplayText = text,
                Content = MediaButtonContent.Text,
                Clicked = clicked,
                Enabled = true
            }));
        }

        RectangleF GetContentArea()
        {
            var area = Host.ViewBox;
            if (area.Width <= 0f || area.Height <= 0f)
                return area;

            var scale = GeneralComponent.GetScale();
            var padding = Math.Max(4f, SIDE_PADDING * scale);
            var titleHeight = Host.TitleVisible
                ? 40f * scale * Host.Surface.FontSize
                : 0f;
            return new RectangleF(
                area.X + padding,
                area.Y + titleHeight + padding,
                Math.Max(0f, area.Width - padding * 2f),
                Math.Max(0f, area.Height - titleHeight - padding * 2f));
        }

        string GetCurrentSongName()
        {
            if (_pickedAudio != null)
                return GetPickedAudioSongName(_pickedAudio);

            if (_library.Length > 0)
            {
                var selected = _library[_selectedIndex];
                if (!string.IsNullOrEmpty(selected.DisplayName))
                    return selected.DisplayName;

                return GetFileNameWithoutExtension(selected.WavePath);
            }

            return string.Empty;
        }

        string GetCurrentSongPath()
        {
            if (_pickedAudio != null)
            {
                if (_pickedAudio.IsLocal && _pickedAudio.LocalAsset != null)
                {
                    if (!string.IsNullOrEmpty(_pickedAudio.LocalAsset.SourceArchivePath))
                        return "Local/" + _pickedAudio.LocalAsset.SourceArchivePath.Replace('/', '\\');

                    if (!string.IsNullOrEmpty(_pickedAudio.LocalAsset.SourcePath))
                        return "Local/" + _pickedAudio.LocalAsset.SourcePath.Replace('/', '\\');
                }

                if (!string.IsNullOrEmpty(_pickedAudio.DefinitionPath))
                    return "Content/" + _pickedAudio.DefinitionPath.Replace('/', '\\');

                return GetPickedAudioTitle(_pickedAudio);
            }

            if (_library.Length > 0)
            {
                var selected = _library[_selectedIndex];
                return string.IsNullOrEmpty(selected.WavePath)
                    ? selected.Subtype
                    : "Content/" + selected.WavePath.Replace('/', '\\');
            }

            return string.Empty;
        }

        float DrawPlayerStatus(List<MySprite> sprites, float x, float y, float width, float scale)
        {
            var status = _player == null ? "Grid media service unavailable" : _player.Status;
            if (_player != null && _player.IsEmitterPlaying)
                status = status + " - 3D emitter active at this screen";

            var lineHeight = Math.Max(14f, MeasureLineHeight(scale));
            DrawTrimmedText(sprites, "Status: " + status, x, y, width, scale, Host.ForegroundColor);
            y += lineHeight;

            if (_player != null && !string.IsNullOrEmpty(_player.LastError))
            {
                DrawTrimmedText(sprites, "Error: " + _player.LastError, x, y, width, scale, Host.ForegroundColor);
                y += lineHeight;
            }

            return y + LINE_GAP * GeneralComponent.GetScale();
        }

        void DrawMediaControls(List<MySprite> sprites, RectangleF area, float contentBottom)
        {
            var scale = GeneralComponent.GetScale();
            var gap = Math.Max(4f, 8f * scale);
            var buttonHeight = MathHelper.Clamp(area.Height * .18f, MIN_BUTTON_HEIGHT * scale, MAX_BUTTON_HEIGHT * scale);
            var buttonY = area.Bottom - buttonHeight;
            var progressHeight = Math.Max(16f, 20f * scale);
            var progressY = buttonY - gap - progressHeight;
            var progressFits = progressY >= area.Y;
            var bottomControlsTop = progressFits ? progressY : buttonY;
            var visualizerTop = Math.Max(area.Y, contentBottom + gap);
            var visualizerBottom = bottomControlsTop - gap;
            var buttonWidth = Math.Max(1f, (area.Width - gap * 6f) / 7f);
            var libraryEnabled = _library.Length > 0;
            var canStart = _pickedAudio != null || libraryEnabled;
            var canTogglePlay = _player != null && (canStart || _player.IsPlaying || _player.IsPaused);
            var canSeek = _player != null && _player.CanSeek;

            if (visualizerBottom - visualizerTop >= Math.Max(18f, 30f * scale))
                DrawVisualizerAreaAndVolume(sprites, new RectangleF(area.X, visualizerTop, area.Width, visualizerBottom - visualizerTop));

            if (progressFits)
                DrawAudioProgress(sprites, new RectangleF(area.X, progressY, area.Width, progressHeight), canSeek);

            var pickRect = new RectangleF(area.X, buttonY, buttonWidth, buttonHeight);
            var rewindRect = new RectangleF(area.X + (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var previousRect = new RectangleF(area.X + 2f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var playRect = new RectangleF(area.X + 3f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var nextRect = new RectangleF(area.X + 4f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var skipRect = new RectangleF(area.X + 5f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var stopRect = new RectangleF(area.X + 6f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var trackDecoratorHeight = Math.Max(1f, previousRect.Height * .82f);
            var trackDecoratorY = previousRect.Center.Y - trackDecoratorHeight * .5f;
            var previousDecoratorRect = new RectangleF(
                previousRect.X,
                trackDecoratorY,
                Math.Max(1f, playRect.Center.X - previousRect.X),
                trackDecoratorHeight);
            var nextDecoratorRect = new RectangleF(
                playRect.Center.X,
                trackDecoratorY,
                Math.Max(1f, nextRect.Right - playRect.Center.X),
                trackDecoratorHeight);

            DrawMediaButton(_pickButton, pickRect, string.Empty, _interactiveHost != null, sprites, MediaButtonShape.Transparent, MediaButtonContent.Folder);
            DrawMediaButton(_rewindButton, rewindRect, "-10s", canSeek, sprites, MediaButtonShape.Transparent, MediaButtonContent.Text);
            DrawMediaButton(_previousButton, previousRect, "", libraryEnabled, sprites, MediaButtonShape.LeftRounded, MediaButtonContent.PreviousTrack, previousDecoratorRect);
            DrawMediaButton(_nextButton, nextRect, "", libraryEnabled, sprites, MediaButtonShape.RightRounded, MediaButtonContent.NextTrack, nextDecoratorRect);
            DrawMediaButton(_skipButton, skipRect, "+10s", canSeek, sprites, MediaButtonShape.Transparent, MediaButtonContent.Text);
            DrawMediaButton(_stopButton, stopRect, string.Empty, CanResetPlayer(), sprites, MediaButtonShape.Transparent, MediaButtonContent.StopSquare);
            DrawMediaButton(_playButton, playRect, GetPlayButtonText(), canTogglePlay, sprites, MediaButtonShape.Circle, MediaButtonContent.PlayToggle);
        }

        bool CanResetPlayer()
        {
            if (_player == null)
                return false;

            return _player.IsActive || _player.HasLoadedAudio;
        }


        void DrawVisualizerAreaAndVolume(List<MySprite> sprites, RectangleF rect)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var scale = GeneralComponent.GetScale();
            var innerPad = Math.Max(1f, 3f * scale);
            var leftWidth = rect.Width * .05f;
            var centerWidth = rect.Width * .90f;
            var rightWidth = Math.Max(1f, rect.Width - leftWidth - centerWidth);
            var centerRect = new RectangleF(
                rect.X + leftWidth + innerPad,
                rect.Y + innerPad,
                Math.Max(1f, centerWidth - innerPad * 2f),
                Math.Max(1f, rect.Height - innerPad * 2f));
            var sliderRect = new RectangleF(
                rect.X + leftWidth + centerWidth,
                rect.Y,
                rightWidth,
                rect.Height);

            if (MediaPlayerComponent.VisualizerEnabled)
                DrawVisualizer(sprites, centerRect);
            else
                DrawAudioFileIcon(sprites, centerRect);

            _volumeSliderModel.Value = MathHelper.Clamp(_volumeSliderModel.Value, 0f, 1f);
            _volumeSliderModel.TrackColor = Host.BackgroundColor.DeriveAccentColor();
            _volumeSliderModel.FillColor = GetHeaderColor();
            _volumeSliderModel.ThumbColor = GetHeaderColor();

            _volumeSlider.SetRect(sliderRect);
            _volumeSlider.SetVisible(true);
            _volumeSlider.SetEnabled(true);
            _volumeSlider.SetCursor(CursorType.Hand);
            _children.Add(_volumeSlider);
            _volumeSlider.Render(sprites);
        }

        void DrawVisualizer(List<MySprite> sprites, RectangleF rect)
        {
            RefreshVisualizerLevels();

            _audioVisualizerModel.BarCount = DEFAULT_VISUALIZER_BARS;
            _audioVisualizerModel.BarLevels = _visualizerLevels;
            _audioVisualizerModel.CenterLineColor = Color.Black;
            _audioVisualizerModel.BarSaturation = 1f;
            _audioVisualizerModel.BarValue = 1f;
            _audioVisualizerModel.BarAlpha = .9f;
            _audioVisualizerModel.BackgroundColor = null;

            _audioVisualizer.SetRect(rect);
            _audioVisualizer.SetVisible(true);
            _audioVisualizer.SetEnabled(true);
            _audioVisualizer.SetCursor(CursorType.Default);
            _children.Add(_audioVisualizer);
            _audioVisualizer.Render(sprites);

            ScheduleVisualizerFrameIfNeeded();
        }

        void DrawAudioFileIcon(List<MySprite> sprites, RectangleF rect)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var icon = GetCurrentAudioFileIcon();
            var size = Math.Max(1f, Math.Min(rect.Width, rect.Height) * .72f);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = icon,
                Position = rect.Center,
                Size = new Vector2(size, size),
                Color = Host.ForegroundColor,
                Alignment = TextAlignment.CENTER
            });
        }

        string GetCurrentAudioFileIcon()
        {
            string path = null;
            if (_player != null && _player.HasLoadedAudio)
                path = _player.CurrentWavePath;
            else if (_pickedAudio != null)
                path = _pickedAudio.IsLocal ? _pickedAudio.GameContentPath : _pickedAudio.DefinitionPath;
            else if (_library.Length > 0)
                path = _library[_selectedIndex].WavePath;

            return GameAudioPcmLoader.GetContainerKind(path) == GameAudioContainerKind.Xwma
                ? "FileXwm"
                : "FileWav";
        }

        void ScheduleVisualizerFrameIfNeeded()
        {
            if (_visualizerFrameScheduled || !ShouldScheduleVisualizerFrames())
                return;

            _visualizerFrameScheduled = true;
            global::LcdMod.Client.LcdModClientComponent.RunNextFrame.Add(delegate
            {
                _visualizerFrameScheduled = false;
                if (!ShouldScheduleVisualizerFrames())
                    return;

                if (_interactiveHost != null)
                    _interactiveHost.RequestRedraw();
            });
        }

        bool ShouldScheduleVisualizerFrames()
        {
            return MediaPlayerComponent.VisualizerEnabled &&
                   _player != null &&
                   (_player.IsPlaying || HasVisibleVisualizerLevels());
        }

        bool HasVisibleVisualizerLevels()
        {
            for (int i = 0; i < _visualizerLevels.Length; i++)
            {
                if (_visualizerLevels[i] > .004f)
                    return true;
            }

            return false;
        }

        void RefreshVisualizerLevels()
        {
            bool hasLevels = _player != null && _player.IsPlaying && _player.FillSpectrumLevels(_visualizerTargetLevels);
            for (int i = 0; i < _visualizerLevels.Length; i++)
            {
                float target = hasLevels && i < _visualizerTargetLevels.Length
                    ? MathHelper.Clamp(_visualizerTargetLevels[i], 0f, 1f)
                    : 0f;
                float previous = _visualizerLevels[i];
                float next = target > previous ? target : previous * .86f;
                _visualizerLevels[i] = next < .004f ? 0f : next;
            }
        }

        void DrawAudioProgress(List<MySprite> sprites, RectangleF rect, bool canSeek)
        {
            if (_audioProgress == null || _audioProgressModel == null)
                return;

            double duration = _player == null ? 0.0 : _player.CurrentDurationSeconds;
            double position = _player == null ? 0.0 : _player.CurrentPositionSeconds;

            _audioProgressModel.PositionSeconds = position;
            _audioProgressModel.DurationSeconds = duration;
            _audioProgressModel.SeekEnabled = canSeek;
            _audioProgressModel.TextColor = Host.ForegroundColor;
            _audioProgressModel.BackgroundColor = Host.BackgroundColor.DeriveAccentColor();
            _audioProgressModel.FillColor = GetHeaderColor();
            _audioProgressModel.ThumbColor = GetHeaderColor();

            _audioProgress.SetRect(rect);
            _audioProgress.SetVisible(true);
            _audioProgress.SetEnabled(true);
            _audioProgress.SetCursor(canSeek ? CursorType.Hand : CursorType.Default);
            _children.Add(_audioProgress);
            _audioProgress.Render(sprites);
        }

        void DrawMediaButton(
            Button button,
            RectangleF rect,
            string text,
            bool enabled,
            List<MySprite> sprites,
            MediaButtonShape shape,
            MediaButtonContent content,
            RectangleF? decoratorRect = null)
        {
            if (button == null)
                return;

            var model = button.DataContext as MediaButtonModel;
            if (model != null)
            {
                model.Text = text ?? string.Empty;
                model.DisplayText = text ?? string.Empty;
                model.Content = content;
                model.Enabled = enabled;
            }
            else
            {
                var buttonModel = button.DataContext as ButtonModel;
                if (buttonModel != null)
                {
                    buttonModel.Text = text ?? string.Empty;
                    buttonModel.Enabled = enabled;
                }
            }

            var fillColor = enabled ? GetHeaderColor() : Host.BackgroundColor.DeriveAccentColor();
            fillColor.A = byte.MaxValue;
            DrawMediaButtonDecorator(sprites, decoratorRect.HasValue ? decoratorRect.Value : rect, fillColor, shape);

            button.CustomRender = RenderMediaButtonForeground;
            button.BackgroundColor = Color.Transparent;
            button.TextColor = Host.ForegroundColor;
            button.BorderColor = Color.Transparent;
            button.BorderThicknessPixels = 0f;
            button.BorderRadiusPixels = 0f;
            button.SetRect(rect);
            button.SetVisible(true);
            button.SetEnabled(enabled);
            button.SetCursor(enabled ? CursorType.Hand : CursorType.Default);
            button.SetStyleId(null);
            _children.Add(button);
            button.Render(sprites);
        }

        void RenderMediaButtonForeground(ControlTemplate control, List<MySprite> sprites)
        {
            var model = control.DataContext as MediaButtonModel;
            if (model == null)
            {
                RenderSearchStyleTextButton(control, sprites, control.DataContext == null ? string.Empty : control.DataContext.ToString());
                return;
            }

            var foreground = control.TextColor;
            if (!control.Enabled)
                foreground = new Color(foreground.R, foreground.G, foreground.B, byte.MaxValue);

            if (model.Content == MediaButtonContent.Folder)
                DrawCenteredIcon(sprites, control.Bounds, PICK_ICON, foreground, .62f);
            else if (model.Content == MediaButtonContent.StopSquare)
            {
                var errorColor = ColorComponent.ResolveErrorColor();
                errorColor.A = byte.MaxValue;
                DrawCenteredSquare(sprites, control.Bounds, errorColor, control.Enabled ? .42f : .34f);
            }
            else if (model.Content == MediaButtonContent.PreviousTrack)
                DrawTrackIcon(sprites, control.Bounds, foreground, false);
            else if (model.Content == MediaButtonContent.NextTrack)
                DrawTrackIcon(sprites, control.Bounds, foreground, true);
            else if (model.Content == MediaButtonContent.PlayToggle)
                DrawPlayPauseIcon(sprites, control.Bounds, foreground, IsPlayToggleActive());
            else
                RenderSearchStyleTextButton(control, sprites, model.DisplayText);
        }

        void RenderSearchStyleTextButton(ControlTemplate control, List<MySprite> sprites, string text)
        {
            DrawCenteredText(sprites, text, control.Bounds, control.TextColor);
        }

        void DrawMediaButtonDecorator(List<MySprite> sprites, RectangleF rect, Color color, MediaButtonShape shape)
        {
            if (rect.Width <= 0f || rect.Height <= 0f || color.A == 0)
                return;

            if (shape == MediaButtonShape.Transparent)
                return;

            if (shape == MediaButtonShape.Circle)
            {
                float diameter = Math.Max(1f, Math.Min(rect.Width, rect.Height) * 1.1f);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = rect.Center,
                    Size = new Vector2(diameter, diameter),
                    Color = color,
                    Alignment = TextAlignment.CENTER
                });
                return;
            }

            if (shape == MediaButtonShape.LeftRounded)
            {
                DrawSafeCapsule(sprites, rect, color, true, false);
                return;
            }

            if (shape == MediaButtonShape.RightRounded)
            {
                DrawSafeCapsule(sprites, rect, color, false, true);
                return;
            }

            DrawSafeCapsule(sprites, rect, color, true, true);
        }

        static void DrawSafeCapsule(List<MySprite> sprites, RectangleF rect, Color color, bool roundLeft, bool roundRight)
        {
            var radius = Math.Max(0f, Math.Min(rect.Width, rect.Height) * .5f);
            if (radius <= 0f)
            {
                sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", rect.Center, rect.Size, color));
                return;
            }

            var left = rect.X;
            var right = rect.Right;
            var centerY = rect.Center.Y;

            if (roundLeft)
            {
                sprites.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(left + radius, centerY), new Vector2(radius * 2f, rect.Height), color));
                left += radius;
            }

            if (roundRight)
            {
                sprites.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(right - radius, centerY), new Vector2(radius * 2f, rect.Height), color));
                right -= radius;
            }

            if (right > left)
            {
                sprites.Add(new MySprite(
                    SpriteType.TEXTURE,
                    "SquareSimple",
                    new Vector2((left + right) * .5f, centerY),
                    new Vector2(right - left, rect.Height),
                    color));
            }
        }

        void DrawCenteredIcon(List<MySprite> sprites, RectangleF rect, string icon, Color color, float sizeRatio)
        {
            if (string.IsNullOrEmpty(icon) || rect.Width <= 0f || rect.Height <= 0f)
                return;

            var size = Math.Max(1f, Math.Min(rect.Width, rect.Height) * sizeRatio);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = icon,
                Position = rect.Center,
                Size = new Vector2(size, size),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        static void DrawCenteredSquare(List<MySprite> sprites, RectangleF rect, Color color, float sizeRatio)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var size = Math.Max(1f, Math.Min(rect.Width, rect.Height) * sizeRatio);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = rect.Center,
                Size = new Vector2(size, size),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }
        static void DrawTrackIcon(List<MySprite> sprites, RectangleF rect, Color color, bool forward)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var size = Math.Max(1f, Math.Min(rect.Width, rect.Height));
            var triangleSize = size * .27f;
            var barWidth = Math.Max(1f, size * .075f);
            var barHeight = size * .44f;
            // Keep the triangles square, but pack the transport glyph as a single touching mark.
            // The Triangle sprite has a little visual inset, so a small overlap makes |<< / >>| read as connected.
            var triangleOverlap = triangleSize * .16f;
            var barOverlap = Math.Min(barWidth * .45f, triangleSize * .08f);
            var triangleStep = Math.Max(1f, triangleSize - triangleOverlap);
            var groupWidth = barWidth + triangleSize * 2f - triangleOverlap - barOverlap;
            var startX = rect.Center.X - groupWidth * .5f;
            var centerY = rect.Center.Y;
            var triangleSpriteSize = new Vector2(triangleSize, triangleSize);

            if (forward)
            {
                var firstTriangleX = startX + triangleSize * .5f;
                var secondTriangleX = firstTriangleX + triangleStep;
                var barX = secondTriangleX + triangleSize * .5f - barOverlap + barWidth * .5f;
                DrawSideTriangle(sprites, new Vector2(firstTriangleX, centerY), triangleSpriteSize, color, true);
                DrawSideTriangle(sprites, new Vector2(secondTriangleX, centerY), triangleSpriteSize, color, true);
                DrawVerticalRect(sprites, new Vector2(barX, centerY), barWidth, barHeight, color);
            }
            else
            {
                var barX = startX + barWidth * .5f;
                var firstTriangleX = barX + barWidth * .5f - barOverlap + triangleSize * .5f;
                var secondTriangleX = firstTriangleX + triangleStep;
                DrawVerticalRect(sprites, new Vector2(barX, centerY), barWidth, barHeight, color);
                DrawSideTriangle(sprites, new Vector2(firstTriangleX, centerY), triangleSpriteSize, color, false);
                DrawSideTriangle(sprites, new Vector2(secondTriangleX, centerY), triangleSpriteSize, color, false);
            }
        }

        static void DrawPlayPauseIcon(List<MySprite> sprites, RectangleF rect, Color color, bool playing)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var size = Math.Max(1f, Math.Min(rect.Width, rect.Height));
            if (!playing)
            {
                var triangleSize = size * .38f;
                DrawSideTriangle(sprites, rect.Center + new Vector2(size * .035f, 0f), new Vector2(triangleSize, triangleSize), color, true);
                return;
            }

            var barWidth = Math.Max(1f, size * .105f);
            var barHeight = size * .43f;
            var gap = size * .12f;
            DrawVerticalRect(sprites, rect.Center + new Vector2(-(barWidth + gap) * .5f, 0f), barWidth, barHeight, color);
            DrawVerticalRect(sprites, rect.Center + new Vector2((barWidth + gap) * .5f, 0f), barWidth, barHeight, color);
        }

        static void DrawVerticalRect(List<MySprite> sprites, Vector2 center, float width, float height, Color color)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = center,
                Size = new Vector2(Math.Max(1f, width), Math.Max(1f, height)),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        static void DrawSideTriangle(List<MySprite> sprites, Vector2 center, Vector2 size, Color color, bool right)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Triangle",
                Position = center,
                Size = size,
                Color = color,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = right ? MathHelper.PiOver2 : -MathHelper.PiOver2
            });
        }


        void DrawCenteredText(List<MySprite> sprites, string text, RectangleF rect, Color color)
        {
            if (string.IsNullOrEmpty(text) || rect.Width <= 0f || rect.Height <= 0f)
                return;

            var scale = Math.Max(.42f, .58f * GeneralComponent.GetScale() * Host.Surface.FontSize);
            var trimmed = TrimToWidth(text, Math.Max(1f, rect.Width - 6f * GeneralComponent.GetScale()), scale);
            var size = MeasureText(trimmed, scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = trimmed,
                Position = new Vector2(rect.Center.X, rect.Center.Y - size.Y * .5f),
                Color = color,
                FontId = TextFont,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = scale
            });
        }


        void DrawTrimmedText(List<MySprite> sprites, string text, float x, float y, float width, float scale, Color color)
        {
            var data = TrimToWidth(text, width, scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = data,
                Position = new Vector2(x, y),
                Color = color,
                FontId = TextFont,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = scale
            });
        }

        string TrimToWidth(string text, float width, float scale)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (width <= 0f)
                return text;

            if (MeasureText(text, scale).X <= width)
                return text;

            const string ellipsis = "...";
            var max = text.Length;
            while (max > 0)
            {
                var candidate = text.Substring(0, max) + ellipsis;
                if (MeasureText(candidate, scale).X <= width)
                    return candidate;
                max--;
            }

            return ellipsis;
        }

        void PickAudio(ButtonModel model, object sender)
        {
            if (_interactiveHost == null)
                return;

            _interactiveHost.ShowDialog(new FilePickerDialog(
                this,
                "Pick audio",
                FilePickerMode.PickFile,
                MediaAudioFilePickerTreeProvider.BuildRoots(),
                OnAudioPicked,
                _interactiveHost.RequestRedraw,
                null,
                true));
        }

        void Previous(ButtonModel model, object sender)
        {
            if (_library.Length == 0)
                return;

            SetSelectedIndex((_selectedIndex + _library.Length - 1) % _library.Length);
        }

        void Next(ButtonModel model, object sender)
        {
            if (_library.Length == 0)
                return;

            SetSelectedIndex((_selectedIndex + 1) % _library.Length);
        }

        void TogglePlay(ButtonModel model, object sender)
        {
            if (_player != null && _player.IsPlaying)
            {
                _player.Pause();
                MarkDirty();
                return;
            }

            if (_player != null && _player.IsPaused)
            {
                _player.Resume();
                MarkDirty();
                return;
            }

            StartSelectedAudio();
        }

        void StartSelectedAudio()
        {
            StartSelectedAudio(false);
        }

        void StartSelectedAudio(bool startPaused)
        {
            if (_pickedAudio == null && _library.Length == 0)
                return;

            if (Host.GridLogic == null)
                return;

            var block = Host.Block as IMyTerminalBlock;
            if (block == null)
                return;

            Host.GridLogic.MarkRequested();
            _player = Host.GridLogic.MediaPlayer;
            if (_player == null)
                return;

            if (_volumeSliderModel != null)
                _player.Volume = _volumeSliderModel.Value;

            if (_pickedAudio != null)
            {
                if (_pickedAudio.IsLocal)
                    _player.PlayLocalAudio(block, _pickedAudio.LocalAsset, startPaused);
                else
                    _player.PlayGameAudioFile(block, GetPickedAudioTitle(_pickedAudio), _pickedAudio.DefinitionPath, startPaused);
                MarkDirty();
                return;
            }

            _player.PlayGameSound(block, _library[_selectedIndex].Subtype, startPaused);
            MarkDirty();
        }

        void ApplySelectedAudioToActivePlayer()
        {
            var player = _player;
            if (player == null && Host.GridLogic != null)
                player = Host.GridLogic.MediaPlayer;

            if (player == null || !player.IsActive)
                return;

            StartSelectedAudio(player.IsPaused);
        }

        void RewindTenSeconds(ButtonModel model, object sender)
        {
            if (_player != null)
            {
                _player.SeekRelative(-10.0);
                MarkDirty();
            }
        }

        void SkipTenSeconds(ButtonModel model, object sender)
        {
            if (_player != null)
            {
                _player.SeekRelative(10.0);
                MarkDirty();
            }
        }

        bool IsPlayToggleActive()
        {
            return _player != null && _player.IsPlaying;
        }

        string GetPlayButtonText()
        {
            return IsPlayToggleActive() ? "Pause" : "Play";
        }


        void SetPlaybackVolume(float value)
        {
            value = MathHelper.Clamp(value, 0f, 1f);
            if (_volumeSliderModel != null)
                _volumeSliderModel.Value = value;

            var player = _player;
            if (player == null && Host.GridLogic != null)
                player = Host.GridLogic.MediaPlayer;

            if (player != null)
                player.Volume = value;

            MarkDirty();
        }

        void SeekToPosition(double seconds)
        {
            if (_player != null)
            {
                _player.SeekTo(seconds);
                MarkDirty();
            }
        }

        void Stop(ButtonModel model, object sender)
        {
            if (_player != null)
            {
                _player.ResetPlaybackEngine();
                MarkDirty();
            }
        }

        void OnAudioPicked(FilePickerResult result)
        {
            var reference = result == null ? null : result.Tag as MediaAudioFileReference;
            if (reference == null)
                return;

            _pickedAudio = reference;

            if (reference.IsContent && !string.IsNullOrEmpty(reference.FirstSoundSubtype))
                SelectSubtypeWithoutClearingPicked(reference.FirstSoundSubtype);

            SyncConfig();
            ApplySelectedAudioToActivePlayer();
            MarkDirty();
        }

        void SetSelectedIndex(int index)
        {
            if (_library.Length == 0)
                return;

            _pickedAudio = null;

            if (index < 0)
                index = 0;
            if (index >= _library.Length)
                index = _library.Length - 1;

            _selectedIndex = index;
            MediaPlayerComponent.SelectedIndex = _selectedIndex;
            MediaPlayerComponent.SelectedSoundSubtype = _library[_selectedIndex].Subtype;
            SyncConfig();
            ApplySelectedAudioToActivePlayer();
            MarkDirty();
        }

        void SelectSubtypeWithoutClearingPicked(string subtype)
        {
            if (_library.Length == 0 || string.IsNullOrEmpty(subtype))
                return;

            for (int i = 0; i < _library.Length; i++)
            {
                if (string.Equals(_library[i].Subtype, subtype, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedIndex = i;
                    MediaPlayerComponent.SelectedIndex = i;
                    MediaPlayerComponent.SelectedSoundSubtype = _library[i].Subtype;
                    return;
                }
            }
        }

        static string GetPickedAudioSongName(MediaAudioFileReference reference)
        {
            if (reference == null)
                return string.Empty;

            var subtype = reference.FirstSoundSubtype;
            if (!string.IsNullOrEmpty(subtype))
            {
                var definitionName = ResolveAudioDefinitionSongName(subtype, reference.DefinitionPath);
                if (!string.IsNullOrEmpty(definitionName))
                    return definitionName;
            }

            if (reference.IsLocal && reference.LocalAsset != null)
            {
                if (!string.IsNullOrEmpty(reference.LocalAsset.SourcePath))
                    return GetFileNameWithoutExtension(reference.LocalAsset.SourcePath);
                if (!string.IsNullOrEmpty(reference.LocalAsset.SourceArchivePath))
                    return GetFileNameWithoutExtension(reference.LocalAsset.SourceArchivePath);
            }

            if (!string.IsNullOrEmpty(reference.DefinitionPath))
                return GetFileNameWithoutExtension(reference.DefinitionPath);

            return GetFileNameWithoutExtension(GetPickedAudioTitle(reference));
        }

        static string ResolveAudioDefinitionSongName(string subtype, string fallbackPath)
        {
            if (MyDefinitionManager.Static == null || string.IsNullOrEmpty(subtype))
                return GetFileNameWithoutExtension(fallbackPath);

            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition == null || !string.Equals(definition.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    continue;

                return ResolveAudioDefinitionSongName(definition, fallbackPath);
            }

            return GetFileNameWithoutExtension(fallbackPath);
        }

        static string ResolveAudioDefinitionSongName(MyAudioDefinition definition, string fallbackPath)
        {
            if (definition == null)
                return GetFileNameWithoutExtension(fallbackPath);

            var displayName = definition.DisplayNameText;
            if (IsMusicDefinition(definition, fallbackPath) &&
                !string.IsNullOrEmpty(displayName) &&
                !string.Equals(displayName, definition.Id.SubtypeName, StringComparison.OrdinalIgnoreCase))
                return displayName;

            return GetFileNameWithoutExtension(fallbackPath);
        }

        static bool IsMusicDefinition(MyAudioDefinition definition, string path)
        {
            if (definition != null &&
                !string.IsNullOrEmpty(definition.Id.SubtypeName) &&
                definition.Id.SubtypeName.StartsWith("Mus_", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.IsNullOrEmpty(path))
                return false;

            var normalized = path.Replace('\\', '/');
            return normalized.IndexOf("/MUS/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.StartsWith("MUS/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.IndexOf("/Music/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.StartsWith("Music/", StringComparison.OrdinalIgnoreCase);
        }

        static string GetFileNameWithoutExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var normalized = path.Replace('\\', '/');
            var slash = normalized.LastIndexOf('/');
            var name = slash >= 0 && slash + 1 < normalized.Length
                ? normalized.Substring(slash + 1)
                : normalized;
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        static string GetPickedAudioTitle(MediaAudioFileReference reference)
        {
            if (reference == null)
                return string.Empty;

            if (reference.IsLocal && reference.LocalAsset != null)
                return string.IsNullOrEmpty(reference.LocalAsset.SourcePath)
                    ? reference.LocalAsset.Id
                    : reference.LocalAsset.SourcePath;

            return string.IsNullOrEmpty(reference.DefinitionPath)
                ? reference.FirstSoundSubtype
                : reference.DefinitionPath.Replace('/', '\\');
        }

        static string GetPickedAudioDetail(MediaAudioFileReference reference)
        {
            if (reference == null)
                return string.Empty;

            if (reference.IsLocal && reference.LocalAsset != null)
                return "Local · " + reference.LocalAsset.RuntimePath;

            var format = GameAudioPcmLoader.GetContainerDisplayName(GameAudioPcmLoader.GetContainerKind(reference.DefinitionPath));
            var owner = string.IsNullOrEmpty(reference.FirstSoundSubtype)
                ? string.Empty
                : " · " + reference.FirstSoundSubtype + "." + reference.FirstWaveSlot;
            return "Content · " + format + owner;
        }

        void NormalizeSelectionFromConfig()
        {
            if (_library.Length == 0)
            {
                _selectedIndex = 0;
                return;
            }

            var selectedSubtype = MediaPlayerComponent.SelectedSoundSubtype;
            if (!string.IsNullOrEmpty(selectedSubtype))
            {
                for (int i = 0; i < _library.Length; i++)
                {
                    if (string.Equals(_library[i].Subtype, selectedSubtype, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedIndex = i;
                        MediaPlayerComponent.SelectedIndex = i;
                        return;
                    }
                }
            }

            var index = MediaPlayerComponent.SelectedIndex;
            if (index < 0)
                index = 0;
            if (index >= _library.Length)
                index = _library.Length - 1;

            _selectedIndex = index;
            MediaPlayerComponent.SelectedIndex = _selectedIndex;
            MediaPlayerComponent.SelectedSoundSubtype = _library[_selectedIndex].Subtype;
        }

        void EnsureLibrary()
        {
            if (_cachedLibrary == null)
            {
                lock (LibraryLock)
                {
                    if (_cachedLibrary == null)
                        _cachedLibrary = BuildLibrary();
                }
            }

            _library = _cachedLibrary ?? EmptyLibrary;
        }

        static MediaItem[] BuildLibrary()
        {
            if (MyDefinitionManager.Static == null)
                return EmptyLibrary;

            var list = new List<MediaItem>();
            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition == null)
                    continue;

                var wavePath = GridMediaPlayer.FindStartWave(definition);
                if (string.IsNullOrEmpty(wavePath))
                    continue;

                list.Add(new MediaItem
                {
                    Subtype = definition.Id.SubtypeName,
                    DisplayName = ResolveAudioDefinitionSongName(definition, wavePath),
                    WavePath = wavePath,
                    ContainerKind = GameAudioPcmLoader.GetContainerKind(wavePath)
                });
            }

            list.Sort(delegate(MediaItem left, MediaItem right)
            {
                return string.Compare(left.Subtype, right.Subtype, StringComparison.OrdinalIgnoreCase);
            });

            return list.ToArray();
        }

        void SyncConfig()
        {
            var terminalBlock = Host.Block as IMyTerminalBlock;
            if (terminalBlock != null)
                ConfigManager.Sync(terminalBlock, Host.ProviderConfig);
        }
    }
}
#endif
