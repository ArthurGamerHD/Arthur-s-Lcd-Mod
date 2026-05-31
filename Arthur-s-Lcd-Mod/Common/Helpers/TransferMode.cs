namespace LcdMod.Common.Helpers
{
    /// <summary>
    /// Operation picked in the container action dialog (see <see cref="InventoryDistributorCommon"/>).
    /// </summary>
    public enum TransferMode
    {
        Send = 0,    // push the chosen items from the source container into the targets
        Receive = 1, // pull the chosen items from the targets into the source container
        Balance = 2  // even the chosen items out across the source + targets (proportional to size)
    }
}
