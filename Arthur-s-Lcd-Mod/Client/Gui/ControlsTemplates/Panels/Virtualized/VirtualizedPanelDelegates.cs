namespace LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized
{
    public delegate ControlTemplate CreateVirtualizedControlHandler<T>(T item);

    public delegate void BindVirtualizedControlHandler<T>(ControlTemplate control, T item, int index);
}
