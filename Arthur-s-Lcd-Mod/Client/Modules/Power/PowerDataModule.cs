using System;
using System.Collections.Generic;
using LcdMod.Client.GridData;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Power
{
    public sealed class PowerDataModule
    {
        const long ReleaseGraceFrames = 600;
        readonly Dictionary<PowerScopeKey, PowerDataService> _services = new Dictionary<PowerScopeKey, PowerDataService>();
        readonly List<PowerScopeKey> _removeKeys = new List<PowerScopeKey>();
        readonly List<PowerDataService> _serviceSnapshot = new List<PowerDataService>();
        readonly PowerScopeResolver _resolver = new PowerScopeResolver();
        long _lastFrame;

        public PowerDataLease Capture(GridLogic requester, GridLinkTypeEnum linkType)
        {
            var key = _resolver.ResolveKey(requester, linkType);
            PowerDataService service;
            if (!_services.TryGetValue(key, out service))
            {
                service = new PowerDataService(_resolver, requester, linkType, key);
                service.RefreshScope(_lastFrame);
                _services[key] = service;
            }

            service.AddCapture(requester);
            return new PowerDataLease(this, service);
        }

        internal void Release(PowerDataService service)
        {
            if (service != null)
                service.Release(_lastFrame);
        }

        public void Update(long gameplayFrame)
        {
            _lastFrame = gameplayFrame;
            _serviceSnapshot.Clear();
            foreach (var kv in _services)
                _serviceSnapshot.Add(kv.Value);

            for (int i = 0; i < _serviceSnapshot.Count; i++)
            {
                var service = _serviceSnapshot[i];
                if (service == null)
                    continue;

                var oldKey = service.Key;
                service.Update(gameplayFrame);
                ReindexIfNeeded(oldKey, service);
            }

            RemoveExpired(gameplayFrame);
        }

        public void Clear()
        {
            _services.Clear();
            _removeKeys.Clear();
            _serviceSnapshot.Clear();
        }

        void ReindexIfNeeded(PowerScopeKey oldKey, PowerDataService service)
        {
            if (service == null || oldKey.Equals(service.Key))
                return;

            PowerDataService existing;
            _services.Remove(oldKey);
            if (_services.TryGetValue(service.Key, out existing) && !ReferenceEquals(existing, service))
            {
                // Keep the already indexed service; released duplicate will age out after grace.
                service.Release(_lastFrame);
                return;
            }

            _services[service.Key] = service;
        }

        void RemoveExpired(long gameplayFrame)
        {
            _removeKeys.Clear();
            foreach (var kv in _services)
            {
                var service = kv.Value;
                if (service != null && !service.HasCaptures && gameplayFrame - service.ReleasedFrame > ReleaseGraceFrames)
                    _removeKeys.Add(kv.Key);
            }

            for (int i = 0; i < _removeKeys.Count; i++)
                _services.Remove(_removeKeys[i]);
            _removeKeys.Clear();
        }
    }
}
