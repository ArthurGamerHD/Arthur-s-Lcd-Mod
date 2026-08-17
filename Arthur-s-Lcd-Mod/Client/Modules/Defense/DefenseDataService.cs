using System;
using System.Collections.Generic;
using LcdMod.Client.GridData;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace LcdMod.Client.Modules.Defense
{
    public sealed class DefenseDataService
    {
        public const long CACHED_INFO_REFRESH_FRAMES = 100L;

        readonly DefenseScopeResolver _resolver;
        readonly IReadOnlyList<IShieldProvider> _providers;
        readonly Dictionary<IShieldProvider, ShieldInfo> _shieldModels =
            new Dictionary<IShieldProvider, ShieldInfo>();
        readonly List<ShieldInfo> _shields = new List<ShieldInfo>();
        readonly List<ShieldInfo> _nextShields = new List<ShieldInfo>();
        readonly LinkedTypedBlockSourceSet<IMyTerminalBlock> _terminalSources =
            new LinkedTypedBlockSourceSet<IMyTerminalBlock>(blocks => blocks.TerminalBlocks);
        List<IMyCubeGrid> _grids = new List<IMyCubeGrid>();
        long _lastCachedInfoRefreshFrame = long.MinValue;

        internal DefenseDataService(
            DefenseScopeResolver resolver,
            IReadOnlyList<IShieldProvider> providers,
            GridLogic requester,
            GridLinkTypeEnum linkType,
            DefenseScopeKey key)
        {
            _resolver = resolver;
            _providers = providers;
            Requester = requester;
            LinkType = linkType;
            Key = key;
            Latest = new DefenseSnapshot(0L, _shields);

        }

        public GridLogic Requester { get; private set; }
        public GridLinkTypeEnum LinkType { get; private set; }
        public DefenseScopeKey Key { get; internal set; }
        public DefenseSnapshot Latest { get; private set; }
        public int CaptureCount { get; private set; }
        public long ReleasedFrame { get; private set; }
        public bool HasCaptures => CaptureCount > 0;
        public event Action<DefenseDataService> ShieldsChanged;

        internal void AddCapture(GridLogic requester)
        {
            if (requester != null)
                Requester = requester;
            CaptureCount++;
            ReleasedFrame = 0L;
            if (CaptureCount == 1)
                _terminalSources.Bind(Requester, LinkType);
        }

        internal void Release(long frame)
        {
            if (CaptureCount > 0)
                CaptureCount--;
            if (CaptureCount == 0)
            {
                ReleasedFrame = frame;
                _terminalSources.Unbind();
            }
        }

        internal void Update(long gameplayFrame)
        {
            if (gameplayFrame < 0L)
                return;
            if (!HasCaptures)
                return;

            bool refreshCachedData = _lastCachedInfoRefreshFrame == long.MinValue ||
                                     gameplayFrame - _lastCachedInfoRefreshFrame >= CACHED_INFO_REFRESH_FRAMES;
            if (refreshCachedData)
            {
                RefreshScope(gameplayFrame);
                _lastCachedInfoRefreshFrame = gameplayFrame;
            }

            _nextShields.Clear();
            for (int i = 0; i < _providers.Count; i++)
            {
                var provider = _providers[i];
                try
                {
                    ShieldInfo model;
                    if (!_shieldModels.TryGetValue(provider, out model))
                    {
                        model = new ShieldInfo();
                        _shieldModels[provider] = model;
                    }

                    if (provider.TryUpdateShieldInfo(
                            _grids, EnumerateTerminalBlocks(), gameplayFrame, refreshCachedData, model))
                    {
                        model.UpdateChargeGhost(gameplayFrame);
                        _nextShields.Add(model);
                    }
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, provider);
                }
            }

            Latest.SetGameplayFrame(gameplayFrame);
            if (HasShieldTopologyChanged())
            {
                _shields.Clear();
                for (int i = 0; i < _nextShields.Count; i++)
                    _shields.Add(_nextShields[i]);
                NotifyShieldsChanged();
            }
        }

        bool HasShieldTopologyChanged()
        {
            if (_shields.Count != _nextShields.Count)
                return true;

            for (int i = 0; i < _shields.Count; i++)
                if (!ReferenceEquals(_shields[i], _nextShields[i]))
                    return true;
            return false;
        }

        void NotifyShieldsChanged()
        {
            var handler = ShieldsChanged;
            if (handler == null)
                return;

            try
            {
                handler(this);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        internal DefenseScopeKey RefreshScope(long gameplayFrame)
        {
            _grids = _resolver.ResolveGrids(Requester, LinkType);
            Key = _resolver.ResolveKey(Requester, LinkType);

            if (HasCaptures)
                _terminalSources.Bind(Requester, LinkType);
            return Key;
        }

        IEnumerable<IMyTerminalBlock> EnumerateTerminalBlocks()
        {
            var sources = _terminalSources.Sources;
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var source = sources[sourceIndex];
                for (var blockIndex = 0; blockIndex < source.Count; blockIndex++)
                    yield return source[blockIndex];
            }
        }

        internal void Dispose()
        {
            _terminalSources.Dispose();
        }
    }
}
