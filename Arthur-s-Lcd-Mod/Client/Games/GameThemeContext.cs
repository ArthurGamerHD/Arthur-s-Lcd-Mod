using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Styling.Styles;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
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
        Color _themeForegroundColor;
        bool _themeDark;
        string _themeTextFont;
        bool _hasTheme;

        public GameThemeContext(InteractiveSurfaceScript script)
        {
            _script = script;
            _styles = DefaultStyleBuilder.Build();
        }

        public IVisualStyleScope StyleParent => null;

        public StyleTree Styles => _styles;

        public ResourceTree Resources
        {
            get
            {
                GetTheme();
                return _resources;
            }
        }

        public bool IsDirty => _isDirty;

        public void MarkDirty()
        {
            _isDirty = true;
        }




        Color GetHeaderColor()
        {
            var colors = _script?.ColorComponent;
            if (colors != null)
                return colors.ResolveHeaderColor(_script.Block as IMyTerminalBlock);

            return _script?.ForegroundColor ?? Color.White;
        }

        Color GetForegroundColor()
        {
            return _script?.ForegroundColor ?? Color.White;
        }

        Dictionary<string, Color> GetTheme()
        {
            var headerColor = GetHeaderColor();
            var foregroundColor = GetForegroundColor();
            bool dark = ShouldUseDarkTheme();
            string textFont = GetTextFontResourceValue();

            if (!_hasTheme ||
                !headerColor.Equals(_themeHeaderColor) ||
                !foregroundColor.Equals(_themeForegroundColor) ||
                dark != _themeDark ||
                !string.Equals(textFont, _themeTextFont, System.StringComparison.Ordinal))
            {
                _theme = headerColor.ToTheme(dark);
                _resources = ThemeResourceBuilder.FromThemeDictionary(_theme);
                _resources.Set(ThemeResources.FontColor, foregroundColor);
                _resources.Set(ThemeResources.TextFont, textFont);
                _themeHeaderColor = headerColor;
                _themeForegroundColor = foregroundColor;
                _themeDark = dark;
                _themeTextFont = textFont;
                _hasTheme = true;
                MarkDirty();
            }

            return _theme;
        }

        string GetTextFontResourceValue()
        {
            string font = _script != null && _script.Surface != null ? _script.Surface.Font : null;
            return string.IsNullOrEmpty(font) ? "White" : font;
        }

        bool ShouldUseDarkTheme()
        {
            var foregroundColor = _script?.ForegroundColor ?? Color.White;
            return foregroundColor.ContrastRatio(Color.Black) >=
                   foregroundColor.ContrastRatio(Color.White);
        }
    }
}
