using System;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;

namespace LcdMod.Client.Gui.Styling
{
    public class Style : StyleNode
    {
        internal Style(
            StyleNode parent,
            string @class,
            string id = null,
            StyleState requiredState = StyleState.None)
            : base(parent, string.IsNullOrEmpty(@class) ? Control.DefaultStyleClass : @class, id, requiredState)
        {
        }

        public Style Set<TValue>(
            StyleProperty<TValue> property,
            TValue value)
        {
            Resources.Set(property, value);
            return this;
        }

        public Style Set<TValue>(
            StyleProperty<TValue> property,
            ResourceKey<TValue> key)
        {
            Resources.Set(property, key);
            return this;
        }

        public Style State(StyleState state)
        {
            Style child = new Style(this, Class, null, state);
            Children.Add(child);
            return child;
        }

        public Style SetId(string id)
        {
            Style child = new Style(this, Class, id, StyleState.None);
            Children.Add(child);
            return child;
        }

        public Style ClassSelector(string @class)
        {
            Style child = new Style(this, @class, null, StyleState.None);
            Children.Add(child);
            return child;
        }
    }

    public sealed class Style<TControl> : Style
        where TControl : ControlTemplate
    {
        internal Style(
            StyleNode parent = null,
            string id = null,
            StyleState requiredState = StyleState.None)
            : base(parent, GetDefaultClass(), id, requiredState)
        {
        }

        static string GetDefaultClass()
        {
            Type type = typeof(TControl);
            return type == typeof(ControlTemplate) ? Control.DefaultStyleClass : type.Name;
        }

        public new Style<TControl> Set<TValue>(
            StyleProperty<TValue> property,
            TValue value)
        {
            Resources.Set(property, value);
            return this;
        }

        public new Style<TControl> Set<TValue>(
            StyleProperty<TValue> property,
            ResourceKey<TValue> key)
        {
            Resources.Set(property, key);
            return this;
        }

        public new Style<TControl> State(StyleState state)
        {
            Style<TControl> child = new Style<TControl>(this, null, state);
            Children.Add(child);
            return child;
        }

        public new Style<TControl> Id(string id)
        {
            Style<TControl> child = new Style<TControl>(this, id, StyleState.None);
            Children.Add(child);
            return child;
        }

        public Style<TDerived> For<TDerived>()
            where TDerived : TControl
        {
            Style<TDerived> child = new Style<TDerived>(this);
            Children.Add(child);
            return child;
        }
    }
}
