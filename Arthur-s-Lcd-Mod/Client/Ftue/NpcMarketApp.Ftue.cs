using System;

// ReSharper disable once CheckNamespace
namespace LcdMod.Client.Apps
{
    internal sealed partial class NpcMarketApp
    {
        public event Action<string> SearchChanged;

        void NotifySearchChanged()
        {
            SearchChanged?.Invoke(_searchQuery);
        }
    }
}
