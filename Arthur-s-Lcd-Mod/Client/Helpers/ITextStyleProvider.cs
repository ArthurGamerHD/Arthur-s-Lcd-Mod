namespace LcdMod.Client.Helpers
{
    /// <summary>
    /// Supplies the font selected by a UI style scope.
    /// </summary>
    public interface ITextStyleProvider
    {
        string ResolvedTextFont { get; }
    }
}
