using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LcdMod.Client.Apps.ViewModel;
using LcdMod.Client.Config;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Lists;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Styling.DataTemplates;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using LcdMod.Common.Mvvm;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;
using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps.Abstract
{
    [ConfigComponent(Constants.FILTERS, typeof(FilterConfigComponent), PropertyName = "FilterComponent")]
    [ConfigComponent(Constants.BLOCKS, typeof(BlockSelectionConfigComponent), PropertyName = "BlockSelectionComponent")]
    [ConfigComponent(Constants.ITEMS, typeof(ItemSelectionConfigComponent), PropertyName = "ItemSelectionComponent")]
    public abstract partial class ItemsApp : App, IApp
    {
        protected virtual SortMethod SortMethod => (SortMethod)FilterComponent.SortMethod;

        public static Dictionary<MyItemType, string> SpriteCache = new Dictionary<MyItemType, string>();
        readonly List<MySprite> _contentSpriteCache = new List<MySprite>();
        readonly List<MySprite> _footerSpriteCache = new List<MySprite>();
        readonly ObservableList<ItemEntry> _displayItems = new ObservableList<ItemEntry>();
        internal readonly List<Control> _children = new List<Control>();
        readonly ScrollPanel _scrollPanel;
        readonly ListBoxModel<ItemEntry> _listModel;
        readonly ListBox<ItemEntry> _listBox;
        readonly VirtualizedWrapPanel<ItemEntry> _gridPanel;
        readonly Button _itemSortHeader;
        readonly Button _amountSortHeader;
        readonly ObservableObject _observableViewModel;
        readonly GridLogic _gridLogic;
        ScrollPanel _activeScrollPanel;
        bool _scrollRenderQueued;
        bool _contentSpritesDirty = true;
        bool _footerSpritesDirty = true;
        bool _hasRenderConfigSnapshot;
        bool _lastHideEmpty;
        ItemDisplayMode _lastPresentationMode;
        SortMethod _lastSortMethod;
        bool _lastSortDescending;
        SortMethod _activeSortMethod;
        bool _sortDescending;
        bool _sortSyncQueued;
        bool _hasAppliedGridStyleMode;
        ItemDisplayMode _appliedGridStyleMode;
        float _cachedFooterHeight;
        float _caretY;
        float _footerHeight;
        protected string LocalizedTitleCache = string.Empty;
        protected virtual string DefaultTitle => "<Title not Set>";
        protected virtual ItemDisplayMode PresentationMode =>
            ItemDisplayConfigComponentExtensions.ResolveLegacyDisplayMode(GeneralComponent);
        protected IMyCubeBlock Block => Host.Block;
        protected Sandbox.ModAPI.Ingame.IMyTextSurface Surface => Host.Surface;
        protected RectangleF ViewBox => Host.ViewBox;
        protected float Scale => GeneralComponent.GetScale();
        protected float FontScale => Host.Surface.FontSize;
        protected float LayoutScale => Scale * FontScale;
        protected Color ForegroundColor => Host.ForegroundColor;
        protected Color BackgroundColor => Host.BackgroundColor;
        protected GridLogic GridLogic => _gridLogic;
        protected IItemsAppViewModel ViewModel { get; private set; }
        protected bool TitleVisible => Host.TitleVisible;
        protected float CaretY
        {
            get { return _caretY; }
            set { _caretY = value; }
        }
        protected float FooterHeight
        {
            get { return _footerHeight; }
            set { _footerHeight = value; }
        }
        public bool HasItems => ViewModel.HasItems;
        public bool HasFilters { get; private set; }
        public override IReadOnlyList<Control> VisualChildren => _children;

        const int SPRITE_CACHE_MAX_SIZE = 256;

        protected static void AddToSpriteCache(MyItemType key, string sprite)
        {
            SpriteCache[key] = sprite;
            if (SpriteCache.Count > SPRITE_CACHE_MAX_SIZE)
            {
                var oldest = SpriteCache.Keys.First();
                SpriteCache.Remove(oldest);
            }

        }

        protected readonly Dictionary<MyItemType, string> LocKeysCache = new Dictionary<MyItemType, string>();

        string[] _selectedCategories;

        public string Title
        {
            get
            {
                if (_selectedCategories != ItemSelectionComponent.SelectedCategories)
                    LocalizedTitleCache = string.Empty;

                if (!string.IsNullOrEmpty(LocalizedTitleCache))
                    return LocalizedTitleCache;

                if (ItemSelectionComponent.SelectedCategories != null)
                {
                    _selectedCategories = ItemSelectionComponent.SelectedCategories;
                    var sb = new StringBuilder();
                    foreach (var item in ItemSelectionComponent.SelectedCategories)
                        sb.Append(ItemCategoryHelper.GetGroupDisplayName(item) + ", ");

                    if (sb.Length != 0)
                    {
                        sb.Length -= 2;
                        LocalizedTitleCache = sb.ToString();
                    }
                }

                if (string.IsNullOrEmpty(LocalizedTitleCache))
                    LocalizedTitleCache = MyTexts.GetString(DefaultTitle);

                return LocalizedTitleCache;
            }
        }

        protected const int LINE_HEIGHT = 30;
        protected const int MINIMUM_COL_WIDTH = 220;

        protected ItemsApp(IAppHost host)
            : this(host, null)
        {
        }

        protected ItemsApp(
            IAppHost host,
            Func<GridLogic, ItemSelectionConfigComponent, BlockSelectionConfigComponent, IItemsAppViewModel>
                createViewModel)
            : base(host)
        {
            TextureHelper.TextureIconCacheChanged += OnTextureIconCacheChanged;
            var gridLogic = LcdModSessionComponent.GetOrCreateGridLogic(host.Block?.CubeGrid);
            _gridLogic = gridLogic;
            ViewModel = createViewModel != null
                ? createViewModel(gridLogic, ItemSelectionComponent, BlockSelectionComponent)
                : new ItemsAppViewModel(gridLogic, ItemSelectionComponent, BlockSelectionComponent);
            _observableViewModel = ViewModel as ObservableObject;
            if (_observableViewModel != null)
                _observableViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ViewModel.Items.ItemAdded += OnViewModelItemAdded;
            ViewModel.Items.ItemRemoved += OnViewModelItemRemoved;
            _activeSortMethod = NormalizeSortMethod(FilterComponent.SortMethod);
            _sortDescending = ResolveConfiguredSortDescending(_activeSortMethod);
            _scrollPanel = AddLogicalChild(new ScrollPanel(CursorType.Default, this));
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _scrollPanel.SetVisible(false);

            _listModel = new ListBoxModel<ItemEntry>
            {
                Items = _displayItems,
                MultiSelect = false,
                SelectionEnabled = false,
                EntryClicked = OnLegacyListItemClicked,
                ItemStyleIdSelector = ItemDataTemplate.GetItemStyleId,
                ItemRenderer = ItemDataTemplate.DrawItemListEntry
            };
            _listBox = AddLogicalChild(new ListBox<ItemEntry>(default(RectangleF), _listModel));
            _listBox.SetStyles(ItemDataTemplate.ItemListStyles);
            _listBox.ScrollPanel.ManualScrollInertiaEnabled = true;
            _listBox.ScrollPanel.ScrollChanged = OnScrollPanelChanged;
            _listBox.SetVisible(false);

            _itemSortHeader = CreateSortHeader(SortMethod.Type, "StoreBlock_Column_Name");
            _amountSortHeader = CreateSortHeader(SortMethod.Amount, "StoreBlock_Column_Amount");

            _gridPanel = new VirtualizedWrapPanel<ItemEntry>
            {
                ItemsSource = _displayItems,
                CreateControl = CreateGridItemControl,
                BindControl = BindGridItemControl
            };

            ViewModel.UpdateSelection(ItemSelectionComponent, BlockSelectionComponent, FilterComponent.HideEmpty);
            foreach (var item in ViewModel.Items)
                InsertDisplayItem(item);
        }

        public override void Close()
        {
            TextureHelper.TextureIconCacheChanged -= OnTextureIconCacheChanged;
            if (_observableViewModel != null)
                _observableViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.Items.ItemAdded -= OnViewModelItemAdded;
            ViewModel.Items.ItemRemoved -= OnViewModelItemRemoved;
            foreach (var item in _displayItems)
                UnbindDisplayItem(item);
            _gridPanel.ItemsSource = null;
            ViewModel.Dispose();
            base.Close();
        }

        void OnViewModelItemAdded(IObservableCollection<ItemEntry> sender, ItemEntry item)
        {
            InsertDisplayItem(item);
            InvalidateContentAndFooterSprites();
        }

        void OnViewModelPropertyChanged(ObservableObject sender, string propertyName)
        {
            InvalidateContentAndFooterSprites();
        }

        void OnViewModelItemRemoved(IObservableCollection<ItemEntry> sender, ItemEntry item)
        {
            UnbindDisplayItem(item);
            _displayItems.Remove(item);
            if (!ViewModel.HasItems)
                ClearInteractiveTree();

            InvalidateContentAndFooterSprites();
        }

        void OnTextureIconCacheChanged()
        {
            ItemDataTemplate.InvalidateItemAssets();
            if (SpriteCache != null)
                SpriteCache.Clear();

            InvalidateContentSprites();
            Host.RenderSprites();
        }

        public override void Update()
        {
            try
            {
                ViewModel.UpdateSelection(
                    ItemSelectionComponent,
                    BlockSelectionComponent,
                    FilterComponent.HideEmpty);
                ApplyConfiguredSort();

                if (_activeScrollPanel != null && _activeScrollPanel.UpdateAutoScroll())
                    InvalidateContentSprites();

                var hasFilters = HasConfiguredFilters();
                var filterStateChanged = HasFilters != hasFilters;
                if (HasRenderConfigChanged() || filterStateChanged)
                {
                    HasFilters = hasFilters;
                    CaptureRenderConfig();
                    InvalidateContentAndFooterSprites();
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        bool HasConfiguredFilters()
        {
            return (ItemSelectionComponent.SelectedCategories?.Length ?? 0) > 0 ||
                   (ItemSelectionComponent.SelectedDefinition?.Length ?? 0) > 0 ||
                   (BlockSelectionComponent.SelectedBlocks?.Length ?? 0) > 0 ||
                   (BlockSelectionComponent.SelectedGroups?.Length ?? 0) > 0;
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            ItemDataTemplate.InvalidateItemAssets();
            var itemHeader = _itemSortHeader.DataContext as ItemSortHeaderModel;
            if (itemHeader != null)
                itemHeader.Text = MyTexts.GetString("StoreBlock_Column_Name");
            var amountHeader = _amountSortHeader.DataContext as ItemSortHeaderModel;
            if (amountHeader != null)
                amountHeader.Text = MyTexts.GetString("StoreBlock_Column_Amount");
            RebuildDisplayOrder();
            LocKeysCache.Clear();
            LocalizedTitleCache = string.Empty;
            _hasRenderConfigSnapshot = false;
            InvalidateContentAndFooterSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            CaptureDirtyControlSections();

            if (_contentSpritesDirty)
            {
                ClearInteractiveTree();
                _footerSpritesDirty = true;
            }

            if (_footerSpritesDirty)
                RebuildFooterSpriteCache();

            if (_contentSpritesDirty)
                RebuildContentSpriteCache();

            sprites.AddRange(_footerSpriteCache);
            sprites.AddRange(_contentSpriteCache);

            QueueControlRenderIfNeeded();
            ClearDirtyAfterRender();
            return sprites;
        }

        public void CompleteHostRender()
        {
            ClearDirtyAfterRender();
        }

        protected void InvalidateContentSprites()
        {
            _contentSpritesDirty = true;
            MarkDirty();
        }

        protected void InvalidateContentAndFooterSprites()
        {
            _contentSpritesDirty = true;
            _footerSpritesDirty = true;
            MarkDirty();
        }

        bool HasRenderConfigChanged()
        {
            if (!_hasRenderConfigSnapshot)
                return true;

            return _lastHideEmpty != FilterComponent.HideEmpty ||
                   _lastPresentationMode != PresentationMode ||
                   _lastSortMethod != _activeSortMethod ||
                   _lastSortDescending != _sortDescending;
        }

        void CaptureRenderConfig()
        {
            _lastHideEmpty = FilterComponent.HideEmpty;
            _lastPresentationMode = PresentationMode;
            _lastSortMethod = _activeSortMethod;
            _lastSortDescending = _sortDescending;
            _hasRenderConfigSnapshot = true;
        }

        void CaptureDirtyControlSections()
        {
            for (int i = 0; i < _children.Count; i++)
            {
                var child = _children[i];
                if (!IsVisibleTreeDirty(child))
                    continue;

                if (ReferenceEquals(child, _scrollPanel) ||
                    ReferenceEquals(child, _listBox) ||
                    ReferenceEquals(child, _itemSortHeader) ||
                    ReferenceEquals(child, _amountSortHeader))
                    _contentSpritesDirty = true;
                else
                    _footerSpritesDirty = true;
            }
        }

        void RebuildFooterSpriteCache()
        {
            _footerSpriteCache.Clear();
            _caretY = ContentTop();
            _footerHeight = 0f;
            DrawFooter(_footerSpriteCache);
            if (Math.Abs(_cachedFooterHeight - _footerHeight) > 0.001f)
                _contentSpritesDirty = true;

            _cachedFooterHeight = _footerHeight;
            _footerSpritesDirty = false;
        }

        void RebuildContentSpriteCache()
        {
            _contentSpriteCache.Clear();
            _caretY = ContentTop();
            _footerHeight = _cachedFooterHeight;

            switch (PresentationMode)
            {
                case ItemDisplayMode.List:
                case ItemDisplayMode.Table:
                    DrawList(_contentSpriteCache, _displayItems);
                    break;
                case ItemDisplayMode.Card:
                case ItemDisplayMode.Grid:
                    DrawGrid(_contentSpriteCache, _displayItems);
                    break;
            }

            _contentSpritesDirty = false;
        }

        Button CreateSortHeader(SortMethod column, string localizationKey)
        {
            var button = AddLogicalChild(new Button(default(RectangleF), new ItemSortHeaderModel
            {
                Column = column,
                Text = MyTexts.GetString(localizationKey),
                Clicked = OnSortHeaderClicked
            }));
            button.CustomRender = ItemDataTemplate.DrawItemSortHeader;
            button.SetClass("ControlBase Button Sort");
            button.SetVisible(false);
            return button;
        }

        void DrawSortHeader(List<MySprite> sprites)
        {
            var height = LINE_HEIGHT * Scale;
            var header = new RectangleF(ViewBox.X, CaretY, ViewBox.Width, height);
            var amountWidth = Math.Min(header.Width, Math.Max(105f * LayoutScale, header.Width * 0.22f));
            var nameLeft = Math.Min(header.Right, header.X + 38f * LayoutScale);
            var amountLeft = Math.Max(nameLeft, header.Right - amountWidth);

            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
            {
                Position = new Vector2(header.Center.X, header.Bottom - Math.Max(1f, LayoutScale)),
                Size = new Vector2(header.Width, Math.Max(1f, LayoutScale)),
                Color = ResolveResource(ThemeResources.DividerColor, ForegroundColor),
                Alignment = TextAlignment.CENTER
            });

            ConfigureSortHeader(
                _itemSortHeader,
                new RectangleF(nameLeft, header.Y, Math.Max(0f, amountLeft - nameLeft), height),
                SortMethod.Type);
            ConfigureSortHeader(
                _amountSortHeader,
                new RectangleF(amountLeft, header.Y, Math.Max(0f, header.Right - amountLeft), height),
                SortMethod.Amount);

            RenderSortHeader(_itemSortHeader, sprites);
            RenderSortHeader(_amountSortHeader, sprites);
            CaretY += height;
        }

        void ConfigureSortHeader(Button button, RectangleF bounds, SortMethod column)
        {
            button.SetRect(bounds);
            button.SetClass(_activeSortMethod != column
                ? "ControlBase Button Sort"
                : _sortDescending
                    ? "ControlBase Button Sort SortDescending"
                    : "ControlBase Button Sort SortAscending");
            button.SetVisible(bounds.Width > 0f && bounds.Height > 0f);
            if (!_children.Contains(button))
                _children.Add(button);
        }

        static void RenderSortHeader(Button button, List<MySprite> sprites)
        {
            if (button != null && button.Visible)
                button.Render(sprites);
        }

        static string GetTableItemClass(ItemEntry item, int index)
        {
            return (index & 1) == 0 ? "ItemRowEven" : "ItemRowOdd";
        }

        void OnSortHeaderClicked(ButtonModel model, object sender)
        {
            var header = model as ItemSortHeaderModel;
            if (header == null)
                return;

            if (_activeSortMethod == header.Column)
                _sortDescending = !_sortDescending;
            else
            {
                _activeSortMethod = header.Column;
                _sortDescending = GetDefaultSortDescending(header.Column);
            }

            FilterComponent.SortMethod = (int)_activeSortMethod;
            FilterComponent.SortDirection = _sortDescending ? 1 : 0;
            RebuildDisplayOrder();
            CaptureRenderConfig();
            InvalidateContentSprites();
            QueueSortConfigSync();
            Host.RenderSprites();
        }

        void QueueSortConfigSync()
        {
            if (_sortSyncQueued)
                return;

            var block = Host.Block as IMyTerminalBlock;
            var provider = Host.ProviderConfig;
            if (block == null || provider == null || !provider.CanWrite)
                return;

            _sortSyncQueued = true;
            LcdModClientComponent.RunNextFrame.Add(delegate
            {
                _sortSyncQueued = false;
                ConfigManager.Sync(block, provider);
            });
        }

        void ApplyConfiguredSort()
        {
            var method = NormalizeSortMethod((int)SortMethod);
            var descending = ResolveConfiguredSortDescending(method);
            if (_activeSortMethod == method && _sortDescending == descending)
                return;

            _activeSortMethod = method;
            _sortDescending = descending;
            RebuildDisplayOrder();
        }

        bool ResolveConfiguredSortDescending(SortMethod method)
        {
            switch (FilterComponent.SortDirection)
            {
                case 0:
                    return false;
                case 1:
                    return true;
                default:
                    return GetDefaultSortDescending(method);
            }
        }

        static bool GetDefaultSortDescending(SortMethod method)
        {
            return method == SortMethod.Amount;
        }

        static SortMethod NormalizeSortMethod(int value)
        {
            return value == (int)SortMethod.Type ? SortMethod.Type : SortMethod.Amount;
        }

        void InsertDisplayItem(ItemEntry item)
        {
            if (item == null || _displayItems.Contains(item))
                return;

            BindDisplayItem(item);
            var index = 0;
            while (index < _displayItems.Count && CompareDisplayItems(_displayItems[index], item) <= 0)
                index++;
            _displayItems.Insert(index, item);
        }

        void BindDisplayItem(ItemEntry item)
        {
            if (item != null)
                item.PropertyChanged += OnDisplayItemChanged;
        }

        void UnbindDisplayItem(ItemEntry item)
        {
            if (item != null)
                item.PropertyChanged -= OnDisplayItemChanged;
        }

        void OnDisplayItemChanged(ObservableObject sender, string propertyName)
        {
            var item = sender as ItemEntry;
            if (item == null)
                return;

            if ((_activeSortMethod == SortMethod.Amount && propertyName == nameof(ItemEntry.Amount)) ||
                (_activeSortMethod == SortMethod.Type && propertyName == nameof(ItemEntry.DisplayName)))
            {
                RepositionDisplayItem(item);
            }
        }

        void RepositionDisplayItem(ItemEntry item)
        {
            var oldIndex = _displayItems.IndexOf(item);
            if (oldIndex < 0)
                return;

            var targetIndex = 0;
            for (var i = 0; i < _displayItems.Count; i++)
            {
                var other = _displayItems[i];
                if (ReferenceEquals(other, item))
                    continue;
                if (CompareDisplayItems(other, item) <= 0)
                    targetIndex++;
            }

            if (targetIndex != oldIndex)
                _displayItems.Move(oldIndex, targetIndex);
        }

        void RebuildDisplayOrder()
        {
            if (_displayItems.Count <= 1)
                return;

            var ordered = _displayItems.ToList();
            ordered.Sort(CompareDisplayItems);
            for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
            {
                var currentIndex = _displayItems.IndexOf(ordered[targetIndex]);
                if (currentIndex != targetIndex)
                    _displayItems.Move(currentIndex, targetIndex);
            }
        }

        int CompareDisplayItems(ItemEntry left, ItemEntry right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;

            int result;
            if (_activeSortMethod == SortMethod.Amount)
                result = ((double)left.Amount).CompareTo((double)right.Amount);
            else
                result = string.Compare(
                    ItemDataTemplate.GetItemDisplayName(left),
                    ItemDataTemplate.GetItemDisplayName(right),
                    StringComparison.CurrentCultureIgnoreCase);

            if (result != 0 && _sortDescending)
                return -result;
            if (result != 0)
                return result;

            result = string.Compare(left.ItemType.TypeId, right.ItemType.TypeId, StringComparison.OrdinalIgnoreCase);
            return result != 0
                ? result
                : string.Compare(left.ItemType.SubtypeId, right.ItemType.SubtypeId, StringComparison.OrdinalIgnoreCase);
        }

        void DrawList(List<MySprite> sprites, ObservableList<ItemEntry> items)
        {
            var rowHeight = LINE_HEIGHT * Scale;
            if (items.Count <= 0)
                return;

            DrawSortHeader(sprites);
            var contentBounds = GetScrollPanelBounds(CaretY, FooterHeight);
            _listModel.RowHeight = rowHeight;
            _listModel.ScrollerWidthPixels = ScrollPanel.DEFAULT_SCROLLER_WIDTH_PIXELS * Scale;
            _listBox.SetRect(contentBounds);
            _listBox.BackgroundColor = Color.Transparent;
            _listBox.BorderRadiusPixels = PresentationMode == ItemDisplayMode.Table
                ? 0f
                : BorderRenderer.DEFAULT_RADIUS_PIXELS;
            _listModel.ItemClassSelector = PresentationMode == ItemDisplayMode.Table
                ? (Func<ItemEntry, int, string>)GetTableItemClass
                : null;
            _listBox.SetStyles(PresentationMode == ItemDisplayMode.Table
                ? ItemDataTemplate.ItemTableStyles
                : ItemDataTemplate.ItemListStyles);

            BeginInteractiveTree(_listBox);
            _activeScrollPanel = _listBox.ScrollPanel;
            _listBox.Render(sprites);

            CaretY = contentBounds.Bottom;
        }

        void DrawGrid(List<MySprite> sprites, ObservableList<ItemEntry> items)
        {
            var rowHeight = 3f * LINE_HEIGHT * Scale;
            if (items.Count <= 0)
                return;

            var presentationMode = PresentationMode;
            if (!_hasAppliedGridStyleMode || _appliedGridStyleMode != presentationMode)
            {
                _hasAppliedGridStyleMode = true;
                _appliedGridStyleMode = presentationMode;
                _scrollPanel.InvalidateLayout();
            }

            var contentBounds = GetScrollPanelBounds(CaretY, FooterHeight);
            _scrollPanel.SetContent(_gridPanel);
            _gridPanel.RowHeight = rowHeight;
            _gridPanel.MinimumColumnWidth = MINIMUM_COL_WIDTH * Scale;
            _gridPanel.HorizontalGap = 0f;
            _gridPanel.VerticalGap = 0f;
            _gridPanel.ItemsSource = items;

            _scrollPanel.ConfigureAutomatic(
                contentBounds,
                ScrollPanel.DEFAULT_SCROLLER_WIDTH_PIXELS * Scale,
                rowHeight);

            BeginInteractiveTree(_scrollPanel);
            _activeScrollPanel = _scrollPanel;
            _scrollPanel.Render(sprites);

            CaretY = _scrollPanel.PanelBounds.Bottom;
        }

        ControlTemplate CreateGridItemControl(ItemEntry item)
        {
            return new RectangleControl(
                default(RectangleF),
                CursorType.Hand,
                item,
                OnGridItemClicked)
            {
                CustomRender = ItemDataTemplate.DrawItemGridEntry
            };
        }

        void BindGridItemControl(ControlTemplate control, ItemEntry item, int index)
        {
            if (control == null)
                return;

            control.SetDataContext(item);
            control.SetCursor(CursorType.Hand);
            control.SetOnClick(OnGridItemClicked);
            control.SetStyleId(ItemDataTemplate.GetItemStyleId(item));
            control.SetStyles(_appliedGridStyleMode == ItemDisplayMode.Grid
                ? ItemDataTemplate.ItemGridLineStyles
                : ItemDataTemplate.ItemGridCardStyles);
            control.CustomRender = ItemDataTemplate.DrawItemGridEntry;
            control.SetVisible(item != null);
        }

        void OnGridItemClicked(object dataContext, object sender)
        {
            OnLegacyListItemClicked(dataContext as ItemEntry);
        }

        RectangleF GetScrollPanelBounds(float contentTop, float footerHeight)
        {
            float viewportHeight = Math.Max(0f, ViewBox.Bottom - contentTop - Math.Max(0f, footerHeight));
            return new RectangleF(ViewBox.X, contentTop, ViewBox.Width, viewportHeight);
        }

        protected string ResolveSprite(MyItemType itemType)
        {
            string sprite;
            if (SpriteCache.TryGetValue(itemType, out sprite))
                return sprite;

            var itemDefinition = MyDefinitionManager.Static != null
                ? MyDefinitionManager.Static.TryGetPhysicalItemDefinition(itemType)
                : null;

            sprite = TextureHelper.ResolveItemSprite(itemDefinition, Surface);
            if (string.IsNullOrEmpty(sprite))
                sprite = "Textures\\FactionLogo\\Unknown.dds";

            AddToSpriteCache(itemType, sprite);
            return sprite;
        }

        protected string ResolveDisplayName(MyItemType itemType)
        {
            string localizedName;
            if (LocKeysCache.TryGetValue(itemType, out localizedName))
                return localizedName;

            var key =
                MyDefinitionManager.Static.TryGetPhysicalItemDefinition(itemType).DisplayNameEnum?.ToString() ??
                itemType.SubtypeId;

            localizedName = MyTexts.GetString(key);
            LocKeysCache[itemType] = localizedName;
            return localizedName;
        }

        void OnLegacyListItemClicked(ItemEntry item)
        {
            if (item == null)
                return;

            var interactiveHost = Host as InteractiveSurfaceScript;
            if (interactiveHost == null)
                return;

            var displayName = ResolveDisplayName(item.ItemType);
            interactiveHost.ShowDialog(new CraftDialog(
                this,
                GridLogic,
                item.ItemType,
                displayName,
                ResolveSprite(item.ItemType),
                item.CraftAmount,
                delegate(Dialog dialog) { interactiveHost.ShowDialog(dialog); }));
        }

        protected virtual void DrawItemIcon(
            List<MySprite> frame,
            string icon,
            Vector2 position,
            Vector2 size,
            TextAlignment alignment,
            Color backgroundColor)
        {
            if (frame == null || size.X <= 0f || size.Y <= 0f)
                return;

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = string.IsNullOrEmpty(icon) ? "MissingIcon" : icon,
                Position = position,
                Size = size,
                Alignment = alignment,
                Color = string.IsNullOrEmpty(icon) ? backgroundColor : Color.White
            });
        }

        protected virtual void DrawFooter(List<MySprite> frame)
        {
        }

        void ClearInteractiveTree()
        {
            _activeScrollPanel = null;
            _scrollPanel.SetVisible(false);
            _listBox.SetVisible(false);
            _itemSortHeader.SetVisible(false);
            _amountSortHeader.SetVisible(false);
            _children.Clear();
        }

        void BeginInteractiveTree(ControlTemplate root)
        {
            if (root == null)
                return;

            root.SetVisible(true);
            if (!_children.Contains(root))
                _children.Add(root);
        }

        public override bool HasVisibleItems()
        {
            return HasItems;
        }

        void OnScrollPanelChanged(ScrollPanel panel)
        {
            if (!ReferenceEquals(panel, _activeScrollPanel))
                return;

            QueueControlRenderIfNeeded();
        }

        void QueueControlRenderIfNeeded()
        {
            var panel = _activeScrollPanel;
            if (_scrollRenderQueued || panel == null || !panel.IsDirty || !CanSelfRender())
                return;

            _scrollRenderQueued = true;
            LcdModClientComponent.RunNextFrame.Add(RunQueuedScrollRender);
        }

        void RunQueuedScrollRender()
        {
            _scrollRenderQueued = false;

            try
            {
                var panel = _activeScrollPanel;
                if (panel == null || !panel.IsDirty || !CanSelfRender())
                    return;

                Host.RenderSprites();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        bool CanSelfRender()
        {
            try
            {
                return Host != null &&
                       Host.Surface != null &&
                       Host.Block != null &&
                       !Host.Block.MarkedForClose &&
                       !Host.Block.Closed;
            }
            catch
            {
                return false;
            }
        }

        float ContentTop()
        {
            return TitleVisible ? ViewBox.Y + 40f * Scale * FontScale : ViewBox.Y;
        }

        protected void TrimText(ref StringBuilder sb, float availableWidth, float fontSize = 1)
        {
            Vector2 textSize = Surface.MeasureStringInPixels(sb, TextFont, fontSize * Scale * FontScale);

            if (textSize.X <= availableWidth)
                return;

            var source = sb.ToString();
            for (int i = source.Length - 1; i > 0; i--)
            {
                sb.Clear();
                sb.Append(FormatingHelper.TrimName(source, i));
                textSize = Surface.MeasureStringInPixels(sb, TextFont, fontSize * Scale * FontScale);

                if (textSize.X <= availableWidth)
                    break;
            }
        }

        protected string TrimText(string text, float availableWidth, float fontSize = 1)
        {
            var sb = new StringBuilder(text ?? string.Empty);
            TrimText(ref sb, availableWidth, fontSize);
            return sb.ToString();
        }

        protected Vector2 ToScreenMargin(Vector2 absoluteCenterInViewBox)
        {
            return new Vector2(absoluteCenterInViewBox.X, 512f - absoluteCenterInViewBox.Y);
        }

    }
}
