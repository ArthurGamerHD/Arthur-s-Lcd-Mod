using LcdMod.Common.Config.Components;
using ProtoBuf;
using VRage.Game.ModAPI;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class ConfigComponentSerializationTests
{
    [Fact]
    public void SurfaceConfig_RoundTripsLegacyAndConcreteIdentityFieldsIndependently()
    {
        var surface = new SurfaceConfig
        {
            SurfaceIndex = 4,
            LegacyAppKind = 10,
            AppTypeId = 17
        };

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, surface);
        stream.Position = 0;

        var result = Serializer.Deserialize<SurfaceConfig>(stream);

        Assert.Equal(4, result.SurfaceIndex);
        Assert.Equal(10, result.LegacyAppKind);
        Assert.Equal(17, result.AppTypeId);
    }

    [Fact]
    public void PowerConfigComponent_RoundTrips_AllPersistedFields()
    {
        var surface = new SurfaceConfig
        {
            SurfaceIndex = 2,
            AppTypeId = 1
        };

        surface.Set(LcdMod.Common.Helpers.Constants.APP, new PowerConfigComponent
        {
            HideEmpty = false,
            GraphWindowIndex = 4,
            PowerHistoryTier = 7,
            GridLinkTypeInternal = (int)GridLinkTypeEnum.Electrical
        });

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, surface);
        stream.Position = 0;

        var result = Serializer.Deserialize<SurfaceConfig>(stream);
        var power = result.Get<PowerConfigComponent>(LcdMod.Common.Helpers.Constants.APP);

        Assert.False(power.HideEmpty);
        Assert.Equal(4, power.GraphWindowIndex);
        Assert.Equal(7, power.PowerHistoryTier);
        Assert.Equal((int)GridLinkTypeEnum.Electrical, power.GridLinkTypeInternal);
    }
}
