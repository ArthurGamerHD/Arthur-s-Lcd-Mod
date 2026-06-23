using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Audio;
using LcdMod.Client.Gui;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using ManagedDoom;
using ManagedDoom.Audio;
using ManagedDoom.SE;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using DoomConfig = ManagedDoom.Config;

namespace LcdMod.Client.Apps
{
    public sealed class ManagedDoomApp : App
    {
        const string DoomWadDisplayPath = "Data/DOOM1.WAD";
        const string DoomWadRootPath = "DOOM1.WAD";
        const string DoomFont = Constants.MOD_PREFIX + "DoomChannel";
        const int SpaceEngineersSimulationRate = 60;

        // The RGB components intentionally remain non-zero while alpha is zero.
        // With Space Engineers' premultiplied source-over LCD blend state this
        // makes each channel pass additive instead of attenuating prior passes.

        static readonly Color RedChannelTint =
            new Color(255, 0, 0, 254);

        static readonly Color GreenChannelTint =
            new Color(0, 255, 0, 128);

        static readonly Color BlueChannelTint =
            new Color(0, 0, 255, 85);

        static byte[] _cachedWadBytes;

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<Control> _children = new List<Control>();
        readonly StringBuilder _frameBuilder = new StringBuilder(320 * 201);
        readonly StringBuilder _middleFrameBuilder = new StringBuilder(320 * 201);
        readonly StringBuilder _highFrameBuilder = new StringBuilder(320 * 201);
        readonly StringBuilder _measureBuilder = new StringBuilder(320);
        char[] _frameRow;
        char[] _middleFrameRow;
        char[] _highFrameRow;

        CommandLineArgs _doomArgs;
        DoomConfig _doomConfig;
        GameContent _content;
        SEVideoToTextSprite _video;
        SESurfaceSound _sound;
        SESurfaceMusic _music;
        SECockpitUserInput _input;
        Doom _doom;
        string _frameText;
        string _middleFrameText;
        string _highFrameText;
        string _statusMessage;
        bool _initFailed;
        bool _completed;
        float _textScale = 1f;
        Vector2 _textPosition;
        Vector2 _measuredSurfaceSize;
        int _measuredWidth;
        int _measuredHeight;
        int _frameTextWidth;
        int _frameTextHeight;
        int _ticAccumulator;
        int _appliedSfxVolume = -1;
        int _appliedMusicVolume = -1;

        public ManagedDoomApp(ScreenConfigInteractive config, IAppHost host) : base(config, host)
        {
        }

        public override IReadOnlyList<Control> Children => _children;

        public override void Update()
        {
            if (_completed)
                return;

            if (_doom == null)
            {
                if (_initFailed)
                    return;

                if (!TryInitialize())
                {
                    _initFailed = true;
                    return;
                }
            }

            ApplyAudioVolumes();

            // Space Engineers calls this once per 60 Hz simulation frame, but
            // Doom's game logic runs at 35 tics per second. Accumulating 35
            // against 60 spreads the updates evenly instead of running Doom at
            // 60 Hz or skipping a large block of consecutive frames. The
            // resulting 12-frame pattern contains seven game tics.
            _ticAccumulator += GameConst.TicRate;

            if (_ticAccumulator >= SpaceEngineersSimulationRate)
            {
                _ticAccumulator -= SpaceEngineersSimulationRate;

                _input.UpdateEvents(_doom);

                if (_doom.Update() == UpdateResult.Completed)
                {
                    _completed = true;
                    _statusMessage = "Doom completed.";
                }
            }

            if (_music != null)
                _music.Update();

            // Render on every Space Engineers frame. ManagedDoom uses this
            // fraction to interpolate between the previous and next 35 Hz tic.
            var frameFrac = Fixed.FromFloat((float)_ticAccumulator / SpaceEngineersSimulationRate);
            _video.Render(_doom, frameFrac);
            BuildFrameText();
            EnsureLayout();
        }

        public override void LayoutChanged()
        {
            _measuredSurfaceSize = Vector2.Zero;
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();

            var viewBox = Host.ViewBox;
            if (viewBox.Width <= 0f || viewBox.Height <= 0f)
                return _sprites;

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                Host.DrawMessage(_sprites, _statusMessage, "Warning", Host.ForegroundColor, AppConfig.Scale);
                return _sprites;
            }

            if (string.IsNullOrEmpty(_frameText))
                return _sprites;

