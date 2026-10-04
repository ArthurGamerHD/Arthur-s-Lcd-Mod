using LcdMod.Client.Apps.Maps;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class MapOrbitInertiaTests
{
    static void Pose(Quaternion rotation, out Vector3 view, out Vector3 right, out Vector3 up)
    {
        view = Vector3.Transform(Vector3.Backward, rotation);
        right = Vector3.Transform(Vector3.Right, rotation);
        up = Vector3.Transform(Vector3.Up, rotation);
    }

    static MapOrbitInertia Flick(out Vector3 view, out Vector3 right, out Vector3 up)
    {
        var inertia = new MapOrbitInertia();
        inertia.Begin(0, Vector3.Backward, Vector3.Right, Vector3.Up);
        Pose(Quaternion.CreateFromAxisAngle(Vector3.Up, .1f), out view, out right, out up);
        inertia.Record(.02, view, right, up);
        inertia.Release(.02);
        return inertia;
    }

    [Fact]
    public void ReleaseContinuesTheLastRotationThenSlowsToAStop()
    {
        var inertia = Flick(out Vector3 view, out Vector3 right, out Vector3 up);
        float previousAngle = (float)Math.Atan2(view.X, view.Z);
        float previousStep = float.MaxValue;
        int movingSteps = 0;
        for (int i = 1; i < 180; i++)
        {
            if (!inertia.Advance(.02 + i / 60d, ref view, ref right, ref up))
                break;
            float angle = (float)Math.Atan2(view.X, view.Z);
            float step = angle - previousAngle;
            Assert.True(step > 0f);
            Assert.True(step < previousStep);
            previousStep = step;
            previousAngle = angle;
            movingSteps++;
        }
        Assert.InRange(movingSteps, 10, 179);
        Assert.False(inertia.Advance(4, ref view, ref right, ref up));
    }

    [Fact]
    public void DecayDoesNotDependOnUpdateFrequency()
    {
        var smooth = Flick(out Vector3 view, out Vector3 right, out Vector3 up);
        var sparse = Flick(out Vector3 otherView, out Vector3 otherRight, out Vector3 otherUp);
        for (int i = 1; i <= 30; i++)
            smooth.Advance(.02 + i / 60d, ref view, ref right, ref up);
        sparse.Advance(.52, ref otherView, ref otherRight, ref otherUp);
        Assert.InRange(Vector3.Distance(view, otherView), 0f, .00001f);
        Assert.InRange(Vector3.Distance(right, otherRight), 0f, .00001f);
    }

    [Fact]
    public void HoldingStillBeforeReleaseDoesNotStartSpinning()
    {
        var inertia = new MapOrbitInertia();
        inertia.Begin(0, Vector3.Backward, Vector3.Right, Vector3.Up);
        Pose(Quaternion.CreateFromAxisAngle(Vector3.Up, .1f), out Vector3 view, out Vector3 right, out Vector3 up);
        inertia.Record(.02, view, right, up);
        inertia.Release(.3);
        Assert.False(inertia.Advance(.4, ref view, ref right, ref up));
    }

    [Fact]
    public void GrabbingAgainOrCancellingStopsMomentum()
    {
        var inertia = Flick(out Vector3 view, out Vector3 right, out Vector3 up);
        inertia.Begin(.04, view, right, up);
        Assert.False(inertia.Advance(.06, ref view, ref right, ref up));
        inertia.Release(.06);
        Assert.False(inertia.Advance(.08, ref view, ref right, ref up));
        inertia = Flick(out view, out right, out up);
        inertia.Stop();
        Assert.False(inertia.Advance(.08, ref view, ref right, ref up));
    }

    [Fact]
    public void SurfaceGrabMomentumPreservesTheActualRotationAxisAtATiltedOrientation()
    {
        Pose(Quaternion.CreateFromYawPitchRoll(.8f, 1.1f, -.6f),
            out Vector3 view, out Vector3 right, out Vector3 up);
        var inertia = new MapOrbitInertia();
        inertia.Begin(0, view, right, up);
        var drag = new PlanetSurfaceDrag();
        Assert.True(drag.Begin(new Vector2(-20, 15), Vector2.Zero, 100, view, right, up));
        Vector3 originalView = view;
        Vector3 originalRight = right;
        Vector3 originalUp = up;
        drag.Move(new Vector2(10, -5), Vector2.Zero, 100, out view, out right, out up);
        inertia.Record(.02, view, right, up);
        inertia.Release(.02);

        // The displacement of each basis vector has the same sign after release.
        Vector3 beforeView = view;
        Vector3 beforeRight = right;
        Vector3 beforeUp = up;
        Assert.True(inertia.Advance(.03, ref view, ref right, ref up));
        Assert.True(Vector3.Dot(view - beforeView, beforeView - originalView) > 0f);
        Assert.True(Vector3.Dot(right - beforeRight, beforeRight - originalRight) > 0f);
        Assert.True(Vector3.Dot(up - beforeUp, beforeUp - originalUp) > 0f);
        Assert.InRange(Vector3.Distance(Vector3.Cross(view, right), up), 0f, .00001f);
    }
}
