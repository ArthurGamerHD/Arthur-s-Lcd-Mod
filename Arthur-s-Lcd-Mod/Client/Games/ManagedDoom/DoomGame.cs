using System;
using System.Collections.Generic;
using Acrux.Core.Input;
using LcdMod.Client.Apps;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.SurfaceScripts;
using ManagedDoom.SE;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Games.ManagedDoom
{
    /// <summary>The Doom engine hosted by the shared Games surface and its retained view.</summary>
    internal sealed class DoomGame : IGame, IDisposable
    {
        static readonly IReadOnlyList<GameRegion> NoRegions = new GameRegion[0];
        readonly GameSurfaceScript _script;
        ManagedDoomApp _engine;
        readonly GameThemeContext _theme;
        RectangleF _frameBounds;
        bool _disposed;
        bool _failureDialogShown;

        public DoomGame(GameSurfaceScript script)
        {
            _script = script;
            _theme = new GameThemeContext(script);
            _engine = new ManagedDoomApp(script);
            script.SetGlobalMenu(new GlobalMenuEntry("DOOM", new List<GlobalMenuEntry>
            {
                new GlobalMenuEntry("Play", (a, b) => OpenInput())
            }));
        }

        public GameSurfaceScript.GameEnum Id => GameSurfaceScript.GameEnum.Doom;
        public ManagedDoomApp Engine => _engine;
        public IVisualStyleScope StyleParent => _theme.StyleParent;
        public ResourceTree Resources => _theme.Resources;
        public bool IsDirty => _theme.IsDirty;
        public void MarkDirty() => _theme.MarkDirty();
        public IReadOnlyList<GameRegion> Regions => NoRegions;

        internal void SetFrameBounds(RectangleF bounds) => _frameBounds = bounds;

        internal bool TryMapPointer(Vector2 position, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (!_frameBounds.Contains(position) || _frameBounds.Width <= 0f || _frameBounds.Height <= 0f ||
                _engine.FrameWidth <= 0 || _engine.FrameHeight <= 0)
                return false;
            x = Math.Min(319, (int)((position.X - _frameBounds.X) * 320f / _frameBounds.Width));
            y = Math.Min(199, (int)((position.Y - _frameBounds.Y) * 200f / _frameBounds.Height));
            return true;
        }

        public void Update()
        {
            _engine.Update();
            if (_failureDialogShown || string.IsNullOrEmpty(_engine.FailureMessage))
                return;

            DoomInputScreen.CloseFor(this);
            _engine.Close();
            _failureDialogShown = _script.ShowDoomFailure(_engine.FailureMessage, Restart);
        }

        void Restart()
        {
            if (_disposed)
                return;
            DoomInputScreen.CloseFor(this);
            _engine.Close();
            _engine = new ManagedDoomApp(_script);
            _frameBounds = default(RectangleF);
            _failureDialogShown = false;
            _script.RequestRedraw();
        }
        public void LayoutChanged() => _engine.LayoutChanged();
        public List<MySprite> GetSprites() => _engine.GetSprites();
        public bool HasVisibleItems() => true;
        public void OnMouseScroll(int delta, ref bool handled) { }
        public void Save() { DoomUserProfileStorage.FlushPending(); }
        public void Load() { }
        public void PointerMoved(Vector2? position) { }
        public PointerCursor CursorAt(Vector2 position) => PointerCursor.Hand;

        public void PointerClicked(GamePointerButton button, Vector2 position)
        {
            if (button == GamePointerButton.Primary)
                OpenInput();
        }

        void OpenInput()
        {
            if (!_disposed && _engine.Input != null)
                DoomInputScreen.OpenFor(_script, this, _engine.Input);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            DoomInputScreen.CloseFor(this);
            _engine.Close();
        }
    }
}
