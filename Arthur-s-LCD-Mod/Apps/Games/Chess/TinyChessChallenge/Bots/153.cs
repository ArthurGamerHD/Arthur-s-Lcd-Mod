// This is an implementation of SebLague Tiny-Chess Bot_153 (aka WhateverBot) 
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

// the original Implementation can be found at: https://github.com/SebLague/Tiny-Chess-Godot/blob/main/scripts/chess-challenge/Bots/Bot_153.cs

using System;
using ChessChallenge.API;
using System.Collections.Generic;

namespace Graph.Apps.Games.Chess.TinyChessChallenge.Bots
{
    // A chess bot that gives each possible move a score based on various factors
    // and then picks the move with the highest score
    // (or a random move from any move that has the highest score if there are move than one).

    // The bot plays a decent opening, regularly takes trades and does a good job of
    // moving out of immediate danger.

    // Limitations:
    // Due to the limited token count the bot does have some weaknesses.
    // It can only look one move ahead so it can't use any stratergies
    // that require more than one move. This makes it bad at going for checkmate
    // as well as defending against checkmate (it can still see checkmate in one move).
    // The bot will sometimes prioritise moving a piece out of danger over taking
    // the piece that is putting it in danger.
    public class Bot153 : IChessBot
    {
        // Piece values: null, pawn, knight, bishop, rook, queen, king
        private int[] _pieceValues = { 0, 1, 3, 3, 5, 9, 10 };

        private delegate bool IsMoveTypeDelegate(Board board);

        // Defining variables outside of function so I don't have to pass
        // them into functions (lowering token count)
        private Board _myBoard;
        private Move _myMove;
        private Square _mySquare;
        private bool _isWhiteToMove;

        // I switched to hard coding the score values in to lower the token count
        //int startingScore = 0;
        //int pushingCentrePawnsScore = 3; // only if they haven't moved yet
        //int developingPiecesScore = 3; // knights and bishops only
        //int castlingScore = 4;
        //int advancingPiecesScore = 2; // knights and bishops only
        //int bestCaptureScore = 5;
        //int capturingAttackerScore = 2; // multiplied by attacked piece value
        //int promotingToQueenScore = 4;
        //int movePieceInDangerScore = 2; // multiplied by piece value
        //int checkingScore = 3;
        //int checkMatingScore = 1000;
        //int drawingScore = -4;
        //int loseScore = -1000;
        //int unsafeMoveForThisPieceScore = -3; // multiplied by piece value
        //int unsafeMoveForAnotherPieceScore = -3; // multiplied by piece put in danger value

