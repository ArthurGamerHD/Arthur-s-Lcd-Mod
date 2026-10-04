using System;
using System.Collections.Generic;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Apps.Inventory.Crafting
{
    /// <summary>Owns live assembler subscriptions for one craft window. Request keys are MyDefinitionId values.</summary>
    internal sealed class CraftSession : IDisposable
    {
        readonly LinkedTypedBlockSourceSet<IMyAssembler> _sources =
            new LinkedTypedBlockSourceSet<IMyAssembler>(blocks => blocks.Assemblers);
        readonly Dictionary<long, CraftAssemblerModel> _entries = new Dictionary<long, CraftAssemblerModel>();
        readonly CraftDialogViewModel _model;
        bool _initialized;

        public CraftSession(GridLogic grid, CraftDialogViewModel model)
        {
            _model = model;
            _sources.Changed += Refresh;
            try { _sources.Bind(grid, GridLinkTypeEnum.Physical); }
            catch { Dispose(); throw; }
        }

        void Refresh()
        {
            var next = new List<CraftAssemblerModel>();
            var seen = new HashSet<long>();
            foreach (var source in _sources.Sources)
            foreach (var assembler in source)
            {
                if (assembler == null || assembler.Closed || assembler.MarkedForClose || !seen.Add(assembler.EntityId)) continue;
                var blueprints = new Dictionary<MyDefinitionId, MyBlueprintDefinitionBase>();
                foreach (var request in _model.Requests)
                {
                    MyBlueprintDefinitionBase blueprint;
                    var item = (MyDefinitionId)request.Key;
                    if (!blueprints.ContainsKey(item) && AssemblerBlueprintCatalog.TryGetBlueprint(assembler, item, out blueprint))
                        blueprints[item] = blueprint;
                }
                if (blueprints.Count == 0) continue;

                CraftAssemblerModel entry;
                if (!_entries.TryGetValue(assembler.EntityId, out entry))
                    entry = new CraftAssemblerModel { Id = assembler.EntityId };
                entry.Idle = assembler.IsQueueEmpty && !assembler.IsProducing;
                var definition = MyDefinitionManager.Static.GetCubeBlockDefinition(assembler.BlockDefinition) as MyAssemblerDefinition;
                entry.Speed = definition == null ? 1 : definition.AssemblySpeed;
                string name = string.IsNullOrWhiteSpace(assembler.CustomName) ? assembler.DisplayNameText : assembler.CustomName;
                if (string.IsNullOrWhiteSpace(name)) name = LocHelper.GetLoc("LcdMod_CraftDialog_Assembler_FallbackName");
                entry.Label = entry.Idle
                    ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc("LcdMod_CraftDialog_AssemblerOption_WithStatus"),
                        name, LocHelper.GetLoc("LcdMod_CraftDialog_Assembler_Status_Idle"), entry.Speed.ToString("0.##", FormatingHelper.Culture))
                    : string.Format(FormatingHelper.Culture, LocHelper.GetLoc("LcdMod_CraftDialog_AssemblerOption"),
                        name, entry.Speed.ToString("0.##", FormatingHelper.Culture));
                var target = assembler;
                entry.CanCraft = request => blueprints.ContainsKey((MyDefinitionId)request.Key);
                entry.Queue = (request, amount) =>
                {
                    if (target.Closed || target.MarkedForClose)
                        throw new InvalidOperationException("The selected assembler is no longer available.");
                    var item = (MyDefinitionId)request.Key;
                    var recipe = blueprints[item];
                    double queueAmount = amount;
                    if (recipe.Results != null)
                        foreach (var result in recipe.Results)
                            if (result.Id.Equals(item) && (double)result.Amount > 0)
                            {
                                queueAmount = Math.Ceiling(amount / (double)result.Amount);
                                break;
                            }
                    target.AddQueueItem(recipe.Id, queueAmount);
                };
                next.Add(entry);
            }
            next.Sort((left, right) =>
            {
                int order = right.Idle.CompareTo(left.Idle);
                if (order == 0) order = right.Speed.CompareTo(left.Speed);
                return order == 0 ? string.Compare(left.Label, right.Label, StringComparison.CurrentCulture) : order;
            });
            if (!_initialized && next.Count > 0)
            {
                if (_model.IsMultiple)
                    foreach (var entry in next) entry.IsSelected = entry.Speed >= 1;
                else
                    next[0].IsSelected = true;
            }
            _initialized = true;
            _entries.Clear();
            foreach (var entry in next) _entries.Add(entry.Id, entry);
            _model.SetAssemblers(next);
        }

        public void Dispose()
        {
            _sources.Changed -= Refresh;
            _sources.Dispose();
            _model.SetAssemblers(new CraftAssemblerModel[0]);
            _entries.Clear();
        }
    }
}
