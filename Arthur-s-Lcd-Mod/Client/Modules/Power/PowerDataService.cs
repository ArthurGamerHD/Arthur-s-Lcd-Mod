using System;
using System.Collections.Generic;
using LcdMod.Client.GridData;
using LcdMod.Common.Helpers;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Power
{
    public sealed class PowerDataService
    {
        const int ONE_SECOND_WEIGHT = 10;
        const int FIVE_SECOND_WEIGHT = 5;
        const int THIRTY_SECOND_WEIGHT = 30;
        const int ONE_MINUTE_WEIGHT = 60;
        const int FIVE_MINUTE_WEIGHT = 300;
        const int THIRTY_MINUTE_WEIGHT = 1800;

        readonly PowerScopeResolver _resolver;
        readonly SePowerCollector _collector = new SePowerCollector();
        readonly PowerSnapshotAccumulator _oneSecondAccumulator = new PowerSnapshotAccumulator();
        readonly PowerSnapshotAccumulator _fiveSecondAccumulator = new PowerSnapshotAccumulator();
        readonly PowerSnapshotAccumulator _thirtySecondAccumulator = new PowerSnapshotAccumulator();
        readonly PowerSnapshotAccumulator _oneMinuteAccumulator = new PowerSnapshotAccumulator();
        readonly PowerSnapshotAccumulator _fiveMinuteAccumulator = new PowerSnapshotAccumulator();
        readonly PowerSnapshotAccumulator _thirtyMinuteAccumulator = new PowerSnapshotAccumulator();
        readonly HashSet<GridLogic> _neededBlockLogics = new HashSet<GridLogic>();
        readonly HashSet<GridLogic> _nextNeededBlockLogics = new HashSet<GridLogic>();
        List<IMyCubeGrid> _grids = new List<IMyCubeGrid>();
        long _lastSampleFrame = -1;
        long _lastScopeRefreshFrame = -1;

        internal PowerDataService(PowerScopeResolver resolver, GridLogic requester, GridLinkTypeEnum linkType, PowerScopeKey key)
        {
            _resolver = resolver;
            Requester = requester;
            LinkType = linkType;
            Key = key;
            History = new PowerHistory();
        }

        public GridLogic Requester { get; private set; }
        public GridLinkTypeEnum LinkType { get; private set; }
        public PowerScopeKey Key { get; internal set; }
        public PowerSnapshot Latest { get; private set; }
        public PowerHistory History { get; private set; }
        public int CaptureCount { get; private set; }
        public long ReleasedFrame { get; private set; }

        public bool HasCaptures => CaptureCount > 0;

        internal void AddCapture(GridLogic requester)
        {
            if (requester != null)
                Requester = requester;
            CaptureCount++;
            ReleasedFrame = 0;
            if (CaptureCount == 1)
                RebindBlockNeeds();
        }

        internal void Release(long frame)
        {
            if (CaptureCount > 0)
                CaptureCount--;
            if (CaptureCount == 0)
            {
                ReleasedFrame = frame;
                ReleaseBlockNeeds();
            }
        }

        internal void Update(long gameplayFrame)
        {
            if (gameplayFrame < 0)
                return;
            if (!HasCaptures)
                return;

            if (_lastScopeRefreshFrame < 0 || gameplayFrame - _lastScopeRefreshFrame >= 120)
                RefreshScope(gameplayFrame);

            if (_lastSampleFrame >= 0 && gameplayFrame - _lastSampleFrame < 6)
                return;

            _lastSampleFrame = gameplayFrame;
            try
            {
                AddRawSnapshot(_collector.Collect(_grids, gameplayFrame));
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        void AddRawSnapshot(PowerSnapshot snapshot)
        {
            Latest = snapshot;
            History.RawSamples.Add(snapshot);
            AddAveragedSnapshot(_oneSecondAccumulator, ONE_SECOND_WEIGHT, History.Average1Second, snapshot);
            AddAveragedSnapshot(_fiveSecondAccumulator, FIVE_SECOND_WEIGHT, History.Average5Seconds, snapshot);
            AddAveragedSnapshot(_thirtySecondAccumulator, THIRTY_SECOND_WEIGHT, History.Average30Seconds, snapshot);
            AddAveragedSnapshot(_oneMinuteAccumulator, ONE_MINUTE_WEIGHT, History.Average1Minute, snapshot);
            AddAveragedSnapshot(_fiveMinuteAccumulator, FIVE_MINUTE_WEIGHT, History.Average5Minutes, snapshot);
            AddAveragedSnapshot(_thirtyMinuteAccumulator, THIRTY_MINUTE_WEIGHT, History.Average30Minutes, snapshot);
        }

        static void AddAveragedSnapshot(
            PowerSnapshotAccumulator accumulator,
            int weight,
            RingBuffer<PowerSnapshot> history,
            PowerSnapshot snapshot)
        {
            accumulator.Add(snapshot);

            if (accumulator.Count >= weight)
                history.Add(accumulator.DrainAverage());
        }

        internal PowerScopeKey RefreshScope(long gameplayFrame)
        {
            _lastScopeRefreshFrame = gameplayFrame;
            _grids = _resolver.ResolveGrids(Requester, LinkType);
            Key = _resolver.ResolveKey(Requester, LinkType);
            if (HasCaptures)
                RebindBlockNeeds();
            return Key;
        }

        internal void Dispose()
        {
            CaptureCount = 0;
            ReleaseBlockNeeds();
        }

        void RebindBlockNeeds()
        {
            _nextNeededBlockLogics.Clear();
            for (var i = 0; i < _grids.Count; i++)
            {
                var logic = LcdModSessionComponent.GetOrCreateGridLogic(_grids[i]);
                if (logic != null)
                    _nextNeededBlockLogics.Add(logic);
            }

            foreach (var logic in _nextNeededBlockLogics)
            {
                if (_neededBlockLogics.Add(logic))
                    logic.RequestCapability(GridCapability.Blocks);
            }

            var removed = new List<GridLogic>();
            foreach (var logic in _neededBlockLogics)
            {
                if (!_nextNeededBlockLogics.Contains(logic))
                    removed.Add(logic);
            }
            foreach (var logic in removed)
            {
                _neededBlockLogics.Remove(logic);
                logic.Release(GridCapability.Blocks);
            }
            _nextNeededBlockLogics.Clear();
        }

        void ReleaseBlockNeeds()
        {
            foreach (var logic in _neededBlockLogics)
                logic.Release(GridCapability.Blocks);
            _neededBlockLogics.Clear();
            _nextNeededBlockLogics.Clear();
        }
    }
}
