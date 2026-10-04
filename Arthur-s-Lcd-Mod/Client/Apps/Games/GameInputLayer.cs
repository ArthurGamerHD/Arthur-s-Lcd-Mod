using System.Collections.Generic;
using Adk.Gui.Controls;
using Adk.Gui.Core.Input;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Layout;
using Adk.Gui.Rendering;
using LcdMod.Client.Games;
using VRageMath;

namespace LcdMod.Client.Apps.Games
{
    /// <summary>
    /// Takes pointer input for the current game over its sprites. Each of the game's regions becomes an invisible
    /// button, so cells hover, click on release and set their cursor through the visual tree; anything else over
    /// the game reaches it as a raw position and click.
    /// </summary>
    internal sealed class GameInputLayer : Panel
    {
        static readonly IReadOnlyList<GameRegion> NoRegions = new GameRegion[0];

        readonly List<Button> _buttons = new List<Button>();
        IReadOnlyList<GameRegion> _regions = NoRegions;
        bool _wasPointerOver;

        IGameInput Input => (DataContext as IGameInputSource)?.GameInput;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return availableSize;
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            Sync();
        }

        protected override bool HitTestSelf(Vector2 point)
        {
            return Input != null;
        }

        internal override void PointerHovered(Vector2 point)
        {
            IGameInput input = Input;
            if (input != null)
                input.PointerMoved(point);
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            IGameInput input = Input;
            if (clicked && input != null)
                input.PointerClicked(GamePointerButton.Primary, point);
        }

        internal override void SecondaryPointerReleased(Vector2 point, bool clicked)
        {
            IGameInput input = Input;
            if (clicked && input != null)
                input.PointerClicked(GamePointerButton.Secondary, point);
        }

        internal override PointerCursor GetPointerCursor(Vector2 point)
        {
            IGameInput input = Input;
            return input == null ? PointerCursor.Inherit : input.CursorAt(point);
        }

        protected override void DrawSelf(RenderContext context)
        {
            Sync();
            if (_wasPointerOver && !IsPointerOver)
            {
                IGameInput input = Input;
                if (input != null)
                    input.PointerMoved(null);
            }
            _wasPointerOver = IsPointerOver;
        }

        void Sync()
        {
            IGameInput input = Input;
            _regions = input == null ? NoRegions : input.Regions ?? NoRegions;
            while (_buttons.Count < _regions.Count)
                AddRegionButton(_buttons.Count);
            for (int i = 0; i < _buttons.Count; i++)
            {
                Button button = _buttons[i];
                bool shown = i < _regions.Count;
                button.Visible = shown;
                if (!shown)
                    continue;
                GameRegion region = _regions[i];
                button.Cursor = region.Cursor;
                button.Arrange(region.Bounds);
            }
        }

        void AddRegionButton(int index)
        {
            var button = new Button { StyleClass = "game-region" };
            button.Clicked = () => Click(index, false);
            button.SecondaryClicked = () => Click(index, true);
            _buttons.Add(button);
            AddChild(button);
            Invalidate(UiInvalidation.Measure);
        }

        void Click(int index, bool secondary)
        {
            if (index >= _regions.Count)
                return;
            GameRegion region = _regions[index];
            var action = secondary ? region.SecondaryClicked : region.Clicked;
            if (action != null)
                action();
        }
    }
}