        public Move Think(Board board, Timer timer)
        {
            _myBoard = board;
            var moves = _myBoard.GetLegalMoves();
            List<int> moveScores = new List<int>(),
                bestCapturesMoveIndexes = new List<int>(),
                posOfPiecesInDanger = new List<int>();

            _isWhiteToMove = _myBoard.IsWhiteToMove;
            ulong myPieceBitBoard = _myBoard.WhitePiecesBitboard;
            if (!_isWhiteToMove)
                myPieceBitBoard = _myBoard.BlackPiecesBitboard;

            int bestCapturesValue = -20;

            // Determine which pieces (if any) are in danger
            for (int i = 0; i < 64; i++)
            {
                _mySquare = new Square(i);
                if (BitboardHelper.SquareIsSet(myPieceBitBoard, _mySquare)
                    && !PieceIsSafe(_myBoard.GetPiece(_mySquare).PieceType))
                    posOfPiecesInDanger.Add(i);
            }

            // Determine the score of each move
            for (int i = 0; i < moves.Length; i++)
            {
                _myMove = moves[i];
                Square myStartSquare = _myMove.StartSquare,
                    myTargetSquare = _myMove.TargetSquare;
                PieceType myMovePieceType = _myMove.MovePieceType;
                int myMovePieceValue = _pieceValues[(int)myMovePieceType],
                    myMoveScore = 0, // startingScore
                    myStartSquareFile = myStartSquare.File,
                    myStartSquareRank = myStartSquare.Rank;

                // Pushing centre pawns if they haven't moved yet
                if (myMovePieceType == PieceType.Pawn
                    && ((myStartSquareRank == 1 && _isWhiteToMove) || (myStartSquareRank == 6 && !_isWhiteToMove))
                    && (myStartSquareFile == 3 || myStartSquareFile == 4))
                    myMoveScore += 3; // pushingCentrePawnsScore
                if (myMovePieceType == PieceType.Knight || myMovePieceType == PieceType.Bishop)
                {
                    // Developing pieces
                    if ((myStartSquareRank == 0 && _isWhiteToMove)
                        || (myStartSquareRank == 7 && !_isWhiteToMove))
                        myMoveScore += 3; // developingPiecesScore
                    // Advancing pieces
                    else if ((myTargetSquare.Rank > myStartSquareRank && _isWhiteToMove)
                             || (myTargetSquare.Rank < myStartSquareRank && !_isWhiteToMove))
                        myMoveScore += 2; // advancingPiecesScore
                }

                // Castling
                if (_myMove.IsCastles)
                    myMoveScore += 4; // castlingScore
                // Capturing pieces that gives the highest value lead
                // (capture most valuable piece with least valuable attacker)
                if (_myMove.IsCapture)
                {
                    int capturePieceValue = _pieceValues[(int)_myBoard.GetPiece(myTargetSquare).PieceType],
                        captureValue = capturePieceValue - myMovePieceValue;
                    _mySquare = myTargetSquare;
                    // If capture piece is undefended
                    if (CalculateSquareAttackerValues(!_isWhiteToMove).Count == 0)
                        captureValue = capturePieceValue;
                    if (captureValue == bestCapturesValue)
                        bestCapturesMoveIndexes.Add(i);
                    if (captureValue > bestCapturesValue)
                    {
                        bestCapturesMoveIndexes.Clear();
                        bestCapturesMoveIndexes.Add(i);
                        bestCapturesValue = captureValue;
                    }

                    // Removing danger by taking a piece
                    for (int j = 0; j < posOfPiecesInDanger.Count; j++)
                    {
                        _mySquare = new Square(posOfPiecesInDanger[j]);
                        _myBoard.MakeMove(_myMove);
                        if (PieceIsSafe(myMovePieceType) && !(_mySquare == myStartSquare))
                            myMoveScore += 2 * myMovePieceValue; // capturingAttackerScore
                        _myBoard.UndoMove(_myMove);
                    }
                }

                // Promoting to queen
                if (_myMove.IsPromotion && _myMove.PromotionPieceType == PieceType.Queen)
                    myMoveScore += 4; // promotingToQueenScore
                // Check
                if (MoveIsOfType(myBoard => myBoard.IsInCheck()))
                    myMoveScore += 3; // checkingScore
                // Checkmate
                if (MoveIsOfType(myBoard => myBoard.IsInCheckmate()))
                    myMoveScore += 1000; // checkMatingScore
                // Draw
                if (MoveIsOfType(myBoard => myBoard.IsDraw()))
                    myMoveScore += -4; // drawingScore
                // Moving a piece that is in danger
                foreach (int pos in posOfPiecesInDanger)
                {
                    if (myStartSquare.Index == pos)
                        myMoveScore += 2 * myMovePieceValue; // movePieceInDangerScore
                }

                // Reduce score of move if it leads to checkmate in one move
                _myBoard.MakeMove(_myMove);
                var opponentMoves = _myBoard.GetLegalMoves();
                // It would be better to pass the move into the variable
                // instead of using temp but that requires more tokens
                Move temp = _myMove;
                for (int j = 0; j < opponentMoves.Length; j++)
                {
                    _myMove = opponentMoves[j];
                    if (MoveIsOfType(myBoard => myBoard.IsInCheckmate()))
                        myMoveScore += -1000; // LoseScore
                }

                _myMove = temp;
                // Reduce score of move if it is unsafe
                // Get new bitboard since a piece has moved
                myPieceBitBoard = _myBoard.WhitePiecesBitboard;
                if (!_isWhiteToMove)
                    myPieceBitBoard = _myBoard.BlackPiecesBitboard;
                for (int j = 0; j < 64; j++)
                {
                    // Determine if j is the position of a piece in danger
                    // This is used to check if the move puts a piece in
                    // danger that wasn't before
                    bool isInPosOfPieceInDanger = false;
                    foreach (int pos in posOfPiecesInDanger)
                    {
                        if (pos == j)
                            isInPosOfPieceInDanger = true;
                    }

                    _mySquare = new Square(j);
                    PieceType currentPieceType = _myBoard.GetPiece(_mySquare).PieceType;
                    if (BitboardHelper.SquareIsSet(myPieceBitBoard, _mySquare)
                        && !PieceIsSafe(currentPieceType))
                    {
                        // If move puts moving piece in danger
                        // and not (capturing a piece and the value of the captured piece is equal
                        // or greater than of the caputuring piece), it is unsafe
                        if (_mySquare == myTargetSquare)
                        {
                            if (!(_myMove.IsCapture && _pieceValues[(int)_myMove.CapturePieceType] >= myMovePieceValue))
                                myMoveScore += -3 * myMovePieceValue; // unsafeMoveForThisPieceScore
                        }
                        // If move puts another piece in danger
                        else if (!isInPosOfPieceInDanger)
                        {
                            myMoveScore += -3 * (int)currentPieceType; // unsafeMoveForAnotherPieceScore
                        }
                    }
                }

                _myBoard.UndoMove(_myMove);

                moveScores.Add(myMoveScore);
            }

            // Add score for best captures
            if (bestCapturesValue >= 0)
            {
                foreach (int index in bestCapturesMoveIndexes)
                {
                    moveScores[index] += 5; // bestCaptureScore
                }
            }

            // Find all moves with the highest score and put them in an array
            int bestMovesScore = moveScores[0];
            foreach (int moveScore in moveScores)
            {
                if (moveScore > bestMovesScore)
                    bestMovesScore = moveScore;
            }

            List<Move> bestMoves = new List<Move>();
            for (int i = 0; i < moveScores.Count; i++)
            {
                if (moveScores[i] == bestMovesScore)
                    bestMoves.Add(moves[i]);
            }

            // Select random move out of best moves
            Random rng = new Random();
            return bestMoves[rng.Next(bestMoves.Count)];
        }

