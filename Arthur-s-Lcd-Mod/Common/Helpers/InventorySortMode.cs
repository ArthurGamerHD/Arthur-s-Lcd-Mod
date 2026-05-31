namespace LcdMod.Common.Helpers
{
    /// <summary>
    /// Criteria the cargo Sorter uses to decide the order items are packed into the
    /// fullest-available container (see <see cref="InventorySorterCommon"/>).
    /// </summary>
    public enum InventorySortMode
    {
        Quantity = 0,
        Weight = 1,
        Alphabetical = 2
    }
}
