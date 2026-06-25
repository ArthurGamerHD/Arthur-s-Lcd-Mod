using System;
using System.Linq;
using LcdMod.Common.Helpers;
using VRage.Game;

namespace LcdMod.Common.Config.Components
{
    public static class ItemSelectionConfigComponentExtensions
    {
        public static MyDefinitionId[] GetSelectedItems(this ItemSelectionConfigComponent config)
        {
            if (config == null || config.SelectedDefinition == null || config.SelectedDefinition.Length == 0)
                return Array.Empty<MyDefinitionId>();

            try
            {
                return config.SelectedDefinition.Select(MyDefinitionId.Parse).ToArray();
            }
            catch (Exception exception)
            {
                ErrorHandlerHelper.LogError(exception, typeof(ItemSelectionConfigComponentExtensions));
                return Array.Empty<MyDefinitionId>();
            }
        }

        public static void SetSelectedItems(this ItemSelectionConfigComponent config, MyDefinitionId[] values)
        {
            if (config == null)
                return;
            config.SelectedDefinition = values == null
                ? Array.Empty<string>()
                : values.Select(value => value.ToString()).ToArray();
        }
    }
}
