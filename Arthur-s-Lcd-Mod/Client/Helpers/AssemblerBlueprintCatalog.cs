using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;

namespace LcdMod.Client.Helpers
{
    static class AssemblerBlueprintCatalog
    {
        static readonly object SyncRoot = new object();
        static readonly Dictionary<MyDefinitionId, MyBlueprintDefinitionBase> PrimaryBlueprintByItem =
            new Dictionary<MyDefinitionId, MyBlueprintDefinitionBase>();
        static bool _initialized;

        public static bool TryGetBlueprint(
            IMyAssembler assembler,
            MyDefinitionId itemDefinitionId,
            out MyBlueprintDefinitionBase blueprint)
        {
            blueprint = null;
            if (assembler == null)
                return false;

            EnsureInitialized();
            if (!PrimaryBlueprintByItem.TryGetValue(itemDefinitionId, out blueprint) || blueprint == null)
                return false;

            try
            {
                return assembler.CanUseBlueprint(blueprint.Id);
            }
            catch (Exception exception)
            {
                ErrorHandlerHelper.LogError(exception, typeof(AssemblerBlueprintCatalog));
                blueprint = null;
                return false;
            }
        }

        public static string GetAssemblerSubtype(IMyAssembler assembler)
        {
            if (assembler == null)
                return string.Empty;

            var definitionId = assembler.BlockDefinition;
            return string.IsNullOrEmpty(definitionId.SubtypeName)
                ? definitionId.ToString()
                : definitionId.SubtypeName;
        }

        static void EnsureInitialized()
        {
            if (_initialized || MyDefinitionManager.Static == null)
                return;

            lock (SyncRoot)
            {
                if (_initialized || MyDefinitionManager.Static == null)
                    return;

                foreach (var blueprint in MyDefinitionManager.Static.GetBlueprintDefinitions())
                {
                    if (blueprint == null || blueprint.Results == null || blueprint.Results.Length == 0)
                        continue;

                    var itemDefinitionId = blueprint.Results[0].Id;
                    if (blueprint.IsPrimary || !PrimaryBlueprintByItem.ContainsKey(itemDefinitionId))
                        PrimaryBlueprintByItem[itemDefinitionId] = blueprint;
                }

                _initialized = true;
            }
        }
    }
}
