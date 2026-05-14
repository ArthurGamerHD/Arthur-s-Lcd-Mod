using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Client.Games.Minesweeper;
using LcdMod.Client.Games.EightBallPool;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.Controls.Interactive;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using ChessGame = LcdMod.Client.Games.Chess.ChessGame;
using InteractiveSurfaceScript = LcdMod.Client.Apps.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Apps
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class GameSurfaceScript : InteractiveSurfaceScript, IMultiDisplayMode
    {
        public enum GameEnum
        {
            Chess,
            Game2048,
            Minesweeper,
            EightBallPool
        }
        
        public const string ID = "LcdMod_GameSurfaceScript";
        public const string TITLE = "LcdMod_Games";
        protected override string DefaultTitle => TITLE;
        
        static readonly List<MyTerminalControlComboBoxItem> GameList =
            new List<MyTerminalControlComboBoxItem>
            {
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)GameEnum.Chess,
                    Value = MyStringId.GetOrCompute("LcdMod_Chess")
                }/*,
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)GameEnum.Game2048,
                    Value = VRage.Utils.MyStringId.GetOrCompute("2048")
                }*/,
                new MyTerminalControlComboBoxItem
                {
                Key = (long)GameEnum.Minesweeper,
                Value = MyStringId.GetOrCompute("LcdMod_Minesweeper")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)GameEnum.EightBallPool,
                    Value = MyStringId.GetOrCompute("LcdMod_EightBallPool")
                }
            };

        IGame _currentGame;

        readonly List<InteractiveEntry> _emptyInteractiveList = new List<InteractiveEntry>();

        public override List<InteractiveEntry> InteractiveList => _currentGame != null ? _currentGame.Interactive : _emptyInteractiveList;

        GlobalMenuEntry _rootMenu;

        protected override bool RendersInteractiveEntriesInGetSprites
        {
            get { return true; }
        }
        
        public GameSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
            BuildGlobalMenu();
        }

        void BuildGlobalMenu()
        {
            var subEntries = new List<GlobalMenuEntry>(GameList.Count);
            foreach (var entry in GameList) subEntries.Add(new GlobalMenuEntry(entry.Value.ToString(), (a,b) => SetGame(entry.Key)));
            _rootMenu = new GlobalMenuEntry(TITLE, subEntries);
        }

        public override void SetGlobalMenu(params GlobalMenuEntry[] entries)
        {
            var newEntries = new List<GlobalMenuEntry>(entries.Length + 1) { _rootMenu };
            foreach (var globalMenuEntry in entries)
                newEntries.Add(globalMenuEntry);
            
            base.SetGlobalMenu(newEntries);
        }

        void SetGame(long entryValue)
        {
            AppConfig.DisplayMode = (int)entryValue;
            Sync();
        }

        public void Sync() => ConfigManager.Sync(Block, ProviderConfig);

        public override void SafeRun()
        {
            
            if(AppConfig == null)
                return;
            
            if(_currentGame == null)
                InitGame((GameEnum)AppConfig.DisplayMode);

            _currentGame?.Tick();
            
            if (_currentGame != null)
                RenderSprites();
        }

        protected override List<MySprite> GetSprites()
        {
            var sprites = _currentGame?.Render() ?? new List<MySprite>();
            RenderInteractiveEntryVisuals(sprites);
            DrawTitle(sprites);
            return sprites;
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
            }

            if(_currentGame != null)
                LcdModSessionComponent.OnSave += _currentGame.Save;
        }
        
        public override CursorType CursorType { get; protected set; } = CursorType.Default;

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
            
            if ((GameEnum)AppConfig.DisplayMode != _currentGame.Id)
            {
                var old = _currentGame;
                LcdModSessionComponent.OnSave -= old.Save;
                _currentGame = null;
                old.Save();
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

    internal interface IGame
    {
        List<InteractiveEntry> Interactive { get; }
        GameSurfaceScript.GameEnum Id { get; }
        void Tick();
        List<MySprite> Render();
        void Save();
        void Load();
        void LayoutChanged();
    }
}
