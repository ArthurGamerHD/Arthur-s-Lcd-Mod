using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Games.Chess.Enum;
using LcdMod.Client.Games.Chess.TinyChessChallenge;
using VRageMath;

namespace LcdMod.Client.Games.Chess
{
    /// <summary>
    /// PGN-like persistent half-move record.
    ///
    /// SAN is kept for human/export display, while LAN-like from/to + exact
    /// promotion/special/capture/state fields make replay deterministic for bots.
    /// </summary>
    public sealed class ChessMoveRecord
    {
        const string HEADER = "LCMCHESSHISTORY1";

        public int Ply;
        public PieceColor MovingColor;
        public PieceType MovingPieceType;
        public Point Origin;
        public Point Target;
        public PieceType CapturedPieceType;
        public PieceType PromotionPieceType;
        public SpecialMoves SpecialMove;
        public string San;
        public string EnPassantTargetAfter;
        public int HalfmoveClockAfter;
        public Castling CastlingRightsAfter;
        public string FenAfter;

        public static string SerializeList(List<ChessMoveRecord> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HEADER);

            if (records == null)
                return sb.ToString();

            for (int i = 0; i < records.Count; i++)
                sb.AppendLine(records[i].ToStorageLine());

            return sb.ToString();
        }

        public static List<ChessMoveRecord> DeserializeList(string data)
        {
            var result = new List<ChessMoveRecord>();
            if (string.IsNullOrWhiteSpace(data))
                return result;

            var normalized = data.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);

            if (lines.Length == 0 || lines[0] != HEADER)
                throw new FormatException("Chess history is not in the PGN-like v1 format.");

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                result.Add(FromStorageLine(line));
            }

            return result;
        }

        public string ToStorageLine()
        {
            return string.Join("|", new[]
            {
                Ply.ToString(),
                ((int)MovingColor).ToString(),
                ((int)MovingPieceType).ToString(),
                PointToCoordinate(Origin),
                PointToCoordinate(Target),
                ((int)CapturedPieceType).ToString(),
                ((int)PromotionPieceType).ToString(),
                ((int)SpecialMove).ToString(),
                Encode(San),
                Encode(EnPassantTargetAfter),
                HalfmoveClockAfter.ToString(),
                ((int)CastlingRightsAfter).ToString(),
                Encode(FenAfter)
            });
        }

        static ChessMoveRecord FromStorageLine(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 13)
                throw new FormatException("Malformed chess history move record.");

            return new ChessMoveRecord
            {
                Ply = ParseInt(parts[0]),
                MovingColor = (PieceColor)ParseInt(parts[1]),
                MovingPieceType = (PieceType)ParseInt(parts[2]),
                Origin = CoordinateToPoint(parts[3]),
                Target = CoordinateToPoint(parts[4]),
                CapturedPieceType = (PieceType)ParseInt(parts[5]),
                PromotionPieceType = (PieceType)ParseInt(parts[6]),
                SpecialMove = (SpecialMoves)ParseInt(parts[7]),
                San = Decode(parts[8]),
                EnPassantTargetAfter = Decode(parts[9]),
                HalfmoveClockAfter = ParseInt(parts[10]),
                CastlingRightsAfter = (Castling)ParseInt(parts[11]),
                FenAfter = Decode(parts[12])
            };
        }

        static int ParseInt(string value)
        {
            int result;
            if (!int.TryParse(value, out result))
                throw new FormatException("Malformed numeric chess history field.");
            return result;
        }

        static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }

        static string PointToCoordinate(Point point)
        {
            return string.Concat((char)('a' + point.X), 8 - point.Y);
        }

        static Point CoordinateToPoint(string coordinate)
        {
            if (string.IsNullOrEmpty(coordinate) || coordinate.Length < 2)
                throw new FormatException("Malformed chess square in history.");

            int file = coordinate[0] - 'a';
            int rank;
            if (!int.TryParse(coordinate.Substring(1), out rank))
                throw new FormatException("Malformed chess rank in history.");

            if (file < 0 || file > 7 || rank < 1 || rank > 8)
                throw new FormatException("Chess square outside board in history.");

            return new Point(file, 8 - rank);
        }
    }
}
