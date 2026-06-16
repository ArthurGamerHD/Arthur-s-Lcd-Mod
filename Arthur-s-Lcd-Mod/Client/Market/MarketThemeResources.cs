using LcdMod.Client.Gui.Styling;
using VRageMath;

namespace LcdMod.Client.Market
{
    public static class MarketThemeResources
    {
        public static readonly ResourceKey<Color> PriceTrendUpColor =
            ResourceKey.Register<Color>("Market.PriceTrendUpColor");

        public static readonly ResourceKey<Color> PriceTrendDownColor =
            ResourceKey.Register<Color>("Market.PriceTrendDownColor");

        public static readonly ResourceKey<Color> PriceTrendNeutralColor =
            ResourceKey.Register<Color>("Market.PriceTrendNeutralColor");
    }
}
