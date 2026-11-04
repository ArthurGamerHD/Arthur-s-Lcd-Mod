using System;
using System.Collections.Generic;
using Jakaria.API;
using LcdMod.Common.Ships;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Helpers
{
    // What a scan reads of a ship, in one pass over its blocks (its mechanical subgrids too; docked ships are not
    // part of it): its class (ShipClassTable) and its identification code (ShipIdCode).
    internal static class ShipClassifier
    {
        const double EARTH_GRAVITY = 9.81d;
        const double MOON_GRAVITY = EARTH_GRAVITY * 0.25d;
        // Cargo volume (cubic metres) from which a ship counts as a cargo carrier.
        const double LARGE_CARGO_VOLUME = 100d;
        const double SMALL_CARGO_VOLUME = 10d;

        static readonly List<IMyCubeGrid> Group = new List<IMyCubeGrid>();
        static readonly List<IMySlimBlock> Blocks = new List<IMySlimBlock>();
        // Thrust along each of the main grid's six directions: what lifts in an atmosphere, and on the Moon.
        static readonly double[] AtmosphereThrust = new double[6];
        static readonly double[] MoonThrust = new double[6];

        // The class as shown (e.g. "Major Sagittarius Class") and the code (e.g. "[BAP-647]").
        public static void Identify(IMyCubeGrid grid, out string className, out string code)
        {
            var ship = new ShipTraits { WaterWorld = WaterModAPI.Registered };
            int mainBlocks = -1;
            IMyCubeGrid main = grid;
            bool seat = false, remote = false, medical = false, gasGenerator = false, artificialMass = false,
                gravityGenerator = false;
            double cargo = 0d, mass = 0d;
            int weapons = 0, functional = 0;
            Array.Clear(AtmosphereThrust, 0, 6);
            Array.Clear(MoonThrust, 0, 6);

            Group.Clear();
            MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, Group);
            if (!Group.Contains(grid))
                Group.Add(grid);
            for (int g = 0; g < Group.Count; g++)
            {
                IMyCubeGrid part = Group[g];
                Blocks.Clear();
                part.GetBlocks(Blocks);
                if (Blocks.Count > mainBlocks)
                {
                    mainBlocks = Blocks.Count;
                    main = part;
                }
            }
            MatrixD toMain = MatrixD.Transpose(main.WorldMatrix.GetOrientation());
            for (int g = 0; g < Group.Count; g++)
            {
                IMyCubeGrid part = Group[g];
                ship.Station |= part.IsStatic;
                if (part.Physics != null && !part.IsStatic)
                    mass += part.Physics.Mass;
                Blocks.Clear();
                part.GetBlocks(Blocks);
                ship.Blocks += Blocks.Count;
                for (int b = 0; b < Blocks.Count; b++)
                {
                    IMySlimBlock slim = Blocks[b];
                    IMyCubeBlock fat = slim.FatBlock;
                    if (fat == null)
                    {
                        CountArmor(slim, ref ship);
                        continue;
                    }
                    string type = slim.BlockDefinition.Id.TypeId.ToString();
                    // Decoration (walls, pipes, catwalks, windows) has a block entity too; only what has a terminal
                    // does something.
                    if (fat is IMyTerminalBlock && !(fat is IMyLightingBlock))
                        functional++;
                    if (fat is IMyThrust)
                        AddThrust((IMyThrust)fat, slim, toMain, ref ship);
                    else if (fat is IMyUserControllableGun) weapons++;
                    else if (fat is IMyShipDrill) ship.Drill = true;
                    else if (fat is IMyShipWelder) ship.Welder = true;
                    else if (fat is IMyShipGrinder) ship.Grinder = true;
                    else if (fat is IMyJumpDrive) ship.JumpDrive = true;
                    else if (fat is IMySafeZoneBlock) ship.SafeZone = true;
                    else if (fat is IMyMotorSuspension) ship.Wheels = true;
                    else if (fat is IMyReactor) ship.Reactor = true;
                    else if (fat is IMyBatteryBlock) ship.Battery = true;
                    else if (fat is IMySolarPanel || fat is IMyWindTurbine) ship.CleanEnergy = true;
                    else if (type == "MyObjectBuilder_HydrogenEngine") ship.HydrogenEngine = true;
                    else if (fat is IMyStoreBlock) ship.Trade = true;
                    else if (fat is IMyAssembler || fat is IMyRefinery) ship.Production = true;
                    else if (fat is IMyMedicalRoom || type == "MyObjectBuilder_SurvivalKit") medical = true;
                    else if (fat is IMyGasGenerator) gasGenerator = true;
                    else if (fat is IMyArtificialMassBlock) artificialMass = true;
                    else if (fat is IMyGravityGenerator) gravityGenerator = true;
                    else if (fat is IMyRemoteControl || fat is IMyFlightMovementBlock) remote = true;
                    else if (fat is IMyCockpit) seat = true;
                    else if (fat is IMyCargoContainer && fat.InventoryCount > 0)
                        cargo += (double)fat.GetInventory(0).MaxVolume;
                }
            }
            Blocks.Clear();
            Group.Clear();

            ship.Large = main.GridSizeEnum == MyCubeSize.Large;
            ship.Unmanned = !seat && remote;
            ship.Combat = ShipIdCode.IsCombat(weapons, functional);
            ship.GravityDrive = artificialMass && gravityGenerator;
            ship.Cargo = cargo >= (ship.Large ? LARGE_CARGO_VOLUME : SMALL_CARGO_VOLUME);
            // A jump drive, or what keeps a crew alive far from home.
            ship.Exploration = ship.JumpDrive || (medical && gasGenerator);
            ship.LiftsInAtmosphere = mass > 0d && Strongest(AtmosphereThrust) >= mass * EARTH_GRAVITY;
            ship.LiftsOnMoon = mass > 0d && Strongest(MoonThrust) >= mass * MOON_GRAVITY;

            ShipKind kind = ship.Station ? ShipKind.Station : ship.Large ? ShipKind.LargeGrid : ShipKind.SmallGrid;
            ShipClassSize size;
            string name = ShipClassTable.Name(kind, ship.Blocks, out size);
            string format = size == ShipClassSize.Major ? "ShipClass_Major"
                : size == ShipClassSize.Minor ? "ShipClass_Minor" : "ShipClass_Format";
            className = string.Format(LocHelper.GetLoc(MOD_PREFIX + format), name);
            code = ShipIdCode.Compose(ship);
        }

        // Armor is a plain cube block named so; heavy armor says so too.
        static void CountArmor(IMySlimBlock slim, ref ShipTraits ship)
        {
            string subtype = slim.BlockDefinition.Id.SubtypeName;
            if (subtype == null || subtype.IndexOf("Armor", StringComparison.OrdinalIgnoreCase) < 0)
                return;
            ship.ArmorBlocks++;
            if (subtype.IndexOf("Heavy", StringComparison.OrdinalIgnoreCase) >= 0)
                ship.HeavyArmorBlocks++;
        }

        // By the thruster's own kind; one the game does not ship is exotic.
        static void AddThrust(IMyThrust thrust, IMySlimBlock slim, MatrixD toMain, ref ShipTraits ship)
        {
            var definition = MyDefinitionManager.Static.GetCubeBlockDefinition(slim.BlockDefinition.Id) as MyThrustDefinition;
            string kind = definition == null ? null : definition.ThrusterType.String;
            int axis = Axis(Vector3D.TransformNormal(thrust.WorldMatrix.Backward, toMain));
            double force = thrust.MaxThrust;
            switch (kind)
            {
                case "Atmospheric":
                    ship.AtmosphericThrust = true;
                    AtmosphereThrust[axis] += force;
                    break;
                case "Ion":
                    ship.IonThrust = true;
                    MoonThrust[axis] += force;
                    break;
                case "Hydrogen":
                    ship.HydrogenThrust = true;
                    AtmosphereThrust[axis] += force;
                    MoonThrust[axis] += force;
                    break;
                default:
                    ship.ExoticThrust = true;
                    break;
            }
        }

        static int Axis(Vector3D direction)
        {
            double x = Math.Abs(direction.X), y = Math.Abs(direction.Y), z = Math.Abs(direction.Z);
            if (x >= y && x >= z)
                return direction.X >= 0d ? 0 : 1;
            if (y >= z)
                return direction.Y >= 0d ? 2 : 3;
            return direction.Z >= 0d ? 4 : 5;
        }

        static double Strongest(double[] thrust)
        {
            double strongest = 0d;
            for (int i = 0; i < thrust.Length; i++)
                strongest = Math.Max(strongest, thrust[i]);
            return strongest;
        }
    }
}
