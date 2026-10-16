using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using LcdMod.Client.Apps.Inventory;
using LcdMod.Client.Apps.ViewModel;
using VRage;
using Adk.Gui.Extensions;
using LcdMod.Client.Apps.Inventory.Crafting;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;

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
        readonly Inventory.InventoryViewModel _nativeViewModel;
        readonly Dictionary<ItemEntry, InventoryItemModel> _entries = new Dictionary<ItemEntry, InventoryItemModel>();
        readonly List<InventoryItemModel> _nextItems = new List<InventoryItemModel>();
        readonly HashSet<ItemEntry> _seen = new HashSet<ItemEntry>();
        readonly List<ItemEntry> _stale = new List<ItemEntry>();
        readonly List<ItemEntry> _sourceItems = new List<ItemEntry>();
        readonly Inventory.InventoryView _nativeView;
        internal override float WindowRenderScaling => Scale > 0f ? Scale : 1f;

        public InventoryApp(IAppHost host) : base(host)
        {
            _nativeViewModel = new Inventory.InventoryViewModel
            {
                NameSortCommand = new DelegateCommand(_ => SortNativeItems(false)),
                AmountSortCommand = new DelegateCommand(_ => SortNativeItems(true))
            };
            _nativeView = new Inventory.InventoryView(_nativeViewModel);
            SetContent(_nativeView);
            RefreshPresentation();
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

        public override void Update()
        {
            base.Update();
            RefreshPresentation();
            if (_nativeView.AdvanceAutoScroll(MyAPIGateway.Session.GameplayFrameCounter,
                InteractionComponent.AutoScrollStep)) MarkDirty();
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            RefreshPresentation();
        }

        protected override void OnNativeSortChanged() { RefreshPresentation(); }

        void RefreshPresentation()
        {
            if (_nativeViewModel == null) return;
            var mode = PresentationMode;
            _nativeViewModel.Configure(mode == ItemDisplayMode.Card || mode == ItemDisplayMode.Grid,
                mode == ItemDisplayMode.Card, mode == ItemDisplayMode.Table, SurfaceFontSize, TextFont,
                MyTexts.GetString("StoreBlock_Column_Name"), MyTexts.GetString("StoreBlock_Column_Amount"),
                NativeSortByAmount, NativeSortDescending);
            _nextItems.Clear(); _seen.Clear();
            // Name resolution may reorder the source; never mutate it while enumerating.
            _sourceItems.Clear(); _sourceItems.AddRange(DisplayItems);
            foreach (var item in _sourceItems) PrepareNativeItem(item);
            foreach (var item in DisplayItems)
            {
                _seen.Add(item);
                InventoryItemModel model;
                if (!_entries.TryGetValue(item, out model))
                {
                    model = new InventoryItemModel(() => OpenCraftWindow(item));
                    _entries.Add(item, model);
                }
                model.Update(item.Icon, item.DisplayName, item.AmountText, (int)item.AvailabilityStatus);
                _nextItems.Add(model);
            }
            _stale.Clear();
            foreach (var item in _entries.Keys) if (!_seen.Contains(item)) _stale.Add(item);
            foreach (var item in _stale) _entries.Remove(item);
            _nativeViewModel.SetItems(_nextItems);
        }

        void OpenCraftWindow(ItemEntry item)
        {
            if (HasOpenDialog) return;
            Inventory.Crafting.CraftLauncher.Craft(this, GridLogic, item.ItemType, ResolveDisplayName(item.ItemType),
                ResolveSprite(item.ItemType), item.CraftAmount);
        }
    }
}
