using LcdMod.Common.Ships;

namespace Arthur_s_Lcd_Mod.Tests;

public class ShipIdCodeTests
{
    // A large flying ship with a reactor and batteries, half its blocks light armor.
    static ShipTraits LargeShip() => new ShipTraits
    {
        Large = true,
        IonThrust = true,
        LiftsOnMoon = true,
        Reactor = true,
        Battery = true,
        Blocks = 100,
        ArmorBlocks = 50
    };

    [Fact]
    public void SupportCraftCarriesItsPropulsionAndTools()
    {
        ShipTraits ship = LargeShip();
        ship.JumpDrive = true;
        ship.Drill = true;
        // Note 4: light armor is left out; Note 1: tools only for a support craft.
        Assert.Equal("[AF-023]", ShipIdCode.Compose(ship));
    }

    [Fact]
    public void StationThatIsNotSupportEndsWithItsRole()
    {
        var station = new ShipTraits { Station = true, Battery = true, Blocks = 100, Cargo = true };
        // Note 3: no propulsion or tools; unarmored stays.
        Assert.Equal("[KMX-7]", ShipIdCode.Compose(station));
    }

    [Fact]
    public void SupportStationDoesNotFly()
    {
        var station = new ShipTraits { Station = true, SafeZone = true, JumpDrive = true, Battery = true, Blocks = 100, Welder = true };
        // Note 2: a station's propulsion is 0.
        Assert.Equal("[MMX-001]", ShipIdCode.Compose(station));
    }

    [Fact]
    public void RoverThatIsNotSupportEndsWithItsRole()
    {
        var rover = new ShipTraits { Large = true, Wheels = true, Battery = true, Blocks = 100, Combat = true };
        Assert.Equal("[DMX-6]", ShipIdCode.Compose(rover));
    }

    [Fact]
    public void RoverThatFliesIsARoverShip()
    {
        var rover = new ShipTraits { Wheels = true, AtmosphericThrust = true, LiftsInAtmosphere = true, Battery = true, Blocks = 100 };
        Assert.Equal('H', ShipIdCode.Kind(rover));
    }

    [Fact]
    public void BoatNeedsWaterAndCannotFly()
    {
        var boat = new ShipTraits { Large = true, AtmosphericThrust = true, WaterWorld = true };
        Assert.Equal('R', ShipIdCode.Kind(boat));
        boat.WaterWorld = false;
        Assert.Equal('B', ShipIdCode.Kind(boat));
        boat.WaterWorld = true;
        boat.LiftsInAtmosphere = true;
        Assert.Equal('B', ShipIdCode.Kind(boat));
    }

    [Fact]
    public void UnmannedCraftIsADrone()
    {
        var drone = new ShipTraits { Unmanned = true, IonThrust = true };
        Assert.Equal('P', ShipIdCode.Kind(drone));
        drone.Large = true;
        drone.JumpDrive = true;
        Assert.Equal('N', ShipIdCode.Kind(drone));
    }

    [Fact]
    public void NothingToMoveItIsUncategorized()
    {
        Assert.Equal('X', ShipIdCode.Kind(new ShipTraits()));
    }

    [Theory]
    // Under 5% armor: unarmored; over 90% one kind: light or heavy; in between: mixed.
    [InlineData(100, 4, 0, 'X')]
    [InlineData(100, 5, 0, 'L')]
    [InlineData(100, 50, 4, 'L')]
    [InlineData(100, 50, 5, 'H')]
    [InlineData(100, 50, 25, 'H')]
    [InlineData(100, 50, 45, 'H')]
    [InlineData(100, 50, 46, 'P')]
    public void ArmorFollowsItsShares(int blocks, int armor, int heavy, char expected)
    {
        Assert.Equal(expected, ShipIdCode.Armor(new ShipTraits { Blocks = blocks, ArmorBlocks = armor, HeavyArmorBlocks = heavy }));
    }

    [Theory]
    [InlineData(true, true, true, true, 'A')]
    [InlineData(true, true, false, true, 'B')]
    [InlineData(true, true, false, false, 'C')]
    [InlineData(true, false, true, true, 'D')]
    [InlineData(true, false, true, false, 'E')]
    [InlineData(true, false, false, true, 'F')]
    [InlineData(false, true, true, true, 'G')]
    [InlineData(false, true, false, true, 'H')]
    [InlineData(false, false, true, true, 'I')]
    [InlineData(true, false, false, false, 'J')]
    [InlineData(false, true, false, false, 'K')]
    [InlineData(false, false, true, false, 'L')]
    [InlineData(false, false, false, true, 'M')]
    // Mixes the scheme does not name.
    [InlineData(true, true, true, false, 'X')]
    [InlineData(false, true, true, false, 'X')]
    [InlineData(false, false, false, false, 'X')]
    public void PowerFollowsTheTable(bool reactor, bool engine, bool clean, bool battery, char expected)
    {
        var ship = new ShipTraits { Reactor = reactor, HydrogenEngine = engine, CleanEnergy = clean, Battery = battery };
        Assert.Equal(expected, ShipIdCode.Power(ship));
    }

