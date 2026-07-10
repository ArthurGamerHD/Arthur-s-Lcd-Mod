#if EXPERIMENTAL
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyFunctionalBlock = Sandbox.ModAPI.IMyFunctionalBlock;
using IMyTextSurface = Sandbox.ModAPI.IMyTextSurface;

namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.MediaPlayerApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class MediaPlayerSurfaceScript : InteractiveSurfaceScript
    {
        static bool lazyInitialized;
        public const string ID = "MediaPlayer";
        public const string TITLE = "Media Player";

        static readonly Dictionary<MediaPlayerAppKey, CachedMediaPlayerApp> AppCache =
            new Dictionary<MediaPlayerAppKey, CachedMediaPlayerApp>();

        MediaPlayerApp _app;

        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override IApp App => _app;
        public override List<Control> InteractiveList => _app == null ? new List<Control>() : _app.VisualChildren as List<Control>;
        public override string Title => _app != null ? _app.Title : base.Title;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

        public MediaPlayerSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            if (lazyInitialized) 
                return;

            LcdModClientComponent.OnUpdateBeforeSimulation += UpdateDetachedApps;
            lazyInitialized = true;
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        public override void Dispose()
        {
            ReleaseCachedApp(IsParentBlockUnavailable());
            _app = null;
            base.Dispose();
        }

        public override void SafeRun()
        {
            base.SafeRun();

            var cached = GetOrCreateCachedApp();
            cached.Attach(this);
            _app = cached.App;

            UpdateViewBox();
            _app.Update();
            RenderSprites();
        }

        public static void UpdateDetachedApps()
        {
            if (AppCache.Count == 0)
                return;

            var stale = new List<MediaPlayerAppKey>();
            foreach (var pair in AppCache)
            {
                var cached = pair.Value;
                if (cached == null || cached.Attached)
                    continue;

                if (cached.IsParentBlockUnavailable())
                {
                    cached.Close();
                    stale.Add(pair.Key);
                    continue;
                }

                try
                {
                    cached.App.Update();
                }
                catch (System.Exception error)
                {
                    ErrorHandlerHelper.LogError(error, cached.Host);
                }
            }

            for (int i = 0; i < stale.Count; i++)
                AppCache.Remove(stale[i]);
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            DrawTitle(sprites);
            if (_app == null)
                DrawLoading(sprites);
            else
                sprites.AddRange(_app.GetSprites());
            return sprites;
        }

        CachedMediaPlayerApp GetOrCreateCachedApp()
        {
            var key = GetCacheKey();
            CachedMediaPlayerApp cached;
            if (!AppCache.TryGetValue(key, out cached) || cached == null)
            {
                cached = new CachedMediaPlayerApp(this, new MediaPlayerApp(this));
                AppCache[key] = cached;
            }

            return cached;
        }

        void ReleaseCachedApp(bool close)
        {
            CachedMediaPlayerApp cached;
            var key = GetCacheKey();
            if (!AppCache.TryGetValue(key, out cached) || cached == null || !ReferenceEquals(cached.App, _app))
                return;

            if (close)
            {
                cached.Close();
                AppCache.Remove(key);
            }
            else
            {
                cached.Detach(this);
            }
        }

        MediaPlayerAppKey GetCacheKey()
        {
            return new MediaPlayerAppKey(Block == null ? 0L : Block.EntityId, ResolveCacheSurfaceIndex());
        }

        int ResolveCacheSurfaceIndex()
        {
            return Config == null ? RotationOrSurfaceIndex : Config.SurfaceIndex;
        }

        bool IsParentBlockUnavailable()
        {
            return IsBlockUnavailable(Block);
        }

        static bool IsBlockUnavailable(IMyCubeBlock block)
        {
            if (block == null || block.MarkedForClose || block.Closed)
                return true;

            var functional = block as IMyFunctionalBlock;
            return functional != null && !functional.IsFunctional;
        }

        struct MediaPlayerAppKey
        {
            readonly long _blockId;
            readonly int _surfaceIndex;

            public MediaPlayerAppKey(long blockId, int surfaceIndex)
            {
                _blockId = blockId;
                _surfaceIndex = surfaceIndex;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is MediaPlayerAppKey))
                    return false;

                var other = (MediaPlayerAppKey)obj;
                return _blockId == other._blockId && _surfaceIndex == other._surfaceIndex;
            }

            public override int GetHashCode()
            {
                return (_blockId.GetHashCode() * 397) ^ _surfaceIndex;
            }
        }

        sealed class CachedMediaPlayerApp
        {
            readonly IMyCubeBlock _block;
            MediaPlayerSurfaceScript _attachedHost;

            public CachedMediaPlayerApp(MediaPlayerSurfaceScript host, MediaPlayerApp app)
            {
                _attachedHost = host;
                _block = host.Block;
                App = app;
            }

            public MediaPlayerApp App { get; }
            public MediaPlayerSurfaceScript Host => _attachedHost;
            public bool Attached => _attachedHost != null;

            public void Attach(MediaPlayerSurfaceScript host)
            {
                _attachedHost = host;
                App.RebindHost(host);
            }

            public void Detach(MediaPlayerSurfaceScript host)
            {
                if (ReferenceEquals(_attachedHost, host))
                    _attachedHost = null;
            }

            public bool IsParentBlockUnavailable()
            {
                return IsBlockUnavailable(_block);
            }

            public void Close()
            {
                App.Close();
                _attachedHost = null;
            }
        }
    }
}
#endif
