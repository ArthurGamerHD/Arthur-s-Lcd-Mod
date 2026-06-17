using System.Collections.Generic;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;

namespace LcdMod.Client.Gui.Styling
{
    public sealed class StyleTree
    {
        readonly List<StyleNode> _roots = new List<StyleNode>();

        public Style<TControl> For<TControl>()
            where TControl : ControlTemplate
        {
            Style<TControl> root = new Style<TControl>();
            _roots.Add(root);
            return root;
        }

        public bool TryResolve<TValue>(
            ControlTemplate target,
            string id,
            StyleState state,
            StyleProperty<TValue> property,
            out TValue value)
        {
            return StyleTreeWalker.TryResolve(this, target, id, state, property, out value);
        }

        internal IList<StyleNode> Roots => _roots;
    }
}
