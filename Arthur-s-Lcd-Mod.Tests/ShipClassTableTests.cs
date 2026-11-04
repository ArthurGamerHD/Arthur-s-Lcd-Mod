using LcdMod.Common.Ships;

namespace Arthur_s_Lcd_Mod.Tests;

public class ShipClassTableTests
{
    [Theory]
    // Small grids: Aries >2000 (Major from 4000), Taurus >1000, Gemini >500, Cancer up to 500 (Minor up to 100).
    [InlineData((int)ShipKind.SmallGrid, 4000, "Aries", (int)ShipClassSize.Major)]
    [InlineData((int)ShipKind.SmallGrid, 3999, "Aries", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.SmallGrid, 2001, "Aries", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.SmallGrid, 2000, "Taurus", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.SmallGrid, 1001, "Taurus", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.SmallGrid, 1000, "Gemini", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.SmallGrid, 501, "Gemini", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.SmallGrid, 500, "Cancer", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.SmallGrid, 101, "Cancer", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.SmallGrid, 100, "Cancer", (int)ShipClassSize.Minor)]
    [InlineData((int)ShipKind.SmallGrid, 1, "Cancer", (int)ShipClassSize.Minor)]
    // Stations: Leo >3000 (Major from 6000), Virgo >1500, Libra >500, Scorpius up to 500 (Minor up to 100).
    [InlineData((int)ShipKind.Station, 6000, "Leo", (int)ShipClassSize.Major)]
    [InlineData((int)ShipKind.Station, 3001, "Leo", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.Station, 3000, "Virgo", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.Station, 1500, "Libra", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.Station, 500, "Scorpius", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.Station, 100, "Scorpius", (int)ShipClassSize.Minor)]
    // Large grids: Sagittarius >4000 (Major from 8000), Capricornus >2000, Aquarius >600, Pisces up to 600 (Minor
    // up to 120).
    [InlineData((int)ShipKind.LargeGrid, 8000, "Sagittarius", (int)ShipClassSize.Major)]
    [InlineData((int)ShipKind.LargeGrid, 7999, "Sagittarius", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.LargeGrid, 4000, "Capricornus", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.LargeGrid, 2000, "Aquarius", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.LargeGrid, 601, "Aquarius", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.LargeGrid, 600, "Pisces", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.LargeGrid, 121, "Pisces", (int)ShipClassSize.Normal)]
    [InlineData((int)ShipKind.LargeGrid, 120, "Pisces", (int)ShipClassSize.Minor)]
    public void ClassesFollowTheTable(int kind, int blocks, string name, int size)
    {
        ShipClassSize actual;
        Assert.Equal(name, ShipClassTable.Name((ShipKind)kind, blocks, out actual));
        Assert.Equal((ShipClassSize)size, actual);
    }
}
