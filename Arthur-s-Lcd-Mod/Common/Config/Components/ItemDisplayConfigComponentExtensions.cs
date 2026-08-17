namespace LcdMod.Common.Config.Components
{
    public enum ItemDisplayMode
    {
        Card = 0,
        List = 1,
        Table = 2,
        Grid = 3
    }

    public static class ItemDisplayConfigComponentExtensions
    {
        public static ItemDisplayMode ResolveDisplayMode(
            this ItemDisplayConfigComponent config,
            GeneralConfigComponent legacyConfig)
        {
            if (config != null && IsValid(config.DisplayMode))
                return (ItemDisplayMode)config.DisplayMode;

            return ResolveLegacyDisplayMode(legacyConfig);
        }

        public static bool MigrateLegacyDisplayMode(
            this ItemDisplayConfigComponent config,
            GeneralConfigComponent legacyConfig)
        {
            if (config == null || IsValid(config.DisplayMode))
                return false;

            config.DisplayMode = (int)ResolveLegacyDisplayMode(legacyConfig);
            return true;
        }

        public static ItemDisplayMode ResolveLegacyDisplayMode(GeneralConfigComponent legacyConfig)
        {
            var legacy = legacyConfig != null && legacyConfig.DisplayMode == 1;
            var lines = legacyConfig != null && legacyConfig.DrawLines;

            if (legacy)
                return lines ? ItemDisplayMode.Table : ItemDisplayMode.List;

            return lines ? ItemDisplayMode.Grid : ItemDisplayMode.Card;
        }

        static bool IsValid(int value)
        {
            return value >= (int)ItemDisplayMode.Card && value <= (int)ItemDisplayMode.Grid;
        }
    }
}
