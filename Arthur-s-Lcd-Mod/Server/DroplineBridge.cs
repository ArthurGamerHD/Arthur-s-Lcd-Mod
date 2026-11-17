using System;
using System.Collections.Generic;
using LcdMod.Common.Market;
using LcdMod.Common.Networking;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;

namespace LcdMod.Server
{
    // Host side of the Dropline delivery integration (workshop 3840411549): forwards client quote/submit requests
    // to the Dropline API, which answers synchronously on the game thread, and keeps each player's multi-store cart.
    internal sealed class DroplineBridge
    {
        internal const long ModApiId = 3840411549;
        const string RequestMessage = "Dropline.Api.Request";
        const int SupportedVersion = 1;

        sealed class Branch
        {
            public long Buyer;
            public double QuotedAt;
            public List<DroplineCartEntry> Lines;
        }

        readonly DroplineCartStore _carts = new DroplineCartStore();
        // Branch quotes by Dropline quote id, then the submitted ones until Dropline settles the order.
        readonly Dictionary<string, Branch> _branchQuotes = new Dictionary<string, Branch>(StringComparer.Ordinal);
        readonly Dictionary<string, Branch> _submitted = new Dictionary<string, Branch>(StringComparer.Ordinal);
        readonly Action<long, string, string, string> _orderChanged;
        Func<long, long, MyTuple<long, long, string>> _findShop;
        Func<long, long, long, List<MyTuple<long, MyDefinitionId, int>>, MyTuple<string, long, long, long, int, string>> _quote;
        Func<long, string, string> _submit;
        Func<long, long, List<MyTuple<long, string, int>>, string> _getDestinations;
        Func<long, long, long, string> _selectDestination;
        Action<Action<long, string, string, string>, bool> _subscribe;
        bool _registered;

        public DroplineBridge()
        {
            _orderChanged = OnOrderChanged;
        }

        public void LoadData()
        {
            MyAPIGateway.Utilities.RegisterMessageHandler(ModApiId, OnMessage);
            _registered = true;
            MyAPIGateway.Utilities.SendModMessage(ModApiId, RequestMessage);
        }

        public void SaveData()
        {
            _carts.Save();
        }

        public void UnloadData()
        {
            if (_registered)
                MyAPIGateway.Utilities.UnregisterMessageHandler(ModApiId, OnMessage);
            _registered = false;
            if (_subscribe != null)
                _subscribe(_orderChanged, false);
            _findShop = null;
            _quote = null;
            _submit = null;
            _getDestinations = null;
            _selectDestination = null;
            _subscribe = null;
            _branchQuotes.Clear();
            _submitted.Clear();
            _carts.Unload();
        }

        void OnMessage(object message)
        {
            var api = message as Dictionary<string, Delegate>;
            if (api == null)
                return;
            Delegate version;
            if (!api.TryGetValue("Version", out version) || !(version is Func<int>) || ((Func<int>)version)() != SupportedVersion)
                return;
            _findShop = Get<Func<long, long, MyTuple<long, long, string>>>(api, "FindShop");
            _quote = Get<Func<long, long, long, List<MyTuple<long, MyDefinitionId, int>>, MyTuple<string, long, long, long, int, string>>>(api, "Quote");
            _submit = Get<Func<long, string, string>>(api, "Submit");
            _getDestinations = Get<Func<long, long, List<MyTuple<long, string, int>>, string>>(api, "GetDestinations");
            _selectDestination = Get<Func<long, long, long, string>>(api, "SelectDestination");
            var subscribe = Get<Action<Action<long, string, string, string>, bool>>(api, "Subscribe");
            if (subscribe != null && _subscribe == null && MyAPIGateway.Session.IsServer)
                subscribe(_orderChanged, true);
            _subscribe = subscribe;
        }

        static T Get<T>(Dictionary<string, Delegate> api, string name) where T : class
        {
            Delegate value;
            return api.TryGetValue(name, out value) ? value as T : null;
        }

        public void HandleRequest(ulong senderSteamId, PacketDroplineRequest request)
        {
            if (request == null)
                return;
            var reply = new PacketDroplineReply { RequestId = request.RequestId };
            try
            {
                reply.Error = Handle(senderSteamId, request, reply);
            }
            catch (Exception e)
            {
                reply.Error = e.Message;
            }
            Deliver(senderSteamId, reply);
        }

