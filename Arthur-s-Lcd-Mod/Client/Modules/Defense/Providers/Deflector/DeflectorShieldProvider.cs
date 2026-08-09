using LcdMod.Client.Modules.Defense.Providers.EnergyShield;

namespace LcdMod.Client.Modules.Defense.Providers.Deflector
{
    public sealed class DeflectorShieldProvider : CythonPointShieldProvider
    {
        const ulong WORKSHOP_ID = 3147276777UL;

        public DeflectorShieldProvider()
            : base(
                "Deflector Shields",
                WORKSHOP_ID,
                "LargeMESLargeShieldGeneratorBase",
                "SmallShipSmallShieldGeneratorBase",
                "SmallShipSmallShieldMES",
                "LargeShipLargeShieldGeneratorBase")
        {
        }
    }
}
