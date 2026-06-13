using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Apps
{
    internal sealed class DigitalPictureFramesApp : App, IApp
    {
        const float BUTTON_WIDTH_PIXELS = 220f;
        const float BUTTON_HEIGHT_PIXELS = 42f;
        const int MAX_TILE_SPRITES = 2048;
        const long TRANSITION_FRAMES = 24L;

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<Control> _children = new List<Control>();
        readonly Button _pickBackgroundButton;
        readonly RectangleControl _imagePickerHitbox;
        readonly InteractiveSurfaceScript _interactiveHost;
        string _currentSprite = string.Empty;
        string _previousSprite = string.Empty;
        long _transitionStartFrame = long.MinValue;
        bool _redrawQueued;

        new ScreenConfigDigitalPictureFrames AppConfig => (ScreenConfigDigitalPictureFrames)base.AppConfig;

        public DigitalPictureFramesApp(ScreenConfigDigitalPictureFrames config, InteractiveSurfaceScript host)
            : base(config, host)
        {
            _interactiveHost = host;

            _pickBackgroundButton = AddChild(new Button(default(RectangleF), new ButtonModel
            {
                Text = LocHelper.GetLoc("LcdMod_PickTexture"),
                Clicked = OnPickBackgroundClicked
            }));
            _imagePickerHitbox = AddChild(new RectangleControl(default(RectangleF), CursorType.Hand, null, OnImageClicked)
            {
                CustomRender = RenderImagePickerHitbox
            });

            _children.Add(_pickBackgroundButton);
            _children.Add(_imagePickerHitbox);
        }

        public override IReadOnlyList<Control> Children => _children;

        public override void Update()
        {
            UpdateControls();
        }
        
        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();

            var config = AppConfig;
            if (config == null)
                return _sprites;

            Host.AddBackground(_sprites);
            DrawBackgroundImage(config, (PictureFrameDisplayMode)config.DisplayMode);
            Host.DrawTitle(_sprites);
            ClearDirtyAfterRender();
            
            return _sprites;
        }

        void DrawBackgroundImage(ScreenConfigDigitalPictureFrames config, PictureFrameDisplayMode displayMode)
        {
            var spriteName = GetCurrentSprite(config);
            UpdateTransition(spriteName);

            if (string.IsNullOrWhiteSpace(spriteName))
                return;

            var viewBox = Host.ViewBox;
            if (viewBox.Width <= 0f || viewBox.Height <= 0f)
                return;

            _sprites.Add(MySprite.CreateClipRect(ToRectangle(viewBox)));

            float progress;
            if (TryGetTransitionProgress(out progress) && !string.IsNullOrWhiteSpace(_previousSprite))
            {
                var eased = EaseInOutCubic(progress);
                var previousTravel = GetTransitionTravel(_previousSprite, displayMode, viewBox);
                var currentTravel = GetTransitionTravel(spriteName, displayMode, viewBox);
                DrawImage(_previousSprite, displayMode, viewBox, new Vector2(-previousTravel * eased, 0f));
                DrawImage(spriteName, displayMode, viewBox, new Vector2(currentTravel * (1f - eased), 0f));
                QueueTransitionRedraw();
            }
            else
            {
                DrawImage(spriteName, displayMode, viewBox, Vector2.Zero);
            }

            _sprites.Add(MySprite.CreateClearClipRect());
        }

        string GetCurrentSprite(ScreenConfigDigitalPictureFrames config)
        {
            var sprites = GetConfiguredSprites(config);
            if (sprites.Length == 0)
                return string.Empty;

            var interval = Math.Max(0f, config.ImageChangeInterval);
            if (interval <= 0f || sprites.Length == 1)
                return sprites[0];

            var session = MyAPIGateway.Session;
            var totalSeconds = session != null ? session.ElapsedPlayTime.TotalSeconds : 0d;
            var index = (int)(totalSeconds / interval) % sprites.Length;
            return sprites[index];
        }

        static string[] GetConfiguredSprites(ScreenConfigDigitalPictureFrames config)
        {
            if (config == null)
                return new string[0];

            if (config.SelectedSprites != null && config.SelectedSprites.Length > 0)
                return config.SelectedSprites.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            return string.IsNullOrWhiteSpace(config.BackgroundSprite)
                ? new string[0]
                : new[] { config.BackgroundSprite };
        }

        void UpdateTransition(string spriteName)
        {
            spriteName = spriteName ?? string.Empty;

            if (string.Equals(_currentSprite, spriteName, StringComparison.OrdinalIgnoreCase))
                return;

            _previousSprite = _currentSprite;
            _currentSprite = spriteName;
            _transitionStartFrame = !string.IsNullOrWhiteSpace(_previousSprite) &&
                                    !string.IsNullOrWhiteSpace(_currentSprite)
                ? GetFrameCounter()
                : long.MinValue;
        }

        bool TryGetTransitionProgress(out float progress)
        {
            progress = 1f;
            if (_transitionStartFrame == long.MinValue)
                return false;

            var elapsed = GetFrameCounter() - _transitionStartFrame;
            if (elapsed < 0L || elapsed >= TRANSITION_FRAMES)
            {
                _transitionStartFrame = long.MinValue;
                _previousSprite = string.Empty;
                return false;
            }

            progress = MathHelper.Clamp((float)elapsed / TRANSITION_FRAMES, 0f, 1f);
            return true;
        }

        void DrawImage(string spriteName, PictureFrameDisplayMode displayMode, RectangleF viewBox, Vector2 offset)
        {
            if (displayMode == PictureFrameDisplayMode.Tile)
            {
                DrawTiledImage(spriteName, viewBox, GetImageScale(), offset);
                return;
            }

            Vector2 sourceSize;
            var hasSourceSize = TryGetSourceSize(spriteName, out sourceSize);
            var drawSize = GetImageDrawSize(viewBox.Size, sourceSize, hasSourceSize, displayMode, GetImageScale());

            _sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = spriteName,
                Position = viewBox.Center + offset,
                Size = drawSize,
                Color = Color.White,
                Alignment = TextAlignment.CENTER
            });
        }

        float GetTransitionTravel(string spriteName, PictureFrameDisplayMode displayMode, RectangleF viewBox)
        {
            if (displayMode == PictureFrameDisplayMode.Tile)
                return viewBox.Width;

            Vector2 sourceSize;
            var hasSourceSize = TryGetSourceSize(spriteName, out sourceSize);
            var drawSize = GetImageDrawSize(viewBox.Size, sourceSize, hasSourceSize, displayMode, GetImageScale());
            return Math.Max(0f, viewBox.Width * 0.5f + drawSize.X * 0.5f);
        }

        void DrawTiledImage(string spriteName, RectangleF viewBox, float imageScale, Vector2 offset)
        {
            Vector2 sourceSize;
            if (!TryGetSourceSize(spriteName, out sourceSize))
                sourceSize = viewBox.Size;

            sourceSize *= imageScale;
            sourceSize.X = Math.Max(1f, sourceSize.X);
            sourceSize.Y = Math.Max(1f, sourceSize.Y);

            var startX = GetFirstTileCenter(viewBox.X, viewBox.Center.X + offset.X, sourceSize.X);
            var startY = GetFirstTileCenter(viewBox.Y, viewBox.Center.Y + offset.Y, sourceSize.Y);
            var right = viewBox.X + viewBox.Width;
            var bottom = viewBox.Y + viewBox.Height;
            var spriteCount = 0;

            for (var y = startY; y - sourceSize.Y * 0.5f < bottom; y += sourceSize.Y)
            {
                for (var x = startX; x - sourceSize.X * 0.5f < right; x += sourceSize.X)
                {
                    if (spriteCount >= MAX_TILE_SPRITES)
                        return;

                    _sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = spriteName,
                        Position = new Vector2(x, y),
                        Size = sourceSize,
                        Color = Color.White,
                        Alignment = TextAlignment.CENTER
                    });
                    spriteCount++;
                }
            }
        }

        void QueueTransitionRedraw()
        {
            if (_redrawQueued || _interactiveHost == null)
                return;

            _redrawQueued = true;
            LcdModClientComponent.RunNextFrame.Add(delegate
            {
                _redrawQueued = false;
                if (_interactiveHost != null)
                    _interactiveHost.RequestRedraw();
            });
        }

        static long GetFrameCounter()
        {
            return MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
        }

        static float EaseInOutCubic(float value)
        {
            value = MathHelper.Clamp(value, 0f, 1f);
            return value < 0.5f
                ? 4f * value * value * value
                : 1f - (float)Math.Pow(-2f * value + 2f, 3d) * 0.5f;
        }

        Vector2 GetImageDrawSize(Vector2 viewSize, Vector2 sourceSize, bool hasSourceSize,
            PictureFrameDisplayMode displayMode, float imageScale)
        {
            if (!hasSourceSize || sourceSize.X <= 0f || sourceSize.Y <= 0f)
                return viewSize;

            switch (displayMode)
            {
                case PictureFrameDisplayMode.Center:
                    return sourceSize * imageScale;
                case PictureFrameDisplayMode.Fit:
                {
                    var scale = Math.Min(viewSize.X / sourceSize.X, viewSize.Y / sourceSize.Y);
                    return sourceSize * Math.Max(0f, scale);
                }
                case PictureFrameDisplayMode.Fill:
                {
                    var scale = Math.Max(viewSize.X / sourceSize.X, viewSize.Y / sourceSize.Y);
                    return sourceSize * Math.Max(0f, scale);
                }
                case PictureFrameDisplayMode.Stretch:
                default:
                    return viewSize;
            }
        }

        float GetImageScale()
        {
            var config = AppConfig;
            return config != null ? config.Scale : 1f;
        }

        static bool TryGetSourceSize(string spriteName, out Vector2 size)
        {
            size = Vector2.Zero;

            Vector2I textureSize;
            if (!LcdTextureSizeHelper.TryGetTextureSize(spriteName, out textureSize) ||
                textureSize.X <= 0 ||
                textureSize.Y <= 0)
                return false;

            size = new Vector2(textureSize.X, textureSize.Y);
            return true;
        }

        static float GetFirstTileCenter(float boundsStart, float baseCenter, float tileSize)
        {
            var halfSize = tileSize * 0.5f;
            var start = baseCenter;
            while (start - halfSize > boundsStart)
                start -= tileSize;

            return start;
        }

        static Rectangle ToRectangle(RectangleF rect)
        {
            var x = (int)Math.Floor(rect.X);
            var y = (int)Math.Floor(rect.Y);
            var right = (int)Math.Ceiling(rect.X + rect.Width);
            var bottom = (int)Math.Ceiling(rect.Y + rect.Height);
            return new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
        }

        void UpdateControls()
        {
            var config = AppConfig;
            if (config == null)
            {
                _pickBackgroundButton.SetVisible(false);
                _imagePickerHitbox.SetVisible(false);
                return;
            }

            var hasBackground = GetConfiguredSprites(config).Length > 0;
            var canAccessBlock = HasLocalPlayerAccess();
            var scale = Math.Max(0.75f, AppConfig.Scale);
            var width = Math.Min(BUTTON_WIDTH_PIXELS * scale, Math.Max(1f, Host.ViewBox.Width * 0.5f));
            var height = Math.Max(1f, BUTTON_HEIGHT_PIXELS * scale);

            _pickBackgroundButton.SetRect(new RectangleF(
                Host.ViewBox.Center.X - width * 0.5f,
                Host.ViewBox.Center.Y - height * 0.5f,
                width,
                height));
            _pickBackgroundButton.SetVisible(!hasBackground);

            _imagePickerHitbox.SetRect(Host.ViewBox);
            _imagePickerHitbox.SetCursor(canAccessBlock ? CursorType.Hand : CursorType.No);
            _imagePickerHitbox.SetVisible(hasBackground);
            _imagePickerHitbox.CustomRender = RenderImagePickerHitbox;

            var model = _pickBackgroundButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = LocHelper.GetLoc("LcdMod_PickTexture");
                model.Enabled = canAccessBlock;
                model.Clicked = OnPickBackgroundClicked;
            }

            _pickBackgroundButton.SetCursor(canAccessBlock ? CursorType.Hand : CursorType.No);
            _pickBackgroundButton.SetStyleId(canAccessBlock ? "Primary" : "Disabled");
            _pickBackgroundButton.SetEnabled(canAccessBlock);
        }

        void OnPickBackgroundClicked(ButtonModel model, object sender)
        {
            if (_interactiveHost == null || !HasLocalPlayerAccess())
                return;

            _interactiveHost.ShowDialog(new SpritePicker(
                this,
                OnSpritesSelected,
                GetConfiguredSprites(AppConfig),
                _interactiveHost.RequestRedraw));
        }

        void OnImageClicked(object dataContext, object sender)
        {
            if (!HasLocalPlayerAccess())
                return;

            OnPickBackgroundClicked(null, sender);
        }

        static void RenderImagePickerHitbox(ControlTemplate control, ControlRenderContext context, List<MySprite> sprites)
        {
        }

        bool HasLocalPlayerAccess()
        {
            var block = _interactiveHost != null ? _interactiveHost.Block as IMyTerminalBlock : null;
            return block != null && block.HasLocalPlayerAccess();
        }

        void OnSpriteSelected(string spriteName)
        {
            OnSpritesSelected(string.IsNullOrWhiteSpace(spriteName)
                ? new string[0]
                : new[] { spriteName });
        }

        void OnSpritesSelected(string[] spriteNames)
        {
            var config = AppConfig;
            if (config == null)
                return;

            var normalized = NormalizeSpriteSelection(spriteNames);
            var backgroundSprite = normalized.Length > 0 ? normalized[0] : string.Empty;

            if (string.Equals(config.BackgroundSprite, backgroundSprite, StringComparison.OrdinalIgnoreCase) &&
                AreSpriteSelectionsEqual(config.SelectedSprites, normalized))
                return;

            config.BackgroundSprite = backgroundSprite;
            config.SelectedSprites = normalized;

            if (_interactiveHost != null && _interactiveHost.Block != null && _interactiveHost.ProviderConfig != null)
                ConfigManager.Sync(_interactiveHost.Block, _interactiveHost.ProviderConfig);
        }

        static string[] NormalizeSpriteSelection(IEnumerable<string> spriteNames)
        {
            if (spriteNames == null)
                return new string[0];

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
