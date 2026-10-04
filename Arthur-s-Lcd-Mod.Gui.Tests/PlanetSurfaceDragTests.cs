using LcdMod.Client.Apps.Maps;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class PlanetSurfaceDragTests
{
    [Theory]
    [InlineData(0.55f)]
    [InlineData(1f)]
    [InlineData(8f)]
    [InlineData(16f)]
    public void GrabbedSurfacePointProjectsBackToTheCursorAtEveryZoom(float zoom)
    {
        var center = new Vector2(231, 147);
        float radius = (int)(301 * .47f * zoom);
        var press = center + new Vector2(-.31f, .22f) * radius;
        Quaternion pose = Quaternion.CreateFromYawPitchRoll(.8f, 1.3f, -.7f);
        Vector3 initialView = Vector3.Transform(Vector3.Backward, pose);
        Vector3 initialRight = Vector3.Transform(Vector3.Right, pose);
        Vector3 initialUp = Vector3.Transform(Vector3.Up, pose);
        Vector3 anchor = initialRight * -.31f + initialUp * -.22f +
            initialView * (float)Math.Sqrt(1f - .31f * .31f - .22f * .22f);
        var drag = new PlanetSurfaceDrag();
        Assert.True(drag.Begin(press, center, radius, initialView, initialRight, initialUp));

        foreach (Vector2 offset in new[] { new Vector2(.2f, -.4f), new Vector2(.8f, .3f), new Vector2(-.31f, .22f) })
        {
            Vector2 cursor = center + offset * radius;
            drag.Move(cursor, center, radius, out Vector3 view, out Vector3 right, out Vector3 up);
            Vector2 projected = center + new Vector2(Vector3.Dot(anchor, right), -Vector3.Dot(anchor, up)) * radius;
            Assert.InRange(Vector2.Distance(projected, cursor), 0f, .01f);
            Assert.True(Vector3.Dot(anchor, view) >= 0f);
            AssertBasis(view, right, up);
        }
    }

    [Fact]
    public void DraggingOutsideTheGlobeClampsToTheLimbAndReturningRestoresTheAnchor()
    {
        var drag = new PlanetSurfaceDrag();
        Assert.True(drag.Begin(Vector2.Zero, Vector2.Zero, 100, Vector3.Backward, Vector3.Right, Vector3.Up));
        drag.Move(new Vector2(500, 0), Vector2.Zero, 100, out Vector3 view, out Vector3 right, out Vector3 up);
        Assert.InRange(Vector3.Dot(Vector3.Backward, right), .99999f, 1.00001f);
        Assert.InRange(Math.Abs(Vector3.Dot(Vector3.Backward, view)), 0f, .00001f);
        AssertBasis(view, right, up);
        drag.Move(Vector2.Zero, Vector2.Zero, 100, out view, out right, out up);
        Assert.InRange(Vector3.Distance(view, Vector3.Backward), 0f, .00001f);
        Assert.InRange(Vector3.Distance(right, Vector3.Right), 0f, .00001f);
    }

    [Fact]
    public void OppositeLimbPointsProduceAValidHalfTurn()
    {
        var drag = new PlanetSurfaceDrag();
        Assert.True(drag.Begin(new Vector2(-100, 0), Vector2.Zero, 100,
            Vector3.Backward, Vector3.Right, Vector3.Up));
        drag.Move(new Vector2(100, 0), Vector2.Zero, 100, out Vector3 view, out Vector3 right, out Vector3 up);
        Assert.InRange(Vector3.Dot(Vector3.Left, right), .99999f, 1.00001f);
        AssertBasis(view, right, up);
    }

    [Fact]
    public void BackgroundAndZeroSizedGlobeCannotBeGrabbed()
    {
        var drag = new PlanetSurfaceDrag();
        Assert.False(drag.Begin(new Vector2(101, 0), Vector2.Zero, 100,
            Vector3.Backward, Vector3.Right, Vector3.Up));
        Assert.False(drag.Begin(Vector2.Zero, Vector2.Zero, 0,
            Vector3.Backward, Vector3.Right, Vector3.Up));
    }

    static void AssertBasis(Vector3 view, Vector3 right, Vector3 up)
    {
        Assert.InRange(view.Length(), .99999f, 1.00001f);
        Assert.InRange(right.Length(), .99999f, 1.00001f);
        Assert.InRange(up.Length(), .99999f, 1.00001f);
        Assert.InRange(Math.Abs(Vector3.Dot(view, right)), 0f, .00001f);
        Assert.InRange(Vector3.Distance(Vector3.Cross(view, right), up), 0f, .00001f);
    }
}
