using System;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using VRageMath;

namespace LcdMod.Client.Gui.Styling
{
    /// <summary>
    /// The theme an app gets on a surface, built from the IDE preview's settings instead of a block's: the ADK
    /// preview (Acrux UI, Tools/AcruxPreviewHost) sets these properties and uses this as the previewed view's
    /// data context, so views and styles resolve the same resources as in game. Named by
    /// <c>AcruxPreviewDesignResources</c> in the project file; never created by the mod itself (a *.AcruxDesign.cs file:
    /// compiled for the preview, not packed by MDK).
    /// </summary>
    public sealed class DesignThemeResources : Acrux.Styling.IResourceProvider
    {
        ResourceTree _resources;
        Color _foreground = new Color(197, 237, 255);
        Color _seed = FactionHelperCommon.DefaultColor;
        string _fontId = "White";
        float _fontScale = 1f;

        public event Action<string> ResourceChanged;

        /// <summary>The surface's background; the theme does not depend on it (the surface paints it).</summary>
        public Color Background { get; set; }

        /// <summary>The surface's text colour (the block's font colour).</summary>
        public Color Foreground
        {
            get { return _foreground; }
            set { _foreground = value; Invalidate(); }
        }

        /// <summary>The header colour the theme is generated from (the owner faction's colour by default).</summary>
        public Color Seed
        {
            get { return _seed; }
            set { _seed = value; Invalidate(); }
        }

        public string FontId
        {
            get { return _fontId; }
            set { _fontId = string.IsNullOrEmpty(value) ? "White" : value; Invalidate(); }
        }

        /// <summary>The surface's font size.</summary>
        public float FontScale
        {
            get { return _fontScale; }
            set { _fontScale = value > 0f ? value : 1f; Invalidate(); }
        }

        // What the mod's session installs in game (ClientComponent), which the preview never starts: dialogs' texts.
        static DesignThemeResources()
        {
            LcdMod.Client.Apps.Dialogs.DialogViewModel.Localize = LocHelper.GetLoc;
        }

        /// <summary>The LCD textures the mod registers at run time that design-time data shows (block icons).</summary>
        public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>> DesignTextures()
        {
            return DesignBlockSprites.Registrations;
        }

        // The preview's default theme, for design-time data that needs theme colours (as apps resolve them).
        internal static readonly DesignThemeResources Default = new DesignThemeResources();

        // A theme colour, as an app's ResolveResource gives it.
        internal Color Resolve(ResourceKey<Color> key, Color fallback)
        {
            Color value;
            return Resources.TryGet(key, out value) ? value : fallback;
        }

        System.Collections.Generic.Dictionary<string, object> _appResources;

        public bool TryGetResource<T>(string key, out T value)
        {
            // What the app shell (AppView) adds above every app's views in game.
            if (_appResources == null)
            {
                _appResources = new System.Collections.Generic.Dictionary<string, object>(StringComparer.Ordinal);
                LcdMod.Client.Apps.Views.AppViewResources.AddTo((name, resource) => _appResources[name] = resource);
            }
            object appResource;
            if (key != null && _appResources.TryGetValue(key, out appResource) && appResource is T)
            {
                value = (T)appResource;
                return true;
            }

            string icon = FactionHelper.GetIcon((VRage.Game.ModAPI.IMyFaction)null);
            if (string.Equals(key, "factionIcon", StringComparison.Ordinal) && typeof(T) == typeof(string))
            {
                value = (T)(object)icon;
                return true;
            }
            if (string.Equals(key, "factionIconMask", StringComparison.Ordinal))
            {
                object mask = new Acrux.Styling.ImageBrush(icon) { Stretch = Acrux.Styling.Stretch.Uniform };
                if (mask is T)
                {
                    value = (T)mask;
                    return true;
                }
            }

            // Building the theme first also registers the ThemeResources keys, which the lookup below needs.
            ResourceTree resources = Resources;
            ResourceKey<T> resourceKey;
            if (ResourceKey.TryGet(key, out resourceKey))
                return resources.TryGet(resourceKey, out value);
            value = default(T);
            return false;
        }

        ResourceTree Resources
        {
            get
            {
                if (_resources == null)
                    _resources = ThemeResourceBuilder.ForSurface(_seed, _foreground,
                        ColorConfigComponentExtensions.ResolveWarningColor(null),
                        ColorConfigComponentExtensions.ResolveErrorColor(null),
                        ThemeResourceBuilder.UsesDarkTheme(_foreground),
                        1f, _fontScale, new InteractiveConfigComponent().AutoScrollStep, _fontId);
                return _resources;
            }
        }

        void Invalidate()
        {
            _resources = null;
            Action<string> handlers = ResourceChanged;
            if (handlers != null)
                handlers(null);
        }
    }
}
