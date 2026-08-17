using System.Collections.Generic;
using Sandbox.ModAPI;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace LcdMod.Client.Modules.Defense.Providers.DefenseShields
{
    public sealed class DefenseShieldProvider : IShieldProvider
    {
        const int API_RETRY_FRAME_LIMIT = 5;

        readonly ShieldApi _api = new ShieldApi();
        readonly HashSet<long> _visitedShieldBlocks = new HashSet<long>();
        int _apiRetryFrames;

        public string Name => "Defense Shields";

        public void Load()
        {
            if (MyAPIGateway.Utilities == null)
                return;

            _api.Load();
        }

        public void Update(long gameplayFrame)
        {
            if (_api.IsReady || _apiRetryFrames >= API_RETRY_FRAME_LIMIT)
                return;

            _apiRetryFrames++;
            if (MyAPIGateway.Utilities != null)
                _api.Load();
        }

        public void Unload()
        {
            _api.Unload();
            _visitedShieldBlocks.Clear();
            _apiRetryFrames = 0;
        }

        public bool TryUpdateShieldInfo(
            IReadOnlyList<IMyCubeGrid> grids,
            IEnumerable<IMyTerminalBlock> terminalBlocks,
            long gameplayFrame,
            bool refreshCachedData,
            ShieldInfo info)
        {
            if (!_api.IsReady || _api.Compromised || grids == null || info == null)
                return false;

            float currentHp = 0f;
            float maximumHp = 0f;
            bool anyShieldUp = false;
            IMyTerminalBlock representative = null;
            _visitedShieldBlocks.Clear();

            try
            {
                for (int i = 0; i < grids.Count; i++)
                {
                    var grid = grids[i];
                    if (grid == null || grid.Closed || grid.MarkedForClose || !_api.GridHasShield(grid))
                        continue;

                    var shieldBlock = _api.GetShieldBlock(grid);
                    if (shieldBlock == null || shieldBlock.Closed || shieldBlock.MarkedForClose ||
                        !_visitedShieldBlocks.Add(shieldBlock.EntityId))
                        continue;

                    float hpPerCharge = _api.HpToChargeRatio(shieldBlock);
                    if (hpPerCharge <= 0)
                        continue;

                    if (representative == null)
                        representative = shieldBlock;

                    currentHp += DefenseShieldValueConverter.ChargeToHp(
                        _api.GetCharge(shieldBlock), hpPerCharge);
                    maximumHp += DefenseShieldValueConverter.ChargeToHp(
                        _api.GetMaxCharge(shieldBlock), hpPerCharge);
                    anyShieldUp |= _api.IsShieldUp(shieldBlock);
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
            info.RepresentativeName = representative.CustomName ?? string.Empty;
            info.ValueUnit = "HP";
            info.UseSiPrefixes = false;
            info.CurrentPoints = currentHp;
            info.MaximumPoints = maximumHp;
            info.RechargePointsPerSecond = 0f;
            info.MaximumRechargePointsPerSecond = 0f;
            info.EffectivenessRatio = 0f;
            info.TicksUntilRecharge = 0;
            info.HasCapacity = maximumHp > 0f;
            info.HasRecharge = false;
            info.HasMaximumRecharge = false;
            info.HasEffectiveness = false;
            info.HasRechargeDelay = false;
            info.IsWorking = anyShieldUp;
            info.UsesLiveData = true;
            info.LastLiveDataFrame = gameplayFrame;
            info.LastCachedDataFrame = 0L;
            return true;
        }
    }
}
