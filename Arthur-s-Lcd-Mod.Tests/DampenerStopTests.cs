using LcdMod.Common.Physics;

namespace Arthur_s_Lcd_Mod.Tests;

// SE's dampeners clamp each grid axis on its own and taper with speed (v / 0.5 s); a collinear weakest-axis,
// constant-deceleration estimate overshot Ion/Hydrogen stops by up to ~2x and undershot Prototech.
public class DampenerStopTests
{
    static readonly double[] NoGravity = { 0d, 0d, 0d };

    static double Stop(double[] velocity, double[] positive, double[] negative, double mass = 1000d, double[] gravity = null)
    {
        Assert.True(DampenerStop.Simulate(velocity, gravity ?? NoGravity, mass, positive, negative,
            out _, out double distance, out _));
        return distance;
    }

    [Fact]
    public void AWeakAxisDoesNotSlowTheStrongAxisDown()
    {
        double[] thrust = { 20000d, 2000d, 2000d };
        double diagonal = Stop(new[] { 30d, 30d, 0d }, thrust, thrust);

        // The old model braked the whole diagonal at the weak axis' rate: |v|^2 / (2 * a) with a = 2000 / 1000 / sin(45).
        double speed = Math.Sqrt(30d * 30d * 2d);
        double collinear = speed * speed / (2d * (2000d / 1000d / Math.Sqrt(.5d)));
        Assert.True(diagonal < collinear * .9, $"per-axis {diagonal:0.0} m vs collinear {collinear:0.0} m");

        // The strong axis alone stops just as quickly with or without the weak axis' velocity.
        double strongOnly = Stop(new[] { 30d, 0d, 0d }, thrust, thrust);
        double weakOnly = Stop(new[] { 0d, 30d, 0d }, thrust, thrust);
        Assert.InRange(diagonal, Math.Sqrt(strongOnly * strongOnly + weakOnly * weakOnly) * .95, strongOnly + weakOnly);
    }

    [Fact]
    public void AbundantThrustTapersLikeTheDampenerInsteadOfStoppingInstantly()
    {
        double[] huge = { 1e9, 1e9, 1e9 };
        // Exponential approach with a 0.5 s time constant covers about v * 0.5 s; v^2 / 2a would be ~0 m.
        Assert.InRange(Stop(new[] { 10d, 0d, 0d }, huge, huge), 4.8d, 5d);
    }

    [Fact]
    public void GravityIsHeldAgainstOnlyWhileTheAxisCanCarryIt()
    {
        double[] gravity = { 0d, 0d, -9.81d };
        Assert.True(DampenerStop.Simulate(new[] { 10d, 0d, 0d }, gravity, 1000d, new[] { 20000d, 20000d, 20000d },
            new[] { 20000d, 20000d, 20000d }, out _, out _, out _));
        Assert.False(DampenerStop.Simulate(new[] { 10d, 0d, 0d }, gravity, 1000d, new[] { 20000d, 20000d, 5000d },
            new[] { 20000d, 20000d, 20000d }, out _, out _, out _));
    }
}
