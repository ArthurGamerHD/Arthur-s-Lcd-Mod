using System;
using LcdMod.Client.Helpers;

namespace LcdMod.Client.Gui.ControlsTemplates.Inputs
{
    public sealed class TextInputModel : ControlModelBase
    {
        public TextInputModel()
        {
            Cursor = CursorType.Hand;
        }

        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Placeholder { get; set; }
        public string Value { get; set; }
        public bool Enabled { get; set; } = true;
        public Action<string> ValueChanged { get; set; }

        public override bool CanClick
        {
            get { return Enabled; }
        }

        public override bool Click(object sender)
        {
            if (!Enabled)
                return false;

            TextInputHelper.SpawnForLocalPlayer(
                string.IsNullOrEmpty(Title) ? "Input" : Title,
                ApplyValue,
                Value ?? string.Empty,
                Subtitle ?? string.Empty);
            return true;
        }

        void ApplyValue(string value)
        {
            Value = value ?? string.Empty;
            if (ValueChanged != null)
                ValueChanged(Value);
        }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(Value))
                return Value;

            return Placeholder ?? string.Empty;
        }
    }
}
