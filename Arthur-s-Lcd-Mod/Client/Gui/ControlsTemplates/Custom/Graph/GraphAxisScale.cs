using System;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Graph
{
    public struct GraphAxisScale
    {
        public double Minimum;
        public double Maximum;
        public double Step;
        public int Steps;

        public static GraphAxisScale FromMaximum(double maxValue, int targetDivisions)
        {
            if (targetDivisions <= 0)
                targetDivisions = 4;

            double max = maxValue > 0 ? maxValue : 1.0;
            double rawStep = max / targetDivisions;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
            double norm = rawStep / mag;
            double niceNorm = norm <= 1.0 ? 1 : norm <= 2.0 ? 2 : norm <= 5.0 ? 5 : 10;
            double step = niceNorm * mag;
            double axisMax = Math.Ceiling(max / step) * step;
            if (axisMax < step)
                axisMax = step;

            return new GraphAxisScale
            {
                Minimum = 0,
                Maximum = axisMax,
                Step = step,
                Steps = Math.Max(1, (int)Math.Round(axisMax / step))
            };
        }
    }
}
