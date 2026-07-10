using System;
using System.Collections.Generic;
using LcdMod.Client.Animation;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Styling.Styles;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;

namespace LcdMod.Client.Apps.Abstract
{
    [ConfigComponent(Constants.GENERAL, typeof(GeneralConfigComponent), PropertyName = "GeneralComponent")]
    [ConfigComponent(Constants.COLORS, typeof(ColorConfigComponent), PropertyName = "ColorComponent")]
    [ConfigComponent(Constants.INTERACTION, typeof(InteractiveConfigComponent), PropertyName = "InteractionComponent")]
    public abstract partial class App : Control, IApp, ITextSurfaceProvider, ITextStyleProvider
    {
        readonly List<Control> _logicalChildren = new List<Control>();
        AnimationController _animationController;
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

        protected App(IAppHost host)
        {
            Host = host;
            if (Host == null)
                throw new ArgumentNullException(nameof(host));

            _animationController = Host.Animations;
            _styles = DefaultStyleBuilder.Build();
        }

        protected IAppHost Host { get; private set; }
        protected IComponentContainer Config => Host.Config;
        internal override AnimationController AnimationController => _animationController;
        public override IReadOnlyList<Control> LogicalChildren => _logicalChildren;
        public override StyleTree Styles => _styles;

        protected void RebindAppHost(IAppHost host)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            Host = host;
            _animationController = Host.Animations;
        }

        public override bool IsDirty
        {
            get
            {
                if (_isDirty)
                    return true;

                for (int i = 0; i < _logicalChildren.Count; i++)
                {
                    if (IsVisibleTreeDirty(_logicalChildren[i]))
                        return true;
                }

                return false;
            }
        }

        protected static bool IsVisibleTreeDirty(Control control)
        {
            if (control == null || !control.Visible)
                return false;

            if (control._isDirty)
                return true;

            var children = control.LogicalChildren;
            if (children == null)
                return false;

            for (int i = 0; i < children.Count; i++)
            {
                if (IsVisibleTreeDirty(children[i]))
                    return true;
            }

            return false;
        }

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

        protected T AddLogicalChild<T>(T control)
            where T : ControlTemplate
        {
            if (control == null)
                return null;

            var previousOwner = control.StyleParent as App;
            if (previousOwner != null && !ReferenceEquals(previousOwner, this))
                previousOwner.RemoveLogicalChild(control);

            if (!_logicalChildren.Contains(control))
                _logicalChildren.Add(control);

            control.SetStyleParent(this);
            MarkDirty();
            return control;
        }

        protected bool RemoveLogicalChild(Control control)
        {
            if (control == null || !_logicalChildren.Remove(control))
                return false;

            var visualChildren = VisualChildren as IList<Control>;
            if (visualChildren != null &&
                !ReferenceEquals(visualChildren, _logicalChildren) &&
                !visualChildren.IsReadOnly)
            {
                visualChildren.Remove(control);
            }

            control.CancelAnimationTree(_animationController);
            if (ReferenceEquals(control.StyleParent, this))
                control.SetStyleParent(null);

            MarkDirty();
            return true;
        }

        protected void ClearLogicalChildren()
        {
            for (int i = _logicalChildren.Count - 1; i >= 0; i--)
                RemoveLogicalChild(_logicalChildren[i]);
        }

        public Sandbox.ModAPI.Ingame.IMyTextSurface TextSurface => Host.Surface;

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
            return FormatingHelper.GetSizeInPixel(text, this, scale, TextSurface);
        }

        protected float MeasureLineHeight(float scale, string probe = "Ag")
        {
            return FormatingHelper.LineHeight(scale, this, TextSurface, probe);
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
            return ColorComponent.ResolveHeaderColor(Host.Block as Sandbox.ModAPI.IMyTerminalBlock);
        }

        protected Color GetForegroundColor()
        {
            return Host.ForegroundColor;
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
                !InteractionComponent.AutoScrollStep.Equals(_themeAutoScrollSecondsPerStep) ||
                !string.Equals(textFont, _themeTextFont, StringComparison.Ordinal))
            {
                _theme = headerColor.ToTheme(dark);
                _resources = ThemeResourceBuilder.FromThemeDictionary(_theme);
                _resources.Set(ThemeResources.FontColor, foregroundColor);
                _resources.Set(ThemeResources.LayoutScale, layoutScale);
                _resources.Set(ThemeResources.FontScale, fontScale);
                _resources.Set(ThemeResources.AutoScrollSecondsPerStep, InteractionComponent.AutoScrollStep);
                _resources.Set(ThemeResources.TextFont, textFont);
                _themeHeaderColor = headerColor;
                _themeForegroundColor = foregroundColor;
                _themeDark = dark;
                _themeLayoutScale = layoutScale;
                _themeFontScale = fontScale;
                _themeAutoScrollSecondsPerStep = InteractionComponent.AutoScrollStep;
                _themeTextFont = textFont;
                _hasTheme = true;
                MarkDirty();
            }
        }

        float GetLayoutScaleResourceValue()
        {
            float scale = GeneralComponent.GetScale();
            return scale > 0f ? scale : 1f;
        }

        float GetFontScaleResourceValue()
        {
            float fontScale = Host.Surface.FontSize;
            return fontScale > 0f ? fontScale : 1f;
        }

        string GetTextFontResourceValue()
        {
            string font = Host.Surface.Font;
            return string.IsNullOrEmpty(font) ? "White" : font;
        }

        bool ShouldUseDarkTheme()
        {
            var backgroundColor = Host.ForegroundColor;
            return backgroundColor.ContrastRatio(Color.Black) >=
                   backgroundColor.ContrastRatio(Color.White);
        }

    }
}
