namespace LcdMod.Common.Ships
{
    internal enum ShipKind
    {
        SmallGrid,
        LargeGrid,
        Station
    }

    // Major: far larger than the largest class asks; Minor: far smaller than the smallest.
    internal enum ShipClassSize
    {
        Normal,
        Major,
        Minor
    }

    // The scan classes by kind and block count. The names are proper names and stay as they are in every language.
    internal static class ShipClassTable
    {
        struct Tier
        {
            public string Name;
            // The class holds ships with more blocks than this.
            public int Above;

            public Tier(string name, int above)
            {
                Name = name;
                Above = above;
            }
        }

        // Each kind's classes, largest first; the last one holds everything down to a single block.
        static readonly Tier[] SmallGrid =
        {
            new Tier("Aries", 2000), new Tier("Taurus", 1000), new Tier("Gemini", 500), new Tier("Cancer", 0)
        };
        static readonly Tier[] Stations =
        {
            new Tier("Leo", 3000), new Tier("Virgo", 1500), new Tier("Libra", 500), new Tier("Scorpius", 0)
        };
        static readonly Tier[] LargeGrid =
        {
            new Tier("Sagittarius", 4000), new Tier("Capricornus", 2000), new Tier("Aquarius", 600), new Tier("Pisces", 0)
        };
        // Major: at least this many times the largest class's bound. Minor: at most this share of the smallest
        // class's top (the bound of the class above it).
        const int MAJOR_FACTOR = 2;
        const float MINOR_SHARE = 0.2f;

        public static string Name(ShipKind kind, int blocks, out ShipClassSize size)
        {
            Tier[] tiers = kind == ShipKind.Station ? Stations : kind == ShipKind.SmallGrid ? SmallGrid : LargeGrid;
            int index = tiers.Length - 1;
            for (int i = 0; i < tiers.Length; i++)
                if (blocks > tiers[i].Above)
                {
                    index = i;
                    break;
                }
            size = ShipClassSize.Normal;
            if (index == 0 && blocks >= tiers[0].Above * MAJOR_FACTOR)
                size = ShipClassSize.Major;
            else if (index == tiers.Length - 1 && blocks <= tiers[index - 1].Above * MINOR_SHARE)
                size = ShipClassSize.Minor;
            return tiers[index].Name;
        }
    }
}