    [Theory]
    [InlineData(true, false, false, false, false, '6')]
    [InlineData(true, true, false, false, false, '1')]
    [InlineData(true, true, true, false, false, '5')]
    [InlineData(true, false, true, false, false, '9')]
    [InlineData(false, true, false, true, false, '3')]
    [InlineData(false, true, true, true, false, '4')]
    [InlineData(false, true, true, false, false, '2')]
    [InlineData(false, true, false, false, false, '7')]
    [InlineData(false, false, false, false, true, '7')]
    [InlineData(false, false, true, false, false, '8')]
    [InlineData(false, false, false, false, false, '0')]
    public void RoleFollowsTheTable(bool combat, bool cargo, bool exploration, bool trade, bool production, char expected)
    {
        var ship = new ShipTraits { Combat = combat, Cargo = cargo, Exploration = exploration, Trade = trade, Production = production };
        Assert.Equal(expected, ShipIdCode.Role(ship));
    }

    [Theory]
    // At least two weapons, and at least 8% of the functional blocks.
    [InlineData(1, 5, false)]
    [InlineData(2, 25, true)]
    [InlineData(2, 26, false)]
    [InlineData(4, 30, true)]
    [InlineData(3, 150, false)]
    [InlineData(30, 300, true)]
    public void CombatNeedsAGoodShareOfWeapons(int weapons, int functional, bool expected)
    {
        Assert.Equal(expected, ShipIdCode.IsCombat(weapons, functional));
    }

    [Fact]
    public void CombatToolCarrierIsNotSupport()
    {
        // A warship with repair welders and grinders stays a warship.
        Assert.Equal('6', ShipIdCode.Role(new ShipTraits { Combat = true, Welder = true, Grinder = true }));
        Assert.Equal('1', ShipIdCode.Role(new ShipTraits { Combat = true, Welder = true, Cargo = true }));
        Assert.Equal('0', ShipIdCode.Role(new ShipTraits { Drill = true }));
        // Tools make a work ship even with cargo aboard.
        Assert.Equal('0', ShipIdCode.Role(new ShipTraits { Drill = true, Cargo = true }));
    }

    [Theory]
    // Only the kinds that actually lift count.
    [InlineData(true, false, false, true, false, '1')]
    [InlineData(false, true, false, false, true, '2')]
    [InlineData(true, true, false, true, true, '3')]
    [InlineData(false, false, true, true, true, '4')]
    [InlineData(true, false, true, true, false, '5')]
    [InlineData(false, true, true, false, true, '6')]
    [InlineData(true, true, true, true, true, '7')]
    [InlineData(true, true, false, false, false, '0')]
    [InlineData(true, true, false, true, false, '1')]
    public void PropulsionCountsWhatLifts(bool atmospheric, bool ion, bool hydrogen, bool liftsAtmosphere, bool liftsMoon, char expected)
    {
        var ship = new ShipTraits
        {
            AtmosphericThrust = atmospheric, IonThrust = ion, HydrogenThrust = hydrogen,
            LiftsInAtmosphere = liftsAtmosphere, LiftsOnMoon = liftsMoon
        };
        Assert.Equal(expected, ShipIdCode.Propulsion(ship));
    }

    [Fact]
    public void GravityAndExoticDrives()
    {
        Assert.Equal('8', ShipIdCode.Propulsion(new ShipTraits { GravityDrive = true }));
        Assert.Equal('9', ShipIdCode.Propulsion(new ShipTraits { ExoticThrust = true }));
    }

    [Theory]
    [InlineData(true, false, false, '1')]
    [InlineData(false, true, false, '2')]
    [InlineData(false, false, true, '3')]
    [InlineData(true, true, false, '6')]
    [InlineData(true, false, true, '7')]
    [InlineData(false, true, true, '8')]
    [InlineData(true, true, true, '9')]
    public void ToolsFollowTheTable(bool welder, bool grinder, bool drill, char expected)
    {
        Assert.Equal(expected, ShipIdCode.Tools(new ShipTraits { Welder = welder, Grinder = grinder, Drill = drill }));
    }

    [Fact]
    public void SupportWithoutToolsIsOtherOrComplementary()
    {
        Assert.Equal('4', ShipIdCode.Tools(new ShipTraits { Battery = true }));
        Assert.Equal('5', ShipIdCode.Tools(new ShipTraits()));
    }
}
