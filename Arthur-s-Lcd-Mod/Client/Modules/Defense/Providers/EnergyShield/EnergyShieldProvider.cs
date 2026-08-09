namespace LcdMod.Client.Modules.Defense.Providers.EnergyShield
{
    public sealed class EnergyShieldProvider : CythonPointShieldProvider
    {
        const ulong WORKSHOP_ID = 484504816UL;

        public EnergyShieldProvider()
            : base(
                "Energy Shields",
                WORKSHOP_ID,
                "LargeShipSmallShieldGeneratorBase",
                "SmallShipSmallShieldGeneratorBase",
                "SmallShipMicroShieldGeneratorBase",
                "LargeShipLargeShieldGeneratorBase")
        {
        }
    }
}
