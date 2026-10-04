using System;
using VRageMath;

namespace LcdMod.Client.Apps.Maps
{
    /// <summary>Rotates an orthographic globe while preserving the surface point grabbed on mouse-down.</summary>
    internal sealed class PlanetSurfaceDrag
    {
        Vector3 _anchor;
        Vector3 _initialView;
        Vector3 _initialRight;
        Vector3 _initialUp;

        public bool Begin(Vector2 pixel, Vector2 center, float radius, Vector3 view, Vector3 right, Vector3 up)
        {
            if (radius <= 0f || Vector2.DistanceSquared(pixel, center) > radius * radius)
                return false;
            _anchor = Project(pixel, center, radius);
            _initialView = view;
            _initialRight = right;
            _initialUp = up;
            return true;
        }

        public void Move(Vector2 pixel, Vector2 center, float radius,
            out Vector3 view, out Vector3 right, out Vector3 up)
        {
            Vector3 target = Project(pixel, center, Math.Max(1f, radius));
            // Rotate the camera basis by the inverse of the surface's screen motion:
            // newBasis * target == initialBasis * anchor. Always solve from mouse-down
            // so event frequency and long drags cannot accumulate positional drift.
            Vector3 cross = Vector3.Cross(target, _anchor);
            float scalar = 1f + MathHelper.Clamp(Vector3.Dot(target, _anchor), -1f, 1f);
            Quaternion rotation;
            if (cross.LengthSquared() + scalar * scalar < 1e-12f)
            {
                // Opposite points on the limb need an explicit half-turn axis.
                Vector3 axis = Vector3.Cross(target, Math.Abs(target.Y) < .9f ? Vector3.Up : Vector3.Right);
                axis.Normalize();
                rotation = new Quaternion(axis, 0f);
            }
            else
            {
                rotation = new Quaternion(cross, scalar);
                rotation.Normalize();
            }
            view = ToInitialBasis(Vector3.Transform(Vector3.Backward, rotation));
            right = ToInitialBasis(Vector3.Transform(Vector3.Right, rotation));
            up = ToInitialBasis(Vector3.Transform(Vector3.Up, rotation));
        }

        Vector3 ToInitialBasis(Vector3 value)
        {
            return _initialRight * value.X + _initialUp * value.Y + _initialView * value.Z;
        }

        static Vector3 Project(Vector2 pixel, Vector2 center, float radius)
        {
            Vector2 point = (pixel - center) / radius;
            point.Y = -point.Y;
            float lengthSquared = point.LengthSquared();
            if (lengthSquared > 1f)
            {
                // Outside the silhouette there is no surface under the cursor.
                // Hold the grabbed point on the nearest limb until the cursor returns.
                point /= (float)Math.Sqrt(lengthSquared);
                return new Vector3(point.X, point.Y, 0f);
            }
            return new Vector3(point.X, point.Y, (float)Math.Sqrt(Math.Max(0f, 1f - lengthSquared)));
        }
    }
}
