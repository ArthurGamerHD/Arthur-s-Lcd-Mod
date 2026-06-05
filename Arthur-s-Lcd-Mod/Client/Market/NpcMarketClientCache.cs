using System;
using System.Collections.Generic;
using LcdMod.Common.Market;
using LcdMod.Common.Networking;
using Sandbox.ModAPI;

namespace LcdMod.Client.Market
{
    internal static class NpcMarketClientCache
    {
        static readonly long RetryDelayTicks = WorldTime.FromSeconds(5);
        static uint _nextRequestId;
        static readonly Dictionary<NpcMarketClientCacheKey, NpcMarketClientEntry> Entries =
            new Dictionary<NpcMarketClientCacheKey, NpcMarketClientEntry>();

        public static event Action Updated;

        public static void Reset()
        {
            Entries.Clear();
            Updated = null;
            _nextRequestId = 0;
        }

        public static void Update(NpcMarketClientCacheKey key)
        {
            var entry = GetOrCreate(key);
            var now = WorldTime.NowElapsedTicks();
            if (entry.Waiting && now >= entry.NextRequestAtTicks)
                SendRequest(key, entry.ActiveNoCache, entry.ActiveRequestId);

            if (!entry.Waiting && entry.Snapshot != null && entry.Snapshot.NextEconomyTickWorldElapsedTicks > 0 &&
                now >= entry.Snapshot.NextEconomyTickWorldElapsedTicks)
            {
                RequestRefresh(key, true);
            }
        }

        public static void EnsureRequested(NpcMarketClientCacheKey key)
        {
            var entry = GetOrCreate(key);
            if (entry.Snapshot == null && !entry.Waiting)
                RequestRefresh(key, false);
        }

        public static PacketSyncNpcMarket GetSnapshot(NpcMarketClientCacheKey key)
        {
            NpcMarketClientEntry entry;
            return Entries.TryGetValue(key, out entry) ? entry.Snapshot : null;
        }

        public static bool CanForceRefresh(NpcMarketClientCacheKey key)
        {
            var snapshot = GetSnapshot(key);
            return snapshot == null || WorldTime.NowElapsedTicks() >= snapshot.NextNoCacheAllowedAtWorldElapsedTicks;
        }

        public static void RequestRefresh(NpcMarketClientCacheKey key, bool noCache)
        {
            SendRequest(key, noCache, ++_nextRequestId);
        }

        public static void HandleSync(PacketSyncNpcMarket packet)
        {
            if (packet == null || packet.Scope == null)
                return;

            var key = new NpcMarketClientCacheKey(packet.Scope.HostBlockEntityId, packet.Scope.HostSurfaceIndex);
            var entry = GetOrCreate(key);
            if (entry.Waiting && packet.RequestId != entry.ActiveRequestId)
                return;

            entry.Snapshot = packet;
            entry.Waiting = false;
            entry.ActiveRequestId = packet.RequestId;
            var updated = Updated;
            if (updated != null)
                updated();
        }

        static void SendRequest(NpcMarketClientCacheKey key, bool noCache, uint requestId)
        {
            if (requestId == 0)
                requestId = ++_nextRequestId;

            var entry = GetOrCreate(key);
            entry.ActiveRequestId = requestId;
            entry.ActiveNoCache = noCache;
            entry.Waiting = true;
            entry.NextRequestAtTicks = WorldTime.NowElapsedTicks() + RetryDelayTicks;
            var packet = new PacketRequestNpcMarket
            {
                RequestId = requestId,
                NoCache = noCache,
                HostBlockEntityId = key.HostBlockEntityId,
                HostSurfaceIndex = key.HostSurfaceIndex
            };

            if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer &&
                LcdModSessionComponent.Server != null)
            {
                LcdModSessionComponent.Server.HandleLocalRequestNpcMarket(packet);
                return;
            }

            LcdModSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }

        static NpcMarketClientEntry GetOrCreate(NpcMarketClientCacheKey key)
        {
            NpcMarketClientEntry entry;
            if (!Entries.TryGetValue(key, out entry))
            {
                entry = new NpcMarketClientEntry();
                Entries[key] = entry;
            }

            return entry;
        }
    }

    internal struct NpcMarketClientCacheKey : IEquatable<NpcMarketClientCacheKey>
    {
        public readonly long HostBlockEntityId;
        public readonly int HostSurfaceIndex;

        public NpcMarketClientCacheKey(long hostBlockEntityId, int hostSurfaceIndex)
        {
            HostBlockEntityId = hostBlockEntityId;
            HostSurfaceIndex = hostSurfaceIndex;
        }

        public bool Equals(NpcMarketClientCacheKey other)
        {
            return HostBlockEntityId == other.HostBlockEntityId && HostSurfaceIndex == other.HostSurfaceIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is NpcMarketClientCacheKey && Equals((NpcMarketClientCacheKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (HostBlockEntityId.GetHashCode() * 397) ^ HostSurfaceIndex;
            }
        }
    }

    internal sealed class NpcMarketClientEntry
    {
        public PacketSyncNpcMarket Snapshot;
        public uint ActiveRequestId;
        public long NextRequestAtTicks;
        public bool Waiting;
        public bool ActiveNoCache;
    }
}
