using ListMode = LcdMod.Client.Apps.Projector.ProjectorListMode;
using System;
using System.Collections.Generic;
using Acrux.Core.Input;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.Inventory;
using LcdMod.Client.Apps.ViewModel;
using LcdMod.Client.Config;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps
{
    [LcdApp(17)]
    [ConfigComponent(PROJECTOR_REFERENCE, typeof(BlockReferenceConfigComponent), PropertyName = "ProjectorReferenceComponent")]
    [ConfigComponent(ITEM_DISPLAY, typeof(ItemDisplayConfigComponent), PropertyName = "ItemDisplayComponent")]
    internal sealed partial class ProjectorApp : ItemsApp
    {
        public const string TITLE = "DisplayName_Block_Projector";
        const string LOC_INGOTS_LABEL = MOD_PREFIX + "Projector_Ingots";
        const string LOC_COMPONENTS_LABEL = "DisplayName_InventoryConstraint_Components";

        protected override string DefaultTitle => _customTitle ?? TITLE;
        protected override ItemDisplayMode PresentationMode => ItemDisplayComponent.ResolveDisplayMode(GeneralComponent);
        internal override float WindowRenderScaling => Scale > 0f ? Scale : 1f;

        string _customTitle;
        readonly ProjectorViewModel _projectorViewModel;
        readonly Projector.ProjectorViewModel _nativeViewModel;
        readonly Projector.ProjectorView _nativeView;
        readonly Dictionary<ItemEntry, InventoryItemModel> _entries = new Dictionary<ItemEntry, InventoryItemModel>();
        readonly List<InventoryItemModel> _nextItems = new List<InventoryItemModel>();
        readonly HashSet<ItemEntry> _seen = new HashSet<ItemEntry>();
        readonly List<ItemEntry> _stale = new List<ItemEntry>();
        readonly List<ItemEntry> _sourceItems = new List<ItemEntry>();
        ListMode _mode = ListMode.Components;

        internal static readonly ListMode[] ListModes =
            { ListMode.Components, ListMode.Ingots, ListMode.Blocks };

        internal static string GetModeLabel(ListMode mode)
        {
            switch (mode)
            {
                case ListMode.Ingots: return MyTexts.GetString(LOC_INGOTS_LABEL);
                case ListMode.Blocks: return MyTexts.GetString("TerminalTab_Info_Blocks").TrimEnd(':', ' ');
                default: return MyTexts.GetString(LOC_COMPONENTS_LABEL);
            }
        }
        bool _projectorDataInitialized;

        public bool IsLoading { get; private set; }
        internal bool HasProjection => _projectorViewModel.Projector != null &&
            _projectorViewModel.ComponentMissing.Count > 0;

        public ProjectorApp(IAppHost host) : base(host, CreateViewModel)
        {
            _projectorViewModel = (ProjectorViewModel)ViewModel;
            _nativeViewModel = new Projector.ProjectorViewModel
            {
                SelectMode = SetMode,
                Modes = ListModes,
                ModeLabel = GetModeLabel,
                CraftAllCommand = new DelegateCommand(_ => CraftAll())
            };
            _nativeViewModel.Inventory.NameSortCommand = new DelegateCommand(_ => SortNativeItems(false));
            _nativeViewModel.Inventory.AmountSortCommand = new DelegateCommand(_ => SortNativeItems(true));
            _nativeView = new Projector.ProjectorView(_nativeViewModel);
            SetContent(_nativeView);

            if (ItemDisplayComponent.MigrateLegacyDisplayMode(GeneralComponent))
            {
                var block = Host.Block as IMyTerminalBlock;
                var provider = Host.ProviderConfig;
                if (block != null && provider != null && provider.CanWrite)
                    LcdModClientComponent.RunNextFrame.Add(delegate { ConfigManager.Sync(block, provider); });
            }
            RefreshPresentation();
        }

        static IItemsAppViewModel CreateViewModel(GridLogic gridLogic,
            ItemSelectionConfigComponent selection, BlockSelectionConfigComponent blockSelection)
        {
            return new ProjectorViewModel(gridLogic, selection, blockSelection);
        }

        public override void LayoutChanged()
        {
            _customTitle = _projectorViewModel?.Projector?.CustomName;
            LocalizedTitleCache = string.Empty;
            base.LayoutChanged();
            _projectorDataInitialized = false;
            RefreshPresentation();
        }

        public override void Update()
        {
            IsLoading = false;
            base.Update();
            _projectorViewModel.UpdateProjector(Block?.CubeGrid, ProjectorReferenceComponent.EntityId, _mode);
            if (_projectorViewModel.Projector?.CustomName != _customTitle) LayoutChanged();
            RefreshPresentation();
            if (_nativeView.AdvanceAutoScroll(MyAPIGateway.Session.GameplayFrameCounter,
                InteractionComponent.AutoScrollStep)) MarkDirty();

            if (!_projectorDataInitialized && ProjectorReferenceComponent.EntityId != 0 && _projectorViewModel.Projector == null)
                IsLoading = true;
            _projectorDataInitialized = true;
        }

        protected override void OnNativeSortChanged() { RefreshPresentation(); }

        void RefreshPresentation()
        {
            if (_nativeViewModel == null) return;
            var inventory = _nativeViewModel.Inventory;
            var mode = PresentationMode;
            inventory.Configure(InventoryLayouts.From(mode),
                MyTexts.GetString("StoreBlock_Column_Name"), MyTexts.GetString("StoreBlock_Column_Amount"),
                NativeSortByAmount, NativeSortDescending);
            // Resolving names can change the sorted source. Prepare a snapshot first.
            _sourceItems.Clear(); _sourceItems.AddRange(DisplayItems);
            foreach (var item in _sourceItems) PrepareItem(item);
            _nextItems.Clear(); _seen.Clear();
            foreach (var item in DisplayItems)
            {
                _seen.Add(item);
                InventoryItemModel model;
                if (!_entries.TryGetValue(item, out model))
                {
                    model = new InventoryItemModel(() => { if (item.Clicked != null) item.Clicked(); });
                    _entries.Add(item, model);
                }
                model.Update(item.Icon, item.DisplayName, item.AmountText, (int)item.AvailabilityStatus);
                _nextItems.Add(model);
            }
            _stale.Clear();
            foreach (var item in _entries.Keys) if (!_seen.Contains(item)) _stale.Add(item);
            foreach (var item in _stale) _entries.Remove(item);
            inventory.SetItems(_nextItems);

            var data = _projectorViewModel;
            int built = Math.Max(data.TotalBlocks - data.RemainingBlocks, 0);
            float blocks = Completion(built, data.TotalBlocks);
            float components = Completion(data.AvailableComponents, data.TotalComponents);
            float ingots = Completion(data.AvailableIngots, data.TotalIngots);
            _nativeViewModel.Update(HasProjection, data.MissingComponents > 0, blocks, components, ingots,
                ProgressText(MyTexts.GetString("TerminalTab_Info_Blocks").TrimEnd(':', ' '), blocks, built, data.TotalBlocks),
                ProgressText(MyTexts.GetString(LOC_COMPONENTS_LABEL), components, data.AvailableComponents, data.TotalComponents),
                ProgressText(MyTexts.GetString(LOC_INGOTS_LABEL), ingots, data.AvailableIngots, data.TotalIngots),
                _mode);
        }

        static float Completion(double available, double total) =>
            total <= 0d ? 1f : (float)Math.Max(0d, Math.Min(1d, available / total));

        static string ProgressText(string label, float progress, double available, double total) =>
            string.Format(FormatingHelper.Culture, "{0}: {1:P2}  ({2}/{3})", label, progress,
                FormatingHelper.FormatItemQty(available), FormatingHelper.FormatItemQty(total));

        void SetMode(ListMode mode)
        {
            try
            {
                if (_mode == mode) return;
                _mode = mode;
                Update();
                Host.RenderSprites();
            }
            catch (Exception e) { ErrorHandlerHelper.LogError(e, GetType()); }
        }

        // Block entries use the block's icon and name; only items can be crafted from the list.
        void PrepareItem(ItemEntry item)
        {
            Sandbox.Definitions.MyCubeBlockDefinition block;
            if (item == null || !_projectorViewModel.TryGetBlockDefinition(item.ItemType, out block))
            {
                PrepareNativeItem(item);
                return;
            }
            item.Icon = TextureHelper.GetOrAddTextureForBlock(block);
            item.DisplayName = block.DisplayNameText;
            item.Clicked = delegate { CraftBlock(block); };
        }

        // Opens the multi-craft dialog with one block's components and the multiplier set to the blocks still
        // needed (1 once none remain).
        void CraftBlock(Sandbox.Definitions.MyCubeBlockDefinition block)
        {
            try
            {
                if (block.Components == null) return;
                int blocks = Math.Max(1, _projectorViewModel.GetRemainingBlocks(block.Id));
                var amounts = new Dictionary<VRage.Game.ModAPI.Ingame.MyItemType, double>();
                var order = new List<VRage.Game.ModAPI.Ingame.MyItemType>();
                foreach (var component in block.Components)
                {
                    VRage.Game.ModAPI.Ingame.MyItemType type = component.Definition.Id;
                    double current;
                    if (!amounts.TryGetValue(type, out current))
                        order.Add(type);
                    amounts[type] = current + component.Count;
                }
                var requests = new List<Inventory.CraftRequestModel>();
                foreach (var type in order)
                    requests.Add(new Inventory.CraftRequestModel((VRage.Game.MyDefinitionId)type, ResolveDisplayName(type),
                        ResolveSprite(type), amounts[type]));
                Inventory.Crafting.CraftLauncher.CraftMany(this, GridLogic, requests, blocks);
            }
            catch (Exception e) { ErrorHandlerHelper.LogError(e, GetType()); }
        }

        void CraftAll()
        {
            if (_projectorViewModel.MissingComponents <= 0) return;
            Inventory.Crafting.CraftLauncher.CraftMany(this, GridLogic, BuildCraftAllRequests());
        }

        List<Inventory.CraftRequestModel> BuildCraftAllRequests()
        {
            // Always craft missing components, including when the item view shows ingots.
            var requests = new List<Inventory.CraftRequestModel>();
            foreach (var item in _projectorViewModel.ComponentMissing)
            {
                if (item.Value <= 0d) continue;
                requests.Add(new Inventory.CraftRequestModel((VRage.Game.MyDefinitionId)item.Key, ResolveDisplayName(item.Key),
                    ResolveSprite(item.Key), item.Value));
            }
            return requests;
        }
    }
}
