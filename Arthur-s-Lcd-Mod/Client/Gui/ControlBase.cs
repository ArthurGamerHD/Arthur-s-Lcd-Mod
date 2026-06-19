using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Styling;

namespace LcdMod.Client.Gui
{
    public abstract class Control : IVisualStyleScope
    {
        public const string DefaultStyleClass = "ControlBase";

        IVisualStyleScope _styleParent;
        string _class = DefaultStyleClass;

        public virtual IVisualStyleScope StyleParent => _styleParent;

        public virtual StyleTree Styles { get; protected set; }

        public virtual ResourceTree Resources { get; protected set; }

        public virtual string Class => _class;

        internal bool _isDirty;
        public virtual bool IsDirty => _isDirty;
        
        public abstract IReadOnlyList<Control> Children { get; }
        public object DataContext { get; set; }

        public Control SetClass(string @class)
        {
            if (string.IsNullOrEmpty(@class))
                @class = DefaultStyleClass;

            if (_class == @class)
                return this;

            _class = @class;
            MarkSubtreeDirty();
            return this;
        }

        public bool HasStyleClass(string @class)
        {
            if (string.IsNullOrEmpty(@class))
                return false;

            string classList = Class;
            if (string.IsNullOrEmpty(classList))
                return false;

            int index = 0;
            while (index < classList.Length)
            {
                while (index < classList.Length && char.IsWhiteSpace(classList[index]))
                    index++;

                int start = index;
                while (index < classList.Length && !char.IsWhiteSpace(classList[index]))
                    index++;

                int length = index - start;
                if (length == @class.Length && string.CompareOrdinal(classList, start, @class, 0, length) == 0)
                    return true;
            }

            return false;
        }

        internal void SetStyleParent(IVisualStyleScope styleParent)
        {
            if (ReferenceEquals(this, styleParent))
                styleParent = null;

            if (ReferenceEquals(_styleParent, styleParent))
                return;

            _styleParent = styleParent;
            MarkSubtreeDirty();
        }

        internal void MarkSubtreeDirty()
        {
            MarkDirty();

            var children = Children;
            if (children == null)
                return;

            for (int i = 0; i < children.Count; i++)
            {
                Control child = children[i];
                if (child != null)
                    child.MarkSubtreeDirty();
            }
        }

        public virtual void MarkDirty()
        {
            _isDirty = true;
        }

        public bool Visible { get; protected set; } = true;
        public bool Enabled { get; protected set; } = true;
        

        public void SetVisible(bool visible)
        {
            if (Visible == visible)
                return;

            Visible = visible;
            MarkDirty();
        }

        public virtual Control SetEnabled(bool enabled)
        {
            if (Enabled == enabled)
                return this;

            Enabled = enabled;
            MarkDirty();
            return this;
        }
    }
}