            _sprites.Add(MySprite.CreateClipRect(new Rectangle(
                (int)viewBox.X,
                (int)viewBox.Y,
                (int)viewBox.Width,
                (int)viewBox.Height)));

            // Each glyph contains one 8-bit grayscale channel value. The
            // three zero-alpha tints route those values to R, G and B while
            // leaving the destination multiplier at one for additive assembly.
            AddFrameSprite(_frameText, RedChannelTint);
            AddFrameSprite(_middleFrameText, GreenChannelTint);
            AddFrameSprite(_highFrameText, BlueChannelTint);

            _sprites.Add(MySprite.CreateClearClipRect());
            return _sprites;
        }

        void AddFrameSprite(string text, Color channelTint)
        {
            _sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = _textPosition,
                RotationOrScale = _textScale,
                Color = channelTint,
                Alignment = TextAlignment.LEFT,
                FontId = DoomFont
            });
        }

        public override void Close()
        {
            if (_music != null)
            {
                _music.Dispose();
                _music = null;
            }

            if (_sound != null)
            {
                _sound.Dispose();
                _sound = null;
            }

            if (_content != null)
            {
                _content.Dispose();
                _content = null;
            }

            if (_input != null)
            {
                _input.Dispose();
                _input = null;
            }

            _doom = null;
            _video = null;
            _appliedSfxVolume = -1;
            _appliedMusicVolume = -1;
        }

        void ApplyAudioVolumes()
        {
            var sfxVolume = DoomAudioSettings.GetSfxVolume(AppConfig);
            if (_sound != null && sfxVolume != _appliedSfxVolume)
            {
                _sound.Volume = sfxVolume;
                _appliedSfxVolume = sfxVolume;
            }

            var musicVolume = DoomAudioSettings.GetMusicVolume(AppConfig);
            if (_music != null && musicVolume != _appliedMusicVolume)
            {
                _music.Volume = musicVolume;
                _appliedMusicVolume = musicVolume;
            }
        }

        bool TryInitialize()
        {
            try
            {
                _doomArgs = new CommandLineArgs(new string[]
                {
                    "-skill",
                    "3",
                    "-nomouse",
                    "-nodeh"
                });

                _doomConfig = new DoomConfig();
                _doomConfig.video_highresolution = false;
                // 7 keeps the status bar visible. 8 for borderless with no HUD/status bar.
                _doomConfig.video_gamescreensize = 7;
                _doomConfig.video_fpsscale = 1;
                _doomConfig.video_displaymessage = true;

                _content = GameContent.FromWadBytes("doom1", LoadWad(), _doomArgs);
                _video = new SEVideoToTextSprite(_doomConfig, _content);
                _sound = new SESurfaceSound(_content, Host.Block);
                _music = new SESurfaceMusic(_content, Host.Block);
                _input = new SECockpitUserInput(Host);

                // Apply the persisted surface settings before Doom's
                // constructor starts the title music or any startup SFX.
                _appliedSfxVolume = DoomAudioSettings.GetSfxVolume(AppConfig);
                _appliedMusicVolume = DoomAudioSettings.GetMusicVolume(AppConfig);
                _sound.Volume = _appliedSfxVolume;
                _music.Volume = _appliedMusicVolume;

                _doom = new Doom(
                    _doomArgs,
                    _doomConfig,
                    _content,
                    _video,
                    _sound,
                    _music,
                    _input);

                _ticAccumulator = 0;
                _statusMessage = null;
                return true;
            }
            catch (Exception e)
            {
                Close();
                _statusMessage = "Failed to start Doom: " + e.Message;
                return false;
            }
        }

        static byte[] LoadWad()
        {
            if (_cachedWadBytes != null)
                return _cachedWadBytes;

            if (TryReadWad(DoomWadDisplayPath, out _cachedWadBytes))
                return _cachedWadBytes;

            if (TryReadWad(DoomWadRootPath, out _cachedWadBytes))
                return _cachedWadBytes;

            throw new FileNotFoundException("Missing " + DoomWadDisplayPath + ".");
        }

        static bool TryReadWad(string relativePath, out byte[] bytes)
        {
            bytes = null;

            var utilities = MyAPIGateway.Utilities;
            if (utilities == null)
                return false;

            if (LcdModSessionComponent.ModItem != null)
            {
                MyObjectBuilder_Checkpoint.ModItem modItem = (MyObjectBuilder_Checkpoint.ModItem)LcdModSessionComponent.ModItem;
                if (!utilities.FileExistsInModLocation(relativePath, modItem))
                    return false;

                using (var reader = utilities.ReadBinaryFileInModLocation(relativePath, modItem))
                {
                    var length = reader.BaseStream.Length;
                    if (length <= 0L || length > int.MaxValue)
                        return false;

                    bytes = reader.ReadBytes((int)length);
                }
            }

            if (bytes == null || bytes.Length == 0)
                return false;

            return true;
        }

        void BuildFrameText()
        {
            var width = _video.Width;
            var height = _video.Height;

            _frameText = BuildLayerText(
                _video.RedFrameBuffer,
                _frameBuilder,
                ref _frameRow,
                width,
                height);

            _middleFrameText = BuildLayerText(
                _video.GreenFrameBuffer,
                _middleFrameBuilder,
                ref _middleFrameRow,
                width,
                height);

            _highFrameText = BuildLayerText(
                _video.BlueFrameBuffer,
                _highFrameBuilder,
                ref _highFrameRow,
                width,
                height);

            _frameTextWidth = width;
            _frameTextHeight = height;
        }

        static string BuildLayerText(
            char[] buffer,
            StringBuilder builder,
            ref char[] row,
            int width,
            int height)
        {
            builder.Clear();
            builder.EnsureCapacity(buffer.Length + height);

            if (row == null || row.Length != width)
                row = new char[width];

            // DrawScreen stores pixels by column: index = height * x + y.
            // MySprite multiline text expects each row to be contiguous, so
            // transpose each color plane into row-major order.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    row[x] = buffer[x * height + y];

                builder.Append(row, 0, width);
                if (y + 1 < height)
                    builder.Append('\n');
            }

            return builder.ToString();
        }

        void EnsureLayout()
        {
            if (_video == null || Host.Surface == null)
                return;

            var surfaceSize = Host.Surface.SurfaceSize;
            if (_measuredSurfaceSize == surfaceSize &&
                _measuredWidth == _frameTextWidth &&
                _measuredHeight == _frameTextHeight)
                return;

            var viewBox = Host.ViewBox;
            
            var frameSize = MeasureFrameSize();
            float frameWidth = Math.Max(1f, frameSize.X);
            float frameHeight = Math.Max(1f, frameSize.Y);
            
            float availableWidth = Math.Max(1f, viewBox.Width);
            float availableHeight = Math.Max(1f, viewBox.Height);
            _textScale = Math.Min(availableWidth / frameWidth, availableHeight / frameHeight);
            _textScale = Math.Max(0.01f, _textScale);

            var scaledSize = new Vector2(frameWidth, frameHeight) * _textScale;
            _textPosition = new Vector2(
                viewBox.X + Math.Max(1f, (viewBox.Width - scaledSize.X) * 0.5f),
                viewBox.Y + Math.Max(1f, (viewBox.Height - scaledSize.Y) * 0.5f));

            _measuredSurfaceSize = surfaceSize;
            _measuredWidth = _frameTextWidth;
            _measuredHeight = _frameTextHeight;
        }

        Vector2 MeasureFrameSize()
        {
            var surface = Host.Surface;
            if (surface != null && _frameBuilder.Length > 0)
            {
                var measured = surface.MeasureStringInPixels(_frameBuilder, DoomFont, 1f);
                if (measured.X > 0f && measured.Y > 0f)
                    return measured;
            }

            // Conservative fallback for surfaces that cannot measure the full
            // multiline private-use glyph string.
            var charSize = MeasureCharSize();
            return new Vector2(
                Math.Max(1f, charSize.X * Math.Max(1, _frameTextWidth)),
                Math.Max(1f, charSize.Y * Math.Max(1, _frameTextHeight)));
        }

        Vector2 MeasureCharSize()
        {
            var surface = Host.Surface;
            if (surface == null)
                return new Vector2(1f, 1f);

            _measureBuilder.Clear();
            _measureBuilder.Append('M');
            var single = surface.MeasureStringInPixels(_measureBuilder, DoomFont, 1f);

            _measureBuilder.Clear();
            for (int i = 0; i < 80; i++)
                _measureBuilder.Append('M');
            var row = surface.MeasureStringInPixels(_measureBuilder, DoomFont, 1f);

            float width = row.X > 0f ? row.X / 80f : single.X;
            float height = single.Y;

            if (width <= 0f)
                width = 1f;
            if (height <= 0f)
                height = 1f;

            return new Vector2(width, height);
        }
    }
}
