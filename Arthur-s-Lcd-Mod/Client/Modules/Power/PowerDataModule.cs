using System.Collections.Generic;
using LcdMod.Client.GridData;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Power
{
    public sealed class PowerDataModule
    {
        const long RELEASE_GRACE_FRAMES = 600;
        // The dictionary selects a service for new captures. The list owns every live service,
        // including services whose keys converged while they still have active leases.
        readonly Dictionary<PowerScopeKey, PowerDataService> _services = new Dictionary<PowerScopeKey, PowerDataService>();
        readonly List<PowerDataService> _allServices = new List<PowerDataService>();
        readonly List<PowerDataService> _removeServices = new List<PowerDataService>();
        readonly List<PowerDataService> _serviceSnapshot = new List<PowerDataService>();
        readonly PowerScopeResolver _resolver = new PowerScopeResolver();
        long _lastFrame;

        public PowerDataLease Capture(GridLogic requester, GridLinkTypeEnum linkType)
        {
            var key = _resolver.ResolveKey(requester, linkType);
            PowerDataService service;
            if (!_services.TryGetValue(key, out service) && !TryIndexExistingService(key, out service))
            {
                service = new PowerDataService(_resolver, requester, linkType, key);
                service.RefreshScope(_lastFrame);
                _services[key] = service;
                _allServices.Add(service);
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
            _serviceSnapshot.AddRange(_allServices);

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
            foreach (var service in _allServices)
                service.Dispose();
            _services.Clear();
            _allServices.Clear();
            _removeServices.Clear();
            _serviceSnapshot.Clear();
        }

        void ReindexIfNeeded(PowerScopeKey oldKey, PowerDataService service)
        {
            if (service == null)
                return;

            PowerDataService indexed;
            if (!oldKey.Equals(service.Key) &&
                _services.TryGetValue(oldKey, out indexed) &&
                ReferenceEquals(indexed, service))
                _services.Remove(oldKey);

            if (_services.TryGetValue(service.Key, out indexed))
                return;

            _services[service.Key] = service;
        }

        void RemoveExpired(long gameplayFrame)
        {
            _removeServices.Clear();
            foreach (var service in _allServices)
            {
                if (service != null && !service.HasCaptures && gameplayFrame - service.ReleasedFrame > RELEASE_GRACE_FRAMES)
                    _removeServices.Add(service);
            }

            for (int i = 0; i < _removeServices.Count; i++)
            {
                var service = _removeServices[i];
                PowerDataService indexed;
                if (_services.TryGetValue(service.Key, out indexed) && ReferenceEquals(indexed, service))
                    _services.Remove(service.Key);
                service.Dispose();
                _allServices.Remove(service);
            }
            _removeServices.Clear();
        }

        bool TryIndexExistingService(PowerScopeKey key, out PowerDataService service)
        {
            service = null;
            for (var i = 0; i < _allServices.Count; i++)
            {
                var candidate = _allServices[i];
                if (candidate == null || !candidate.Key.Equals(key))
                    continue;

                if (service == null || candidate.HasCaptures)
                    service = candidate;
                if (candidate.HasCaptures)
                    break;
            }

            if (service == null)
                return false;

            _services[key] = service;
            return true;
        }
    }
}
