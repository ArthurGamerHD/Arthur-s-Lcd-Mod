using System;
using System.Globalization;

namespace LcdMod.Client.Gui.ControlsTemplates.Inputs
{
    public sealed class NumericUpDownModel : ControlModelBase
    {
        public NumericUpDownModel()
        {
            Step = 1d;
            Format = "0.###";
            MinValue = double.MinValue;
            MaxValue = double.MaxValue;
        }

        public double Value { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public string Format { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public bool Enabled { get; set; } = true;
        public double Step { get; set; }
        public Action<double> ValueChanged { get; set; }

        public string GetText()
        {
            return Value.ToString(string.IsNullOrEmpty(Format) ? "0.###" : Format, CultureInfo.CurrentUICulture);
        }

        public void Add(double delta)
        {
            SetValue(Value + delta);
        }

        public bool TrySetValue(string text)
        {
            double value;
            if (!TryParse(text, out value))
                return false;

            SetValue(value);
            return true;
        }

        public void SetValue(double value)
        {
            var clamped = Clamp(value, MinValue, MaxValue);
            if (Math.Abs(clamped - Value) <= 0.0000001d)
                return;

            Value = clamped;
            if (ValueChanged != null)
                ValueChanged(Value);
        }

        static bool TryParse(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentUICulture, out value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
