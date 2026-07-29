using System;
using LcdMod.Client.Gui.Styling;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Basic
{
    /// <summary>
    /// A button whose selected appearance is derived from external state.
    /// The state is queried while rendering so the button always reflects the
    /// current application mode without maintaining a second local copy.
    /// </summary>
    // ReSharper disable once PartialTypeWithSinglePart
    public partial class ToggleButton : Button
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

        public bool IsSelected => GetState != null && GetState();

        protected override StyleState GetStyleState()
        {
            StyleState state = base.GetStyleState();

            if (IsSelected)
                state |= StyleState.Active | StyleState.Selected;

            return state;
        }
    }
}
