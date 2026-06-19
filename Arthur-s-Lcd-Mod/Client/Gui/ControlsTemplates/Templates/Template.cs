using System;

namespace LcdMod.Client.Gui.ControlsTemplates.Templates
{
    public static class Template
    {
        public static IDataTemplate<TItem> For<TItem>(Func<TItem, int, ControlTemplate> build)
        {
            return new DelegateTemplate<TItem>(build);
        }
    }
}
