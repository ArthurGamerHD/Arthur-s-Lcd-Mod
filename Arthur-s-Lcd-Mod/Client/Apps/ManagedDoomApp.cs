using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using ManagedDoom;
using ManagedDoom.Audio;
using ManagedDoom.SE;
using ManagedDoom.UserInput;
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
        const string DoomFont = Constants.MOD_PREFIX + "Monospace";
        const int SpaceEngineersSimulationRate = 60;
        const byte MiddleLayerAlpha = 227; // 8 / 9
        const byte HighLayerAlpha = 224;   // 448 / 511

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

            // Space Engineers calls this once per 60 Hz simulation frame, but
            // Doom's game logic runs at 35 tics per second. Accumulating 35
            // against 60 spreads the updates evenly instead of running Doom at
            // 60 Hz or skipping a large block of consecutive frames. The
            // resulting 12-frame pattern contains seven game tics.
            _ticAccumulator += GameConst.TicRate;

            if (_ticAccumulator >= SpaceEngineersSimulationRate)
            {
                _ticAccumulator -= SpaceEngineersSimulationRate;

                if (_doom.Update() == UpdateResult.Completed)
                {
                    _completed = true;
                    _statusMessage = "Doom completed.";
                }
            }

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

            // The font encodes one RGB333 digit per glyph. Rendering three
            // base-8 digit planes with these source-over alpha weights
            // reconstructs approximately RGB888 while keeping the same font.
            AddFrameSprite(_frameText, 255);
            AddFrameSprite(_middleFrameText, MiddleLayerAlpha);
            AddFrameSprite(_highFrameText, HighLayerAlpha);

            _sprites.Add(MySprite.CreateClearClipRect());
            return _sprites;
        }

        void AddFrameSprite(string text, byte alpha)
        {
            _sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = _textPosition,
                RotationOrScale = _textScale,
                // The sprite renderer uses premultiplied-alpha blending for
                // FontDataPA fonts. Premultiply the white tint by the layer
                // alpha; using (255,255,255,alpha) makes the RGB contribution
                // remain full strength and causes colored halos/tints.
                Color = new Color(alpha, alpha, alpha, alpha),
                Alignment = TextAlignment.LEFT,
                FontId = DoomFont
            });
        }

        public override void Close()
        {
            if (_content != null)
            {
                _content.Dispose();
                _content = null;
            }

            _doom = null;
            _video = null;
        }

        bool TryInitialize()
        {
            try
            {
                _doomArgs = new CommandLineArgs(new string[]
                {
                    "-skill",
                    "3",
                    "-nosound",
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
                _doom = new Doom(
                    _doomArgs,
                    _doomConfig,
                    _content,
                    _video,
                    NullSound.GetInstance(),
                    NullMusic.GetInstance(),
                    NullUserInput.GetInstance());

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
                _video.LowFrameBuffer,
                _frameBuilder,
                ref _frameRow,
                width,
                height);

            _middleFrameText = BuildLayerText(
                _video.MiddleFrameBuffer,
                _middleFrameBuilder,
                ref _middleFrameRow,
                width,
                height);

            _highFrameText = BuildLayerText(
                _video.HighFrameBuffer,
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
