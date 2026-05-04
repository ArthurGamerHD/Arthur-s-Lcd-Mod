using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Graph.Apps.Games.Chess.Enum;
using Graph.Extensions;
using Graph.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Graph.Apps.Games.Chess
{
    public abstract class Overlay : IDisposable
    {
        public bool Disposed { get; protected set; }
        public readonly List<RectangleF> Boxes = new List<RectangleF>();

        public abstract void Render(List<MySprite> frame);

        public virtual void Dispose()
        {
            if (Disposed)
                return;
            Disposed = true;
            Boxes.Clear();
        }

        public abstract void ClickBox(int index);
    }

    public sealed class PromotionOverlay : Overlay
    {
        readonly ChessGame _chessGame;
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

        public PromotionOverlay(Point target, Point origin, ChessGame chessGame)
        {
            _chessGame = chessGame;
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
                _chessGame.Board[_index] &= 0xF0; // remove the piece type but keep the color
                _chessGame.Board[_index] |= value;
                _chessGame.ExecuteMove(_origin, _target, _chessGame.Board, SpecialMoves.Promotion);
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
            var direction = Math.Abs(anchor.Y - _chessGame.BoardViewBox.Y) < 1 ? 1 : -1;

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
                _sprites.Add(box.ToSprite(Color.White));

            _sprites.Add(header.ToSprite(Color.LightGray));

            var color = (_chessGame.Board[_index] & 0xF0);

            for (var i = 0; i < _promotions.Count; i++)
            {
                var grid = Boxes[i];
                var data = _chessGame.GetTextureFromId((byte)(color | _promotions.ElementAt(i).Value));

                _sprites.Add(new MySprite(SpriteType.TEXT, data,
                    new Vector2(grid.Center.X, grid.Position.Y + _chessGame.Padding),
                    fontId: "Monospace", rotation: _chessGame.Scale));
            }

            _sprites.Add(new RectangleF(header.Center.X - header.Height / 2, header.Y, header.Width / 2, header.Height)
                .ToCross(Color.Black));
        }

        public override void Render(List<MySprite> frame) => frame.AddRange(_sprites);

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

    public sealed class GameOverOverlay : Overlay
    {
        readonly ChessGame _chessGame;
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

        public GameOverOverlay(ChessGame chessGame)
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

            if (bk == null || wk == null)
                return;

            var blackKingCell = _chessGame.GetGridCell(_chessGame.PointToIndex(bk.Value));
            var whiteKingCell = _chessGame.GetGridCell(_chessGame.PointToIndex(wk.Value));

            var bkDead = _chessGame.IsPositionInDanger(bk.Value, PieceColor.Black, _chessGame.Board);
            var wkDead = _chessGame.IsPositionInDanger(wk.Value, PieceColor.White, _chessGame.Board);

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

        public override void Dispose()
        {
            if (Disposed)
                return;

            Disposed = true;
            _sprites.Clear();
            base.Dispose();
        }

        MySprite[] DrawTextBox(RectangleF rectangle, string text, Color color, Color background)
        {
            float scale = _chessGame.Scale;
            var size = FormatingHelper.GetSizeInPixel(text, "White", 7 * scale, _chessGame.Surface) + 2;

            var frame = new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(rectangle.Right, rectangle.Y),
                size, color: background);
            var capLeft = new MySprite(SpriteType.TEXTURE, "Circle",
                new Vector2(rectangle.Right - size.X / 2, rectangle.Y), new Vector2(size.Y), color: background);
            var capRight = new MySprite(SpriteType.TEXTURE, "Circle",
                new Vector2(rectangle.Right + size.X / 2, rectangle.Y), new Vector2(size.Y), color: background);

            var textSprite = new MySprite(SpriteType.TEXT, text,
                new Vector2(rectangle.Right, rectangle.Y - size.Y / 2),
                color: color, fontId: "White", rotation: 7 * scale);
            return new[]
            {
                rectangle.ToSprite(new Color(color.R, color.G, color.B, 210)),
                capLeft.Shadow(4 * scale),
                capRight.Shadow(4 * scale),
                frame.Shadow(4 * scale),
                capLeft,
                capRight,
                frame,
                textSprite.Shadow(4 * scale),
                textSprite
            };
        }
    }
}