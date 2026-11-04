using System;
using System.Collections.Generic;
using Acrux.Core.Input;
using VRageMath;

namespace LcdMod.Client.Games
{
    public enum GamePointerButton
    {
        Primary,
        Secondary
    }

    /// <summary>
    /// A clickable area of a game. The game draws it; the games view hosts it as a real (invisible) button, so hover,
    /// release clicks and cursors come from the visual tree.
    /// </summary>
    public sealed class GameRegion
    {
        public RectangleF Bounds;
        public PointerCursor Cursor = PointerCursor.Hand;
        public Action Clicked;
        public Action SecondaryClicked;
    }

    /// <summary>
    /// How a game takes pointer input, in surface pixels. Board games list their clickable areas as
    /// <see cref="Regions"/>; games that hit-test themselves leave it empty and use <see cref="PointerClicked"/>.
    /// </summary>
    public interface IGameInput
    {
        /// <summary>Clickable areas, back to front.</summary>
        IReadOnlyList<GameRegion> Regions { get; }

        /// <summary>The pointer position over the game, or null once it leaves.</summary>
        void PointerMoved(Vector2? position);

        /// <summary>A click released over the game but outside every region.</summary>
        void PointerClicked(GamePointerButton button, Vector2 position);

        /// <summary>The cursor over the game outside every region.</summary>
        PointerCursor CursorAt(Vector2 position);
    }

    /// <summary>A view model exposing the running game's input to the games view.</summary>
    public interface IGameInputSource
    {
        IGameInput GameInput { get; }
    }
}
