using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Defense
{
    /// <summary>
    /// Adapter boundary for shield mods. Providers translate a mod-specific API, network
    /// protocol, or CustomInfo format into the caller-owned, stable dashboard model.
    /// </summary>
    public interface IShieldProvider
    {
        string Name { get; }

        void Load();
        void Update(long gameplayFrame);
        void Unload();

        /// <summary>Updates the supplied observable model and never replaces it.</summary>
        bool TryUpdateShieldInfo(
            IReadOnlyList<IMyCubeGrid> grids,
            IEnumerable<IMyTerminalBlock> terminalBlocks,
            long gameplayFrame,
            bool refreshCachedData,
            ShieldInfo info);
    }
}
