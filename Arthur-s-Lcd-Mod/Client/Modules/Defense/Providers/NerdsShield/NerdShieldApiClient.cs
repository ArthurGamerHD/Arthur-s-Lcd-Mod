using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Modules.Defense.Providers.NerdsShield
{
    /// <summary>
    /// Optional read-only binding for Nerd Shields' v1 inter-mod API. The producer broadcasts
    /// its delegate dictionary on its workshop-ID channel; no hard assembly dependency exists.
    /// </summary>
    internal sealed class NerdShieldApiClient
    {
        public const long MOD_API_MESSAGE_ID = 3514216898L;

        Func<IMyCubeGrid, bool> _gridHasShields;
        Func<IMyCubeGrid, float> _getCurrentShieldHp;
        Func<IMyCubeGrid, float> _getMaximumShieldHp;
        Func<IMyCubeGrid, float> _getCurrentShieldRegen;
        Func<IMyCubeGrid, float> _getMaximumShieldRegen;
        Func<IMyCubeGrid, int> _getTicksUntilShieldRegen;
        bool _listening;

        public bool IsReady { get; private set; }

        public void Load()
        {
            if (_listening || MyAPIGateway.Utilities == null)
                return;

            MyAPIGateway.Utilities.RegisterMessageHandler(MOD_API_MESSAGE_ID, OnModMessageReceived);
            _listening = true;
        }

        public void Unload()
        {
            if (_listening && MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.UnregisterMessageHandler(MOD_API_MESSAGE_ID, OnModMessageReceived);

            _listening = false;
            Reset();
        }

        public bool GridHasShields(IMyCubeGrid grid)
        {
            return IsReady && grid != null && _gridHasShields(grid);
        }

        public float GetCurrentShieldHp(IMyCubeGrid grid)
        {
            return IsReady && grid != null ? _getCurrentShieldHp(grid) : 0f;
        }

        public float GetMaximumShieldHp(IMyCubeGrid grid)
        {
            return IsReady && grid != null ? _getMaximumShieldHp(grid) : 0f;
        }

        public float GetCurrentShieldRegen(IMyCubeGrid grid)
        {
            return IsReady && grid != null ? _getCurrentShieldRegen(grid) : 0f;
        }

        public float GetMaximumShieldRegen(IMyCubeGrid grid)
        {
            return IsReady && grid != null ? _getMaximumShieldRegen(grid) : 0f;
        }

        public int GetTicksUntilShieldRegen(IMyCubeGrid grid)
        {
            return IsReady && grid != null ? _getTicksUntilShieldRegen(grid) : 0;
        }

        void OnModMessageReceived(object payload)
        {
            if (IsReady)
                return;

            var delegates = payload as IReadOnlyDictionary<string, Delegate>;
            if (delegates == null)
                return;

            try
            {
                Func<IMyCubeGrid, bool> gridHasShields;
                Func<IMyCubeGrid, float> getCurrentShieldHp;
                Func<IMyCubeGrid, float> getMaximumShieldHp;
                Func<IMyCubeGrid, float> getCurrentShieldRegen;
                Func<IMyCubeGrid, float> getMaximumShieldRegen;
                Func<IMyCubeGrid, int> getTicksUntilShieldRegen;

                if (!TryGetDelegate(delegates, "GridHasShields", out gridHasShields) ||
                    !TryGetDelegate(delegates, "GetCurrentShieldHP", out getCurrentShieldHp) ||
                    !TryGetDelegate(delegates, "GetMaximumShieldHP", out getMaximumShieldHp) ||
                    !TryGetDelegate(delegates, "GetCurrentShieldRegen", out getCurrentShieldRegen) ||
                    !TryGetDelegate(delegates, "GetMaximumShieldRegen", out getMaximumShieldRegen) ||
                    !TryGetDelegate(delegates, "GetTicksUntilShieldRegen", out getTicksUntilShieldRegen))
                {
                    MyLog.Default.Log(MyLogSeverity.Warning,
                        "[LcdMod] NerdShieldAPI response is missing required v1 read delegates.");
                    return;
                }

                _gridHasShields = gridHasShields;
                _getCurrentShieldHp = getCurrentShieldHp;
                _getMaximumShieldHp = getMaximumShieldHp;
                _getCurrentShieldRegen = getCurrentShieldRegen;
                _getMaximumShieldRegen = getMaximumShieldRegen;
                _getTicksUntilShieldRegen = getTicksUntilShieldRegen;
                IsReady = true;
            }
            catch (Exception exception)
            {
                Reset();
                MyLog.Default.WriteLine("[LcdMod] NerdShieldAPI binding failed: " + exception);
            }
        }

        static bool TryGetDelegate<T>(IReadOnlyDictionary<string, Delegate> delegates, string name, out T result)
            where T : class
        {
            Delegate value;
            result = delegates.TryGetValue(name, out value) ? value as T : null;
            return result != null;
        }

        void Reset()
        {
            IsReady = false;
            _gridHasShields = null;
            _getCurrentShieldHp = null;
            _getMaximumShieldHp = null;
            _getCurrentShieldRegen = null;
            _getMaximumShieldRegen = null;
            _getTicksUntilShieldRegen = null;
        }
    }
}
