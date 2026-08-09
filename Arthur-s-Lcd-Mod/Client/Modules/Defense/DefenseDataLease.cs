using System;

namespace LcdMod.Client.Modules.Defense
{
    public sealed class DefenseDataLease : IDisposable
    {
        readonly DefenseDataModule _module;
        bool _disposed;

        internal DefenseDataLease(DefenseDataModule module, DefenseDataService service)
        {
            _module = module;
            Service = service;
        }

        public DefenseDataService Service { get; private set; }
        public DefenseSnapshot Latest => Service?.Latest ?? DefenseSnapshot.Empty;

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
