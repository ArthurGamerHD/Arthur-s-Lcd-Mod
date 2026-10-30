using System;
using Adk.Video.Apng;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Images
{
    /// <summary>
    /// Plays one animated grid image into a generated texture shared by every screen showing it. Frames
    /// follow the session clock, so all screens and players see the same frame; each one is decoded on a
    /// worker, one at a time, and uploaded on the game thread. Game-thread only.
    /// </summary>
    internal sealed class GridImageAnimation : IDisposable
    {
        readonly string _id;
        readonly ApngVideo _video;
        ApngFrameDecoder _decoder;
        FrameTexture _texture;
        byte[] _pixels;
        bool _decoding;
        bool _disposed;
        int _shownFrame = -1;

        public GridImageAnimation(string id, ApngVideo video)
        {
            _id = id;
            _video = video;
        }

        public int Width => _video.Width;
        public int Height => _video.Height;

        /// <summary>Game frame of the last <see cref="Advance"/>, so unused animations can release their texture.</summary>
        public long LastUsedFrame { get; private set; }

        /// <summary>Incremented whenever a new frame reaches the texture.</summary>
        public int Version { get; private set; }

        /// <summary>The texture name to draw, or null until the first frame is uploaded.</summary>
        public string TextureName => _shownFrame >= 0 && _texture != null ? _texture.Name : null;

        /// <summary>Starts decoding the frame showing at <paramref name="seconds"/> when it is not on the texture yet.</summary>
        public void Advance(double seconds)
        {
            if (_disposed)
                return;

            LastUsedFrame = MyAPIGateway.Session.GameplayFrameCounter;
            if (_texture == null)
            {
                _texture = new FrameTexture("LcdImgAnim", _video.Width, _video.Height);
                if (!_texture.IsAvailable)
                    return;

                _pixels = new byte[_texture.Size.X * _texture.Size.Y * 4];
                _decoder = _video.CreateDecoder();
            }

            int frame = FrameAt(seconds);
            if (frame == _shownFrame || _decoding || !_texture.IsAvailable)
                return;

            _decoding = true;
            string error = null;
            MyAPIGateway.Parallel.Start(() => Decode(frame, ref error), () => Complete(frame, error));
        }

        void Decode(int frame, ref string error)
        {
            try
            {
                _decoder.SeekTo(frame);
                FrameTexture.Scale(_decoder.Canvas, _video.Width, _video.Height,
                    _pixels, _texture.Size.X, _texture.Size.Y, true);
            }
            catch (Exception e)
            {
                error = e.Message;
            }
        }

        void Complete(int frame, string error)
        {
            _decoding = false;
            if (_disposed)
                return;

            if (error != null)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Animated image " + _id + " frame " + frame + " failed: " + error);
                return;
            }

            if (_texture.Upload(_pixels))
            {
                _shownFrame = frame;
                Version++;
            }
        }

        int FrameAt(double seconds)
        {
            double duration = _video.Duration;
            if (_video.FrameCount <= 1 || duration <= 0)
                return 0;

            if (_video.PlayCount > 0 && seconds >= duration * _video.PlayCount)
                return _video.FrameCount - 1;

            return _video.FrameAtTime(seconds % duration);
        }

        /// <summary>Frees the texture; the next <see cref="Advance"/> creates it again.</summary>
        public void Release()
        {
            if (_decoding || _texture == null)
                return;

            _texture.Dispose();
            _texture = null;
            _pixels = null;
            _decoder = null;
            _shownFrame = -1;
        }

        public void Dispose()
        {
            _disposed = true;
            if (_texture != null)
                _texture.Dispose();
            _texture = null;
        }
    }
}
