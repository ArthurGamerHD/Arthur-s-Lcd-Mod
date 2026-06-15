using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Styling.Styles;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using VRageMath;

namespace LcdMod.Client.Games
{
    internal sealed class GameThemeContext : IVisualStyleScope
    {
        readonly InteractiveSurfaceScript _script;
        readonly StyleTree _styles;
        Dictionary<string, Color> _theme;
        ResourceTree _resources;
        bool _isDirty = true;
        Color _themeHeaderColor;
        bool _themeDark;
        bool _hasTheme;

        public GameThemeContext(InteractiveSurfaceScript script)
        {
            _script = script;
            _styles = DefaultStyleBuilder.Build();
        }

        public IVisualStyleScope StyleParent
        {
            get { return null; }
        }

        public StyleTree Styles
        {
            get { return _styles; }
        }

        public ResourceTree Resources
        {
            get
            {
                GetTheme();
                return _resources;
            }
        }

        public bool IsDirty
        {
            get { return _isDirty; }
        }

        public void MarkDirty()
        {
            _isDirty = true;
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
            var foregroundColor = _script == null ? Color.White : _script.ForegroundColor;
            return foregroundColor.ContrastRatio(Color.Black) >=
                   foregroundColor.ContrastRatio(Color.White);
        }
    }
}
