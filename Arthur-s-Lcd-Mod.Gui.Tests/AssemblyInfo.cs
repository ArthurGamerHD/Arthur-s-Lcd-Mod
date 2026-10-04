using Xunit;

// The retained UI and its global style/property registries have a single UI-thread
// lifetime in production. Do not initialize or mutate them from parallel tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Arthur_s_Lcd_Mod.Gui.Tests
{
    static class FrameCacheVerification
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Enable()
        {
            Adk.Gui.Lcd.LcdGuiHost.FrameCacheMismatch = message => throw new Xunit.Sdk.XunitException(message);
        }
    }
}
