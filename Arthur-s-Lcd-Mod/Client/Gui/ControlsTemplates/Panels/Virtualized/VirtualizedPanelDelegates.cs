namespace LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized
{
    public delegate ControlBase CreateVirtualizedControlHandler<T>(T item);

    public delegate void BindVirtualizedControlHandler<T>(ControlBase control, T item, int index);
}
