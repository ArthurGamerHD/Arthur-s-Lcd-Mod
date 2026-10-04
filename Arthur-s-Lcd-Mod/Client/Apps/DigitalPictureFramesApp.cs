using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
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

        internal bool NativeCanEdit => HasLocalPlayerAccess();
        internal string NativePickText => LocHelper.GetLoc(MOD_PREFIX + "PickTexture");
        internal void OpenNativePicker() { OnPickBackgroundClicked(); }

        internal int NativeSelectedIndex(int count)
        {
            float interval = Math.Max(0f, DigitalPictureFramesComponent.ImageChangeInterval);
            if (count <= 1 || interval <= 0f)
                return 0;
            var session = MyAPIGateway.Session;
            double totalSeconds = session != null ? session.ElapsedPlayTime.TotalSeconds : 0d;
            return (int)(totalSeconds / interval) % count;
        }

        internal BackgroundBrush CreateNativeBrush(string sprite)
        {
            float scale = GeneralComponent.GetScale();
            var brush = new ImageBrush(sprite) { TransformOrigin = RelativePoint.Center };
            switch ((PictureFrameDisplayMode)GeneralComponent.DisplayMode)
            {
                case PictureFrameDisplayMode.Center:
                    brush.Stretch = Stretch.None;
                    brush.Transform = new ScaleTransform(scale, scale);
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
                        brush.Transform = new ScaleTransform(scale, scale);
                    }
                    break;
            }
            return brush;
        }

        // A configured image only counts once it can render here: allowed for this player and defined as an LCD
        // texture (synced textures are defined when their download completes).
        static bool CanDisplay(string sprite)
        {
            return TextureHelper.CanRenderTexture(sprite) &&
                   Sandbox.Definitions.MyDefinitionManager.Static != null &&
                   Sandbox.Definitions.MyDefinitionManager.Static.GetDefinition<Sandbox.Definitions.MyLCDTextureDefinition>(
                       VRage.Utils.MyStringHash.GetOrCompute(sprite)) != null;
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
