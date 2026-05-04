using System;
using System.Collections.Generic;
using EmptyKeys.UserInterface.Generated;
using Graph.Apps.Abstract;
using Graph.Apps.Games.Chess;
using Graph.Apps.Games.Minesweeper;
using Graph.Apps.Utility;
using Graph.System;
using Sandbox.Engine.Platform;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
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
                    Value = VRage.Utils.MyStringId.GetOrCompute("Chess")
                }/*,
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)GameEnum.Game2048,
                    Value = VRage.Utils.MyStringId.GetOrCompute("2048")
                }*/,
                new MyTerminalControlComboBoxItem
                {
                Key = (long)GameEnum.Minesweeper,
                Value = VRage.Utils.MyStringId.GetOrCompute("Minesweeper")
                }
            };

        IGame _currentGame;

        readonly List<InteractiveEntry> _emptyInteractiveList = new List<InteractiveEntry>();

        public override List<InteractiveEntry> InteractiveList => _currentGame != null ? _currentGame.Interactive : _emptyInteractiveList;

        public GameSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {

            
        }

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
            
            if(_currentGame == null)
                return;
            
            if ((GameEnum)AppConfig.DisplayInternal != _currentGame.Id)
            {
                var old = _currentGame;
                LcdModSessionComponent.OnSave -= old.Save;
                _currentGame = null;
                old.Save();
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
    }
}