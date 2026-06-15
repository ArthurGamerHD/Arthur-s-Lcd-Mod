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
    public abstract class App : Control, IThemedApp, IVisualStyleScope
    {
        readonly List<Control> _rootControls = new List<Control>();
        StyleTree _styles;
        ResourceTree _resources;
        Dictionary<string, Color> _theme;
        Color _themeHeaderColor;
        bool _themeDark;
        bool _hasTheme;

        protected App(ScreenConfigInteractive config, IAppHost host)
        {
            AppConfig = config;
            Host = host;
            _styles = DefaultStyleBuilder.Build();
        }

        protected IAppHost Host { get; private set; }
        protected ScreenConfigInteractive AppConfig { get; private set; }

        public IReadOnlyDictionary<string, Color> Theme => GetTheme();

        public override StyleTree Styles => _styles;

        public override ResourceTree Resources
        {
            get
            {
                GetTheme();
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

        protected void RemoveChild(ControlTemplate control)
        {
            if (control == null || !_rootControls.Remove(control))
                return;

            control.SetStyleParent(null);
            MarkDirty();
        }

        protected void ClearChildren()
        {
            for (int i = 0; i < _rootControls.Count; i++)
            {
                var control = _rootControls[i] as ControlTemplate;
                if (control != null)
                    control.SetStyleParent(null);
            }

            _rootControls.Clear();
            MarkDirty();
        }

        protected void SetStyles(StyleTree styles)
        {
            if (ReferenceEquals(_styles, styles))
                return;

            _styles = styles;
            MarkDirty();
        }

        protected void SetResources(ResourceTree resources)
        {
            if (ReferenceEquals(_resources, resources))
                return;

            _resources = resources;
            MarkDirty();
        }

        public ControlRenderContext CreateControlRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            Vector2 cursorPosition)
        {
            return new ControlRenderContext(
                surface,
                scale,
                fontScale,
                cursorPosition,
                this);
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

        Dictionary<string, Color> GetTheme()
        {
            var headerColor = GetHeaderColor();
            bool dark = ShouldUseDarkTheme();

            if (!_hasTheme || !headerColor.Equals(_themeHeaderColor) || dark != _themeDark)
            {
                _theme = headerColor.ToTheme(dark);
                _resources = ThemeResourceBuilder.FromThemeDictionary(_theme);
                _themeHeaderColor = headerColor;
                _themeDark = dark;
                _hasTheme = true;
                MarkDirty();
            }

            return _theme;
        }

        bool ShouldUseDarkTheme()
        {
            var backgroundColor = Host?.ForegroundColor ?? Color.White;
            return backgroundColor.ContrastRatio(Color.Black) >=
                   backgroundColor.ContrastRatio(Color.White);
        }
    }
}
