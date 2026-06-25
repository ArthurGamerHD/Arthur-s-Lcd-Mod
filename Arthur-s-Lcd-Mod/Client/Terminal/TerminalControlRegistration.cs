using LcdMod.Client.Terminal.Controls;

namespace LcdMod.Client.Terminal
{
    /// <summary>
    /// Stable presentation-order metadata for one mod-owned terminal control. RegistrationId is
    /// deliberately separate from the Space Engineers terminal API control ID.
    /// </summary>
    public sealed class TerminalControlRegistration
    {
        public TerminalControlRegistration(int registrationId, TerminalControlsWrapper control)
        {
            RegistrationId = registrationId;
            Control = control;
            ControlId = control == null || control.TerminalControl == null
                ? string.Empty
                : control.TerminalControl.Id;
        }

        public int RegistrationId { get; private set; }
        public string ControlId { get; private set; }
        public TerminalControlsWrapper Control { get; private set; }
    }
}
