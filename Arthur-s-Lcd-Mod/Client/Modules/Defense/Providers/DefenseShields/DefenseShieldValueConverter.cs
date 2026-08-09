namespace LcdMod.Client.Modules.Defense.Providers.DefenseShields
{
    internal static class DefenseShieldValueConverter
    {
        public static float ChargeToHp(float charge, float hpPerCharge)
        {
            if (charge <= 0f || hpPerCharge <= 0 || float.IsNaN(charge) || float.IsInfinity(charge))
                return 0f;

            float hp = charge * hpPerCharge;
            return float.IsNaN(hp) || float.IsInfinity(hp) ? 0f : hp;
        }
    }
}
