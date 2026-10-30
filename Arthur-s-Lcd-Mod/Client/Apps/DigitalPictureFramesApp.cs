using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Client.Images;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

using LcdMod.Common.Config.Generation;

namespace LcdMod.Client.Apps
{
    [LcdApp(16)]
    [ConfigComponent(APP, typeof(DigitalPictureFramesConfigComponent), PropertyName = "DigitalPictureFramesComponent")]
    internal sealed partial class DigitalPictureFramesApp : App, IApp
    {
        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<string> _displayable = new List<string>();
        readonly InteractiveSurfaceScript _interactiveHost;
        readonly DigitalPictureFrames.DigitalPictureFramesViewModel _viewModel;
        GridImageAnimation _animation;
        int _renderedAnimationVersion = -1;
        bool _animationFrameScheduled;
        bool _closed;

        public DigitalPictureFramesApp(InteractiveSurfaceScript host)
            : base(host)
        {
            _interactiveHost = host;
            _viewModel = new DigitalPictureFrames.DigitalPictureFramesViewModel(this);
            SetBackgroundContent(new DigitalPictureFrames.DigitalPictureFramesView { DataContext = _viewModel });
        }

        public override void Update()
        {
            _viewModel.Refresh();
            ScheduleAnimationFrame();
        }

        public override void Close()
        {
            _closed = true;
            base.Close();
        }

        /// <summary>The texture to draw for <paramref name="sprite"/>: an animated image's current frame, or the sprite itself.</summary>
        internal string NativeTextureFor(string sprite)
        {
            GridImageAnimation animation = GridImageClientService.Instance?.GetAnimation(sprite);
            if (animation == null)
                return sprite;

            animation.Advance(MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds);
            return animation.TextureName;
        }

        void ScheduleAnimationFrame()
        {
            if (_animationFrameScheduled || !UpdateSelectedAnimation())
                return;

            _animationFrameScheduled = true;
            LcdModClientComponent.RunNextFrame.Add(delegate
            {
                _animationFrameScheduled = false;
                if (!UpdateSelectedAnimation())
                    return;

                _animation.Advance(MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds);
                if (_animation.Version != _renderedAnimationVersion)
                {
                    _renderedAnimationVersion = _animation.Version;
                    _viewModel.Refresh();
                    _interactiveHost?.RenderSprites();
                }

                ScheduleAnimationFrame();
            });
        }

        bool UpdateSelectedAnimation()
        {
            IReadOnlyList<string> sprites = _closed ? null : NativeSprites;
            GridImageAnimation animation = sprites == null || sprites.Count == 0
                ? null
                : GridImageClientService.Instance?.GetAnimation(sprites[NativeSelectedIndex(sprites.Count)]);
            if (!ReferenceEquals(animation, _animation))
            {
                _animation = animation;
                _renderedAnimationVersion = -1;
            }

            return _animation != null;
        }

        public override List<MySprite> GetSprites()
        {
            return _sprites;
        }

        internal IReadOnlyList<string> NativeSprites
        {
            get
            {
                _displayable.Clear();
                string[] configured = GetConfiguredSprites(DigitalPictureFramesComponent);
                for (int i = 0; i < configured.Length; i++)
                    if (CanDisplay(configured[i]))
                        _displayable.Add(configured[i]);
                return _displayable;
            }
        }

        internal bool NativeIsLoading(string sprite)
        {
            return GetImageState(sprite) == GridImageState.Loading;
        }

        internal int NativeDisplayMode => GeneralComponent.DisplayMode;

        internal bool NativeCanEdit => HasLocalPlayerAccess();
        internal string NativePickText => LocHelper.GetLoc(MOD_PREFIX + "PickTexture");
        internal void OpenNativePicker() { OnPickBackgroundClicked(); }

        internal int NativeSelectedIndex(int count)
        {
            float interval = Math.Max(0f, DigitalPictureFramesComponent.ImageChangeInterval);
            if (count <= 1 || interval <= 0f)
                return 0;
            var session = MyAPIGateway.Session;
            double totalSeconds = session.ElapsedPlayTime.TotalSeconds;
            return (int)(totalSeconds / interval) % count;
        }

        internal override float WindowRenderScaling => CardScale;

