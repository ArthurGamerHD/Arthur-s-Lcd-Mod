using System;
using System.Collections.Generic;
using LcdMod.Common.Networking;
using Sandbox.ModAPI;

namespace LcdMod.Client.Market
{
    // Client side of the Dropline delivery integration; the host forwards to the Dropline API (see DroplineBridge).
    internal static class DroplineClient
    {
        const ulong WorkshopId = 3840411549;

        static readonly Dictionary<uint, Action<PacketDroplineReply>> Pending = new Dictionary<uint, Action<PacketDroplineReply>>();
        static uint _nextRequestId;
        static bool? _present;

        // Dropline is a world mod, so the client's mod list matches the host's.
        public static bool IsPresent
        {
            get
            {
                if (_present.HasValue)
                    return _present.Value;
                var mods = MyAPIGateway.Session == null ? null : MyAPIGateway.Session.Mods;
                if (mods == null)
                    return false;
                var present = false;
                foreach (var mod in mods)
                    if (mod.PublishedFileId == WorkshopId)
                    {
                        present = true;
                        break;
                    }
                _present = present;
                return present;
            }
        }

        public static void RequestDestinations(long hostBlockEntityId, Action<PacketDroplineReply> callback)
        {
            Send(new PacketDroplineRequest { Kind = DroplineRequestKind.Destinations, HostBlockEntityId = hostBlockEntityId }, callback);
        }

        public static void SelectDestination(long hostBlockEntityId, long padId, Action<PacketDroplineReply> callback)
        {
            Send(new PacketDroplineRequest
            {
                Kind = DroplineRequestKind.SelectDestination,
                HostBlockEntityId = hostBlockEntityId,
                PadId = padId
            }, callback);
        }

        public static void RequestQuote(long hostBlockEntityId, long padId, long stationId, string typeId, string subtypeId,
            int quantity, Action<PacketDroplineReply> callback)
        {
            Send(new PacketDroplineRequest
            {
                Kind = DroplineRequestKind.Quote,
                HostBlockEntityId = hostBlockEntityId,
                PadId = padId,
                StationId = stationId,
                TypeId = typeId,
                SubtypeId = subtypeId,
                Quantity = quantity
            }, callback);
        }

        public static void AddToCart(long hostBlockEntityId, long stationId, string typeId, string subtypeId, int quantity,
            Action<PacketDroplineReply> callback)
        {
            Send(new PacketDroplineRequest
            {
                Kind = DroplineRequestKind.AddToCart,
                HostBlockEntityId = hostBlockEntityId,
                StationId = stationId,
                TypeId = typeId,
                SubtypeId = subtypeId,
                Quantity = quantity
            }, callback);
        }

        public static void RequestCart(long hostBlockEntityId, Action<PacketDroplineReply> callback)
        {
            Send(new PacketDroplineRequest { Kind = DroplineRequestKind.GetCart, HostBlockEntityId = hostBlockEntityId }, callback);
        }

        public static void SetCartQuantity(long hostBlockEntityId, long stationId, string typeId, string subtypeId, int quantity,
            Action<PacketDroplineReply> callback)
        {
            Send(new PacketDroplineRequest
            {
                Kind = DroplineRequestKind.SetCartQuantity,
                HostBlockEntityId = hostBlockEntityId,
                StationId = stationId,
                TypeId = typeId,
                SubtypeId = subtypeId,
                Quantity = quantity
            }, callback);
        }

        // stationId 0 empties the whole cart.
        public static void ClearCart(long hostBlockEntityId, long stationId, Action<PacketDroplineReply> callback)
        {
            Send(new PacketDroplineRequest { Kind = DroplineRequestKind.ClearCart, HostBlockEntityId = hostBlockEntityId, StationId = stationId }, callback);
        }

        public static void QuoteCart(long hostBlockEntityId, long stationId, long padId, Action<PacketDroplineReply> callback)
        {
            Send(new PacketDroplineRequest
            {
                Kind = DroplineRequestKind.QuoteCart,
                HostBlockEntityId = hostBlockEntityId,
                StationId = stationId,
                PadId = padId
            }, callback);
        }

        public static void Submit(long hostBlockEntityId, string quoteId, Action<PacketDroplineReply> callback)
        {
            Send(new PacketDroplineRequest
            {
                Kind = DroplineRequestKind.Submit,
                HostBlockEntityId = hostBlockEntityId,
                QuoteId = quoteId
            }, callback);
        }

        public static void HandleReply(PacketDroplineReply reply)
        {
            Action<PacketDroplineReply> callback;
            if (reply == null || !Pending.TryGetValue(reply.RequestId, out callback))
                return;
            Pending.Remove(reply.RequestId);
            callback(reply);
        }

        public static void Unload()
        {
            Pending.Clear();
            _present = null;
        }

        static void Send(PacketDroplineRequest packet, Action<PacketDroplineReply> callback)
        {
            packet.RequestId = ++_nextRequestId;
            Pending[packet.RequestId] = callback;
            if (MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
            {
                LcdModSessionComponent.Server.HandleLocalDroplineRequest(packet);
                return;
            }
            LcdModSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }
    }
}
