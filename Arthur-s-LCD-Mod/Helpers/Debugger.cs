using System;

namespace Graph.Helpers
{
    public static class DebuggerHelper
    {
        public static void Break()
        {
            try
            {
                throw new Exception("Hello DNSpy");
            }
            catch
            {
                /* workaround for Debugger.Attach() not available for Mods */
            }
        }
    }
}