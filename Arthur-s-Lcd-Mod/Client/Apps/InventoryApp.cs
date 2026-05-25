using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.ModAPI;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.Apps
{
    internal sealed class InventoryApp : ItemsAppBase
    {
        public const string NAME = "Inventory";

        public override Dictionary<MyItemType, double> ItemSource =>
            AppConfig == null ? null : Host.GridLogic?.GetItems(AppConfig, Host.Block as IMyTerminalBlock);

        protected override string DefaultTitle => NAME;

        public InventoryApp(ScreenConfigWithItems config, IAppHost host) : base(config, host)
        {
        }
    }
}