        string Handle(ulong senderSteamId, PacketDroplineRequest request, PacketDroplineReply reply)
        {
            if (_findShop == null || _quote == null || _submit == null || _getDestinations == null || _selectDestination == null ||
                _subscribe == null)
                return "Dropline is not available on this server.";
            var buyer = MyAPIGateway.Players.TryGetIdentityId(senderSteamId);
            var host = MyEntities.GetEntityById(request.HostBlockEntityId) as IMyTerminalBlock;
            if (buyer == 0 || host == null || host.Closed || !host.HasPlayerAccess(buyer))
                return "Access denied.";

            switch (request.Kind)
            {
                case DroplineRequestKind.Submit:
                    return Submit(buyer, request.QuoteId);
                case DroplineRequestKind.GetCart:
                    return LoadCart(buyer, reply);
                case DroplineRequestKind.AddToCart:
                case DroplineRequestKind.SetCartQuantity:
                {
                    if (!ValidItem(request))
                        return "Invalid item or quantity.";
                    string error = request.Kind == DroplineRequestKind.AddToCart
                        ? (request.Quantity > 0 ? _carts.Add(buyer, request.StationId, request.TypeId, request.SubtypeId, request.Quantity) : "Invalid item or quantity.")
                        : Set(buyer, request);
                    return error ?? LoadCart(buyer, reply);
                }
                case DroplineRequestKind.ClearCart:
                    _carts.Clear(buyer, request.StationId);
                    return LoadCart(buyer, reply);
            }

            // FindShop still names the Shop when it has no destination yet; that is what the selector is for.
            var shop = _findShop(host.EntityId, buyer);
            if (shop.Item1 == 0)
                return shop.Item3;
            switch (request.Kind)
            {
                case DroplineRequestKind.Destinations:
                    return LoadDestinations(buyer, shop.Item1, reply);
                case DroplineRequestKind.SelectDestination:
                    return _selectDestination(buyer, shop.Item1, request.PadId) ?? LoadDestinations(buyer, shop.Item1, reply);
                case DroplineRequestKind.QuoteCart:
                    return QuoteBranch(buyer, shop.Item1, request, reply);
                case DroplineRequestKind.Quote:
                {
                    MyDefinitionId item;
                    if (request.Quantity <= 0 || !MyDefinitionId.TryParse(request.TypeId, request.SubtypeId, out item))
                        return "Invalid item or quantity.";
                    var lines = new List<MyTuple<long, MyDefinitionId, int>> { new MyTuple<long, MyDefinitionId, int>(request.StationId, item, request.Quantity) };
                    return Fill(reply, _quote(buyer, shop.Item1, request.PadId, lines));
                }
                default:
                    return "Unknown request.";
            }
        }

        string Set(long buyer, PacketDroplineRequest request)
        {
            if (request.Quantity < 0)
                return "Invalid item or quantity.";
            _carts.Set(buyer, request.StationId, request.TypeId, request.SubtypeId, request.Quantity);
            return null;
        }

        static bool ValidItem(PacketDroplineRequest request)
        {
            MyDefinitionId item;
            return request.StationId != 0 && MyDefinitionId.TryParse(request.TypeId, request.SubtypeId, out item);
        }

        // One store's lines as one Dropline order; the snapshot is what a successful order takes out of the cart.
        string QuoteBranch(long buyer, long shopId, PacketDroplineRequest request, PacketDroplineReply reply)
        {
            var snapshot = new List<DroplineCartEntry>();
            var lines = new List<MyTuple<long, MyDefinitionId, int>>();
            foreach (var entry in _carts.Lines(buyer))
            {
                MyDefinitionId item;
                if (entry.StationId != request.StationId || !MyDefinitionId.TryParse(entry.TypeId, entry.SubtypeId, out item))
                    continue;
                lines.Add(new MyTuple<long, MyDefinitionId, int>(entry.StationId, item, entry.Quantity));
                snapshot.Add(new DroplineCartEntry { StationId = entry.StationId, TypeId = entry.TypeId, SubtypeId = entry.SubtypeId, Quantity = entry.Quantity });
            }
            if (lines.Count == 0)
                return "This store has nothing in the cart.";
            string error = Fill(reply, _quote(buyer, shopId, request.PadId, lines));
            if (error != null)
                return error;
            PruneBranchQuotes();
            _branchQuotes[reply.QuoteId] = new Branch { Buyer = buyer, Lines = snapshot, QuotedAt = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds };
            return null;
        }

