using VRage.Game;

namespace LcdMod.Client.Farm
{
    internal sealed class FarmGrowthProfile
    {
        public MyDefinitionId PlotLogicDefinitionId;
        public float PlotGrowthMinutes;
        public MyDefinitionId SeedDefinitionId;
        public MyDefinitionId OutputItemId;
        public float SeedGrowthTimeMultiplier;

        public float TotalGrowthFrames => PlotGrowthMinutes * 3600f * SeedGrowthTimeMultiplier;

        public float TotalGrowthSeconds => TotalGrowthFrames / 60f;
    }

    internal sealed class FarmSeedGrowthProfile
    {
        public MyDefinitionId SeedDefinitionId;
        public MyDefinitionId OutputItemId;
        public float GrowthTimeMultiplier;
    }

    internal sealed class FarmPlotGrowthProfile
    {
        public MyDefinitionId DefinitionId;
        public float PlantGrowthMinutes;
    }
}
