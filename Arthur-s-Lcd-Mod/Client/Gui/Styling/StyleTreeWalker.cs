using LcdMod.Client.Gui.ControlsTemplates;

namespace LcdMod.Client.Gui.Styling
{
    static class StyleTreeWalker
    {
        public static bool TryResolve<TValue>(
            StyleTree tree,
            ControlTemplate target,
            string id,
            StyleState state,
            StyleProperty<TValue> property,
            out TValue value)
        {
            value = default(TValue);

            if (tree == null)
                return false;

            bool found = false;
            var roots = tree.Roots;
            for (int i = 0; i < roots.Count; i++)
                found = TryResolve(roots[i], target, id, state, property, ref value) || found;

            return found;
        }

        static bool TryResolve<TValue>(
            StyleNode node,
            ControlTemplate target,
            string id,
            StyleState state,
            StyleProperty<TValue> property,
            ref TValue value)
        {
            if (node == null ||
                !node.MatchesControl(target) ||
                !node.MatchesSelector(id, state))
            {
                return false;
            }

            TValue current;
            bool found = node.Resources.TryResolve(target, property, out current);
            if (found)
                value = current;

            var children = node.Children;
            for (int i = 0; i < children.Count; i++)
                found = TryResolve(children[i], target, id, state, property, ref value) || found;

            return found;
        }
    }
}
