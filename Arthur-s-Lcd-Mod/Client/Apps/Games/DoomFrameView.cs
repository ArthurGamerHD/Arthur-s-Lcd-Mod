using System;
using Acrux.Core;
using Acrux.Rendering;
using LcdMod.Client.Apps;
using LcdMod.Client.Games.ManagedDoom;
using VRageMath;

namespace LcdMod.Client.Apps.Games
{
    /// <summary>Draws the latest Doom framebuffer as a single generated texture behind the Games input layer.</summary>
    internal sealed class DoomFrameView : VisualElement
    {
        IDynamicTexture _texture;
        IDynamicTextureFactory _factory;
        DoomGame _game;
        ManagedDoomApp _engine;
        int _paintedVersion = -1;

        protected override void DrawSelf(RenderContext context)
        {
            var model = DataContext as GamesViewModel;
            var game = model == null ? null : model.Game as DoomGame;
            var currentEngine = game == null ? null : game.Engine;
            if (!ReferenceEquals(game, _game) || !ReferenceEquals(currentEngine, _engine))
            {
                Release();
                _game = game;
                _engine = currentEngine;
            }
            if (game != null)
                context.MarkVolatile();
            if (game == null || Bounds.Width <= 0f || Bounds.Height <= 0f ||
                !string.IsNullOrEmpty(game.Engine.StatusMessage))
                return;

            var engine = currentEngine;
            int width = engine.FrameWidth;
            int height = engine.FrameHeight;
            byte[] frame = engine.DisplayFrame;
            if (frame == null || width <= 0 || height <= 0)
                return;

            IDynamicTextureFactory factory = context.DynamicTextures;
            if (factory == null)
                return;
            Vector2I size = factory.FitToSupportedSize(new Vector2I(width, height));
            if (size.X <= 0 || size.Y <= 0)
                return;

            bool created = false;
            if (_texture == null || !_texture.IsAvailable || !ReferenceEquals(factory, _factory) || _texture.Size != size)
            {
                Release();
                _texture = factory.Create("DoomFrame", size);
                _factory = factory;
                created = true;
            }
            if (_texture == null || !_texture.IsAvailable)
                return;

            if (_paintedVersion != engine.DisplayFrameVersion)
            {
                if (_texture.Update(canvas => Paint(canvas, frame, width, height)))
                    _paintedVersion = engine.DisplayFrameVersion;
            }

            float scale = Math.Min(Bounds.Width / width, Bounds.Height / height);
            Vector2 drawnSize = new Vector2(width * scale, height * scale);
            var frameBounds = new RectangleF(Bounds.Center - drawnSize * .5f, drawnSize);
            game.SetFrameBounds(frameBounds);
            context.DrawTexture(_texture.Source, frameBounds, Color.White);
            if (created)
                context.MarkVolatile();
        }

        static void Paint(PixelCanvas canvas, byte[] frame, int width, int height)
        {
            // The software renderer stores pixels in columns; generated textures use rows.
            for (int y = 0; y < canvas.Height; y++)
            for (int x = 0; x < canvas.Width; x++)
            {
                int sourceX = x * width / canvas.Width;
                int sourceY = y * height / canvas.Height;
                int offset = (sourceX * height + sourceY) * 4;
                canvas.SetPixel(x, y, new Color(frame[offset], frame[offset + 1], frame[offset + 2], frame[offset + 3]));
            }
        }

        void Release()
        {
            if (_texture != null)
                _texture.Dispose();
            _texture = null;
            _factory = null;
            _paintedVersion = -1;
        }

        public override void Dispose()
        {
            Release();
            base.Dispose();
        }
    }
}
