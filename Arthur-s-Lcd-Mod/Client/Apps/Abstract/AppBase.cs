using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps.Abstract
{
    public abstract class AppBase : IThemedApp
    {
        readonly ControlStyle _controlStyle;
        Dictionary<string, Color> _theme;
        Color _themeHeaderColor;
        bool _themeDark;
        bool _hasTheme;

        protected AppBase(ScreenConfigGeneral config, IAppHost host)
        {
            AppConfig = config;
            Host = host;
            _controlStyle = ControlStyle.FromThemeRoles(
                Constants.ON_PRIMARY,
                Constants.PRIMARY,
                Constants.PRIMARY + Constants.HOVER,
                Constants.ON_PRIMARY);
        }

        protected IAppHost Host { get; private set; }
        protected ScreenConfigGeneral AppConfig { get; private set; }

        public IReadOnlyDictionary<string, Color> Theme => GetTheme();

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
                _controlStyle,
                Theme,
                cursorPosition);
        }

        public Color GetThemeColor(string role)
        {
            var theme = Theme;
            if (theme == null || string.IsNullOrEmpty(role))
                throw new ResourceKeyNotFoundException(role, "Theme");

            Color color;
            if (!theme.TryGetValue(role, out color))
                throw new ResourceKeyNotFoundException(role, "Theme");

            return color;
        }

        public abstract void Update();

        public virtual void LayoutChanged()
        {
        }

        public abstract List<MySprite> GetSprites();

        protected Color GetHeaderColor()
        {
            var colorable = AppConfig as ScreenConfigColorable;
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
                _themeHeaderColor = headerColor;
                _themeDark = dark;
                _hasTheme = true;
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
