using System;
using Acrux.Lcd;
using Acrux.Rendering;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.Apps.Maps
{
    /// <summary>
    /// Borrows an upload buffer per request on the UI thread and publishes it from the completion
    /// callback. Workers only write the borrowed pixels. The previous texture remains visible
    /// throughout. Two requests may be in flight (ping-pong: one painting while the next starts),
    /// each with its own buffer; completions arrive in request order, so frames upload in order.
    /// </summary>
    internal sealed class AsyncMapTexture : IDynamicTexture
    {
        const int MAXIMUM_PENDING = 2;
        readonly IMyGeneratedTexture _texture;
        // A borrowed buffer whose request failed or was refused, kept for the next request.
        byte[] _spare;
        int _pending;
        bool _disposed;

        public AsyncMapTexture(Vector2I size)
        {
            var textures = MyAPIGateway.GeneratedTextures;
            if (textures.IsSupported)
                _texture = textures.Create("CartographyMap", size, MyGeneratedTextureFormat.Srgb);
        }

        public bool IsAvailable => !_disposed && _texture != null && _texture.IsValid;
        public Vector2I Size => IsAvailable ? _texture.Size : Vector2I.Zero;
        // Both the LCD and screen-space backends accept generated texture names.
        public TextureSource Source => IsAvailable ? TextureSource.FromKey(_texture.Name) : default(TextureSource);
        public bool IsPending => _pending > 0;

        public bool IsFull => _pending >= MAXIMUM_PENDING;

        public bool Request(IAsyncMapSurface surface, Action<bool> completed)
        {
            if (!IsAvailable || IsFull)
                return false;
            byte[] pixels = _spare ?? _texture.BeginUpdate();
            _spare = null;
            if (pixels == null)
                return false;

            _pending++;
            try
            {
                bool accepted = surface.RequestTexture(new PixelCanvas(pixels, Size.X, Size.Y), error =>
                {
                    _pending--;
                    if (_disposed)
                    {
                        if (_pending == 0)
                            Release();
                        return;
                    }

                    bool published = error == null && IsAvailable;
                    if (published)
                    {
                        _texture.EndUpdate(pixels); // Ownership returns to the game renderer.
                        LcdDynamicTexture.NotifyContentChanged(_texture.Name);
                    }
                    else
                    {
                        // Its partial contents are never uploaded over the last good frame.
                        _spare = pixels;
                    }
                    completed(published);
                });
                if (!accepted)
                {
                    _pending--;
                    _spare = pixels;
                }
                return accepted;
            }
            catch
            {
                _pending--;
                _spare = pixels;
                throw;
            }
        }

        public bool Update(Action<PixelCanvas> paint)
        {
            // CartographyMapView uses Request; never run a synchronous painter here.
            return false;
        }

        public void Dispose()
        {
            _disposed = true;
            // A worker may still own the borrowed buffer. Its callback releases the
            // texture after it stops writing; disposal must never wait for that work.
            if (_pending == 0)
                Release();
        }

        void Release()
        {
            _spare = null;
            if (_texture == null || !_texture.IsValid)
                return;
            LcdDynamicTexture.NotifyDisposed(_texture.Name);
            _texture.Dispose();
        }
    }
}
