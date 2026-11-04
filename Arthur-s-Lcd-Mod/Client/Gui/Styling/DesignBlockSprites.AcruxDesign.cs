using System.Collections.Generic;

namespace LcdMod.Client.Gui.Styling
{
    /// <summary>
    /// Block icons as the mod names them in game: TextureHelper.GetOrAddTextureForBlock registers an LCD texture named
    /// after the block definition's id, with the definition's icon. The preview cannot run that registration, so the
    /// blocks design-time data shows are listed here (ids and icons as in the game's CubeBlocks definitions) and given
    /// to the preview through <see cref="DesignThemeResources.DesignTextures"/>. A *.AcruxDesign.cs file: compiled for the
    /// preview, not packed by MDK.
    /// </summary>
    internal static class DesignBlockSprites
    {
        public const string LargeCargoContainer = "MyObjectBuilder_CargoContainer/LargeBlockLargeContainer";
        public const string SmallCargoContainer = "MyObjectBuilder_CargoContainer/LargeBlockSmallContainer";
        public const string Connector = "MyObjectBuilder_ShipConnector/Connector";
        public const string HydrogenTank = "MyObjectBuilder_OxygenTank/LargeHydrogenTank";
        public const string OxygenTank = "MyObjectBuilder_OxygenTank/Oxygen";
        public const string GatlingTurret = "MyObjectBuilder_LargeGatlingTurret/LargeGatlingTurretReskin";
        public const string ArtilleryTurret = "MyObjectBuilder_LargeMissileTurret/LargeCalibreTurret";
        public const string AssaultCannonTurret = "MyObjectBuilder_LargeMissileTurret/LargeBlockMediumCalibreTurret";
        public const string Battery = "MyObjectBuilder_BatteryBlock/LargeBlockBatteryBlock";
        public const string JumpDrive = "MyObjectBuilder_JumpDrive/LargeJumpDrive";
        public const string SlideDoor = "MyObjectBuilder_AirtightSlideDoor/LargeBlockSlideDoor";
        public const string InteriorLight = "MyObjectBuilder_InteriorLight/SmallLight";
        public const string Drill = "MyObjectBuilder_Drill/LargeBlockDrill";
        public const string Rotor = "MyObjectBuilder_MotorStator/LargeStator";

        public static readonly KeyValuePair<string, string>[] Registrations =
        {
            Icon(LargeCargoContainer, "container_large.dds"),
            Icon(SmallCargoContainer, "container.dds"),
            Icon(Connector, "Connector.dds"),
            Icon(HydrogenTank, "HydrogenTank.dds"),
            Icon(OxygenTank, "OxygenTank.dds"),
            Icon(GatlingTurret, "GatlingTurretReskinLarge.dds"),
            Icon(ArtilleryTurret, "HeavyCalibreTurret.dds"),
            Icon(AssaultCannonTurret, "MediumCalibreTurretLarge.dds"),
            Icon(Battery, "Battery.dds"),
            Icon(JumpDrive, "JumpDrive.dds"),
            Icon(SlideDoor, "SlideDoor.dds"),
            Icon(InteriorLight, "interior_light.dds"),
            Icon(Drill, "drill.dds"),
            Icon(Rotor, "motor.dds"),
        };

        static KeyValuePair<string, string> Icon(string id, string file)
        {
            return new KeyValuePair<string, string>(id, @"Textures\GUI\Icons\Cubes\" + file);
        }
    }
}
