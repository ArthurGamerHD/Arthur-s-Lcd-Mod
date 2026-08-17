using System.Collections.Generic;
using LcdMod.Client.GridData;
using LcdMod.Client.Modules.Defense.Providers;
using LcdMod.Client.Modules.Defense.Providers.DefenseShields;
using LcdMod.Client.Modules.Defense.Providers.Deflector;
using LcdMod.Client.Modules.Defense.Providers.EnergyShield;
using LcdMod.Client.Modules.Defense.Providers.NerdsShield;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Defense
{
    public sealed class DefenseDataModule
    {
        const long RELEASE_GRACE_FRAMES = 600L;

        // The dictionary selects a service for new captures. The list owns every live service,
        // including services whose keys converged while they still have active leases.
        readonly Dictionary<DefenseScopeKey, DefenseDataService> _services =
            new Dictionary<DefenseScopeKey, DefenseDataService>();
        readonly List<DefenseDataService> _allServices = new List<DefenseDataService>();
        readonly List<DefenseDataService> _removeServices = new List<DefenseDataService>();
        readonly List<DefenseDataService> _serviceSnapshot = new List<DefenseDataService>();
        readonly List<IShieldProvider> _providers = new List<IShieldProvider>();
        readonly DefenseScopeResolver _resolver = new DefenseScopeResolver();
        long _lastFrame;
        bool _loaded;

        public DefenseDataModule()
        {
            _providers.Add(new DeflectorShieldProvider());
            _providers.Add(new EnergyShieldProvider());
            _providers.Add(new NerdShieldProvider());
            _providers.Add(new DefenseShieldProvider());
        }

        public void Load()
        {
            if (_loaded)
                return;

            for (int i = 0; i < _providers.Count; i++)
                _providers[i].Load();
            _loaded = true;
        }

        public DefenseDataLease Capture(GridLogic requester, GridLinkTypeEnum linkType)
        {
            var key = _resolver.ResolveKey(requester, linkType);
            DefenseDataService service;
            if (!_services.TryGetValue(key, out service) && !TryIndexExistingService(key, out service))
            {
                service = new DefenseDataService(_resolver, _providers, requester, linkType, key);
                service.RefreshScope(_lastFrame);
                _services[key] = service;
                _allServices.Add(service);
            }

            service.AddCapture(requester);
            return new DefenseDataLease(this, service);
        }

        internal void Release(DefenseDataService service)
        {
            if (service != null)
                service.Release(_lastFrame);
        }

        public void Update(long gameplayFrame)
        {
            _lastFrame = gameplayFrame;
            for (int i = 0; i < _providers.Count; i++)
                _providers[i].Update(gameplayFrame);

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

        public void Unload()
        {
            foreach (var service in _allServices)
                if (service != null)
                    service.Dispose();
            _services.Clear();
            _allServices.Clear();
            _removeServices.Clear();
            _serviceSnapshot.Clear();

            if (_loaded)
                for (int i = 0; i < _providers.Count; i++)
                    _providers[i].Unload();
            _loaded = false;
        }

        void ReindexIfNeeded(DefenseScopeKey oldKey, DefenseDataService service)
        {
            if (service == null)
                return;

            DefenseDataService indexed;
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
                if (service != null && !service.HasCaptures &&
                    gameplayFrame - service.ReleasedFrame > RELEASE_GRACE_FRAMES)
                    _removeServices.Add(service);
            }

            for (int i = 0; i < _removeServices.Count; i++)
            {
                var service = _removeServices[i];
                DefenseDataService indexed;
                if (_services.TryGetValue(service.Key, out indexed) && ReferenceEquals(indexed, service))
                    _services.Remove(service.Key);
                service.Dispose();
                _allServices.Remove(service);
            }
            _removeServices.Clear();
        }

        bool TryIndexExistingService(DefenseScopeKey key, out DefenseDataService service)
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
