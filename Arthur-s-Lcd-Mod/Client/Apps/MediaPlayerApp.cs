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
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Extensions;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;
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
        const string SHUFFLE_ICON = "Shuffle";
        const string REPEAT_ICON = "Repeat";
        const string SOUND_LOW_ICON = "SoundLow";
        const string SOUND_HIGH_ICON = "SoundHigh";


        static readonly object LibraryLock = new object();
        static readonly List<FolderModel> EmptyFolderRoots = new List<FolderModel>(0);
        static MediaItem[] _cachedLibrary;
        static SoundCategoryNameLookup _soundCategoryNameLookup;

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<Control> _children = new List<Control>();
        readonly Button _pickButton;
        readonly Button _previousButton;
        readonly ToggleButton _shuffleButton;
        readonly ToggleButton _playButton;
        readonly ToggleButton _repeatButton;
        readonly Button _nextButton;
        readonly Button _stopButton;
        readonly AudioProgressModel _audioProgressModel;
        readonly AudioProgress _audioProgress;
        readonly AudioVisualizerModel _audioVisualizerModel;
        readonly AudioVisualizer _audioVisualizer;
        readonly HorizontalSliderModel _volumeSliderModel;
        readonly HorizontalSlider _volumeSlider;
        readonly float[] _visualizerLevels = new float[DEFAULT_VISUALIZER_BARS];
        readonly float[] _visualizerTargetLevels = new float[DEFAULT_VISUALIZER_BARS];
        bool _visualizerFrameScheduled;
        bool _handledPlaybackCompletion;
        bool _handlingPlaybackCompletion;
        bool _restorePickedAudioAttempted;
        readonly Random _shuffleRandom = new Random();

        MediaItem[] _library = EmptyLibrary;
        int _selectedIndex = -1;
        GridMediaPlayer _player;
        MediaAudioFileReference _pickedAudio;
        readonly InteractiveSurfaceScript _interactiveHost;

        static readonly MediaItem[] EmptyLibrary = new MediaItem[0];

        public string Title
        {
            get
            {
                var title = GetCurrentSongName();
                if (_player != null && _player.IsPaused)
                {
                    var pausedTitle = string.IsNullOrEmpty(title) ? LocHelper.GetLoc(LOC_TITLE) : title;
                    return string.Format(FormatingHelper.Culture, LocHelper.GetLoc(LOC_PAUSED_TITLE_FORMAT), pausedTitle);
                }

                if (_player != null && _player.IsPlaying && !string.IsNullOrEmpty(title))
                    return title;

                return LOC_TITLE;
            }
        }

        const string LOC_TITLE = MOD_PREFIX + "MediaPlayer_Title";
        const string LOC_PAUSED_TITLE_FORMAT = MOD_PREFIX + "MediaPlayer_PausedTitleFormat";

        sealed class MediaItem
        {
            public string Subtype;
            public string DisplayName;
            public string WavePath;
            public GameAudioContainerKind ContainerKind;
        }

        sealed class SoundCategoryNameLookup
        {
            public readonly Dictionary<string, string> BySoundId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> ByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            PlayToggle,
            Shuffle,
            Repeat
        }

        enum MediaRepeatMode
        {
            Disabled = 0,
            Single = 1,
            Folder = 2
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
            _shuffleButton = AddLogicalChild(new ToggleButton(default(RectangleF), new MediaButtonModel
            {
                Text = "Shuffle",
                DisplayText = string.Empty,
                Content = MediaButtonContent.Shuffle,
                Clicked = ToggleShuffle,
                Enabled = true
            })
            {
                GetState = IsShuffleEnabled
            });
            _previousButton = CreateButton("Prev", Previous);
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
            _nextButton = CreateButton("Next", Next);
            _repeatButton = AddLogicalChild(new ToggleButton(default(RectangleF), new MediaButtonModel
            {
                Text = "Repeat",
                DisplayText = string.Empty,
                Content = MediaButtonContent.Repeat,
                Clicked = CycleRepeatMode,
                Enabled = true
            })
            {
                GetState = IsRepeatActive
            });
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
            _volumeSliderModel = new HorizontalSliderModel
            {
                Value = 1f,
                ValueChanged = SetPlaybackVolume
            };
            _volumeSlider = AddLogicalChild(new HorizontalSlider(default(RectangleF), _volumeSliderModel));
        }

        public override IReadOnlyList<Control> VisualChildren => _children;

        public override void Update()
        {
            EnsureLibrary();
            _player = Host.GridLogic == null ? null : Host.GridLogic.MediaPlayer;
            if (_player != null && _volumeSliderModel != null)
                _player.Volume = _volumeSliderModel.Value;
            RestorePickedAudioFromConfig();
            NormalizeSelectionFromConfig();
            HandlePlaybackCompletion();
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

            if (HasLibrarySelection())
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
                        return "Local/" + _pickedAudio.LocalAsset.SourceArchivePath.Replace('\\', '/');

                    if (!string.IsNullOrEmpty(_pickedAudio.LocalAsset.SourcePath))
                        return "Local/" + _pickedAudio.LocalAsset.SourcePath.Replace('\\', '/');
                }

                if (!string.IsNullOrEmpty(_pickedAudio.DefinitionPath))
                    return "Content/" + _pickedAudio.DefinitionPath.Replace('\\', '/');

                return GetPickedAudioTitle(_pickedAudio);
            }

            if (HasLibrarySelection())
            {
                var selected = _library[_selectedIndex];
                return string.IsNullOrEmpty(selected.WavePath)
                    ? selected.Subtype
                    : "Content/" + selected.WavePath.Replace('\\', '/');
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
            var volumeHeight = Math.Max(12f, 16f * scale);
            var volumeY = area.Bottom - volumeHeight;
            var volumeFits = volumeY - gap - buttonHeight >= area.Y;
            var buttonY = volumeFits
                ? volumeY - gap - buttonHeight
                : area.Bottom - buttonHeight;
            var progressHeight = Math.Max(16f, 20f * scale);
            var progressY = buttonY - gap - progressHeight;
            var progressFits = progressY >= area.Y;
            var bottomControlsTop = progressFits ? progressY : buttonY;
            var visualizerTop = Math.Max(area.Y, contentBottom + gap);
            var visualizerBottom = bottomControlsTop - gap;
            var buttonWidth = Math.Max(1f, (area.Width - gap * 6f) / 7f);
            var libraryEnabled = _pickedAudio != null || HasLibrarySelection();
            var canStart = _pickedAudio != null || HasLibrarySelection();
            var canTogglePlay = _player != null && (canStart || _player.IsPlaying || _player.IsPaused);
            var canSeek = _player != null && _player.CanSeek;

            if (visualizerBottom - visualizerTop >= Math.Max(18f, 30f * scale))
                DrawVisualizerArea(sprites, new RectangleF(area.X, visualizerTop, area.Width, visualizerBottom - visualizerTop));

            if (progressFits)
                DrawAudioProgress(sprites, new RectangleF(area.X, progressY, area.Width, progressHeight), canSeek);

            if (volumeFits)
                DrawVolumeSlider(sprites, new RectangleF(area.X, volumeY, area.Width, volumeHeight));

            var pickRect = new RectangleF(area.X, buttonY, buttonWidth, buttonHeight);
            var shuffleRect = new RectangleF(area.X + (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var previousRect = new RectangleF(area.X + 2f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var playRect = new RectangleF(area.X + 3f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var nextRect = new RectangleF(area.X + 4f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var repeatRect = new RectangleF(area.X + 5f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
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
            DrawMediaButton(_shuffleButton, shuffleRect, string.Empty, true, sprites, MediaButtonShape.Transparent, MediaButtonContent.Shuffle);
            DrawMediaButton(_previousButton, previousRect, string.Empty, libraryEnabled, sprites, MediaButtonShape.LeftRounded, MediaButtonContent.PreviousTrack, previousDecoratorRect);
            DrawMediaButton(_nextButton, nextRect, string.Empty, libraryEnabled, sprites, MediaButtonShape.RightRounded, MediaButtonContent.NextTrack, nextDecoratorRect);
            DrawMediaButton(_repeatButton, repeatRect, GetRepeatButtonText(), true, sprites, MediaButtonShape.Transparent, MediaButtonContent.Repeat);
            DrawMediaButton(_stopButton, stopRect, string.Empty, CanResetPlayer(), sprites, MediaButtonShape.Transparent, MediaButtonContent.StopSquare);
            DrawMediaButton(_playButton, playRect, GetPlayButtonText(), canTogglePlay, sprites, MediaButtonShape.Circle, MediaButtonContent.PlayToggle);
        }

        bool CanResetPlayer()
        {
            if (_player == null)
                return false;

            return _player.IsActive || _player.HasLoadedAudio;
        }

        Color GetMediaActiveColor()
        {
            return ResolveResource(ThemeResources.AccentContainerColor, GetHeaderColor());
        }

        Color GetMediaInactiveSurfaceColor()
        {
            return ResolveResource(ThemeResources.SecondaryContainerColor, Host.BackgroundColor);
        }

        Color GetMediaInactiveForegroundColor()
        {
            return ResolveResource(ThemeResources.OnSecondaryContainerColor, Host.ForegroundColor);
        }


        void DrawVisualizerArea(List<MySprite> sprites, RectangleF rect)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var scale = GeneralComponent.GetScale();
            var innerPad = Math.Max(1f, 3f * scale);
            var contentRect = new RectangleF(
                rect.X + innerPad,
                rect.Y + innerPad,
                Math.Max(1f, rect.Width - innerPad * 2f),
                Math.Max(1f, rect.Height - innerPad * 2f));

            if (MediaPlayerComponent.VisualizerEnabled)
                DrawVisualizer(sprites, contentRect);
            else
                DrawAudioFileIcon(sprites, contentRect);
        }

        void DrawVolumeSlider(List<MySprite> sprites, RectangleF rect)
        {
            if (_volumeSlider == null || _volumeSliderModel == null || rect.Width <= 0f || rect.Height <= 0f)
                return;

            var scale = GeneralComponent.GetScale();
            var iconSize = Math.Max(1f, Math.Min(rect.Height, 18f * scale));
            var iconGap = Math.Max(2f, 5f * scale);
            var iconColor = GetMediaInactiveForegroundColor();
            var leftIconRect = new RectangleF(rect.X, rect.Center.Y - iconSize * .5f, iconSize, iconSize);
            var rightIconRect = new RectangleF(rect.Right - iconSize, rect.Center.Y - iconSize * .5f, iconSize, iconSize);
            DrawCenteredIcon(sprites, leftIconRect, SOUND_LOW_ICON, iconColor, 1f);
            DrawCenteredIcon(sprites, rightIconRect, SOUND_HIGH_ICON, iconColor, 1f);

            var sliderRect = new RectangleF(
                leftIconRect.Right + iconGap,
                rect.Y,
                Math.Max(1f, rightIconRect.X - leftIconRect.Right - iconGap * 2f),
                rect.Height);

            _volumeSliderModel.Value = MathHelper.Clamp(_volumeSliderModel.Value, 0f, 1f);
            _volumeSliderModel.TrackColor = GetMediaInactiveSurfaceColor();
            _volumeSliderModel.FillColor = GetMediaActiveColor();
            _volumeSliderModel.ThumbColor = GetMediaActiveColor();

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
            else if (HasLibrarySelection())
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
            _audioProgressModel.BackgroundColor = GetMediaInactiveSurfaceColor();
            _audioProgressModel.FillColor = GetMediaActiveColor();
            _audioProgressModel.ThumbColor = GetMediaActiveColor();

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

            var fillColor = enabled ? GetMediaActiveColor() : GetMediaInactiveSurfaceColor();
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
            button.SetClass(GetMediaButtonClass(content));
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
            if (model.Content == MediaButtonContent.Shuffle)
                foreground = IsShuffleEnabled()
                    ? GetMediaActiveColor()
                    : GetMediaInactiveForegroundColor();
            else if (model.Content == MediaButtonContent.Repeat)
                foreground = GetRepeatMode() == MediaRepeatMode.Disabled
                    ? GetMediaInactiveForegroundColor()
                    : GetMediaActiveColor();
            else if (!control.Enabled)
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
            else if (model.Content == MediaButtonContent.Shuffle)
                DrawCenteredIcon(sprites, control.Bounds, SHUFFLE_ICON, foreground, .62f);
            else if (model.Content == MediaButtonContent.Repeat)
                DrawRepeatIcon(sprites, control.Bounds, foreground, model.DisplayText);
            else
                RenderSearchStyleTextButton(control, sprites, model.DisplayText);
        }


        string GetMediaButtonClass(MediaButtonContent content)
        {
            if (content == MediaButtonContent.Repeat)
                return "ControlBase Button MediaButton Repeat " + GetRepeatModeClass(GetRepeatMode());

            if (content == MediaButtonContent.Shuffle)
                return IsShuffleEnabled()
                    ? "ControlBase Button MediaButton Shuffle active"
                    : "ControlBase Button MediaButton Shuffle disabled";

            return "ControlBase Button MediaButton";
        }

        static string GetRepeatModeClass(MediaRepeatMode mode)
        {
            switch (mode)
            {
                case MediaRepeatMode.Single:
                    return "single";
                case MediaRepeatMode.Folder:
                    return "folder";
                default:
                    return "disabled";
            }
        }

        string GetRepeatButtonText()
        {
            return GetRepeatMode() == MediaRepeatMode.Single ? "1" : string.Empty;
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


        void DrawRepeatIcon(List<MySprite> sprites, RectangleF rect, Color color, string suffixText)
        {
            DrawCenteredIcon(sprites, rect, REPEAT_ICON, color, .62f);
            if (string.IsNullOrEmpty(suffixText) || rect.Width <= 0f || rect.Height <= 0f)
                return;

            var scale = Math.Max(.8f, 1.05f * GeneralComponent.GetScale() * Host.Surface.FontSize);
            var size = MeasureText(suffixText, scale);
            var x = rect.Center.X + Math.Min(rect.Width, rect.Height) * .20f;
            var y = rect.Center.Y - size.Y * .5f + Math.Min(rect.Width, rect.Height) * .12f;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = suffixText,
                Position = new Vector2(x, y),
                Color = color,
                FontId = TextFont,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = scale
            });
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
            var triangleOverlap = triangleSize * .16f;
            var barOverlap = triangleSize * .08f;
            var triangleStep = Math.Max(1f, triangleSize - triangleOverlap);
            var groupWidth = triangleSize * 2f - triangleOverlap - barOverlap;
            var startX = rect.Center.X - groupWidth * .5f;
            var centerY = rect.Center.Y;
            var triangleSpriteSize = new Vector2(triangleSize, triangleSize);

            if (forward)
            {
                var firstTriangleX = startX + triangleSize * .5f;
                var secondTriangleX = firstTriangleX + triangleStep;
                DrawSideTriangle(sprites, new Vector2(firstTriangleX, centerY), triangleSpriteSize, color, true);
                DrawSideTriangle(sprites, new Vector2(secondTriangleX, centerY), triangleSpriteSize, color, true);
            }
            else
            {
                var firstTriangleX = startX + triangleSize * .5f;
                var secondTriangleX = firstTriangleX + triangleStep;
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

            var dialog = new FilePickerDialog(
                this,
                "Pick audio",
                FilePickerMode.PickFile,
                EmptyFolderRoots,
                OnAudioPicked,
                _interactiveHost.RequestRedraw,
                null,
                true,
                MediaAudioFilePickerTreeProvider.CurrentPath,
                MediaAudioFilePickerTreeProvider.SetCurrentPath);
            dialog.SetLoading(true, "Loading audio files...");
            _interactiveHost.ShowDialog(dialog);

            MediaAudioFilePickerTreeProvider.BuildRootsAsync(delegate(List<FolderModel> roots, Exception error)
            {
                if (dialog.Dismissed)
                    return;

                dialog.SetRoots(roots ?? EmptyFolderRoots);
                dialog.SetLoading(false);
                if (error != null)
                    LogHelper.Log(MyLogSeverity.Warning, "Could not build media audio picker tree: " + error.Message);

                if (_interactiveHost != null)
                    _interactiveHost.RequestRedraw();
            });
        }

        void Previous(ButtonModel model, object sender)
        {
            MoveSelection(
                -1,
                allowScopeLoop: GetRepeatMode() == MediaRepeatMode.Folder,
                useShuffle: false,
                allowSameShuffleSelection: false,
                applyToActivePlayer: true);
        }

        void Next(ButtonModel model, object sender)
        {
            if (!MoveSelection(
                    1,
                    allowScopeLoop: GetRepeatMode() == MediaRepeatMode.Folder,
                    useShuffle: IsShuffleEnabled(),
                    allowSameShuffleSelection: GetRepeatMode() == MediaRepeatMode.Folder,
                    applyToActivePlayer: true) &&
                _pickedAudio != null &&
                _pickedAudio.IsContent &&
                _player != null)
            {
                _player.ResetPlaybackEngine();
                MarkDirty();
            }
        }

        bool MoveSelection(
            int direction,
            bool allowScopeLoop,
            bool useShuffle,
            bool allowSameShuffleSelection,
            bool applyToActivePlayer)
        {
            if (direction == 0)
                return false;

            if (_pickedAudio != null)
                return useShuffle && direction > 0
                    ? ShufflePickedAudio(allowSameShuffleSelection, applyToActivePlayer)
                    : MovePickedAudio(direction, allowScopeLoop, applyToActivePlayer);

            if (!HasLibrarySelection())
                return false;

            return useShuffle && direction > 0
                ? ShuffleLibrarySelection(allowSameShuffleSelection, applyToActivePlayer)
                : MoveLibrarySelection(direction, allowScopeLoop, applyToActivePlayer);
        }

        bool MovePickedAudio(int direction, bool loopScope, bool applyToActivePlayer)
        {
            if (_pickedAudio == null || direction == 0)
                return false;

            var root = GetPickedAudioRoot();
            if (root == null)
                return false;

            var candidates = new List<FileModel>();
            AddAudioFiles(root, candidates);
            if (candidates.Count == 0)
                return false;

            var index = IndexOfMatchingFile(candidates, _pickedAudio);
            if (index < 0)
                return false;

            var nextIndex = index + (direction < 0 ? -1 : 1);
            if (nextIndex < 0 || nextIndex >= candidates.Count)
            {
                if (!loopScope)
                    return false;

                nextIndex = nextIndex < 0 ? candidates.Count - 1 : 0;
            }

            var reference = candidates[nextIndex].Tag as MediaAudioFileReference;
            if (reference == null)
                return false;

            SelectPickedAudio(reference, syncContentSubtype: true, applyToActivePlayer: applyToActivePlayer);
            return true;
        }

        bool MoveLibrarySelection(int direction, bool loop, bool applyToActivePlayer)
        {
            if (!HasLibrarySelection() || direction == 0)
                return false;

            var nextIndex = _selectedIndex + (direction < 0 ? -1 : 1);
            if (nextIndex < 0 || nextIndex >= _library.Length)
            {
                if (!loop)
                    return false;

                nextIndex = nextIndex < 0 ? _library.Length - 1 : 0;
            }

            SetSelectedIndex(nextIndex, applyToActivePlayer);
            return true;
        }

        bool ShuffleLibrarySelection(bool allowSameSelection, bool applyToActivePlayer)
        {
            if (!HasLibrarySelection())
                return false;

            if (_library.Length == 1 && !allowSameSelection)
                return false;

            var nextIndex = _shuffleRandom.Next(_library.Length);
            if (_library.Length > 1 && nextIndex == _selectedIndex)
                nextIndex = (nextIndex + 1) % _library.Length;

            SetSelectedIndex(nextIndex, applyToActivePlayer);
            return true;
        }

        bool ShufflePickedAudio(bool allowSameSelection, bool applyToActivePlayer)
        {
            if (_pickedAudio == null)
                return false;

            var root = GetPickedAudioRoot();
            if (root == null)
                return false;

            var candidates = new List<FileModel>();
            AddAudioFiles(root, candidates);
            if (candidates.Count == 0)
                return false;

            if (candidates.Count == 1 && FileMatchesReference(candidates[0], _pickedAudio) && !allowSameSelection)
                return false;

            var nextIndex = _shuffleRandom.Next(candidates.Count);
            if (candidates.Count > 1 && FileMatchesReference(candidates[nextIndex], _pickedAudio))
                nextIndex = (nextIndex + 1) % candidates.Count;

            var reference = candidates[nextIndex].Tag as MediaAudioFileReference;
            if (reference == null)
                return false;

            SelectPickedAudio(reference, syncContentSubtype: true, applyToActivePlayer: applyToActivePlayer);
            return true;
        }

        FolderModel GetPickedAudioRoot()
        {
            if (_pickedAudio == null)
                return null;

            var roots = MediaAudioFilePickerTreeProvider.GetCachedRootsOrBuild();
            return FindAudioRoot(roots, _pickedAudio.Source);
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
            ClearPlaybackCompletionHandled();

            if (_pickedAudio == null && !HasLibrarySelection())
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
                else if (_pickedAudio.IsSoundBlock)
                    _player.PlayGameSound(block, _pickedAudio.FirstSoundSubtype, startPaused);
                else
                    _player.PlayGameAudioFile(block, GetPickedAudioTitle(_pickedAudio), _pickedAudio.DefinitionPath, startPaused);
                MarkDirty();
                return;
            }

            if (HasLibrarySelection())
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

        bool IsPlayToggleActive()
        {
            return _player != null && _player.IsPlaying;
        }

        string GetPlayButtonText()
        {
            return IsPlayToggleActive() ? "Pause" : "Play";
        }


        bool IsShuffleEnabled()
        {
            return MediaPlayerComponent.ShuffleEnabled;
        }

        void ToggleShuffle(ButtonModel model, object sender)
        {
            MediaPlayerComponent.ShuffleEnabled = !MediaPlayerComponent.ShuffleEnabled;
            SyncConfig();
            MarkDirty();
        }

        MediaRepeatMode GetRepeatMode()
        {
            var value = MediaPlayerComponent.RepeatModeInternal;
            if (value == (int)MediaRepeatMode.Single)
                return MediaRepeatMode.Single;
            if (value == (int)MediaRepeatMode.Folder)
                return MediaRepeatMode.Folder;
            return MediaRepeatMode.Disabled;
        }

        bool IsRepeatActive()
        {
            return GetRepeatMode() != MediaRepeatMode.Disabled;
        }

        void CycleRepeatMode(ButtonModel model, object sender)
        {
            var mode = GetRepeatMode();
            MediaRepeatMode next;
            if (mode == MediaRepeatMode.Disabled)
                next = MediaRepeatMode.Single;
            else if (mode == MediaRepeatMode.Single)
                next = MediaRepeatMode.Folder;
            else
                next = MediaRepeatMode.Disabled;

            MediaPlayerComponent.RepeatModeInternal = (int)next;
            SyncConfig();
            MarkDirty();
        }

        void HandlePlaybackCompletion()
        {
            var player = _player;
            if (player == null)
                return;

            if (player.IsActive)
            {
                _handledPlaybackCompletion = false;
                return;
            }

            if (_handledPlaybackCompletion ||
                player.IsPaused ||
                player.IsDecoding ||
                !player.HasLoadedAudio ||
                player.CurrentDurationSeconds <= 0.0 ||
                player.CurrentPositionSeconds + 0.05 < player.CurrentDurationSeconds)
            {
                return;
            }

            _handledPlaybackCompletion = true;

            var previousHandlingState = _handlingPlaybackCompletion;
            _handlingPlaybackCompletion = true;
            try
            {
                var repeatMode = GetRepeatMode();
                if (repeatMode == MediaRepeatMode.Single)
                {
                    StartSelectedAudio();
                    return;
                }

                if (IsShuffleEnabled() || repeatMode == MediaRepeatMode.Folder)
                {
                    var moved = MoveSelection(
                        1,
                        allowScopeLoop: repeatMode == MediaRepeatMode.Folder,
                        useShuffle: IsShuffleEnabled(),
                        allowSameShuffleSelection: repeatMode == MediaRepeatMode.Folder,
                        applyToActivePlayer: false);

                    if (moved)
                        StartSelectedAudio();
                }
            }
            finally
            {
                _handlingPlaybackCompletion = previousHandlingState;
            }
        }

        void ClearPlaybackCompletionHandled()
        {
            if (!_handlingPlaybackCompletion)
                _handledPlaybackCompletion = false;
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

            SelectPickedAudio(reference, syncContentSubtype: true, applyToActivePlayer: true);
        }

        void SelectPickedAudio(MediaAudioFileReference reference, bool syncContentSubtype, bool applyToActivePlayer)
        {
            if (reference == null)
                return;

            _pickedAudio = reference;
            _restorePickedAudioAttempted = true;
            ClearPlaybackCompletionHandled();
            MediaPlayerComponent.SelectedAudioSource = reference.Source ?? string.Empty;
            MediaPlayerComponent.SelectedPickerFullPath = reference.PickerFullPath ?? string.Empty;

            if (syncContentSubtype && !string.IsNullOrEmpty(reference.FirstSoundSubtype))
                SelectSubtypeWithoutClearingPicked(reference.FirstSoundSubtype);

            SyncConfig();
            if (applyToActivePlayer)
                ApplySelectedAudioToActivePlayer();
            MarkDirty();
        }

        static FolderModel FindAudioRoot(List<FolderModel> roots, string source)
        {
            if (roots == null || string.IsNullOrEmpty(source))
                return null;

            for (int i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (AudioRootMatchesSource(root, source))
                    return root;
            }

            return null;
        }

        static bool AudioRootMatchesSource(FolderModel root, string source)
        {
            if (root == null || string.IsNullOrEmpty(source))
                return false;

            if (string.Equals(root.FullPath, source, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(root.Name, source, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var files = new List<FileModel>();
            AddAudioFiles(root, files);
            for (int i = 0; i < files.Count; i++)
            {
                var reference = files[i] == null ? null : files[i].Tag as MediaAudioFileReference;
                if (reference != null && string.Equals(reference.Source, source, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static FileModel FindAdjacentFileInCurrentFolder(FolderModel root, MediaAudioFileReference current, int direction, bool loop)
        {
            if (root == null || current == null)
                return null;

            FolderModel folder;
            FileModel file;
            if (!FindFile(root, current, out folder, out file) || folder == null || folder.Files == null || folder.Files.Count == 0)
                return null;

            folder.Files.Sort(ComparePickerFiles);
            var index = IndexOfFile(folder.Files, file);
            if (index < 0)
                return null;

            var nextIndex = index + (direction < 0 ? -1 : 1);
            if (nextIndex < 0 || nextIndex >= folder.Files.Count)
            {
                if (!loop)
                    return null;

                nextIndex = nextIndex < 0 ? folder.Files.Count - 1 : 0;
            }

            return folder.Files[nextIndex];
        }

        static FileModel FindAdjacentContentFile(FolderModel root, MediaAudioFileReference current, int direction, bool loop)
        {
            if (root == null || current == null)
                return null;

            var folders = new List<FolderModel>();
            AddFoldersWithFiles(root, folders);
            if (folders.Count == 0)
                return null;

            FolderModel currentFolder;
            FileModel currentFile;
            if (!FindFile(root, current, out currentFolder, out currentFile) || currentFolder == null)
                return null;

            var folderIndex = IndexOfFolder(folders, currentFolder);
            if (folderIndex < 0)
                return null;

            currentFolder.Files.Sort(ComparePickerFiles);
            var fileIndex = IndexOfFile(currentFolder.Files, currentFile);
            if (fileIndex < 0)
                return null;

            var step = direction < 0 ? -1 : 1;
            var nextFileIndex = fileIndex + step;
            if (nextFileIndex >= 0 && nextFileIndex < currentFolder.Files.Count)
                return currentFolder.Files[nextFileIndex];

            var nextFolderIndex = folderIndex + step;
            if (nextFolderIndex < 0 || nextFolderIndex >= folders.Count)
            {
                if (!loop)
                    return null;

                nextFolderIndex = nextFolderIndex < 0 ? folders.Count - 1 : 0;
            }

            var nextFolder = folders[nextFolderIndex];
            if (nextFolder == null || nextFolder.Files == null || nextFolder.Files.Count == 0)
                return null;

            nextFolder.Files.Sort(ComparePickerFiles);
            return step > 0 ? nextFolder.Files[0] : nextFolder.Files[nextFolder.Files.Count - 1];
        }


        static void AddAudioFiles(FolderModel folder, List<FileModel> result)
        {
            if (folder == null || result == null)
                return;

            var stack = new Stack<FolderModel>();
            var visited = new HashSet<FolderModel>();
            stack.Push(folder);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null || !visited.Add(current))
                    continue;

                if (current.Files != null && current.Files.Count > 0)
                {
                    current.Files.Sort(ComparePickerFiles);
                    for (int i = 0; i < current.Files.Count; i++)
                    {
                        if (current.Files[i] != null && (current.Files[i].Tag as MediaAudioFileReference) != null)
                            result.Add(current.Files[i]);
                    }
                }

                if (current.Folders == null || current.Folders.Count == 0)
                    continue;

                current.Folders.Sort(ComparePickerFolders);
                for (int i = current.Folders.Count - 1; i >= 0; i--)
                    stack.Push(current.Folders[i]);
            }
        }

        static void AddFoldersWithFiles(FolderModel folder, List<FolderModel> result)
        {
            if (folder == null || result == null)
                return;

            var stack = new Stack<FolderModel>();
            var visited = new HashSet<FolderModel>();
            stack.Push(folder);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null || !visited.Add(current))
                    continue;

                if (current.Files != null && current.Files.Count > 0)
                    result.Add(current);

                if (current.Folders == null || current.Folders.Count == 0)
                    continue;

                current.Folders.Sort(ComparePickerFolders);
                for (int i = current.Folders.Count - 1; i >= 0; i--)
                    stack.Push(current.Folders[i]);
            }
        }

        static bool FindFile(FolderModel folder, MediaAudioFileReference reference, out FolderModel foundFolder, out FileModel foundFile)
        {
            foundFolder = null;
            foundFile = null;
            if (folder == null || reference == null)
                return false;

            var stack = new Stack<FolderModel>();
            var visited = new HashSet<FolderModel>();
            stack.Push(folder);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null || !visited.Add(current))
                    continue;

                if (current.Files != null)
                {
                    current.Files.Sort(ComparePickerFiles);
                    for (int i = 0; i < current.Files.Count; i++)
                    {
                        var file = current.Files[i];
                        if (FileMatchesReference(file, reference))
                        {
                            foundFolder = current;
                            foundFile = file;
                            return true;
                        }
                    }
                }

                if (current.Folders == null || current.Folders.Count == 0)
                    continue;

                current.Folders.Sort(ComparePickerFolders);
                for (int i = current.Folders.Count - 1; i >= 0; i--)
                    stack.Push(current.Folders[i]);
            }

            return false;
        }

        static FileModel FindFileByPickerFullPath(FolderModel folder, string pickerFullPath)
        {
            if (folder == null || string.IsNullOrEmpty(pickerFullPath))
                return null;

            var files = new List<FileModel>();
            AddAudioFiles(folder, files);
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (file == null)
                    continue;

                if (string.Equals(file.FullPath, pickerFullPath, StringComparison.OrdinalIgnoreCase))
                    return file;

                var reference = file.Tag as MediaAudioFileReference;
                if (reference != null &&
                    string.Equals(reference.PickerFullPath, pickerFullPath, StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            return null;
        }

        static int IndexOfMatchingFile(List<FileModel> files, MediaAudioFileReference reference)
        {
            if (files == null || reference == null)
                return -1;

            for (int i = 0; i < files.Count; i++)
            {
                if (FileMatchesReference(files[i], reference))
                    return i;
            }

            return -1;
        }

        static bool FileMatchesReference(FileModel file, MediaAudioFileReference reference)
        {
            if (file == null || reference == null)
                return false;

            var fileReference = file.Tag as MediaAudioFileReference;
            if (ReferenceEquals(fileReference, reference))
                return true;

            if (fileReference == null)
                return false;

            if (!string.Equals(fileReference.Source, reference.Source, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(reference.PickerFullPath) &&
                string.Equals(fileReference.PickerFullPath, reference.PickerFullPath, StringComparison.OrdinalIgnoreCase))
                return true;

            if (reference.IsLocal)
                return reference.LocalAsset != null && ReferenceEquals(fileReference.LocalAsset, reference.LocalAsset);

            if (reference.IsSoundBlock)
                return !string.IsNullOrEmpty(reference.FirstSoundSubtype) &&
                       string.Equals(fileReference.FirstSoundSubtype, reference.FirstSoundSubtype, StringComparison.OrdinalIgnoreCase);

            if (reference.IsContent && !string.IsNullOrEmpty(reference.DefinitionPath) &&
                string.Equals(fileReference.DefinitionPath, reference.DefinitionPath, StringComparison.OrdinalIgnoreCase))
                return true;

            return !string.IsNullOrEmpty(reference.FirstSoundSubtype) &&
                   string.Equals(fileReference.FirstSoundSubtype, reference.FirstSoundSubtype, StringComparison.OrdinalIgnoreCase);
        }

        static int IndexOfFile(List<FileModel> files, FileModel file)
        {
            if (files == null || file == null)
                return -1;

            for (int i = 0; i < files.Count; i++)
            {
                if (ReferenceEquals(files[i], file))
                    return i;
            }

            return -1;
        }

        static int IndexOfFolder(List<FolderModel> folders, FolderModel folder)
        {
            if (folders == null || folder == null)
                return -1;

            for (int i = 0; i < folders.Count; i++)
            {
                if (ReferenceEquals(folders[i], folder))
                    return i;
            }

            return -1;
        }

        static int ComparePickerFolders(FolderModel left, FolderModel right)
        {
            return string.Compare(left == null ? null : left.Name, right == null ? null : right.Name, StringComparison.OrdinalIgnoreCase);
        }

        static int ComparePickerFiles(FileModel left, FileModel right)
        {
            return string.Compare(left == null ? null : left.Name, right == null ? null : right.Name, StringComparison.OrdinalIgnoreCase);
        }

        void SetSelectedIndex(int index, bool applyToActivePlayer)
        {
            if (_library.Length == 0)
                return;

            _pickedAudio = null;
            _restorePickedAudioAttempted = false;
            ClearPlaybackCompletionHandled();
            MediaPlayerComponent.SelectedAudioSource = string.Empty;
            MediaPlayerComponent.SelectedPickerFullPath = string.Empty;

            if (index < 0)
                index = 0;
            if (index >= _library.Length)
                index = _library.Length - 1;

            _selectedIndex = index;
            MediaPlayerComponent.SelectedIndex = _selectedIndex;
            MediaPlayerComponent.SelectedSoundSubtype = _library[_selectedIndex].Subtype;
            SyncConfig();
            if (applyToActivePlayer)
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

            var lookupName = ResolveSoundCategorySongName(subtype, fallbackPath);
            if (!string.IsNullOrEmpty(lookupName))
                return lookupName;

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

            var lookupName = ResolveSoundCategorySongName(definition.Id.SubtypeName, fallbackPath);
            if (!string.IsNullOrEmpty(lookupName))
                return lookupName;

            var displayName = definition.DisplayNameText;
            if (IsMusicDefinition(definition, fallbackPath) &&
                !string.IsNullOrEmpty(displayName) &&
                !string.Equals(displayName, definition.Id.SubtypeName, StringComparison.OrdinalIgnoreCase))
                return displayName;

            return GetFileNameWithoutExtension(fallbackPath);
        }

        static string ResolveSoundCategorySongName(string subtype, string path)
        {
            var lookup = GetSoundCategoryNameLookup();
            if (lookup == null)
                return string.Empty;

            string name;
            if (!string.IsNullOrEmpty(subtype) && lookup.BySoundId.TryGetValue(subtype, out name))
                return name;

            var fileName = GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(fileName) && lookup.ByFileName.TryGetValue(fileName, out name))
                return name;

            return string.Empty;
        }

        static SoundCategoryNameLookup GetSoundCategoryNameLookup()
        {
            if (_soundCategoryNameLookup != null)
                return _soundCategoryNameLookup;

            if (MyDefinitionManager.Static == null)
                return null;

            var lookup = new SoundCategoryNameLookup();
            foreach (MySoundCategoryDefinition category in MyDefinitionManager.Static.GetSoundCategoryDefinitions())
            {
                if (category == null || category.Sounds == null)
                    continue;

                for (int i = 0; i < category.Sounds.Count; i++)
                {
                    var sound = category.Sounds[i];
                    if (sound == null || string.IsNullOrEmpty(sound.SoundId))
                        continue;

                    var text = sound.SoundText;
                    if (string.IsNullOrEmpty(text))
                        text = sound.SoundName;
                    if (!string.IsNullOrEmpty(text) && !lookup.BySoundId.ContainsKey(sound.SoundId))
                        lookup.BySoundId.Add(sound.SoundId, text);
                }
            }

            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition == null || string.IsNullOrEmpty(definition.Id.SubtypeName))
                    continue;

                string text;
                if (!lookup.BySoundId.TryGetValue(definition.Id.SubtypeName, out text) || string.IsNullOrEmpty(text))
                    continue;

                AddWaveFileNameLookup(lookup, GridMediaPlayer.FindStartWave(definition), text);
            }

            _soundCategoryNameLookup = lookup;
            return _soundCategoryNameLookup;
        }

        static void AddWaveFileNameLookup(SoundCategoryNameLookup lookup, string path, string text)
        {
            if (lookup == null || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text))
                return;

            var fileName = GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(fileName) || lookup.ByFileName.ContainsKey(fileName))
                return;

            lookup.ByFileName.Add(fileName, text);
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

        void RestorePickedAudioFromConfig()
        {
            if (_pickedAudio != null || _restorePickedAudioAttempted)
                return;

            var source = MediaPlayerComponent.SelectedAudioSource;
            if (string.IsNullOrEmpty(source))
                return;

            _restorePickedAudioAttempted = true;

            var roots = MediaAudioFilePickerTreeProvider.GetCachedRootsOrBuild();
            var root = FindAudioRoot(roots, source);
            if (root == null)
                return;

            var file = FindFileByPickerFullPath(root, MediaPlayerComponent.SelectedPickerFullPath);
            var reference = file == null ? null : file.Tag as MediaAudioFileReference;
            if (reference == null)
                return;

            _pickedAudio = reference;
            if (!string.IsNullOrEmpty(reference.FirstSoundSubtype))
                SelectSubtypeWithoutClearingPicked(reference.FirstSoundSubtype);
        }

        void NormalizeSelectionFromConfig()
        {
            if (_library.Length == 0)
            {
                _selectedIndex = -1;
                MediaPlayerComponent.SelectedIndex = -1;
                MediaPlayerComponent.SelectedSoundSubtype = string.Empty;
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

                MediaPlayerComponent.SelectedSoundSubtype = string.Empty;
            }

            _selectedIndex = -1;
            MediaPlayerComponent.SelectedIndex = -1;
        }

        bool HasLibrarySelection()
        {
            return _library != null && _selectedIndex >= 0 && _selectedIndex < _library.Length;
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
