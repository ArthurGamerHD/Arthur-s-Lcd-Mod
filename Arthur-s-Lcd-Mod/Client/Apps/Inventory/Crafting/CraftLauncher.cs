using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Common.Helpers;
using VRage.Game;

namespace LcdMod.Client.Apps.Inventory.Crafting
{
    /// <summary>Opens the craft window for one item, or for several items scaled by a multiplier.</summary>
    internal static class CraftLauncher
    {
        public static bool Craft(App app, GridLogic grid, MyDefinitionId item, string name, string icon, double amount)
        {
            return CraftMany(app, grid, new[] { new CraftRequestModel(item, name, icon, amount) });
        }

        public static bool CraftMany(App app, GridLogic grid, IList<CraftRequestModel> requests, int multiplier = 1)
        {
            if (requests.Count == 0)
                return false;
            var model = CraftDialogViewModel.ForRequests(requests, multiplier);
            model.Failed = error => ErrorHandlerHelper.LogError(error, typeof(CraftLauncher));
            var session = new CraftSession(grid, model);
            var dialog = new CraftDialogView(model);
            dialog.Closed += session.Dispose;
            if (app.ShowDialog(dialog))
            {
                (app as ItemsApp)?.NotifyCraftOpened();
                return true;
            }
            session.Dispose();
            return false;
        }
    }
}