        internal BackgroundBrush CreateNativeBrush(string sprite, string texture)
        {
            var brush = new ImageBrush(texture) { TransformOrigin = RelativePoint.Center };
            switch ((PictureFrameDisplayMode)GeneralComponent.DisplayMode)
            {
                case PictureFrameDisplayMode.Center:
                    brush.Stretch = Stretch.None;
                    break;
                case PictureFrameDisplayMode.Fit:
                    brush.Stretch = Stretch.Uniform;
                    break;
                case PictureFrameDisplayMode.Fill:
                    brush.Stretch = Stretch.UniformToFill;
                    break;
                case PictureFrameDisplayMode.Tile:
                    Vector2I size;
                    if (LcdTextureSizeHelper.TryGetTextureSize(sprite, out size) && size.X > 0 && size.Y > 0)
                    {
                        brush.TileMode = TileMode.Tile;
                        brush.DestinationRect = new RelativeRect(0f, 0f, size.X, size.Y, RelativeUnit.Absolute);
                    }
                    break;
            }
            return brush;
        }

        bool CanDisplay(string sprite)
        {
            if (GridImageClientService.IsImageSprite(sprite))
            {
                GridImageState state = GetImageState(sprite);
                return state == GridImageState.Ready || state == GridImageState.Loading;
            }

            return TextureHelper.EnsureRegistered(sprite);
        }

        GridImageState GetImageState(string sprite)
        {
            var images = GridImageClientService.Instance;
            if (images == null || !GridImageClientService.IsImageSprite(sprite))
                return GridImageState.Missing;

            var grid = _interactiveHost?.Block?.CubeGrid;
            return images.GetState(sprite, grid?.EntityId ?? 0);
        }

        static string[] GetConfiguredSprites(DigitalPictureFramesConfigComponent config)
        {
            if (config.SelectedSprites != null && config.SelectedSprites.Length > 0)
                return NormalizeSpriteSelection(config.SelectedSprites);

            return string.IsNullOrWhiteSpace(config.BackgroundSprite)
                ? Array.Empty<string>()
                : new[] { config.BackgroundSprite };
        }

        internal bool PickerOpened { get; private set; }

        void OnPickBackgroundClicked()
        {
            if (_interactiveHost == null || !HasLocalPlayerAccess())
                return;

            PickerOpened = true;
            Dialogs.Launchers.SpritePickerLauncher.PickMany(this, _interactiveHost.Surface,
                GetConfiguredSprites(DigitalPictureFramesComponent), OnSpritesSelected);
        }

        bool HasLocalPlayerAccess()
        {
            var block = _interactiveHost?.Block as IMyTerminalBlock;
            return block != null && block.HasLocalPlayerAccess();
        }

        void OnSpritesSelected(string[] spriteNames)
        {
            var config = DigitalPictureFramesComponent;
            var normalized = NormalizeSpriteSelection(spriteNames);
            var backgroundSprite = normalized.Length > 0 ? normalized[0] : string.Empty;

            if (string.Equals(config.BackgroundSprite, backgroundSprite, StringComparison.OrdinalIgnoreCase) &&
                AreSpriteSelectionsEqual(config.SelectedSprites, normalized))
                return;

            config.BackgroundSprite = backgroundSprite;
            config.SelectedSprites = normalized;
            _viewModel.Refresh();

            if (_interactiveHost != null && _interactiveHost.Block != null && _interactiveHost.ProviderConfig != null)
                ConfigManager.Sync(_interactiveHost.Block, _interactiveHost.ProviderConfig);
        }

        static string[] NormalizeSpriteSelection(IEnumerable<string> spriteNames)
        {
            if (spriteNames == null)
                return Array.Empty<string>();

            var selectedSprites = new List<string>();
            var seenSprites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var spriteName in spriteNames)
            {
                if (string.IsNullOrWhiteSpace(spriteName))
                    continue;

                var normalized = spriteName.Trim();
                if (!seenSprites.Add(normalized))
                    continue;

                selectedSprites.Add(normalized);
            }

            return selectedSprites.ToArray();
        }

        static bool AreSpriteSelectionsEqual(string[] left, string[] right)
        {
            left = NormalizeSpriteSelection(left);
            right = NormalizeSpriteSelection(right);

            if (left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        public enum PictureFrameDisplayMode
        {
            Stretch = 0,
            Center = 1,
            Fit = 2,
            Fill = 3,
            Tile = 4
        }
    }
}
