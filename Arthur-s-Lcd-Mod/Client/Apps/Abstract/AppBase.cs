using System;
using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Styling.Styles;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Models;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps.Abstract
{
    public abstract class App : Control, IApp, ITextSurfaceProvider, ITextStyleProvider
    {
        readonly List<Control> _rootControls = new List<Control>();
        StyleTree _styles;
        ResourceTree _resources;
        Dictionary<string, Color> _theme;
        Color _themeHeaderColor;
        Color _themeForegroundColor;
        bool _themeDark;
        float _themeLayoutScale;
        float _themeFontScale;
        float _themeAutoScrollSecondsPerStep;
        string _themeTextFont;
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

        public Sandbox.ModAPI.Ingame.IMyTextSurface TextSurface => Host?.Surface;

        public string TextFont
        {
            get
            {
                string value;
                return ScopedResourceResolver.TryResolve(this, ThemeResources.TextFont, out value) &&
                       !string.IsNullOrEmpty(value)
                    ? value
                    : "White";
            }
        }

        string ITextStyleProvider.ResolvedTextFont => TextFont;

        protected Vector2 MeasureText(string text, float scale)
        {
            var surface = TextSurface;
            return surface != null ? FormatingHelper.GetSizeInPixel(text, this, scale, surface) : Vector2.Zero;
        }

        protected float MeasureLineHeight(float scale, string probe = "Ag")
        {
            var surface = TextSurface;
            return surface != null ? FormatingHelper.LineHeight(scale, this, surface, probe) : 0f;
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

            return Host?.ForegroundColor ?? Color.White;
        }

        protected Color GetForegroundColor()
        {
            return Host?.ForegroundColor ?? Color.White;
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
            var foregroundColor = GetForegroundColor();
            bool dark = ShouldUseDarkTheme();
            float layoutScale = GetLayoutScaleResourceValue();
            float fontScale = GetFontScaleResourceValue();
            string textFont = GetTextFontResourceValue();

            if (!_hasTheme ||
                !headerColor.Equals(_themeHeaderColor) ||
                !foregroundColor.Equals(_themeForegroundColor) ||
                dark != _themeDark ||
                !layoutScale.Equals(_themeLayoutScale) ||
                !fontScale.Equals(_themeFontScale) ||
                ! AppConfig.AutoScrollStep.Equals(_themeAutoScrollSecondsPerStep) ||
                !string.Equals(textFont, _themeTextFont, StringComparison.Ordinal))
            {
                _theme = headerColor.ToTheme(dark);
                _resources = ThemeResourceBuilder.FromThemeDictionary(_theme);
                _resources.Set(ThemeResources.FontColor, foregroundColor);
                _resources.Set(ThemeResources.LayoutScale, layoutScale);
                _resources.Set(ThemeResources.FontScale, fontScale);
                _resources.Set(ThemeResources.AutoScrollSecondsPerStep, AppConfig.AutoScrollStep);
                _resources.Set(ThemeResources.TextFont, textFont);
                _themeHeaderColor = headerColor;
                _themeForegroundColor = foregroundColor;
                _themeDark = dark;
                _themeLayoutScale = layoutScale;
                _themeFontScale = fontScale;
                _themeAutoScrollSecondsPerStep = AppConfig.AutoScrollStep;
                _themeTextFont = textFont;
                _hasTheme = true;
                MarkDirty();
            }
        }

        float GetLayoutScaleResourceValue()
        {
            float scale = AppConfig?.Scale ?? (Host != null && Host.Config != null ? Host.Config.Scale : 1f);
            return scale > 0f ? scale : 1f;
        }

        float GetFontScaleResourceValue()
        {
            float fontScale = Host != null && Host.Surface != null ? Host.Surface.FontSize : 1f;
            return fontScale > 0f ? fontScale : 1f;
        }

        string GetTextFontResourceValue()
        {
            string font = Host != null && Host.Surface != null ? Host.Surface.Font : null;
            return string.IsNullOrEmpty(font) ? "White" : font;
        }

        bool ShouldUseDarkTheme()
        {
            var backgroundColor = Host?.ForegroundColor ?? Color.White;
            return backgroundColor.ContrastRatio(Color.Black) >=
                   backgroundColor.ContrastRatio(Color.White);
        }
    }
}
