using Adk.Mvvm;
using System.Collections.Generic;
using LcdMod.Client.Apps.Views;
using LcdMod.Client.Games;
using LcdMod.Client.Games.ManagedDoom;

namespace LcdMod.Client.Apps.Games
{
    internal sealed class GamesViewModel : SpriteAppViewModel, IGameInputSource
    {
        const long REVEAL_FRAMES = 180;

        IReadOnlyList<GlobalMenuEntry> _menuEntries;
        int _menuVersion;
        bool _menuVisible;
        bool _titleVisible = true;
        long _revealedUntil = long.MinValue;
        bool _menuDocked;
        object _game;

        public GamesViewModel(GamesConfigApp app) : base(app) { }

        public IReadOnlyList<GlobalMenuEntry> MenuEntries => _menuEntries;

        // The running game; the view takes its input through IGameInput.
        public object Game
        {
            get { return _game; }
            set
            {
                if (ReferenceEquals(_game, value))
                    return;
                _game = value;
                RaisePropertyChanged<object>(nameof(Game));
                RaisePropertyChanged<IGameInput>(nameof(GameInput));
                RaisePropertyChanged<bool>(nameof(IsDoom));
            }
        }

        public IGameInput GameInput => Game as IGameInput;
        public bool IsDoom => Game is DoomGame;
        [PreviewWith(1)]
        public int MenuVersion => _menuVersion;
        [PreviewWith(true)]
        public bool MenuVisible => _menuVisible;
        [PreviewWith(1)]
        public int GameRow => _menuDocked ? 1 : 0;
        [PreviewWith(1)]
        public int GameRowSpan => _menuDocked ? 1 : 2;

        public void SetMenu(IReadOnlyList<GlobalMenuEntry> entries)
        {
            if (ReferenceEquals(entries, _menuEntries))
                return;
            _menuEntries = entries;
            RaisePropertyChanged<IReadOnlyList<GlobalMenuEntry>>(nameof(MenuEntries));
            RaiseAndSetIfChanged(ref _menuVersion, _menuVersion + 1, nameof(MenuVersion));
            Refresh(_titleVisible, long.MinValue, false);
        }

        // With the title hidden, a secondary click shows the menu for a while; pointing at it keeps it open.
        public void Reveal(long frame)
        {
            _revealedUntil = frame + REVEAL_FRAMES;
            Refresh(_titleVisible, frame, false);
        }

        public void Refresh(bool titleVisible, long frame, bool pointerOnMenu)
        {
            _titleVisible = titleVisible;
            if (pointerOnMenu && _revealedUntil != long.MinValue)
                _revealedUntil = frame + REVEAL_FRAMES;
            if (frame != long.MinValue && frame > _revealedUntil)
                _revealedUntil = long.MinValue;
            bool visible = _menuEntries != null && _menuEntries.Count > 0 &&
                           (titleVisible || _revealedUntil != long.MinValue);
            RaiseAndSetIfChanged(ref _menuVisible, visible, nameof(MenuVisible));
            // The menu shown with the title has its own row; one revealed over a full-screen game only overlays it.
            bool docked = visible && titleVisible;
            if (docked != _menuDocked)
            {
                _menuDocked = docked;
                RaisePropertyChanged<int>(nameof(GameRow));
                RaisePropertyChanged<int>(nameof(GameRowSpan));
            }
        }
    }
}
