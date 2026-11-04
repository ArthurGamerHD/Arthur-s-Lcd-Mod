using System;

namespace LcdMod.Client.Apps.Views
{
    /// <summary>
    /// The resources every app's views find above them in <see cref="AppView"/> (converters and shared sizes); also
    /// what the IDE preview's design-time theme answers, since previewed views have no app shell around them.
    /// </summary>
    internal static class AppViewResources
    {
        public static void AddTo(Action<string, object> add)
        {
            add("FontSizeMultiplier", new FontSizeMultiplierConverter());
            add("Multiply", new MultiplyConverter());
            add("PercentText", new PercentTextConverter());
            add("MinimumContrast", new MinimumContrastConverter());
            add("donutThickness", 17.6f);
            add("donutTextMargin", new VRageMath.Vector4(20f));
        }
    }
}
