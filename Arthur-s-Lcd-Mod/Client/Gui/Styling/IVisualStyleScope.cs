namespace LcdMod.Client.Gui.Styling
{
    public interface IVisualStyleScope
    {
        IVisualStyleScope StyleParent { get; }
        StyleTree Styles { get; }
        ResourceTree Resources { get; }
        bool IsDirty { get; }
        void MarkDirty();
    }
}
