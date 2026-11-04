using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Games.Minesweeper;
using LcdMod.Client.Games.EightBallPool;
using LcdMod.Client.Gui;
using LcdMod.Client.Games;
using LcdMod.Client.Games.ManagedDoom;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Terminal.Controls;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using ChessGame = LcdMod.Client.Games.Chess.ChessGame;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(Apps.GamesConfigApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class GameSurfaceScript : InteractiveSurfaceScript, IMultiDisplayMode
    {
        public enum GameEnum
        {
            Chess,
            Game2048,
            Minesweeper,
            EightBallPool,
            Doom
        }
        
        public const string ID = MOD_PREFIX + "GameSurfaceScript";
        public const string TITLE = MOD_PREFIX + "Games";
        protected override string DefaultTitle => TITLE;
        
        static readonly List<MyTerminalControlComboBoxItem> GameList =
            new List<MyTerminalControlComboBoxItem>
            {
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)GameEnum.Chess,
                    Value = MyStringId.GetOrCompute(MOD_PREFIX + "Chess")
                }/*,
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)GameEnum.Game2048,
                    Value = VRage.Utils.MyStringId.GetOrCompute("2048")
                }*/,
                new MyTerminalControlComboBoxItem
                {
                Key = (long)GameEnum.Minesweeper,
                Value = MyStringId.GetOrCompute(MOD_PREFIX + "Minesweeper")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)GameEnum.EightBallPool,
                    Value = MyStringId.GetOrCompute(MOD_PREFIX + "EightBallPool")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)GameEnum.Doom,
                    Value = MyStringId.GetOrCompute("DOOM")
                }
            };

        public override IApp App => _currentGame;
        IGame _currentGame;
        Apps.GamesConfigApp _viewApp;

        GlobalMenuEntry _rootMenu;
        List<GlobalMenuEntry> _menuEntries;

        public GameSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
            BuildGlobalMenu();
            LcdModSessionComponent.OnAfterSimulationUpdate += RunDoomFrame;
        }

        void RunDoomFrame()
        {
            if (_currentGame is DoomGame && Surface != null && LastRunTick != MyAPIGateway.Session.GameplayFrameCounter)
                Run();
        }

        void BuildGlobalMenu()
        {
            var subEntries = new List<GlobalMenuEntry>(GameList.Count);
            foreach (var entry in GameList) subEntries.Add(new GlobalMenuEntry(entry.Value.ToString(), (a,b) => SetGame(entry.Key)));
            _rootMenu = new GlobalMenuEntry(TITLE, subEntries);
            if (_menuEntries == null)
                return;
            var entries = new List<GlobalMenuEntry>(_menuEntries);
            entries[0] = _rootMenu;
            _menuEntries = entries;
        }

        public void SetGlobalMenu(params GlobalMenuEntry[] entries)
        {
            var newEntries = new List<GlobalMenuEntry>(entries.Length + 1) { _rootMenu };
            foreach (var globalMenuEntry in entries)
                newEntries.Add(globalMenuEntry);

            _menuEntries = newEntries;
            if (_viewApp != null)
                _viewApp.ViewModel.SetMenu(_menuEntries);
        }

        public override void SetPointerButtonState(ControlClickButton button, bool pressed)
        {
            base.SetPointerButtonState(button, pressed);
            if (button == ControlClickButton.Secondary && pressed && !TitleVisible && _viewApp != null)
                _viewApp.ViewModel.Reveal(CurrentFrame);
        }

        internal void HideDoomCursor()
        {
            ResetPointerButtonInput();
            CursorPosition = new Vector2(float.NaN, float.NaN);
            RenderSprites();
        }

        static long CurrentFrame => MyAPIGateway.Session.GameplayFrameCounter;

        // The area the game view occupies, between the title and footer; the games lay out inside it.
        internal RectangleF GameViewBox => _viewApp != null ? _viewApp.GameBounds : ViewBox;

        void SetGame(long entryValue)
        {
            GeneralComponent.DisplayMode = (int)entryValue;
            Sync();
        }

        public void Sync() => ConfigManager.Sync(Block, ProviderConfig);

        internal void SyncDoomAudioSettings()
        {
            Sync();
            if (LcdModSessionComponent.Client != null)
                LcdModSessionComponent.Client.RefreshDoomAudioTerminalControls();
        }

        public override void SafeRun()
        {
            if (_viewApp == null)
            {
                _viewApp = new Apps.GamesConfigApp(this)
                {
                    SpriteProvider = delegate
                    {
                        return _currentGame != null
                            ? _currentGame.GetSprites()
                            : new List<MySprite>();
                    }
                };
            }

            if(_currentGame == null)
                InitGame((GameEnum)GeneralComponent.DisplayMode);

            _viewApp.ViewModel.Game = _currentGame;
            EnsureRetainedAppView(_viewApp);
            if (_menuEntries == null)
                _menuEntries = new List<GlobalMenuEntry> { _rootMenu };
            _viewApp.ViewModel.SetMenu(_menuEntries);
            _currentGame?.Update();
            
            if (_currentGame != null)
                RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            if (_viewApp != null)
            {
                var host = RetainedAppGuiHost;
                _viewApp.ViewModel.Refresh(TitleVisible, CurrentFrame, host != null && host.OwnsPointer);
            }
            DrawRetainedAppView(sprites);
            return sprites;
        }

        public override void Dispose()
        {
            LcdModSessionComponent.OnAfterSimulationUpdate -= RunDoomFrame;
            (_currentGame as IDisposable)?.Dispose();
            _currentGame = null;
            var viewApp = _viewApp;
            _viewApp = null;
            base.Dispose();
            if (viewApp != null)
                viewApp.Close();
        }

        public void InitGame(GameEnum gameEnum)
        {
            switch (gameEnum)
            {
                case GameEnum.Chess:
                {
                    _currentGame = new ChessGame(Surface as IMyTextSurface, this);
                    break;
                }
                case GameEnum.Minesweeper:
                {
                    _currentGame = new MinesweeperGame(Surface as IMyTextSurface, this);
                    break;
                }
                case GameEnum.EightBallPool:
                {
                    _currentGame = new EightBallPoolGame(Surface as IMyTextSurface, this);
                    break;
                }
                case GameEnum.Doom:
                {
                    _currentGame = new DoomGame(this);
                    break;
                }
            }

            if(_currentGame != null)
                LcdModSessionComponent.OnSave += _currentGame.Save;
        }
        

        internal void ConfirmNewGame(Action startNewGame)
        {
            ShowMessageBox(Loc(MOD_PREFIX + "NewGame"), Loc(MOD_PREFIX + "Games_NewGamePrompt"),
                new Apps.Dialogs.DialogButton(Loc(MOD_PREFIX + "Common_Button_Cancel")),
                new Apps.Dialogs.DialogButton(Loc(MOD_PREFIX + "NewGame"), startNewGame, true));
        }

        internal void OfferNewGame(Action startNewGame)
        {
            ShowMessageBox(Loc(MOD_PREFIX + "Games_GameOver"), Loc(MOD_PREFIX + "Games_PlayAgain"),
                new Apps.Dialogs.DialogButton(Loc(MOD_PREFIX + "Common_Button_Cancel")),
                new Apps.Dialogs.DialogButton(Loc(MOD_PREFIX + "NewGame"), startNewGame, true));
        }

        internal bool ShowDoomFailure(string failure, Action restart)
        {
            return _viewApp != null && _viewApp.ShowMessage(
                "Doom stopped",
                failure + "\n\nClick OK to restart Doom.",
                new Apps.Dialogs.DialogButton(Loc(MOD_PREFIX + "Common_Button_OK"), restart, true));
        }

        static string Loc(string key) => LcdMod.Client.Helpers.LocHelper.GetLoc(key);

        internal bool IsCurrentGame(IGame game)
        {
            return ReferenceEquals(_currentGame, game);
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            BuildGlobalMenu();
            
            if(_currentGame == null)
                return;
            
            if ((GameEnum)GeneralComponent.DisplayMode != _currentGame.Id)
            {
                var old = _currentGame;
                LcdModSessionComponent.OnSave -= old.Save;
                _currentGame = null;
                old.Save();
                (old as IDisposable)?.Dispose();
            }
            else
            {
                _currentGame.LayoutChanged();
            }
            
            _currentGame?.Load();
        }

        public override void RequestRedraw()
        {
            base.RequestRedraw();
            _currentGame?.Load();
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes() => GameList;
    }

    internal interface IGame : IApp, IGameInput
    {
        GameSurfaceScript.GameEnum Id { get; }
        void Save();
        void Load();
    }
}
