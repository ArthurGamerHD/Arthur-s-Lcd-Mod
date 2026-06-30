using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates;
using Sandbox.ModAPI;

namespace LcdMod.Client.Gui.Styling
{
    public class StyleNode
    {
        readonly List<StyleNode> _children = new List<StyleNode>();

        internal StyleNode(
            StyleNode parent,
            Type targetType,
            string @class,
            string id,
            StyleState requiredState)
        {
            Parent = parent;
            TargetType = targetType;
            Class = @class;
            Id = id;
            RequiredState = requiredState;
            Resources = new ResourceSet();
            Animations = new StyleAnimationSet();
        }

        public StyleNode Parent { get; private set; }
        public Type TargetType { get; private set; }
        public string Class { get; private set; }
        public string Id { get; private set; }
        public StyleState RequiredState { get; private set; }
        public ResourceSet Resources { get; private set; }
        internal StyleAnimationSet Animations { get; private set; }

        internal IList<StyleNode> Children => _children;

        internal bool MatchesControl(ControlTemplate control)
        {
            if (control == null)
                return false;

            if (TargetType != null && !MyAPIGateway.Reflection.IsAssignableFrom(TargetType, control.GetType()))
                return false;

            return string.IsNullOrEmpty(Class) || control.HasStyleClass(Class);
        }

        internal bool MatchesSelector(string id, StyleState state)
        {
            if (Id != null && Id != id)
                return false;

            return (state & RequiredState) == RequiredState;
        }
    }
}
