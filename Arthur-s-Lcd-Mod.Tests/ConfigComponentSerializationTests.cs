using LcdMod.Common.Config.Components;
using ProtoBuf;
using VRage.Game.ModAPI;
using Generated;

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

    [Fact]
    public void PlanetaryMapConfigComponent_RoundTrips_OrientationAndCameraLock()
    {
        var surface = new SurfaceConfig
        {
            SurfaceIndex = 1,
            AppTypeId = (int)AppType.PlanetaryMap
        };

        surface.Set(LcdMod.Common.Helpers.Constants.APP, new PlanetaryMapConfigComponent
        {
            NorthUp = false,
            FollowCamera = false,
            OrbitYawRadians = 0.75f,
            OrbitPitchRadians = -0.25f,
            HasStaticCameraPosition = true,
            StaticCameraPositionX = 123.5d,
            StaticCameraPositionY = -456.25d,
            StaticCameraPositionZ = 789.125d,
            Zoom = 2.5f,
            MapLayer = 2,
            DisplayMyGps = true,
            IncludeRadioSignals = true,
            AlwaysDisplayedGpsHashes = new[] { 101 },
            AlwaysDisplayedGpsWaypoints = new[]
            {
                new GpsDisplayWaypoint
                {
                    SourceHash = 202,
                    Name = "Ice Hauler",
                    X = 1234.5d,
                    Y = -987.25d,
                    Z = 42.125d,
                    Color = new VRageMath.Color { PackedValue = 0xFF336699u }
                }
            }
        });

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, surface);
        stream.Position = 0;

        var result = Serializer.Deserialize<SurfaceConfig>(stream);
        var planetaryMap = result.Get<PlanetaryMapConfigComponent>(LcdMod.Common.Helpers.Constants.APP);

        Assert.False(planetaryMap.NorthUp);
        Assert.False(planetaryMap.FollowCamera);
        Assert.Equal(0.75f, planetaryMap.OrbitYawRadians);
        Assert.Equal(-0.25f, planetaryMap.OrbitPitchRadians);
        Assert.True(planetaryMap.HasStaticCameraPosition);
        Assert.Equal(123.5d, planetaryMap.StaticCameraPositionX);
        Assert.Equal(-456.25d, planetaryMap.StaticCameraPositionY);
        Assert.Equal(789.125d, planetaryMap.StaticCameraPositionZ);
        Assert.Equal(2.5f, planetaryMap.Zoom);
        Assert.Equal(2, planetaryMap.MapLayer);
        Assert.True(planetaryMap.DisplayMyGps);
        Assert.True(planetaryMap.IncludeRadioSignals);
        Assert.Equal(new[] { 101 }, planetaryMap.AlwaysDisplayedGpsHashes);
        GpsDisplayWaypoint waypoint = Assert.Single(planetaryMap.AlwaysDisplayedGpsWaypoints);
        Assert.Equal(202, waypoint.SourceHash);
        Assert.Equal("Ice Hauler", waypoint.Name);
        Assert.Equal(1234.5d, waypoint.X);
        Assert.Equal(-987.25d, waypoint.Y);
        Assert.Equal(42.125d, waypoint.Z);
        Assert.Equal(0xFF336699u, waypoint.Color.PackedValue);
    }

    [Fact]
    public void StarMapConfigComponent_RoundTrips_GpsWaypoints()
    {
        var surface = new SurfaceConfig
        {
            SurfaceIndex = 3,
            AppTypeId = 13
        };

        surface.Set(LcdMod.Common.Helpers.Constants.APP, new StarMapConfigComponent
        {
            FoV = 55f,
            DisplayMyGps = true,
            IncludeRadioSignals = true,
            AlwaysDisplayedGpsWaypoints = new[]
            {
                new GpsDisplayWaypoint
                {
                    SourceHash = 303,
                    Name = "Depot",
                    X = -12d,
                    Y = 34d,
                    Z = 56d,
                    Color = new VRageMath.Color { PackedValue = 0xFFAA5500u }
                }
            }
        });

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, surface);
        stream.Position = 0;

        var result = Serializer.Deserialize<SurfaceConfig>(stream);
        var starMap = result.Get<StarMapConfigComponent>(LcdMod.Common.Helpers.Constants.APP);

        Assert.Equal(55f, starMap.FoV);
        Assert.True(starMap.DisplayMyGps);
        Assert.True(starMap.IncludeRadioSignals);
        GpsDisplayWaypoint waypoint = Assert.Single(starMap.AlwaysDisplayedGpsWaypoints);
        Assert.Equal(303, waypoint.SourceHash);
        Assert.Equal("Depot", waypoint.Name);
        Assert.Equal(-12d, waypoint.X);
        Assert.Equal(34d, waypoint.Y);
        Assert.Equal(56d, waypoint.Z);
        Assert.Equal(0xFFAA5500u, waypoint.Color.PackedValue);
    }
}
