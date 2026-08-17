using System;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.ViewModel
{
    public interface IItemsAppViewModel : IDisposable
    {
        ObservableList<ItemEntry> Items { get; }

        bool HasItems { get; }

        void UpdateSelection(
            ItemSelectionConfigComponent selection,
            BlockSelectionConfigComponent blockSelection,
            bool hideEmpty);
    }
}
