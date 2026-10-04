using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace LcdMod.Client.Apps.CargoFilled
{
    /// <summary>Opens the container action wizard for a cargo block and runs the chosen transfer (server-authoritative).</summary>
    internal static class ContainerActionLauncher
    {
        public static bool Show(App app, IMyTerminalBlock source, List<IMyTerminalBlock> candidates,
            IEnumerable<string> savedItems, IEnumerable<string> savedCategories,
            Action<List<string>, List<string>> saveFilter, Action<string> status)
        {
            var targets = new List<ContainerChoice>();
            var byId = new Dictionary<string, IMyTerminalBlock>();
            foreach (var block in candidates)
            {
                if (block == null || block == source || !block.HasInventory)
                    continue;
                string id = block.EntityId.ToString(FormatingHelper.Culture);
                if (byId.ContainsKey(id))
                    continue;
                byId[id] = block;
                targets.Add(new ContainerChoice { Id = id, Label = SafeName(block) });
            }

            var model = new ContainerActionViewModel(SafeName(source), targets, LoadItems, savedItems, savedCategories,
                request => Apply(source, byId, request, status), saveFilter);
            return app.ShowDialog(new ContainerActionView(model));
        }

        static IList<ContainerChoice> LoadItems()
        {
            var groups = ItemCategoryHelper.Groups;
            var choices = new List<ContainerChoice>();
            for (var i = 0; i < groups.Length; i++)
                choices.Add(new ContainerChoice
                {
                    Id = groups[i], Label = ItemCategoryHelper.GetGroupDisplayName(groups[i]), IsCategory = true
                });

            var types = new List<KeyValuePair<int, ContainerChoice>>();
            var seenKeys = new HashSet<string>();
            var seenNames = new HashSet<string>();
            foreach (var definitionBase in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var definition = definitionBase as MyPhysicalItemDefinition;
                if (definition == null || !definition.Public || !PassesWhiteList(definition))
                    continue;
                string key = definition.Id.TypeId + "/" + definition.Id.SubtypeName;
                if (!seenKeys.Add(key))
                    continue;
                string name = !string.IsNullOrEmpty(definition.DisplayNameText) ? definition.DisplayNameText : definition.Id.SubtypeName;
                int order;
                string category = CategoryFor(definition.Id.TypeId.ToString(), out order);
                if (!seenNames.Add(category + (name ?? string.Empty).ToLowerInvariant()))
                    continue;
                types.Add(new KeyValuePair<int, ContainerChoice>(order, new ContainerChoice { Id = key, Label = name ?? string.Empty }));
            }
            types.Sort((a, b) => a.Key != b.Key
                ? a.Key.CompareTo(b.Key)
                : string.Compare(a.Value.Label, b.Value.Label, StringComparison.OrdinalIgnoreCase));
            foreach (var type in types)
                choices.Add(type.Value);
            return choices;
        }

        static void Apply(IMyTerminalBlock source, Dictionary<string, IMyTerminalBlock> byId, ContainerActionRequest request,
            Action<string> status)
        {
            string message = null;
            try
            {
                var targets = new List<IMyTerminalBlock>();
                foreach (string id in request.Targets)
                {
                    IMyTerminalBlock block;
                    if (byId.TryGetValue(id, out block))
                        targets.Add(block);
                }

                var keys = new HashSet<string>(request.Items);
                if (request.Categories.Count > 0)
                {
                    AddCategoryItemKeys(source, request.Categories, keys);
                    foreach (var target in targets)
                        AddCategoryItemKeys(target, request.Categories, keys);
                }
                if (keys.Count == 0)
                    return;

                var mode = ToMode(request.Transfer);
                if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer)
                {
                    int moved = InventoryDistributorCommon.Execute(source, targets, keys, mode);
                    message = string.Format(LocHelper.GetLoc(Constants.MOD_PREFIX + "CargoActions_ActionDone"),
                        ContainerActionViewModel.TransferText(request.Transfer), moved);
                }
                else
                {
                    var targetIds = new long[targets.Count];
                    for (var i = 0; i < targets.Count; i++)
                        targetIds[i] = targets[i].EntityId;
                    var keyArray = new string[keys.Count];
                    keys.CopyTo(keyArray);
                    LcdModSessionComponent.NetworkManager.TransmitToServer(
                        new PacketTransferItems(source.EntityId, targetIds, keyArray, (int)mode), false);
                    message = LocHelper.GetLoc(Constants.MOD_PREFIX + "Cargo_SortRequested");
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(ContainerActionLauncher));
            }
            if (status != null && !string.IsNullOrEmpty(message))
                status(message);
        }

        static TransferMode ToMode(ContainerTransfer transfer)
        {
            return transfer == ContainerTransfer.Send ? TransferMode.Send
                : transfer == ContainerTransfer.Receive ? TransferMode.Receive
                : TransferMode.Balance;
        }

        static void AddCategoryItemKeys(IMyTerminalBlock block, List<string> categories, HashSet<string> keys)
        {
            if (block == null || !block.HasInventory)
                return;
            var inventory = block.GetInventory(0);
            if (inventory == null)
                return;
            var items = new List<MyInventoryItem>();
            inventory.GetItems(items);
            foreach (var item in items)
                foreach (var category in categories)
                    if (item.Type.TypeId.EndsWith(category, StringComparison.OrdinalIgnoreCase))
                    {
                        keys.Add(InventoryDistributorCommon.KeyOf(item.Type));
                        break;
                    }
        }

        static bool PassesWhiteList(MyPhysicalItemDefinition definition)
        {
            var id = definition.Id.ToString();
            return !id.Contains("_TreeObject/")
                   && !id.Contains("GunObject/GoodAIReward")
                   && !id.Contains("GunObject/CubePlacerItem");
        }

        static string CategoryFor(string typeId, out int order)
        {
            var type = typeId ?? string.Empty;
            const string prefix = "MyObjectBuilder_";
            if (type.StartsWith(prefix, StringComparison.Ordinal))
                type = type.Substring(prefix.Length);
            var groups = ItemCategoryHelper.Groups;
            for (var i = 0; i < groups.Length; i++)
                if (type.StartsWith(groups[i], StringComparison.OrdinalIgnoreCase))
                {
                    order = i;
                    return ItemCategoryHelper.GetGroupDisplayName(groups[i]);
                }
            order = groups.Length + 1;
            return string.IsNullOrEmpty(type) ? "Other" : type;
        }

        static string SafeName(IMyTerminalBlock block)
        {
            if (block == null)
                return "?";
            try
            {
                var name = block.CustomName;
                if (string.IsNullOrEmpty(name)) name = block.DisplayNameText;
                if (string.IsNullOrEmpty(name)) name = block.BlockDefinition.SubtypeName;
                return string.IsNullOrEmpty(name) ? "?" : name;
            }
            catch
            {
                return "?";
            }
        }
    }
}
