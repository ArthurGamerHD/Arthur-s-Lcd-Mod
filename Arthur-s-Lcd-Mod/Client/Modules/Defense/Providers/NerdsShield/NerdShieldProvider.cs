using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.Modules.Defense.Providers.NerdsShield
{
    public sealed class NerdShieldProvider : IShieldProvider
    {
        readonly NerdShieldApiClient _api = new NerdShieldApiClient();

        public string Name => "Nerd's Shield Api";

        public void Load()
        {
            _api.Load();
        }

        public void Update(long gameplayFrame)
        {
            _api.Load();
        }

        public void Unload()
        {
            _api.Unload();
        }

        public bool TryUpdateShieldInfo(
            IReadOnlyList<IMyCubeGrid> grids,
            IReadOnlyList<IMyTerminalBlock> terminalBlocks,
            long gameplayFrame,
            bool refreshCachedData,
            ShieldInfo info)
        {
            if (!_api.IsReady || grids == null || info == null)
                return false;

            float current = 0f;
            float maximum = 0f;
            float currentRegen = 0f;
            float maximumRegen = 0f;
            int ticksUntilRegen = 0;
            IMyCubeGrid representative = null;

            try
            {
                for (int i = 0; i < grids.Count; i++)
                {
                    var grid = grids[i];
                    if (grid == null || grid.Closed || grid.MarkedForClose || !_api.GridHasShields(grid))
                        continue;

                    if (representative == null)
                        representative = grid;
                    current += Sanitize(_api.GetCurrentShieldHp(grid));
                    maximum += Sanitize(_api.GetMaximumShieldHp(grid));
                    currentRegen += Sanitize(_api.GetCurrentShieldRegen(grid));
                    maximumRegen += Sanitize(_api.GetMaximumShieldRegen(grid));
                    ticksUntilRegen = Math.Max(ticksUntilRegen, _api.GetTicksUntilShieldRegen(grid));
                }
            }
            catch
            {
                return false;
            }

            if (representative == null)
                return false;

            info.ProviderName = Name;
            info.RepresentativeEntityId = representative.EntityId;
            info.RepresentativeName = representative.DisplayName ?? string.Empty;
            info.ValueUnit = "HP";
            info.UseSiPrefixes = false;
            info.CurrentPoints = current;
            info.MaximumPoints = maximum;
            info.RechargePointsPerSecond = currentRegen;
            info.MaximumRechargePointsPerSecond = maximumRegen;
            info.EffectivenessRatio = maximumRegen > 0f
                ? MathHelper.Clamp(currentRegen / maximumRegen, 0f, 1f)
                : 0f;
            info.HasCapacity = maximum > 0f;
            info.HasRecharge = true;
            info.HasMaximumRecharge = maximumRegen > 0f;
            info.HasEffectiveness = maximumRegen > 0f;
            info.UpdateRechargeDelayCountdown(
                ticksUntilRegen,
                current,
                maximum,
                currentRegen,
                gameplayFrame);
            info.IsWorking = maximum > 0f;
            info.UsesLiveData = true;
            info.LastLiveDataFrame = gameplayFrame;
            info.LastCachedDataFrame = 0L;
            return true;
        }

        static float Sanitize(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : 0f;
        }
    }
}
