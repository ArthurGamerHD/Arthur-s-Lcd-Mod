namespace LcdMod.Client.Market
{
    internal static class NpcMarketPercentages
    {
        public static float GetPersonalizedTrendPercent(int personalizedPrevious, int personalizedCurrent)
        {
            return personalizedPrevious <= 0
                ? 0f
                : 100f * (personalizedCurrent - personalizedPrevious) / personalizedPrevious;
        }

        public static float GetRelationBenefitPercent(NpcMarketMode mode, int rawCurrent, int personalizedCurrent)
        {
            if (rawCurrent <= 0)
                return 0f;

            return mode == NpcMarketMode.Buy
                ? 100f * (rawCurrent - personalizedCurrent) / rawCurrent
                : 100f * (personalizedCurrent - rawCurrent) / rawCurrent;
        }

        public static float GetEffectiveViewerChangePercent(int rawPrevious, int personalizedCurrent)
        {
            return rawPrevious <= 0
                ? 0f
                : 100f * (personalizedCurrent - rawPrevious) / rawPrevious;
        }
    }
}
