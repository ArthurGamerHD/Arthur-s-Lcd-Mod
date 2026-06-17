using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Styling.Styles;
using LcdMod.Common.Config.Models;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps.Abstract
{
    public abstract class App : Control, IApp, ITextSurfaceProvider
    {
        readonly List<Control> _rootControls = new List<Control>();
        StyleTree _styles;
        ResourceTree _resources;
        Dictionary<string, Color> _theme;
        Color _themeHeaderColor;
        bool _themeDark;
        float _themeLayoutScale;
        float _themeFontScale;
        bool _hasTheme;

        protected App(ScreenConfigInteractive config, IAppHost host)
        {
            AppConfig = config;
            Host = host;
            _styles = DefaultStyleBuilder.Build();
        }

        protected IAppHost Host { get; private set; }
        protected ScreenConfigInteractive AppConfig { get; private set; }

        public override StyleTree Styles => _styles;

        public override ResourceTree Resources
        {
            get
            {
                EnsureResources();
                return _resources;
            }
        }

        protected void ClearDirtyAfterRender()
        {
            _isDirty = false;
        }

        protected T AddChild<T>(T control)
            where T : ControlTemplate
        {
            if (control == null)
                return null;

            if (!_rootControls.Contains(control))
                _rootControls.Add(control);

            control.SetStyleParent(this);
            MarkDirty();
            return control;
        }

        public Sandbox.ModAPI.Ingame.IMyTextSurface TextSurface
        {
            get { return Host != null ? Host.Surface : null; }
        }


        public abstract void Update();

        public virtual void LayoutChanged()
        {
        }

        public abstract List<MySprite> GetSprites();
        

        public virtual bool HasVisibleItems()
        {
            return true;
        }

        public virtual void OnMouseScroll(int delta, ref bool handled)
        {
        }

        public virtual void Close()
        {
        }

        protected Color GetHeaderColor()
        {
            var colorable = AppConfig;
            if (colorable != null)
                return colorable.HeaderColor;

            return Host != null ? Host.ForegroundColor : Color.White;
        }

        protected Color ResolveResource(ResourceKey<Color> key, Color fallback)
        {
            Color value;
            return ScopedResourceResolver.TryResolve(this, key, out value) ? value : fallback;
        }

        protected Color ResolveRole(string role, Color fallback)
        {
            return ResolveResource(ThemeResources.FromThemeRole(role), fallback);
        }

        void EnsureResources()
        {
            var headerColor = GetHeaderColor();
            bool dark = ShouldUseDarkTheme();
            float layoutScale = GetLayoutScaleResourceValue();
            float fontScale = GetFontScaleResourceValue();

            if (!_hasTheme ||
                !headerColor.Equals(_themeHeaderColor) ||
                dark != _themeDark ||
                !layoutScale.Equals(_themeLayoutScale) ||
                !fontScale.Equals(_themeFontScale))
            {
                _theme = headerColor.ToTheme(dark);
                _resources = ThemeResourceBuilder.FromThemeDictionary(_theme);
                _resources.Set(ThemeResources.LayoutScale, layoutScale);
                _resources.Set(ThemeResources.FontScale, fontScale);
                _themeHeaderColor = headerColor;
                _themeDark = dark;
                _themeLayoutScale = layoutScale;
                _themeFontScale = fontScale;
                _hasTheme = true;
                MarkDirty();
            }
        }

        float GetLayoutScaleResourceValue()
        {
            float scale = AppConfig != null ? AppConfig.Scale : Host != null && Host.Config != null ? Host.Config.Scale : 1f;
            return scale > 0f ? scale : 1f;
        }

        float GetFontScaleResourceValue()
        {
            float fontScale = Host != null && Host.Surface != null ? Host.Surface.FontSize : 1f;
            return fontScale > 0f ? fontScale : 1f;
        }

        bool ShouldUseDarkTheme()
        {
            var backgroundColor = Host?.ForegroundColor ?? Color.White;
            return backgroundColor.ContrastRatio(Color.Black) >=
                   backgroundColor.ContrastRatio(Color.White);
        }
    }
}
