using System;

namespace LcdMod.Common.Config.Generation
{
    /// <summary>
    /// Compile-time metadata for a direct property-backed terminal slider. MDK removes this
    /// declaration and all usages after the source generator has emitted the runtime control.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class TerminalControl_SliderAttribute : Attribute
    {
        public TerminalControl_SliderAttribute(
            int registrationId,
            string controlId,
            string title,
            float minimum,
            float maximum,
            string writerFormat)
        {
            RegistrationId = registrationId;
            ControlId = controlId;
            Title = title;
            Minimum = minimum;
            Maximum = maximum;
            WriterFormat = writerFormat;
        }

        public int RegistrationId { get; private set; }
        public string ControlId { get; private set; }
        public string Title { get; private set; }
        public float Minimum { get; private set; }
        public float Maximum { get; private set; }
        public string WriterFormat { get; private set; }

        public string Tooltip { get; set; }
        public string Slot { get; set; }
        public string WriterSuffix { get; set; }
        public bool RequiresAdvancedTweakables { get; set; }
        public float Quantum { get; set; }
    }

    /// <summary>
    /// Compile-time metadata for a direct boolean property-backed terminal on/off switch.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class TerminalControl_SwitchAttribute : Attribute
    {
        public TerminalControl_SwitchAttribute(
            int registrationId,
            string controlId,
            string title)
        {
            RegistrationId = registrationId;
            ControlId = controlId;
            Title = title;
        }

        public int RegistrationId { get; private set; }
        public string ControlId { get; private set; }
        public string Title { get; private set; }

        public string TitleSuffix { get; set; }
        public string Tooltip { get; set; }
        public string Slot { get; set; }
        public string OnText { get; set; }
        public string OffText { get; set; }
        public bool RequiresAdvancedTweakables { get; set; }
        public bool RefreshTerminalOnSet { get; set; }
    }

    /// <summary>
    /// Compile-time metadata for a direct color property-backed terminal color picker.
    /// OptionalValue&lt;Color&gt; properties use a compile-time resolved method named
    /// Resolve{PropertyName} for their displayed fallback value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class TerminalControl_ColorAttribute : Attribute
    {
        public TerminalControl_ColorAttribute(
            int registrationId,
            string controlId,
            string title)
        {
            RegistrationId = registrationId;
            ControlId = controlId;
            Title = title;
        }

        public int RegistrationId { get; private set; }
        public string ControlId { get; private set; }
        public string Title { get; private set; }

        public string Tooltip { get; set; }
        public string Slot { get; set; }
        public bool RequiresCustomColor { get; set; }
        public bool RequiresAdvancedTweakables { get; set; }
    }
}
