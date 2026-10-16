using System;
using Adk.Gui.Lcd;
using Adk.Gui.Rendering;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.Apps.Maps
{
    /// <summary>
    /// Borrows an upload buffer on the UI thread and publishes it from the completion
    /// callback. Workers only write the borrowed pixels. The previous texture remains
    /// visible throughout the request, and no second request is queued while busy.
    /// </summary>
    internal sealed class AsyncMapTexture : IDynamicTexture
    {
        readonly IMyGeneratedTexture _texture;
        byte[] _pixels;
        bool _pending;
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
        public bool IsPending => _pending;

        public bool Request(IAsyncMapSurface surface, Action<bool> completed)
        {
            if (!IsAvailable || _pending)
                return false;
            if (_pixels == null)
                _pixels = _texture.BeginUpdate();
            if (_pixels == null)
                return false;

            _pending = true;
            try
            {
                bool accepted = surface.RequestTexture(new PixelCanvas(_pixels, Size.X, Size.Y), error =>
                {
                    _pending = false;
                    if (_disposed)
                    {
                        Release();
                        return;
                    }

                    bool published = error == null && IsAvailable;
                    if (published)
                    {
                        _texture.EndUpdate(_pixels);
                        _pixels = null; // Ownership has returned to the game renderer.
                        LcdDynamicTexture.NotifyContentChanged(_texture.Name);
                    }
                    // On failure retain the borrowed buffer for the next attempt.
                    // Its partial contents are never uploaded over the last good frame.
                    completed(published);
                });
                if (!accepted)
                    _pending = false;
                return accepted;
            }
            catch
            {
                _pending = false;
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
            if (!_pending)
                Release();
        }

        void Release()
        {
            _pixels = null;
            if (_texture == null || !_texture.IsValid)
                return;
            LcdDynamicTexture.NotifyDisposed(_texture.Name);
            _texture.Dispose();
        }
    }
}
