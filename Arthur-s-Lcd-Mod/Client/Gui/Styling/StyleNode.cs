using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates;

namespace LcdMod.Client.Gui.Styling
{
    public class StyleNode
    {
        readonly List<StyleNode> _children = new List<StyleNode>();

        internal StyleNode(
            StyleNode parent,
            string @class,
            string id,
            StyleState requiredState)
        {
            Parent = parent;
            Class = @class;
            Id = id;
            RequiredState = requiredState;
            Resources = new ResourceSet();
        }

        public StyleNode Parent { get; private set; }
        public string Class { get; private set; }
        public string Id { get; private set; }
        public StyleState RequiredState { get; private set; }
        public ResourceSet Resources { get; private set; }

        internal IList<StyleNode> Children
        {
            get { return _children; }
        }

        internal bool MatchesControl(ControlTemplate control)
        {
            return control != null && control.HasStyleClass(Class);
        }

        internal bool MatchesSelector(string id, StyleState state)
        {
            if (Id != null && Id != id)
                return false;

            return (state & RequiredState) == RequiredState;
        }
    }
}
