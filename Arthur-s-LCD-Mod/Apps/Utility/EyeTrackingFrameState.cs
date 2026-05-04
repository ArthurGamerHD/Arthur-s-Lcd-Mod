using VRageMath;

namespace Graph.Apps.Utility
{
    /// <summary>
    /// Stores the latest look-at sample so a surface script can consume it in the next frame.
    /// Coordinates are expected in raw view-space pixels.
    /// </summary>
    public sealed class EyeTrackingFrameState
    {
        Vector2? _pendingRawCoordinates;

        public void Receive(Vector2 onScreenCoordinates)
        {
            _pendingRawCoordinates = onScreenCoordinates;
        }

        public bool TryConsumeMapped(RectangleF viewBox, out Vector2 mappedCoordinates)
        {
            if (!_pendingRawCoordinates.HasValue)
            {
                mappedCoordinates = Vector2.Zero;
                return false;
            }

            var raw = _pendingRawCoordinates.Value;
            _pendingRawCoordinates = null;

            // Raw mapping: do not scale/remap by normalized ratios, only clamp to visible area.
            var x = MathHelper.Clamp(raw.X, viewBox.X, viewBox.Right);
            var y = MathHelper.Clamp(raw.Y, viewBox.Y, viewBox.Bottom);
            mappedCoordinates = new Vector2(
                x,
                y);
            return true;
        }
    }
}
