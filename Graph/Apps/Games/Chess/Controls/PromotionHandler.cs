using System;
using System.Collections.Generic;
using System.Linq;
using Graph.Apps.Games.Chess.Enum;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Graph.Apps.Games.Chess
{
    public partial class ChessGame
    {
        public class PromotionHandler : Control
        {
            readonly Chess.ChessGame _chessGame;
            readonly List<MySprite> _sprites = new List<MySprite>();

            readonly Dictionary<char, byte> _promotions = new Dictionary<char, byte>()
            {
                { 'q', 0x05 },
                { 'k', 0x03 },
                { 'r', 0x02 },
                { 'b', 0x04 },
            };

            readonly int _index;
            readonly Point _target;
            readonly Point _origin;

            public PromotionHandler(Point target, Point origin, Chess.ChessGame chessGame)
            {
                this._chessGame = chessGame;
                _index = chessGame.PointToIndex(origin);
                _target = target;
                _origin = origin;

                BakeControls();
            }

            void SelectPromotion(char symbol)
            {
                byte value;
                if (_promotions.TryGetValue(symbol, out value))
                {
                    _chessGame._board[_index] &= 0xF0; // remove the piece type but keep the color
                    _chessGame._board[_index] |= value;
                    _chessGame.ExecuteMove(_origin, _target, _chessGame._board, SpecialMoves.Promotion);
                }

                Dispose();
            }

            public override void ClickBox(int index)
            {
                if (index >= 0 && index < _promotions.Count)
                    SelectPromotion(_promotions.ElementAt(index).Key);
                else
                    Dispose();
            }

            void BakeControls()
            {
                var anchor = _chessGame.GetGridCell(_chessGame.PointToIndex(_target));
                var direction = Math.Abs(anchor.Y - _chessGame._boardViewBox.Y) < 1 ? 1 : -1;

                var header = new RectangleF(
                    anchor.X,
                    anchor.Y + anchor.Height * 3 * direction + (direction == -1 ? -anchor.Height / 2 : anchor.Height),
                    anchor.Width,
                    anchor.Height / 2);

                for (int i = 0; i < 4; i++)
                {
                    Boxes.Add(new RectangleF(
                        anchor.X,
                        anchor.Y + anchor.Height * i * direction,
                        anchor.Width,
                        anchor.Height
                    ));
                }

                foreach (var box in Boxes)
                    _sprites.Add(Chess.ChessGame.DrawRectangle(box, Color.White));

                _sprites.Add(Chess.ChessGame.DrawRectangle(header, Color.LightGray));

                var color = (_chessGame._board[_index] & 0xF0);

                for (var i = 0; i < _promotions.Count; i++)
                {
                    var grid = Boxes[i];
                    var data = _chessGame.GetTextureFromId((byte)(color | _promotions.ElementAt(i).Value));

                    _sprites.Add(new MySprite(SpriteType.TEXT, data,
                        new Vector2(grid.Center.X, grid.Position.Y + _chessGame._padding),
                        fontId: "Monospace", rotation: _chessGame._scale));
                }

                _sprites.Add(Chess.ChessGame.DrawCross(
                    new RectangleF(header.Center.X - header.Height / 2, header.Y, header.Width / 2, header.Height),
                    Color.Black));
            }

            public override void Render(List<MySprite> frame) => frame.AddRange(_sprites);
            
            public override void HandleCommand(string command)
            {
                if(command.Length == 1) 
                    SelectPromotion(command[0]);
                if(command.Length == 5) 
                    SelectPromotion(command[4]);

                Dispose();
            }

            public override void Dispose()
            {
                if (Disposed)
                    return;

                Disposed = true;
                _sprites.Clear();
                _promotions.Clear();
                base.Dispose();
            }
        }
    }
}