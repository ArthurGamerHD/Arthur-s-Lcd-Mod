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

        readonly Dictionary<DefenseScopeKey, DefenseDataService> _services =
            new Dictionary<DefenseScopeKey, DefenseDataService>();
        readonly List<DefenseScopeKey> _removeKeys = new List<DefenseScopeKey>();
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
            if (!_services.TryGetValue(key, out service))
            {
                service = new DefenseDataService(_resolver, _providers, requester, linkType, key);
                service.RefreshScope(_lastFrame);
                _services[key] = service;
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
            foreach (var pair in _services)
                _serviceSnapshot.Add(pair.Value);

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
            _services.Clear();
            _removeKeys.Clear();
            _serviceSnapshot.Clear();

            if (_loaded)
                for (int i = 0; i < _providers.Count; i++)
                    _providers[i].Unload();
            _loaded = false;
        }

        void ReindexIfNeeded(DefenseScopeKey oldKey, DefenseDataService service)
        {
            if (service == null || oldKey.Equals(service.Key))
                return;

            DefenseDataService existing;
            _services.Remove(oldKey);
            if (_services.TryGetValue(service.Key, out existing) && !ReferenceEquals(existing, service))
            {
                service.Release(_lastFrame);
                return;
            }

            _services[service.Key] = service;
        }

        void RemoveExpired(long gameplayFrame)
        {
            _removeKeys.Clear();
            foreach (var pair in _services)
            {
                var service = pair.Value;
                if (service != null && !service.HasCaptures &&
                    gameplayFrame - service.ReleasedFrame > RELEASE_GRACE_FRAMES)
                    _removeKeys.Add(pair.Key);
            }

            for (int i = 0; i < _removeKeys.Count; i++)
                _services.Remove(_removeKeys[i]);
            _removeKeys.Clear();
        }
    }
}
