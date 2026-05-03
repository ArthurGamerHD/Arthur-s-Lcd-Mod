using System.Collections.Generic;
using System.Text;
using Graph.Apps.Games.Chess.Enum;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Graph.Apps.Games.Chess

{

    public partial class ChessGame
    {
        public class GameOverHandler : Control
        {
            readonly Chess.ChessGame _chessGame;
            readonly List<MySprite> _sprites = new List<MySprite>();

            const string CHECKMATE = "Checkmate";
            const string WINNER = "Winner";
            const string DRAW = "Draw";
            
            readonly Color[] _colors =
            {
                new Color(224, 40, 40),
                new Color(131, 184, 79),
                new Color(128, 128, 128)
            };

            public GameOverHandler(Chess.ChessGame chessGame)
            {
                _chessGame = chessGame;
                BakeControls();
            }

            public override void ClickBox(int index)
            {
            }

            void BakeControls()
            {
                var bk = _chessGame.FindKing(PieceColor.Black);
                var wk = _chessGame.FindKing(PieceColor.White);
                
                if(bk == null || wk == null)
                    return;
                
                var blackKingCell = _chessGame.GetGridCell(_chessGame.PointToIndex(bk.Value));
                var whiteKingCell = _chessGame.GetGridCell(_chessGame.PointToIndex(wk.Value));

                var bkDead = _chessGame.IsPositionInDanger(bk.Value, PieceColor.Black, _chessGame._board);
                var wkDead = _chessGame.IsPositionInDanger(wk.Value, PieceColor.White, _chessGame._board);
                
                if (!bkDead && !wkDead)
                {
                    _sprites.AddRange(DrawTextBox(blackKingCell, DRAW, _colors[2], Color.White));
                    _sprites.AddRange(DrawTextBox(whiteKingCell, DRAW, _colors[2], Color.White));
                }
                else
                {
                    var winner = bkDead ? whiteKingCell : blackKingCell;
                    var loser = wkDead ? whiteKingCell : blackKingCell;
                    
                    _sprites.AddRange(DrawTextBox(loser, CHECKMATE, _colors[0], Color.White));
                    _sprites.AddRange(DrawTextBox(winner, WINNER, _colors[1], Color.White));
                }
                
            }

            public override void Render(List<MySprite> frame) => frame.AddRange(_sprites);
            
            public override void HandleCommand(string command)
            {
            }

            public override void Dispose()
            {
                if (Disposed)
                    return;

                Disposed = true;
                _sprites.Clear();
                base.Dispose();
            }

            readonly StringBuilder _tempSb = new StringBuilder();

            MySprite[] DrawTextBox(RectangleF rectangle, string text, Color color, Color background)
            {
                _tempSb.Clear();
                _tempSb.Append(text);
                var size = _chessGame._panel.MeasureStringInPixels(_tempSb, "White", 7 * _chessGame._scale) + 2;
                
                return new[]
                {
                    Chess.ChessGame.DrawRectangle(rectangle, new Color(color.R, color.G, color.B, 210)),
                    
                    new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(rectangle.Right + size.X/2, rectangle.Y)+1,  new Vector2(size.Y),
                        color: Color.Black),
                    
                    new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(rectangle.Right - size.X/2, rectangle.Y)+1, new Vector2(size.Y),
                        color: Color.Black),
                    
                    new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(rectangle.Right, rectangle.Y)+1, size,
                        color: Color.Black),

                    new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(rectangle.Right + size.X/2, rectangle.Y),  new Vector2(size.Y),
                        color: background),
                    
                    new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(rectangle.Right - size.X/2, rectangle.Y), new Vector2(size.Y),
                        color: background),
                    
                    new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(rectangle.Right, rectangle.Y), size,
                        color: background),
                    
                                        
                    new MySprite(SpriteType.TEXT, text, new Vector2(rectangle.Right, rectangle.Y - size.Y/2)+1,
                        color: Color.Black, fontId: "White", rotation: 7 * _chessGame._scale),
                    
                    new MySprite(SpriteType.TEXT, text, new Vector2(rectangle.Right, rectangle.Y - size.Y/2),
                        color: color, fontId: "White", rotation: 7 * _chessGame._scale),
                };
            }
        }
    }
}
