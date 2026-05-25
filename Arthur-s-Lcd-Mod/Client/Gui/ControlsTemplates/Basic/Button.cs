using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Common.Helpers;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Basic
{
    public class Button : RectangleControl
    {
        public Button(RectangleF bounds, ButtonModel model = null)
            : base(bounds, CursorType.Hand, model ?? new ButtonModel())
        {
        }

        public Button(RectangleF bounds, string text, Action<ButtonModel, object> clicked = null)
            : this(bounds, new ButtonModel { Text = text, Clicked = clicked })
        {
        }

        public Button(RectangleF bounds, CursorType? cursor, object dataContext = null,
            Action<object, object> onClick = null, InteractiveTooltip tooltip = null)
            : base(bounds, cursor, dataContext, onClick, tooltip)
        {
        }

        public static ControlStyle CreatePrimaryButtonStyle()
        {
            return CreatePrimaryButtonStyle(null);
        }

        public static ControlStyle CreatePrimaryButtonStyle(IReadOnlyDictionary<string, Color> theme)
        {
            var style = ControlStyle.FromThemeRoles(
                Constants.ON_PRIMARY_CONTAINER,
                Constants.PRIMARY_CONTAINER,
                Constants.PRIMARY_CONTAINER + Constants.HOVER,
                Constants.ON_PRIMARY_CONTAINER,
                theme);
            style.BorderPercentage = 0.5f;
            return style;
        }

        public static ControlStyle CreateDisabledButtonStyle()
        {
            return CreateDisabledButtonStyle(null);
        }

        public static ControlStyle CreateDisabledButtonStyle(IReadOnlyDictionary<string, Color> theme)
        {
            var style = ControlStyle.FromThemeRoles(
                Constants.DISABLED_FOREGROUND,
                Constants.DISABLED_BACKGROUND,
                Constants.DISABLED_BACKGROUND,
                Constants.DISABLED_FOREGROUND,
                theme);
            style.BorderPercentage = 0.5f;
            return style;
        }
    }
}
