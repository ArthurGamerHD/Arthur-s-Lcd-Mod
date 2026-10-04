using System.Collections.Generic;
using Adk.Gui.Controls;
using Adk.Gui.Core.Input;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;

namespace LcdMod.Client.Games
{
    /// <summary>The games' menu bar: an ADK <see cref="Menu"/> rebuilt from <see cref="GlobalMenuEntry"/> data.</summary>
    internal sealed class GameMenuBar : Menu
    {
        public static readonly StyledProperty<IReadOnlyList<GlobalMenuEntry>> EntriesProperty =
            StyledProperty.Register<GameMenuBar, IReadOnlyList<GlobalMenuEntry>>(nameof(Entries), null, UiInvalidation.Measure);
        public static readonly StyledProperty<int> VersionProperty =
            StyledProperty.Register<GameMenuBar, int>(nameof(Version), 0, UiInvalidation.Measure);

        IReadOnlyList<GlobalMenuEntry> _built;
        int _builtVersion = -1;

        public IReadOnlyList<GlobalMenuEntry> Entries
        {
            get { return GetValue(EntriesProperty); }
            set { SetValue(EntriesProperty, value); }
        }

        public int Version
        {
            get { return GetValue(VersionProperty); }
            set { SetValue(VersionProperty, value); }
        }

        protected override void OnPropertyChanged(IStyledProperty property)
        {
            base.OnPropertyChanged(property);
            if (property == EntriesProperty || property == VersionProperty)
                Rebuild();
        }

        void Rebuild()
        {
            IReadOnlyList<GlobalMenuEntry> entries = Entries;
            if (ReferenceEquals(entries, _built) && Version == _builtVersion)
                return;
            _built = entries;
            _builtVersion = Version;
            Close();
            Items.Clear();
            if (entries == null)
                return;
            for (int i = 0; i < entries.Count; i++)
                Items.Add(Create(entries[i]));
        }

        static MenuItem Create(GlobalMenuEntry entry)
        {
            var item = new MenuItem { Header = entry.MenuItem, Icon = entry.Icon };
            if (entry.HasChildren)
            {
                for (int i = 0; i < entry.Children.Count; i++)
                    item.Items.Add(Create(entry.Children[i]));
            }
            else if (entry.OnClick != null)
            {
                item.Command = new DelegateCommand(_ => entry.OnClick(entry.DataContext ?? entry, item));
            }
            return item;
        }
    }
}
