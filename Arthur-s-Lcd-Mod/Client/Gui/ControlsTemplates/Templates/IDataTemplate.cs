namespace LcdMod.Client.Gui.ControlsTemplates.Templates
{
    public interface IDataTemplate<TItem>
    {
        ControlTemplate Build(TItem item, int index);
    }
}
