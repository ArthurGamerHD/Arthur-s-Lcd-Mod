using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using Sandbox.ModAPI;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(4)]
    // ReSharper disable once PartialTypeWithSinglePart
    internal sealed partial class InventoryApp : ItemsApp
    {
        public const string NAME = "Inventory";

        public override Dictionary<MyItemType, double> ItemSource =>
            Host.GridLogic?.GetItems(BlockSelectionComponent, ItemSelectionComponent, Host.Block as IMyTerminalBlock);

        protected override ItemSnapshot ReadItemSnapshot(IMyTerminalBlock lcd)
        {
            return Host.GridLogic?.GetItemsSnapshot(BlockSelectionComponent, ItemSelectionComponent, lcd)
                   ?? base.ReadItemSnapshot(lcd);
        }

        protected override string DefaultTitle => NAME;

        public InventoryApp(IAppHost host) : base(host)
        {
        }
    }
}
