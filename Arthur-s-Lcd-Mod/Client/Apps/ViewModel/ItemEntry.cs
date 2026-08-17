using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Common.Mvvm;
using VRage;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace LcdMod.Client.Apps.ViewModel
{
    public sealed partial class ItemEntry : ControlModelBase
    {
        [ObservableProperty] MyFixedPoint _amount;
        [ObservableProperty] double _craftAmount = 1d;
        [ObservableProperty] string _icon;
        [ObservableProperty] string _displayName;
        [ObservableProperty] string _amountText;
        [ObservableProperty] string _primaryAmountText;
        [ObservableProperty] string _secondaryAmountText;
        [ObservableProperty] ItemAmountDisplayMode _amountDisplayMode;
        [ObservableProperty] ItemAvailabilityStatus _availabilityStatus;
        [ObservableProperty] Color _listTextColor;
        [ObservableProperty] Color _listAmountColor;
        [ObservableProperty] Color _listIconColor;
        [ObservableProperty] Color _gridTextColor;
        [ObservableProperty] Color _gridAmountColor;
        [ObservableProperty] Color _gridIconColor;

        public ItemEntry(MyItemType itemType, MyFixedPoint amount)
        {
            ItemType = itemType;
            _amount = amount;
        }

        public MyItemType ItemType { get; private set; }

        public string TypeId => ItemType.TypeId;

        public void SetSimpleAmount(string amountText)
        {
            AmountDisplayMode = ItemAmountDisplayMode.Simple;
            AmountText = amountText;
            PrimaryAmountText = amountText;
            SecondaryAmountText = null;
        }

        public void SetQuotaAmount(string hasText, string needText)
        {
            AmountDisplayMode = ItemAmountDisplayMode.Quota;
            PrimaryAmountText = hasText;
            SecondaryAmountText = needText;
            AmountText = hasText + "/" + needText;
        }
    }

    public enum ItemAmountDisplayMode
    {
        Simple,
        Quota
    }

    public enum ItemAvailabilityStatus
    {
        Normal,
        Warning,
        Error
    }
}