        // Test if move is of type (e.g. isDraw)
        private bool MoveIsOfType(IsMoveTypeDelegate isMoveType)
        {
            _myBoard.MakeMove(_myMove);
            bool isType = isMoveType(_myBoard);
            _myBoard.UndoMove(_myMove);
            return isType;
        }

        // Checks if a piece is currently on a safe square
        private bool PieceIsSafe(PieceType pieceType)
        {
            // Get all attackers and all defenders of the square
            List<int> attackerValues = CalculateSquareAttackerValues(!_isWhiteToMove),
                defenderValues = CalculateSquareAttackerValues(_isWhiteToMove);

            // If there is an attacker weaker that the piece,
            // the piece isn't safe
            if (attackerValues.Count > 0 && attackerValues[0] < _pieceValues[(int)pieceType])
                return false;

            // Go through both lists to find first attacker with
            // a different value to the corresponding defender
            for (int i = 0; i < attackerValues.Count; i++)
            {
                // If we run out of defenders but still have at least
                // one more attacker, the piece isn't safe
                if (i > defenderValues.Count - 1)
                    return false;
                // If the attacker is weaker, piece isn't safe
                if (attackerValues[i] < defenderValues[i])
                    return false;
                // If defender is weaker, piece is safe
                if (attackerValues[i] > defenderValues[i])
                    return true;
            }

            // If we run out of attackers to check, the piece is safe
            return true;
        }

        // Calculate the values of pieces attacking a certain square
        // and return in a sorted list
        private List<int> CalculateSquareAttackerValues(bool isWhite)
        {
            List<int> attackers = new List<int>();

            ulong[] attackingPieces =
            {
                0,
                BitboardHelper.GetPawnAttacks(_mySquare, !isWhite),
                BitboardHelper.GetKnightAttacks(_mySquare),
                BitboardHelper.GetSliderAttacks(PieceType.Bishop, _mySquare, _myBoard),
                BitboardHelper.GetSliderAttacks(PieceType.Rook, _mySquare, _myBoard),
                BitboardHelper.GetSliderAttacks(PieceType.Queen, _mySquare, _myBoard),
                BitboardHelper.GetKingAttacks(_mySquare)
            };

            for (int i = 0; i < 64; i++)
            {
                Square currentSquare = new Square(i);
                if (_myBoard.GetPiece(currentSquare).IsWhite == isWhite)
                {
                    for (int j = 1; j < attackingPieces.Length; j++)
                    {
                        if (BitboardHelper.SquareIsSet(attackingPieces[j], currentSquare)
                            && _myBoard.GetPiece(currentSquare).PieceType == (PieceType)j)
                            attackers.Add(_pieceValues[j]);
                    }
                }
            }

            attackers.Sort();

            return attackers;
        }
    }
}