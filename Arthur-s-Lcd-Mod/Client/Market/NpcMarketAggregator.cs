using System;
using System.Collections.Generic;
using System.IO;
using LcdMod.Client.Helpers;
using LcdMod.Common.Market;
using LcdMod.Common.Networking;
using Sandbox.Definitions;
using VRage;
using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Utils;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Market
{
    internal sealed class NpcMarketAggregator
    {
        const string OxygenIconPath = "Textures\\GUI\\Icons\\OxygenIcon.dds";
        const string HydrogenIconPath = "Textures\\GUI\\Icons\\HydrogenIcon.dds";

        readonly Dictionary<string, NpcMarketRow> _bestRows = new Dictionary<string, NpcMarketRow>(StringComparer.Ordinal);
        readonly List<NpcMarketRow> _rows = new List<NpcMarketRow>();

        public List<NpcMarketRow> BuildRows(PacketSyncNpcMarket packet, IMyTextSurface surface, NpcMarketMode mode,
            NpcMarketSortColumn sortColumn, bool sortDescending)
        {
            _bestRows.Clear();
            _rows.Clear();

            if (packet == null || packet.Sellers == null)
                return _rows;

            for (var sellerIndex = 0; sellerIndex < packet.Sellers.Count; sellerIndex++)
            {
                var seller = packet.Sellers[sellerIndex];
                if (seller == null || seller.Stations == null)
                    continue;

                for (var stationIndex = 0; stationIndex < seller.Stations.Count; stationIndex++)
                {
                    var station = seller.Stations[stationIndex];
                    if (station == null || station.Offers == null)
                        continue;

                    for (var offerIndex = 0; offerIndex < station.Offers.Count; offerIndex++)
                        AddOffer(seller, station, station.Offers[offerIndex], surface, mode);
                }
            }

            foreach (var row in _bestRows.Values)
                _rows.Add(row);

            _rows.Sort(delegate(NpcMarketRow a, NpcMarketRow b)
            {
                return CompareRows(a, b, sortColumn, sortDescending);
            });

            return _rows;
        }

        static int CompareRows(NpcMarketRow a, NpcMarketRow b, NpcMarketSortColumn sortColumn, bool descending)
        {
            var left = descending ? b : a;
            var right = descending ? a : b;
            int result;

            switch (sortColumn)
            {
                case NpcMarketSortColumn.Name:
                    result = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
                    break;
                case NpcMarketSortColumn.Trend:
                    result = left.EffectiveViewerChangePercent.CompareTo(right.EffectiveViewerChangePercent);
                    break;
                default:
                    result = left.PersonalizedCurrentPricePerUnit.CompareTo(right.PersonalizedCurrentPricePerUnit);
                    break;
            }

            return result != 0
                ? result
                : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        }

        void AddOffer(NpcMarketSellerFactionDto seller, NpcMarketStationDto station, NpcMarketOfferDto offer,
            IMyTextSurface surface, NpcMarketMode mode)
        {
            if (offer == null || offer.StoreItemType != mode.ToStoreItemType())
                return;

            var key = GetOfferKey(offer);
            if (string.IsNullOrEmpty(key))
                return;

            var sellerFactionId = seller != null ? seller.FactionId : 0L;
            var adjustedPrice = NpcMarketPricing.ApplyViewerPrice(sellerFactionId, offer.RawPricePerUnit, offer.StoreItemType);
            NpcMarketRow existing;
            if (_bestRows.TryGetValue(key, out existing) &&
                !IsBetterPrice(mode, adjustedPrice, existing.PersonalizedCurrentPricePerUnit))
                return;

            var previousAdjusted = offer.PreviousRawPricePerUnit > 0
                ? NpcMarketPricing.ApplyViewerPrice(sellerFactionId, offer.PreviousRawPricePerUnit, offer.StoreItemType)
                : 0;
            var presentation = ResolvePresentation(offer, surface);

            _bestRows[key] = new NpcMarketRow
            {
                ItemType = offer.ItemType,
                TypeId = offer.TypeId,
                SubtypeId = offer.SubtypeId,
                PrefabName = offer.PrefabName,
                PrefabTotalPcu = offer.PrefabTotalPcu,
                DisplayName = presentation.DisplayName,
                SpriteName = presentation.SpriteName,
                StoreItemType = offer.StoreItemType,
                RawPreviousPricePerUnit = offer.PreviousRawPricePerUnit,
                RawCurrentPricePerUnit = offer.RawPricePerUnit,
                PersonalizedPreviousPricePerUnit = previousAdjusted,
                PersonalizedCurrentPricePerUnit = adjustedPrice,
                PersonalizedTrendPercent = NpcMarketPercentages.GetPersonalizedTrendPercent(previousAdjusted, adjustedPrice),
                RelationBenefitPercent = NpcMarketPercentages.GetRelationBenefitPercent(mode, offer.RawPricePerUnit, adjustedPrice),
                EffectiveViewerChangePercent = NpcMarketPercentages.GetEffectiveViewerChangePercent(
                    offer.PreviousRawPricePerUnit, adjustedPrice),
                PricePerUnit = adjustedPrice,
                PreviousPricePerUnit = previousAdjusted,
                Amount = offer.Amount,
                DeltaPercent = NpcMarketPercentages.GetEffectiveViewerChangePercent(
                    offer.PreviousRawPricePerUnit, adjustedPrice),
                BestStationId = station != null ? station.StationId : 0L,
                BestStationName = station != null ? station.Name : string.Empty,
                BestStationPosition = station != null ? station.Position : Vector3D.Zero,
                BestSellerFactionId = sellerFactionId,
                BestSellerFactionTag = seller != null ? seller.Tag : string.Empty
            };
        }

        static bool IsBetterPrice(NpcMarketMode mode, int candidate, int currentBest)
        {
            return mode == NpcMarketMode.Buy ? candidate < currentBest : candidate > currentBest;
        }

        static string GetOfferKey(NpcMarketOfferDto offer)
        {
            if (offer == null)
                return string.Empty;

            switch (offer.ItemType)
            {
                case ItemTypes.PhysicalItem:
                    return HasDefinitionId(offer) ? "item:" + offer.TypeId + "/" + offer.SubtypeId : string.Empty;
                case ItemTypes.Gas:
                    return HasDefinitionId(offer) ? "gas:" + offer.TypeId + "/" + offer.SubtypeId : string.Empty;
                case ItemTypes.Oxygen:
                    return "gas:oxygen";
                case ItemTypes.Hydrogen:
                    return "gas:hydrogen";
                case ItemTypes.Grid:
                    return string.IsNullOrWhiteSpace(offer.PrefabName) ? string.Empty : "grid:" + offer.PrefabName;
                default:
                    return string.Empty;
            }
        }

        static bool HasDefinitionId(NpcMarketOfferDto offer)
        {
            return offer != null && !string.IsNullOrEmpty(offer.TypeId) && !string.IsNullOrEmpty(offer.SubtypeId);
        }

        static NpcMarketPresentation ResolvePresentation(NpcMarketOfferDto offer, IMyTextSurface surface)
        {
            if (offer == null)
                return new NpcMarketPresentation("Unknown listing", "MissingIcon");

            switch (offer.ItemType)
            {
                case ItemTypes.PhysicalItem:
                    return ResolvePhysicalItem(offer, surface);
                case ItemTypes.Grid:
                    return ResolvePrefab(offer);
                case ItemTypes.Oxygen:
                    return ResolveFixedGas("Oxygen", OxygenIconPath, offer);
                case ItemTypes.Hydrogen:
                    return ResolveFixedGas("Hydrogen", HydrogenIconPath, offer);
                case ItemTypes.Gas:
                    return ResolveGenericGas(offer);
                default:
                    return new NpcMarketPresentation("Unknown listing", "MissingIcon");
            }
        }

        static NpcMarketPresentation ResolvePhysicalItem(NpcMarketOfferDto offer, IMyTextSurface surface)
        {
            MyDefinitionId id;
            if (TryGetDefinitionId(offer, out id) && MyDefinitionManager.Static != null)
            {
                MyPhysicalItemDefinition def;
                if (MyDefinitionManager.Static.TryGetPhysicalItemDefinition(id, out def) && def != null)
                {
                    var display = !string.IsNullOrEmpty(def.DisplayNameText) ? def.DisplayNameText : offer.SubtypeId;
                    return new NpcMarketPresentation(display, TextureHelper.ResolveItemSprite(def, surface));
                }
            }

            return new NpcMarketPresentation(!string.IsNullOrEmpty(offer.SubtypeId) ? offer.SubtypeId : "Unknown item", "MissingIcon");
        }

        static NpcMarketPresentation ResolvePrefab(NpcMarketOfferDto offer)
        {
            var displayName = string.IsNullOrWhiteSpace(offer.PrefabName) ? "Ship" : offer.PrefabName;
            if (MyDefinitionManager.Static == null || string.IsNullOrWhiteSpace(offer.PrefabName))
                return new NpcMarketPresentation(displayName, "MissingIcon");

            var prefab = MyDefinitionManager.Static.GetPrefabDefinition(offer.PrefabName);
            if (prefab == null)
                return new NpcMarketPresentation(displayName, "MissingIcon");

            if (!string.IsNullOrWhiteSpace(prefab.DisplayNameString))
                displayName = prefab.DisplayNameString;
            else if (prefab.CubeGrids != null && prefab.CubeGrids.Length > 0 && !string.IsNullOrWhiteSpace(prefab.CubeGrids[0].DisplayName))
                displayName = prefab.CubeGrids[0].DisplayName;

            var iconPath = GetPrefabIconPath(prefab);
            var spriteName = string.IsNullOrWhiteSpace(iconPath)
                ? "MissingIcon"
                : TextureHelper.GetOrAddTextureForPath("Prefab:" + offer.PrefabName, displayName, iconPath);
            return new NpcMarketPresentation(displayName, spriteName);
        }

        static string GetPrefabIconPath(MyPrefabDefinition prefab)
        {
            if (prefab == null || prefab.Icons == null || prefab.Icons.Length == 0 || string.IsNullOrWhiteSpace(prefab.Icons[0]))
                return string.Empty;

            var path = prefab.Icons[0];
            if (prefab.Context != null && !prefab.Context.IsBaseGame && !Path.IsPathRooted(path) &&
                !string.IsNullOrWhiteSpace(prefab.Context.ModPath))
            {
                path = Path.Combine(prefab.Context.ModPath, path);
            }

            return path;
        }

        static NpcMarketPresentation ResolveFixedGas(string fallbackName, string fallbackIconPath, NpcMarketOfferDto offer)
        {
            var gas = ResolveGasDefinition(offer.ItemType == ItemTypes.Oxygen ? "Oxygen" : "Hydrogen", fallbackName, fallbackIconPath);
            return gas;
        }

        static NpcMarketPresentation ResolveGenericGas(NpcMarketOfferDto offer)
        {
            var subtype = !string.IsNullOrWhiteSpace(offer.SubtypeId) ? offer.SubtypeId : "Gas";
            return ResolveGasDefinition(subtype, subtype, string.Empty);
        }

        static NpcMarketPresentation ResolveGasDefinition(string subtype, string fallbackName, string fallbackIconPath)
        {
            var displayName = string.IsNullOrWhiteSpace(fallbackName) ? "Gas" : fallbackName;
            var iconPath = fallbackIconPath ?? string.Empty;

            if (MyDefinitionManager.Static != null && !string.IsNullOrWhiteSpace(subtype))
            {
                try
                {
                    var id = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), subtype);
                    MyGasProperties def;
                    if (MyDefinitionManager.Static.TryGetDefinition(id, out def) && def != null)
                    {
                        displayName = ResolveDefinitionDisplayName(def, displayName);
                        if (def.Icons != null && def.Icons.Length > 0 && !string.IsNullOrWhiteSpace(def.Icons[0]))
                            iconPath = def.Icons[0];
                    }
                }
                catch
                {
                }
            }

            var spriteName = string.IsNullOrWhiteSpace(iconPath)
                ? "MissingIcon"
                : TextureHelper.GetOrAddTextureForPath("Gas:" + subtype, displayName, iconPath);
            return new NpcMarketPresentation(displayName, spriteName);
        }

        static string ResolveDefinitionDisplayName(MyDefinitionBase definition, string fallback)
        {
            if (definition == null)
                return fallback;

            if (!string.IsNullOrEmpty(definition.DisplayNameString))
                return definition.DisplayNameString;

            if (definition.DisplayNameEnum.HasValue)
            {
                var text = MyTexts.Get(definition.DisplayNameEnum.Value);
                if (text != null && text.Length > 0)
                    return text.ToString();
            }

            if (!string.IsNullOrEmpty(definition.DisplayNameText))
                return definition.DisplayNameText;

            return fallback;
        }

        static bool TryGetDefinitionId(NpcMarketOfferDto offer, out MyDefinitionId id)
        {
            id = default(MyDefinitionId);
            return offer != null && HasDefinitionId(offer) && MyDefinitionId.TryParse(offer.TypeId + "/" + offer.SubtypeId, out id);
        }

        struct NpcMarketPresentation
        {
            public readonly string DisplayName;
            public readonly string SpriteName;

            public NpcMarketPresentation(string displayName, string spriteName)
            {
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Unknown listing" : displayName;
                SpriteName = string.IsNullOrWhiteSpace(spriteName) ? "MissingIcon" : spriteName;
            }
        }
    }
}
