using LcdMod.Client.Gui.Styling;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    /// <summary>
    /// A concrete menu item used by <see cref="Menu"/>. It exists so menu entries participate in
    /// the normal visual tree, hit testing, style class matching, and hover/active states.
    /// </summary>
    sealed class MenuItemControl : RectangleControl
    {
        bool _active;

        public MenuItemControl(RectangleF bounds, CursorType cursor, object dataContext, System.Action<object, object> onClick)
            : base(bounds, cursor, dataContext, onClick)
        {
            SetClass("ControlBase MenuItemControl");
        }

        public void SetActive(bool active)
        {
            if (_active == active)
                return;

            _active = active;
            MarkDirty();
        }

        protected override StyleState GetStyleState()
        {
            StyleState state = base.GetStyleState();
            if (_active)
                state |= StyleState.Active;

            return state;
        }
    }
}
