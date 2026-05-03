// This is an implementation of SebLague Tiny-Chess Bot_614 (aka Boychesser) 
// Tiny-Chess-Godot is under MIT License

/*
MIT License

Copyright (c) 2023 Sebastian Lague

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

// the original Implementation can be found at: https://github.com/SebLague/Tiny-Chess-Godot/blob/main/scripts/chess-challenge/Bots/Bot_614.cs

using ChessChallenge.API;
using System;
using static ChessChallenge.API.BitboardHelper;
using static System.Math;

namespace Graph.Apps.Games.Chess.TinyChessChallenge.Bots
{
    // Boychesser
    public class Bot_614 : IChessBot
    {
        const int BoundUpper = 0;
        const int BoundLower = 2147483647;

        struct TTEntry
        {
            public ulong Hash;
            public ushort MoveRaw;
            public int Score;
            public int Depth;
            public int Bound;

            public TTEntry(ulong hash, ushort moveRaw, int score, int depth, int bound)
            {
                Hash = hash;
                MoveRaw = moveRaw;
                Score = score;
                Depth = depth;
                Bound = bound;
            }
        }

        public int maxSearchTime, searchingDepth, lastScore;

        Timer timer;
        Board board;

        Move searchBestMove, rootBestMove;

        readonly TTEntry[] transpositionTable = new TTEntry[0x800000];
        readonly int[,,] history = new int[2, 7, 64];

        readonly ulong[] packedData =
        {
            0x0000000000000000, 0x2328170f2d2a1401, 0x1f1f221929211507, 0x18202a1c2d261507,
            0x252e3022373a230f, 0x585b47456d65321c, 0x8d986f66a5a85f50, 0x0002000300070005,
            0xfffdfffd00060001, 0x2b1f011d20162306, 0x221c0b171f15220d, 0x1b1b131b271c1507,
            0x232d212439321f0b, 0x5b623342826c2812, 0x8db65b45c8c01014, 0x0000000000000000,
            0x615a413e423a382e, 0x6f684f506059413c, 0x82776159705a5543, 0x8b8968657a6a6150,
            0x948c7479826c6361, 0x7e81988f73648160, 0x766f7a7e70585c4e, 0x6c7956116e100000,
            0x3a3d2d2840362f31, 0x3c372a343b3a3838, 0x403e2e343c433934, 0x373e3b2e423b2f37,
            0x383b433c45433634, 0x353d4b4943494b41, 0x46432e354640342b, 0x55560000504f0511,
            0x878f635c8f915856, 0x8a8b5959898e5345, 0x8f9054518f8e514c, 0x96985a539a974a4c,
            0x9a9c67659e9d5f59, 0x989c807a9b9c7a6a, 0xa09f898ba59c6f73, 0xa1a18386a09b7e84,
            0xbcac7774b8c9736a, 0xbab17b7caebd7976, 0xc9ce7376cac57878, 0xe4de6f70dcd87577,
            0xf4ef7175eedc7582, 0xf9fa8383dfe3908e, 0xfffe7a81f4ec707f, 0xdfe79b94e1ee836c,
            0x2027252418003d38, 0x4c42091d31193035, 0x5e560001422c180a, 0x6e6200004d320200,
            0x756c000e5f3c1001, 0x6f6c333f663e3f1d, 0x535b55395c293c1b, 0x2f1e3d5e22005300,
            0x004c0037004b001f, 0x00e000ca00be00ad, 0x02e30266018800eb, 0xffdcffeeffddfff3,
            0xfff9000700010007, 0xffe90003ffeefff4, 0x00000000fff5000d,
        };

        int EvalWeight(int item)
        {
            return (int)(packedData[item >> 1] >> (item * 32));
        }

        public Move Think(Board boardOrig, Timer timerOrig)
        {
            board = boardOrig;
            timer = timerOrig;

            Move[] legalMoves = board.GetLegalMoves();
            if (legalMoves.Length > 0)
                rootBestMove = legalMoves[0];

            maxSearchTime = timer.MillisecondsRemaining / 4;
            searchingDepth = 1;
            do
            {
                try
                {
                    if (Abs(lastScore - Negamax(lastScore - 20, lastScore + 20, searchingDepth)) >= 20)
                        Negamax(-32000, 32000, searchingDepth);
                    rootBestMove = searchBestMove;
                }
                catch
                {
                    break;
                }
            } while (++searchingDepth <= 200 && timer.MillisecondsElapsedThisTurn < maxSearchTime / 10);

            return rootBestMove;
        }

        public int Negamax(int alpha, int beta, int depth)
        {
            if (timer.MillisecondsElapsedThisTurn >= maxSearchTime && searchingDepth > 1)
                throw new Exception();

            int ttIndex = (int)(board.ZobristKey & 0x7FFFFFUL);
            TTEntry tt = transpositionTable[ttIndex];
            ulong ttHash = tt.Hash;
            ushort ttMoveRaw = tt.MoveRaw;
            int score = tt.Score;
            int ttDepth = tt.Depth;
            int ttBound = tt.Bound;

            bool ttHit = ttHash == board.ZobristKey;
            bool nonPv = alpha + 1 == beta;
            bool inQSearch = depth <= 0;

            int eval = 0x000b000a;
            int bestScore = board.PlyCount - 30000;
            int oldAlpha = alpha;
            int moveCount = 0;
            int quietsToCheck = (0x17285100 >> (depth * 6)) & 63;
            int tmp = 0;

            if (ttHit)
            {
                bool canUseTT = false;
                if (ttDepth >= depth)
                {
                    if (ttBound == BoundLower)
                        canUseTT = score >= beta;
                    else if (ttBound == BoundUpper)
                        canUseTT = score <= alpha;
                    else
                        canUseTT = nonPv || inQSearch;
                }

                if (canUseTT)
                    return score;
            }
            else if (depth > 3)
            {
                depth--;
            }

            eval = ttHit && !inQSearch ? score : Eval(board.AllPiecesBitboard, ref eval, ref tmp) / 24;

            if (inQSearch)
            {
                alpha = Max(alpha, bestScore = eval);
            }
            else if (nonPv && eval >= beta && board.TrySkipTurn())
            {
                bestScore = depth <= 4
                    ? eval - 58 * depth
                    : -Negamax(-beta, -alpha, (depth * 100 + beta - eval) / 186 - 1);
                board.UndoSkipTurn();
            }

            if (bestScore >= beta)
                return bestScore;

            if (board.IsInStalemate())
                return 0;

            Move[] moves = board.GetLegalMoves(inQSearch);
            int[] scores = new int[moves.Length];
            tmp = 0;
            foreach (Move move in moves)
            {
                scores[tmp++] -= ttHit && move.RawValue == ttMoveRaw
                    ? 1000000
                    : Max((int)move.CapturePieceType * 32768 - (int)move.MovePieceType - 16384, GetHistoryValue(move));
            }

            Array.Sort(scores, moves);
            Move bestMove = default(Move);
            foreach (Move move in moves)
            {
                if (inQSearch && eval + (int)((0x14d2ce6e9862d000UL >> ((int)move.CapturePieceType * 10)) & 0x7FFUL) <=
                    alpha)
                    break;

                board.MakeMove(move);
                int nextDepth = board.IsInCheck() ? depth : depth - 1;
                int reduction = (depth - nextDepth) *
                                Max((moveCount * 93 + depth * 144) / 1000 + scores[moveCount] / 172, 0);

                if (board.IsRepeatedPosition())
                {
                    score = 0;
                }
                else
                {
                    while (moveCount != 0 && (score = -Negamax(~alpha, -alpha, nextDepth - reduction)) > alpha &&
                           reduction != 0)
                        reduction = 0;

                    if (moveCount == 0 || score > alpha)
                        score = -Negamax(-beta, -alpha, nextDepth);
                }

                board.UndoMove(move);

                if (score > bestScore)
                {
                    alpha = Max(alpha, bestScore = score);
                    bestMove = move;
                }

                if (score >= beta)
                {
                    if (!move.IsCapture)
                    {
                        tmp = eval - alpha >> 31 ^ depth;
                        tmp *= tmp;

                        for (int i = 0; i < moveCount; i++)
                        {
                            Move malusMove = moves[i];
                            if (!malusMove.IsCapture)
                            {
                                int historyValue = GetHistoryValue(malusMove);
                                SetHistoryValue(malusMove, historyValue - tmp - tmp * historyValue / 512);
                            }
                        }

                        int moveHistoryValue = GetHistoryValue(move);
                        SetHistoryValue(move, moveHistoryValue + tmp - tmp * moveHistoryValue / 512);
                    }

                    break;
                }

                if (nonPv && depth <= 4 && !move.IsCapture &&
                    (quietsToCheck-- == 1 || eval + 127 * depth < alpha))
                {
                    break;
                }

                moveCount++;
            }

            transpositionTable[ttIndex] = new TTEntry(
                board.ZobristKey,
                alpha > oldAlpha ? bestMove.RawValue : ttMoveRaw,
                ClampScore(bestScore, -20000, 20000),
                Max(depth, 0),
                bestScore >= beta ? BoundLower : alpha - oldAlpha);

            searchBestMove = bestMove;
            lastScore = bestScore;
            return bestScore;
        }

        int Eval(ulong pieces, ref int eval, ref int tmp)
        {
            while (pieces != 0UL)
            {
                int sqIndex = ClearAndGetIndexOfLSB(ref pieces);
                Piece piece = board.GetPiece(new Square(sqIndex));
                bool pieceIsWhite = piece.IsWhite;
                int pieceType = (int)piece.PieceType;

                pieceType -= (((sqIndex & 7) ^ board.GetKingSquare(pieceIsWhite).File) >> 1) >> pieceType;

                int packedIndex = (((pieceType * 64 + sqIndex) >> 3) ^ (pieceIsWhite ? 0 : 7));
                int shift = (0x01455410 >> (sqIndex * 4)) * 8;

                int value = EvalWeight(112 + pieceType)
                            + (int)((packedData[packedIndex] >> shift) & 0xFF00FFUL)
                            + EvalWeight(11 + pieceType) * GetNumberOfSetBits(
                                GetSliderAttacks((PieceType)Min(5, pieceType), new Square(sqIndex), board))
                            + EvalWeight(118 + pieceType) * GetNumberOfSetBits(
                                (pieceIsWhite
                                    ? 0x0101010101010100UL << sqIndex
                                    : 0x0080808080808080UL >> (63 - sqIndex))
                                & board.GetPieceBitboard(PieceType.Pawn, pieceIsWhite));

                eval += pieceIsWhite == board.IsWhiteToMove ? value : -value;
                tmp += (0x0421100 >> (pieceType * 4)) & 0xF;
            }

            return (short)eval * tmp + eval / 0x10000 * (24 - tmp);
        }

        int GetHistoryValue(Move move)
        {
            return history[board.PlyCount & 1, (int)move.MovePieceType, move.TargetSquare.Index];
        }

        void SetHistoryValue(Move move, int value)
        {
            history[board.PlyCount & 1, (int)move.MovePieceType, move.TargetSquare.Index] = value;
        }

        static int ClampScore(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}