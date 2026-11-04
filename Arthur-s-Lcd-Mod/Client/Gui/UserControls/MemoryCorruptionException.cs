using System;
using System.Text;

namespace LcdMod.Client.Gui.UserControls
{
    // The made-up fault a power-disrupted screen "crashes" with: memory corruption from the EMP, down to a bit flip,
    // with addresses and a stack trace that are different for every screen. Nothing throws it.
    sealed class MemoryCorruptionException : Exception
    {
        readonly string _stackTrace;

        MemoryCorruptionException(string message, Exception inner, string stackTrace) : base(message, inner)
        {
            _stackTrace = stackTrace;
        }

        public override string StackTrace => _stackTrace;

        // As the runtime lays an exception out; it would read the real (empty) trace instead of the made-up one.
        public override string ToString()
        {
            var text = new StringBuilder(GetType().FullName).Append(": ").Append(Message);
            if (InnerException != null)
                text.Append(" ---> ").Append(InnerException).Append("\n   --- End of inner exception stack trace ---");
            if (!string.IsNullOrEmpty(_stackTrace))
                text.Append('\n').Append(_stackTrace);
            return text.ToString();
        }

        public static MemoryCorruptionException Create(int seed)
        {
            var random = new Random(seed);
            Exception bitFlip = new MemoryCorruptionException(
                "Uncorrectable bit flip in cache line " + Hex(random, 8) + " (ECC syndrome " + Hex(random, 2) +
                ", rail undervoltage " + random.Next(18, 61) + "%)",
                null,
                Frames(random, "VRage.Render.Hardware.MyMemoryController.Scrub", "VRage.Render.Hardware.MyCache.Evict"));
            Exception pool = new MemoryCorruptionException(
                "BAD_POOL_HEADER (0x00000019): pool block 0x" + Hex(random, 16) + " overwritten, tag 'LcdT' expected, " +
                "read '" + (char)random.Next(33, 127) + (char)random.Next(33, 127) + "??'",
                bitFlip,
                Frames(random, "VRage.Render.MyGeneratedTexturePool.Allocate",
                    "Sandbox.Game.Components.MyRenderComponentScreenAreas.CreateTexture"));
            return new MemoryCorruptionException(
                "*** STOP: 0x0000001A (MEMORY_MANAGEMENT)\n" +
                "Screen buffer corrupted at 0xFFFFF80" + Hex(random, 9) + ": page table entry 0x" + Hex(random, 16) +
                " failed parity check\n" +
                "Frame checksum mismatch: expected 0x" + Hex(random, 8) + ", read 0x" + Hex(random, 8) + "\n" +
                "Grid power bus " + random.Next(1, 9) + ": electromagnetic transient, " + random.Next(40, 400) +
                " kV/m",
                pool,
                Frames(random, "LcdMod.Client.ScreenAreas.MyScreenBuffer.Present",
                    "LcdMod.Client.SurfaceScripts.SurfaceScriptBase.RenderNow",
                    "Sandbox.Game.Entities.Blocks.MyTextPanelComponent.UpdateAfterSimulation"));
        }

        static string Frames(Random random, params string[] methods)
        {
            var text = new StringBuilder();
            for (int i = 0; i < methods.Length; i++)
            {
                if (i > 0)
                    text.Append('\n');
                text.Append("   at ").Append(methods[i]).Append("() +0x").Append(Hex(random, 3));
            }
            return text.ToString();
        }

        static string Hex(Random random, int digits)
        {
            var text = new StringBuilder(digits);
            for (int i = 0; i < digits; i++)
                text.Append("0123456789ABCDEF"[random.Next(16)]);
            return text.ToString();
        }
    }
}
