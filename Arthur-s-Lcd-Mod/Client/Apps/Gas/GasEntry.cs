using LcdMod.Client.Helpers;

namespace LcdMod.Client.Apps.Gas
{
    internal sealed class GasEntry
    {
        public long EntryId { get; set; }
        public string Icon { get; set; }
        public string Name { get; set; }
        public float Percentage { get; set; }
        public string PercentageText => FormatingHelper.PercentageToString(Percentage);
    }
}
