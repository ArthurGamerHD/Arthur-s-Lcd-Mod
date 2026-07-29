using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.Ftue
{
    internal sealed class LookAroundFtueTip : HintFtueTip
    {
        sealed class ContactState
        {
            public long ContactStartFrame = long.MinValue;
            public long LastContactFrame = long.MinValue;
        }

        const string TIP_ID = "interaction.lookaround.v1";
        const string LOCALIZATION_KEY = Constants.MOD_PREFIX + "Ftue_LookAround_Line1";
        const string HINT_LOCALIZATION_KEY = Constants.MOD_PREFIX + "Ftue_LookAround_Line2";
        const string ALWAYS_ACTIVE_LOCALIZATION_KEY = Constants.MOD_PREFIX + "AlwaysActive";
        const int REQUIRED_CONTACT_FRAMES = 120;
        const int CONTACT_GRACE_FRAMES = 6;

        readonly Dictionary<InteractiveSurfaceScript, ContactState> _contactStates =
            new Dictionary<InteractiveSurfaceScript, ContactState>();
        readonly IMyControl _lookAroundControl;

        public LookAroundFtueTip() : base(TIP_ID, typeof(IApp))
        {
            _lookAroundControl = MyAPIGateway.Input?.GetGameControl(MyStringId.GetOrCompute("LOOKAROUND"));
        }

        internal override void OnHooked(InteractiveSurfaceScript surface, IApp app)
        {
            base.OnHooked(surface, app);
            if (surface == null || app == null)
                return;

            _contactStates[surface] = new ContactState();
            Show(surface, app, null);
        }

        internal override void OnUnhooked(InteractiveSurfaceScript surface, IApp app)
        {
            if (surface != null)
                _contactStates.Remove(surface);

            base.OnUnhooked(surface, app);
        }

        internal override void OnVisualContact(
            InteractiveSurfaceScript surface,
            IApp app,
            Vector2 coordinates)
        {
            Show(surface, app, null);

            ContactState state;
            if (!IsTriggered(surface) || !_contactStates.TryGetValue(surface, out state))
                return;

            long frame = MyAPIGateway.Session.GameplayFrameCounter;
            if (state.LastContactFrame == long.MinValue ||
                frame - state.LastContactFrame > CONTACT_GRACE_FRAMES)
            {
                state.ContactStartFrame = frame;
            }

            state.LastContactFrame = frame;
            if (frame - state.ContactStartFrame >= REQUIRED_CONTACT_FRAMES)
                Complete(surface);
        }

        protected override bool IsEligible(InteractiveSurfaceScript surface)
        {
            return FtueSurfaceEligibility.IsEligible(surface);
        }

        protected override string BuildMarkdown(
            InteractiveSurfaceScript surface,
            IApp app,
            ControlTemplate targetControl)
        {
            string keyName = EscapeMarkdownText(GetLookAroundKeyName());
            string alwaysActiveLabel = LocHelper.GetLoc(ALWAYS_ACTIVE_LOCALIZATION_KEY);
            if (string.IsNullOrWhiteSpace(alwaysActiveLabel))
                alwaysActiveLabel = "Always Active";

            string instruction = string.Format(
                FormatingHelper.Culture,
                LocHelper.GetLoc(LOCALIZATION_KEY),
                keyName);
            string hint = string.Format(
                FormatingHelper.Culture,
                LocHelper.GetLoc(HINT_LOCALIZATION_KEY),
                EscapeMarkdownText(alwaysActiveLabel));
            return instruction + "\n-# " + hint;
        }

        string GetLookAroundKeyName()
        {
            if (_lookAroundControl == null)
                return "LOOKAROUND";

            string name = _lookAroundControl.GetControlButtonName(MyGuiInputDeviceEnum.Keyboard);
            if (string.IsNullOrWhiteSpace(name))
                name = _lookAroundControl.GetControlButtonName(MyGuiInputDeviceEnum.KeyboardSecond);
            if (string.IsNullOrWhiteSpace(name))
                name = _lookAroundControl.GetControlName().ToString();

            return string.IsNullOrWhiteSpace(name) ? "LOOKAROUND" : name;
        }

        static string EscapeMarkdownText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("`", "\\`")
                .Replace("*", "\\*")
                .Replace("_", "\\_")
                .Replace("~", "\\~")
                .Replace("[", "\\[")
                .Replace("]", "\\]");
        }
    }
}
