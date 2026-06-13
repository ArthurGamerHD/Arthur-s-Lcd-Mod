using System;
using System.Collections.Generic;
using LcdMod.Client.GridData;
using LcdMod.Common.Helpers;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Power
{
    public sealed class PowerDataService
    {
        const int OneSecondWeight = 10;
        const int FiveSecondWeight = 5;
        const int ThirtySecondWeight = 30;
        const int OneMinuteWeight = 60;
        const int FiveMinuteWeight = 300;
        const int ThirtyMinuteWeight = 1800;

        readonly PowerScopeResolver _resolver;
        readonly SePowerCollector _collector = new SePowerCollector();
        readonly PowerSnapshotAccumulator _oneSecondAccumulator = new PowerSnapshotAccumulator();
        readonly PowerSnapshotAccumulator _fiveSecondAccumulator = new PowerSnapshotAccumulator();
        readonly PowerSnapshotAccumulator _thirtySecondAccumulator = new PowerSnapshotAccumulator();
        readonly PowerSnapshotAccumulator _oneMinuteAccumulator = new PowerSnapshotAccumulator();
        readonly PowerSnapshotAccumulator _fiveMinuteAccumulator = new PowerSnapshotAccumulator();
        readonly PowerSnapshotAccumulator _thirtyMinuteAccumulator = new PowerSnapshotAccumulator();
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

        public bool HasCaptures { get { return CaptureCount > 0; } }

        internal void AddCapture(GridLogic requester)
        {
            if (requester != null)
                Requester = requester;
            CaptureCount++;
            ReleasedFrame = 0;
        }

        internal void Release(long frame)
        {
            if (CaptureCount > 0)
                CaptureCount--;
            if (CaptureCount == 0)
                ReleasedFrame = frame;
        }

        internal void Update(long gameplayFrame)
        {
            if (gameplayFrame < 0)
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
            AddAveragedSnapshot(_oneSecondAccumulator, OneSecondWeight, History.Average1Second, snapshot);
            AddAveragedSnapshot(_fiveSecondAccumulator, FiveSecondWeight, History.Average5Seconds, snapshot);
            AddAveragedSnapshot(_thirtySecondAccumulator, ThirtySecondWeight, History.Average30Seconds, snapshot);
            AddAveragedSnapshot(_oneMinuteAccumulator, OneMinuteWeight, History.Average1Minute, snapshot);
            AddAveragedSnapshot(_fiveMinuteAccumulator, FiveMinuteWeight, History.Average5Minutes, snapshot);
            AddAveragedSnapshot(_thirtyMinuteAccumulator, ThirtyMinuteWeight, History.Average30Minutes, snapshot);
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
            return Key;
        }
    }
}
