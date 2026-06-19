using System;

namespace LcdMod.Client.Modules.Power
{
    public sealed class PowerDataLease : IDisposable
    {
        readonly PowerDataModule _module;
        bool _disposed;

        internal PowerDataLease(PowerDataModule module, PowerDataService service)
        {
            _module = module;
            Service = service;
        }

        public PowerDataService Service { get; private set; }
        public PowerSnapshot Latest => Service?.Latest ?? new PowerSnapshot();
        public PowerHistory History => Service?.History;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_module != null && Service != null)
                _module.Release(Service);
            Service = null;
        }
    }
}
