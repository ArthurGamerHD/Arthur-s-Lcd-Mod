namespace LcdMod.Common.Helpers
{
    /// <summary>
    /// Operation picked by the cargo screen "fill" buttons (see <see cref="BlockFillerCommon"/>).
    /// </summary>
    public enum FillKind
    {
        Weapons = 0,  // top every weapon up to a fixed number of ammo magazines
        Reactors = 1  // top every reactor up to a uranium target based on grid/reactor size
    }
}
