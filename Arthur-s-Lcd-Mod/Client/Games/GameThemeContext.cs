using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Games
{
    internal sealed class GameThemeContext
    {
        readonly InteractiveSurfaceScript _script;
        readonly ControlStyle _controlStyle;
        Dictionary<string, Color> _theme;
        Color _themeHeaderColor;
        bool _themeDark;
        bool _hasTheme;

        public GameThemeContext(InteractiveSurfaceScript script)
        {
            _script = script;
            _controlStyle = ControlStyle.FromThemeRoles(
                Constants.ON_PRIMARY,
                Constants.PRIMARY,
                Constants.PRIMARY + Constants.HOVER,
                Constants.ON_PRIMARY);
        }

        public IReadOnlyDictionary<string, Color> Theme
        {
            get { return GetTheme(); }
        }

        public ControlRenderContext CreateControlRenderContext(
            IMyTextSurface surface,
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

        Color GetHeaderColor()
        {
            var colorableConfig = _script == null ? null : _script.ColorableConfig;
            if (colorableConfig != null)
                return colorableConfig.HeaderColor;

            return _script == null ? Color.White : _script.ForegroundColor;
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
            var foregroundColor = _script == null ? Color.White : _script.ForegroundColor;
            return foregroundColor.ContrastRatio(Color.Black) >=
                   foregroundColor.ContrastRatio(Color.White);
        }
    }
}
