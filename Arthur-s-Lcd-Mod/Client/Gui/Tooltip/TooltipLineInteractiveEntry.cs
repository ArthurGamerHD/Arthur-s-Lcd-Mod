using VRageMath;

namespace LcdMod.Client.Gui.Tooltip
{
    sealed class TooltipLineInteractiveEntry : InteractiveRectangleEntry
    {
        readonly ITooltipLine _line;

        public TooltipLineInteractiveEntry(RectangleF rect, ITooltipLine line, CursorType cursor)
            : base(rect, cursor, line)
        {
            _line = line;
        }

        public override bool CanClick => Visible && _line != null && _line.GetOnClick() != null;

        public override bool Click(object sender)
        {
            if (!Visible || _line == null)
                return false;

            var onClick = _line.GetOnClick();
            if (onClick == null)
                return false;

            onClick(_line.GetDataContext(), sender);
            return true;
        }
    }
}