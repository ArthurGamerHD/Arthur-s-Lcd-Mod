using System;
using System.Collections.Generic;
using EmptyKeys.UserInterface.Generated;
using Graph.Apps.Abstract;
using Graph.Apps.Games.Chess;
using Graph.Apps.Games.Minesweeper;
using Graph.Apps.Utility;
using Graph.System;
using Graph.System.Config;
using Graph.System.Controls;
using Sandbox.Engine.Platform;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Graph.Apps.Games
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class GameSurfaceScript : InteractiveSurfaceScript, IMultiDisplayMode
    {
        public enum GameEnum
        {
            Chess,
            Game2048,
            Minesweeper
        }
        
        public const string ID = "LCDMod_GameSurfaceScript";
        public const string TITLE = "LCDMod_Games";
        
        static readonly List<MyTerminalControlComboBoxItem> GameList =
            new List<MyTerminalControlComboBoxItem>
            {
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)GameEnum.Chess,
                    Value = MyStringId.GetOrCompute("LCDMod_Chess")
                }/*,
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)GameEnum.Game2048,
                    Value = VRage.Utils.MyStringId.GetOrCompute("2048")
                }*/,
                new MyTerminalControlComboBoxItem
                {
                Key = (long)GameEnum.Minesweeper,
                Value = MyStringId.GetOrCompute("LCDMod_Minesweeper")
                }
            };

        IGame _currentGame;

        readonly List<InteractiveEntry> _emptyInteractiveList = new List<InteractiveEntry>();

        public override List<InteractiveEntry> InteractiveList => _currentGame != null ? _currentGame.Interactive : _emptyInteractiveList;

        GlobalMenuEntry _rootMenu;
        
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
            AppConfig.DisplayInternal = (int)entryValue;
            Sync();
        }

        public void Sync() => ConfigManager.Sync(Block, ProviderConfig);

        public override void Run()
        {
            base.Run();
            
            if(AppConfig == null)
                return;
            
            if(_currentGame == null)
                InitGame((GameEnum)AppConfig.DisplayInternal);

            _currentGame?.Tick();
            
            if (_currentGame != null)
                RenderSprites();
        }

        protected override List<MySprite> GetSprites() => _currentGame?.Render() ??  new List<MySprite>();

        public void InitGame(GameEnum gameEnum)
        {
            switch (gameEnum)
            {
                case GameEnum.Chess:
                {
                    _currentGame = new ChessGame(Surface as Sandbox.ModAPI.IMyTextSurface, this);
                    break;
                }
                case GameEnum.Minesweeper:
                {
                    _currentGame = new MinesweeperGame(Surface as Sandbox.ModAPI.IMyTextSurface, this);
                    break;
                }
            }

            if(_currentGame != null)
                LcdModSessionComponent.OnSave += _currentGame.Save;
        }
        
        public override CursorType CursorType { get; protected set; } = CursorType.Default;

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            BuildGlobalMenu();
            
            if(_currentGame == null)
                return;
            
            if ((GameEnum)AppConfig.DisplayInternal != _currentGame.Id)
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