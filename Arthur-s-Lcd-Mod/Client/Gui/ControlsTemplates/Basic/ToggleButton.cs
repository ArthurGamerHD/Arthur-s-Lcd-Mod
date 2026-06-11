using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Basic
{
    /// <summary>
    /// A button whose selected appearance is derived from external state.
    /// The state is queried while rendering so the button always reflects the
    /// current application mode without maintaining a second local copy.
    /// </summary>
    public class ToggleButton : Button
    {
        public ToggleButton(RectangleF bounds, ButtonModel model = null)
            : base(bounds, model)
        {
        }

        public ToggleButton(RectangleF bounds, string text, Func<bool> getState,
            Action<ButtonModel, object> clicked = null)
            : base(bounds, text, clicked)
        {
            GetState = getState;
        }

        /// <summary>
        /// Returns true when the button should be drawn as selected.
        /// </summary>
        public Func<bool> GetState { get; set; }

        /// <summary>
        /// Optional selected-state style. When omitted, a themed secondary
        /// container style is used so selected buttons remain visually distinct.
        /// </summary>
        public ControlStyle SelectedStyle { get; set; }

        public bool IsSelected
        {
            get { return GetState != null && GetState(); }
        }

        public static ControlStyle CreateSelectedButtonStyle()
        {
            return CreateSelectedButtonStyle(null);
        }

        public static ControlStyle CreateSelectedButtonStyle(IReadOnlyDictionary<string, Color> theme)
        {
            var style = ControlStyle.FromThemeRoles(
                Constants.ON_SECONDARY_CONTAINER,
                Constants.SECONDARY_CONTAINER,
                Constants.SECONDARY_CONTAINER + Constants.HOVER,
                Constants.ON_SECONDARY_CONTAINER,
                theme);
            style.BorderRadiusPixels = Border.DEFAULT_RADIUS_PIXELS;
            return style;
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            if (!IsSelected)
            {
                base.RenderDefault(context, sprites);
                return;
            }

            var selectedContext = new ControlRenderContext(
                context.Surface,
                context.Scale,
                context.FontScale,
                SelectedStyle ?? CreateSelectedButtonStyle(context.Theme),
                context.Theme,
                context.CursorPosition);
            base.RenderDefault(selectedContext, sprites);
        }
    }
}
