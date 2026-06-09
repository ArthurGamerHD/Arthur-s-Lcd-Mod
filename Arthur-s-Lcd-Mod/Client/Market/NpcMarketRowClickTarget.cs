namespace LcdMod.Client.Market
{
    internal sealed class NpcMarketRowClickTarget
    {
        public readonly string ItemKey;
        public readonly NpcMarketMode Mode;

        public NpcMarketRowClickTarget(string itemKey, NpcMarketMode mode)
        {
            ItemKey = itemKey;
            Mode = mode == NpcMarketMode.Sell ? NpcMarketMode.Sell : NpcMarketMode.Buy;
        }

        public string Key
        {
            get { return ItemKey + "|" + Mode; }
        }

        public override bool Equals(object obj)
        {
            var other = obj as NpcMarketRowClickTarget;
            return other != null &&
                   string.Equals(ItemKey, other.ItemKey, System.StringComparison.Ordinal) &&
                   Mode == other.Mode;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ItemKey != null ? System.StringComparer.Ordinal.GetHashCode(ItemKey) : 0) * 397) ^
                       (int)Mode;
            }
        }
    }
}
