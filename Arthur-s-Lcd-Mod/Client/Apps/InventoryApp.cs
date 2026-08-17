using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.GridData;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(4)]
    [ConfigComponent(Constants.ITEM_DISPLAY, typeof(ItemDisplayConfigComponent), PropertyName = "ItemDisplayComponent")]
    // ReSharper disable once PartialTypeWithSinglePart
    internal sealed partial class InventoryApp : ItemsApp
    {
        public const string NAME = "Inventory";

        protected override string DefaultTitle => NAME;
        protected override ItemDisplayMode PresentationMode =>
            ItemDisplayComponent.ResolveDisplayMode(GeneralComponent);

        public InventoryApp(IAppHost host) : base(host)
        {
            if (!ItemDisplayComponent.MigrateLegacyDisplayMode(GeneralComponent))
                return;

            var block = Host.Block as IMyTerminalBlock;
            var provider = Host.ProviderConfig;
            if (block == null || provider == null || !provider.CanWrite)
                return;

            LcdModClientComponent.RunNextFrame.Add(delegate
            {
                ConfigManager.Sync(block, provider);
            });
        }
    }
}
