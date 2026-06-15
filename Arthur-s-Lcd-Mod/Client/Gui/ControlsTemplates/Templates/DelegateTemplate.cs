using System;
using LcdMod.Client.Gui.ControlsTemplates;

namespace LcdMod.Client.Gui.ControlsTemplates.Templates
{
    public sealed class DelegateTemplate<TItem> : IDataTemplate<TItem>
    {
        readonly Func<TItem, int, ControlTemplate> _build;

        public DelegateTemplate(Func<TItem, int, ControlTemplate> build)
        {
            if (build == null)
                throw new ArgumentNullException("build");

            _build = build;
        }

        public ControlTemplate Build(TItem item, int index)
        {
            return _build(item, index);
        }
    }
}