        string Submit(long buyer, string quoteId)
        {
            string error = _submit(buyer, quoteId);
            Branch branch;
            if (quoteId != null && _branchQuotes.TryGetValue(quoteId, out branch))
            {
                _branchQuotes.Remove(quoteId);
                if (error == null && branch.Buyer == buyer)
                    _submitted[quoteId] = branch;
            }
            return error;
        }

        // Dropline queues a purchase and charges it on its next tick; only a committed order empties its branch.
        void OnOrderChanged(long buyer, string orderId, string phase, string detail)
        {
            Branch branch;
            if (orderId == null || !_submitted.TryGetValue(orderId, out branch))
                return;
            switch (phase)
            {
                case "Scheduled":
                case "SpawnPending":
                case "Carrier approaching":
                case "Queued for airspace":
                case "Delivered":
                    _carts.Remove(branch.Buyer, branch.Lines);
                    _submitted.Remove(orderId);
                    return;
                case "Rejected":
                case "Review required":
                case "RecoveryRequired":
                    _submitted.Remove(orderId);
                    return;
            }
        }

        void PruneBranchQuotes()
        {
            double now = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;
            var stale = new List<string>();
            foreach (var entry in _branchQuotes)
                if (now - entry.Value.QuotedAt > 90)
                    stale.Add(entry.Key);
            foreach (var key in stale)
                _branchQuotes.Remove(key);
        }

        string LoadCart(long buyer, PacketDroplineReply reply)
        {
            var lines = _carts.Lines(buyer);
            reply.CartLines = new List<DroplineCartLineDto>(lines.Count);
            foreach (var entry in lines)
            {
                string stationName;
                reply.CartLines.Add(new DroplineCartLineDto
                {
                    StationId = entry.StationId,
                    TypeId = entry.TypeId,
                    SubtypeId = entry.SubtypeId,
                    Quantity = entry.Quantity,
                    UnitPrice = UnitPrice(entry, out stationName),
                    StationName = stationName,
                    Name = ItemName(entry)
                });
            }
            return null;
        }

        static long UnitPrice(DroplineCartEntry entry, out string stationName)
        {
            stationName = null;
            MyDefinitionId item;
            if (!MyDefinitionId.TryParse(entry.TypeId, entry.SubtypeId, out item))
                return -1;
            foreach (var faction in MyAPIGateway.Session.Factions.Factions.Values)
            {
                var station = faction.GetStationById(entry.StationId);
                if (station == null)
                    continue;
                stationName = station.Name;
                foreach (var offer in station.StoreItems)
                    if (offer.IsActive && offer.ItemType == ItemTypes.PhysicalItem && offer.StoreItemType == StoreItemTypes.Offer &&
                        offer.Item.HasValue && offer.Item.Value == item)
                        return offer.PricePerUnit;
                return -1;
            }
            return -1;
        }

        static string ItemName(DroplineCartEntry entry)
        {
            MyDefinitionId item;
            var definition = MyDefinitionId.TryParse(entry.TypeId, entry.SubtypeId, out item)
                ? Sandbox.Definitions.MyDefinitionManager.Static.GetPhysicalItemDefinition(item)
                : null;
            return definition == null ? entry.SubtypeId : definition.DisplayNameText ?? entry.SubtypeId;
        }

        string LoadDestinations(long buyer, long shopId, PacketDroplineReply reply)
        {
            var pads = new List<MyTuple<long, string, int>>();
            var error = _getDestinations(buyer, shopId, pads);
            if (error != null)
                return error;
            reply.Destinations = new List<DroplineDestinationDto>(pads.Count);
            foreach (var pad in pads)
                reply.Destinations.Add(new DroplineDestinationDto { PadId = pad.Item1, Name = pad.Item2, Flags = pad.Item3 });
            return null;
        }

        static string Fill(PacketDroplineReply reply, MyTuple<string, long, long, long, int, string> quote)
        {
            if (quote.Item6 != null)
                return quote.Item6;
            reply.QuoteId = quote.Item1;
            reply.Subtotal = quote.Item2;
            reply.Fee = quote.Item3;
            reply.Total = quote.Item4;
            reply.EtaSeconds = quote.Item5;
            return null;
        }

        static void Deliver(ulong steamId, PacketDroplineReply reply)
        {
            var player = MyAPIGateway.Session.Player;
            if (LcdModSessionComponent.Client != null && player != null && player.SteamUserId == steamId)
            {
                LcdModSessionComponent.Client.HandleLocalDroplineReply(reply);
                return;
            }
            LcdModSessionComponent.NetworkManager.TransmitToPlayer(reply, steamId);
        }
    }
}
