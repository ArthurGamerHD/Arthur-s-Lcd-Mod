using System;
using System.Collections.Generic;
using LcdMod.Client.Farm;
using LcdMod.Client.GridData;
using LcdMod.Common.Helpers;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.Helpers
{
    internal sealed class FarmGrowthHelper
    {
        const float MULTIPLIER_EPSILON = 0.0001f;

        readonly Dictionary<MyDefinitionId, FarmSeedGrowthProfile> _uniqueSeedByOutputItem =
            new Dictionary<MyDefinitionId, FarmSeedGrowthProfile>();

        readonly HashSet<MyDefinitionId> _ambiguousOutputItems = new HashSet<MyDefinitionId>();

        readonly Dictionary<MyStringHash, FarmPlotGrowthProfile> _plotProfileByBlockSubtype =
            new Dictionary<MyStringHash, FarmPlotGrowthProfile>();

        readonly HashSet<long> _loggedUnresolvedPlots = new HashSet<long>();
        bool _seedCacheBuilt;

        public bool TryResolveGrowthProfile(FarmPlotEntry plot, out FarmGrowthProfile profile)
        {
            profile = null;
            if (plot == null || plot.Logic == null || !plot.Logic.IsPlantPlanted)
                return false;

            FarmPlotGrowthProfile plotProfile;
            if (!TryResolvePlotProfile(plot, out plotProfile))
            {
                LogUnresolved(plot, "plot definition unresolved");
                return false;
            }

            FarmSeedGrowthProfile seedProfile;
            if (!TryResolveUniqueSeedByOutput(plot.Logic.OutputItem, out seedProfile))
            {
                LogUnresolved(plot, "seed output mapping unresolved");
                return false;
            }

            profile = new FarmGrowthProfile
            {
                PlotLogicDefinitionId = plotProfile.DefinitionId,
                PlotGrowthMinutes = plotProfile.PlantGrowthMinutes,
                SeedDefinitionId = seedProfile.SeedDefinitionId,
                OutputItemId = seedProfile.OutputItemId,
                SeedGrowthTimeMultiplier = seedProfile.GrowthTimeMultiplier
            };
            return true;
        }

        bool TryResolveUniqueSeedByOutput(MyDefinitionId outputItem, out FarmSeedGrowthProfile profile)
        {
            profile = null;
            if (outputItem.Equals(default(MyDefinitionId)) || MyDefinitionManager.Static == null)
                return false;

            EnsureSeedCache();
            return !_ambiguousOutputItems.Contains(outputItem) &&
                   _uniqueSeedByOutputItem.TryGetValue(outputItem, out profile);
        }

        bool TryResolvePlotProfile(FarmPlotEntry plot, out FarmPlotGrowthProfile profile)
        {
            profile = null;
            if (plot == null || plot.Block == null || MyDefinitionManager.Static == null)
                return false;

            var blockDefinition = plot.Block.BlockDefinition;
            var blockSubtype = MyStringHash.GetOrCompute(blockDefinition.SubtypeId);
            if (_plotProfileByBlockSubtype.TryGetValue(blockSubtype, out profile))
                return profile != null;

            if (TryResolvePlotProfileFromContainer(blockDefinition.TypeId, blockSubtype, out profile))
            {
                _plotProfileByBlockSubtype[blockSubtype] = profile;
                return true;
            }

            MyComponentDefinitionBase component;
            var componentType = (MyObjectBuilderType)typeof(MyObjectBuilder_FarmPlotLogic);
            if (!MyDefinitionManager.Static.TryGetComponentDefinition(componentType, blockSubtype, out component))
            {
                _plotProfileByBlockSubtype[blockSubtype] = null;
                return false;
            }

            if (!TryCreatePlotProfile(component, out profile))
                return false;

            _plotProfileByBlockSubtype[blockSubtype] = profile;
            return true;
        }

        static bool TryResolvePlotProfileFromContainer(
            MyObjectBuilderType blockType,
            MyStringHash blockSubtype,
            out FarmPlotGrowthProfile profile)
        {
            profile = null;

            MyContainerDefinition container;
            if (!MyDefinitionManager.Static.TryGetContainerDefinition(blockType, blockSubtype, out container) ||
                container == null ||
                container.DefaultComponents == null)
            {
                return false;
            }

            var farmLogicType = (MyObjectBuilderType)typeof(MyObjectBuilder_FarmPlotLogic);
            for (int i = 0; i < container.DefaultComponents.Count; i++)
            {
                var defaultComponent = container.DefaultComponents[i];
                if (defaultComponent == null || defaultComponent.BuilderType != farmLogicType)
                    continue;

                var componentSubtype = defaultComponent.SubtypeId.HasValue
                    ? defaultComponent.SubtypeId.Value
                    : MyStringHash.NullOrEmpty;

                MyComponentDefinitionBase component;
                if (!MyDefinitionManager.Static.TryGetComponentDefinition(farmLogicType, componentSubtype, out component))
                    return false;

                return TryCreatePlotProfile(component, out profile);
            }

            return false;
        }

        static bool TryCreatePlotProfile(MyComponentDefinitionBase component, out FarmPlotGrowthProfile profile)
        {
            profile = null;
            if (component == null)
                return false;

            var builder = component.GetObjectBuilder() as MyObjectBuilder_FarmPlotLogicDefinition;
            if (builder == null)
                return false;

            profile = new FarmPlotGrowthProfile
            {
                DefinitionId = component.Id,
                PlantGrowthMinutes = Math.Max(builder.PlantGrowthMinutes, 0.1f)
            };
            return true;
        }

        void EnsureSeedCache()
        {
            if (_seedCacheBuilt || MyDefinitionManager.Static == null)
                return;

            _seedCacheBuilt = true;
            _uniqueSeedByOutputItem.Clear();
            _ambiguousOutputItems.Clear();

            foreach (var definition in MyDefinitionManager.Static.GetSeedDefinitions())
            {
                if (definition == null)
                    continue;

                var builder = definition.GetObjectBuilder() as MyObjectBuilder_SeedItemDefinition;
                if (builder == null)
                    continue;

                var outputId = (MyDefinitionId)builder.OutputItemDefinitionId;
                if (outputId.Equals(default(MyDefinitionId)))
                    continue;

                var profile = new FarmSeedGrowthProfile
                {
                    SeedDefinitionId = definition.Id,
                    OutputItemId = outputId,
                    GrowthTimeMultiplier = MathHelper.Clamp(builder.GrowthTimeMultiplier, 0.01f, 100f)
                };

                FarmSeedGrowthProfile existing;
                if (_uniqueSeedByOutputItem.TryGetValue(outputId, out existing))
                {
                    if (Math.Abs(existing.GrowthTimeMultiplier - profile.GrowthTimeMultiplier) > MULTIPLIER_EPSILON)
                    {
                        _uniqueSeedByOutputItem.Remove(outputId);
                        _ambiguousOutputItems.Add(outputId);
                    }

                    continue;
                }

                if (!_ambiguousOutputItems.Contains(outputId))
                    _uniqueSeedByOutputItem[outputId] = profile;
            }
        }

        void LogUnresolved(FarmPlotEntry plot, string reason)
        {
            var block = plot?.Block;
            var entityId = block?.EntityId ?? 0L;
            if (entityId == 0L || !_loggedUnresolvedPlots.Add(entityId))
                return;

            try
            {
                var subtype = block != null ? block.BlockDefinition.SubtypeId : string.Empty;
                var output = plot != null && plot.Logic != null ? plot.Logic.OutputItem.ToString() : string.Empty;
                LogHelper.LogInfo(string.Format(FormatingHelper.Culture,
                    "Farm growth time unresolved: block={0}, subtype={1}, output={2}, reason={3}",
                    entityId, subtype, output, reason));
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(FarmGrowthHelper));
            }
        }

        public static bool TryGetRemainingSeconds(
            FarmGrowthProfile profile,
            float growthProgressPercent,
            out double remainingSeconds)
        {
            remainingSeconds = 0d;
            if (profile == null)
                return false;

            var totalFrames = profile.TotalGrowthFrames;
            if (totalFrames <= 0f)
                return false;

            var progress01 = MathHelper.Clamp(growthProgressPercent / 100f, 0f, 1f);
            remainingSeconds = totalFrames * (1f - progress01) / 60f;
            return true;
        }

        public static bool TryGetRuntimeRemainingSeconds(FarmPlotEntry plot, out double remainingSeconds)
        {
            remainingSeconds = 0d;
            var block = plot != null ? plot.Block : null;
            if (block == null || plot.Logic == null || !plot.Logic.IsPlantPlanted)
                return false;

            try
            {
                var container = block.Components != null ? block.Components.Serialize(false) : null;
                var components = container != null ? container.Components : null;
                if (components == null)
                    return false;

                for (int i = 0; i < components.Count; i++)
                {
                    var farmBuilder = components[i] != null
                        ? components[i].Component as MyObjectBuilder_FarmPlotLogic
                        : null;
                    if (farmBuilder == null)
                        continue;

                    if (farmBuilder.PlantGrowthTimeRemainingInFrames <= 0f && !plot.Logic.IsPlantFullyGrown)
                        return false;

                    remainingSeconds = Math.Max(0d, farmBuilder.PlantGrowthTimeRemainingInFrames / 60d);
                    return true;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(FarmGrowthHelper));
            }

            return false;
        }
    }
}
