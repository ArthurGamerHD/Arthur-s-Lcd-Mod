using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LcdMod.Client.Extensions;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel;
using LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Models.Apps;
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
using VisualStackPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel.StackPanel;
using VisualWrapPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel.WrapPanel;

namespace LcdMod.Client.Apps.Abstract
{
    public abstract class ItemsApp : App, IApp
    {
        protected virtual SortMethod SortMethod => (SortMethod)AppConfig.SortMethod;

        public static Dictionary<MyItemType, string> SpriteCache =
            new Dictionary<MyItemType, string>();

        static readonly Dictionary<MyDefinitionId, MyItemType> TypeCache = new Dictionary<MyDefinitionId, MyItemType>();

        readonly Dictionary<MyItemType, double> _itemsCache = new Dictionary<MyItemType, double>();
        readonly List<ItemViewModel> _items = new List<ItemViewModel>();
        readonly Dictionary<MyItemType, ItemViewModel> _models = new Dictionary<MyItemType, ItemViewModel>();
        readonly Dictionary<MyItemType, ItemControl> _listItemControls =
            new Dictionary<MyItemType, ItemControl>();
        readonly Dictionary<MyItemType, ItemControl> _gridItemControls =
            new Dictionary<MyItemType, ItemControl>();
        readonly internal List<Control> _children = new List<Control>();
        readonly ScrollPanel _scrollPanel;
        readonly VisualStackPanel _listPanel;
        readonly VisualWrapPanel _gridPanel;
        int _viewModelLayoutVersion = 1;
        bool _scrollRenderQueued;
        float _caretY;
        float _footerHeight;
        protected string LocalizedTitleCache = string.Empty;

        public abstract Dictionary<MyItemType, double> ItemSource { get; }
        protected virtual string DefaultTitle => "<Title not Set>";

        protected new ScreenConfigWithItems AppConfig => (ScreenConfigWithItems)base.AppConfig;
        protected IMyCubeBlock Block => Host.Block;
        protected Sandbox.ModAPI.Ingame.IMyTextSurface Surface => Host.Surface;
        protected RectangleF ViewBox => Host.ViewBox;
        protected float Scale => AppConfig.Scale;
        protected float FontScale => Host.Surface.FontSize;
        protected float LayoutScale => Scale * FontScale;
        protected Color ForegroundColor => Host.ForegroundColor;
        protected Color BackgroundColor => Host.BackgroundColor;
        protected GridLogic GridLogic => Host.GridLogic;
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
        public bool HasItems => _items.Count > 0;
        public bool HasFilters { get; private set; }
        public override IReadOnlyList<Control> Children => _children;

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.GridAndLegacy;
        }

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
                if (_selectedCategories != AppConfig?.SelectedCategories)
                    LocalizedTitleCache = string.Empty;

                if (!string.IsNullOrEmpty(LocalizedTitleCache))
                    return LocalizedTitleCache;

                if (AppConfig?.SelectedCategories != null)
                {
                    _selectedCategories = AppConfig.SelectedCategories;
                    var sb = new StringBuilder();
                    foreach (var item in AppConfig.SelectedCategories)
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

        protected const int TITLE_HEIGHT = 35;
        protected const int LINE_HEIGHT = 30;
        protected const int MINIMUM_COL_WIDTH = 220;
        protected string PreviousType = "";

        protected ItemsApp(ScreenConfigWithItems config, IAppHost host) : base(config, host)
        {
            _scrollPanel = AddChild(new ScrollPanel(CursorType.Default, this));
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _scrollPanel.SetVisible(false);
            _listPanel = new VisualStackPanel();
            _gridPanel = new VisualWrapPanel();
            _gridPanel.CustomRender = RenderGridPanelContent;
        }


        protected virtual List<KeyValuePair<MyItemType, double>> ReadItems(IMyTerminalBlock lcd)
        {
            if (AppConfig.HideEmpty || AppConfig.SelectedItems.Any())
                _itemsCache.Clear();

            if (lcd == null || ItemSource == null)
                return new List<KeyValuePair<MyItemType, double>>();

            if (_itemsCache.Any())
            {
                var ar = _itemsCache.Keys.ToArray();
                foreach (var key in ar) // will be 0 unless Clear() was NOT called
                    _itemsCache[key] = 0;
            }


            if (!AppConfig.HideEmpty)
            {
                foreach (var configSelectedItem in AppConfig.SelectedItems)
                {
                    MyItemType type;
                    if (!TypeCache.TryGetValue(configSelectedItem, out type))
                    {
                        type = MyItemType.Parse(configSelectedItem.ToString());
                        TypeCache[configSelectedItem] = type;
                    }

                    _itemsCache[type] = 0;
                }
            }

            foreach (var keyValuePair in ItemSource)
                _itemsCache[keyValuePair.Key] = (keyValuePair.Value);


            switch (SortMethod)
            {
                case SortMethod.Type:
                    var sortedByType = new SortedDictionary<MyItemType, double>(ItemTypeComparer.Instance);
                    foreach (var entry in _itemsCache)
                    {
                        sortedByType[entry.Key] = entry.Value;
                    }

                    return sortedByType.ToList();
                default:
                    var sortedByValue = new SortedDictionary<double, List<KeyValuePair<MyItemType, double>>>(
                        DescendingDoubleComparer.Instance);
                    foreach (var entry in _itemsCache)
                    {
                        List<KeyValuePair<MyItemType, double>> bucket;
                        if (!sortedByValue.TryGetValue(entry.Value, out bucket))
                        {
                            bucket = new List<KeyValuePair<MyItemType, double>>();
                            sortedByValue[entry.Value] = bucket;
                        }

                        bucket.Add(entry);
                    }

                    return sortedByValue.SelectMany(b => b.Value).ToList();
            }
        }

        public override void Update()
        {
            _items.Clear();
            ClearInteractiveTree();
            
            if (AppConfig == null)
                return;

            try
            {
                var items = ReadItems(Block as IMyTerminalBlock);
                for (int i = 0; i < items.Count; i++)
                    _items.Add(GetOrCreateItemViewModel(items[i]));

                HasFilters = AppConfig.SelectedCategories.Any() || AppConfig.SelectedBlocks.Any() ||
                             AppConfig.SelectedItems.Any() || AppConfig.SelectedGroups.Any() ||
                             AppConfig.SelectedDefinition.Any();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            LocKeysCache.Clear();
            LocalizedTitleCache = string.Empty;
            _viewModelLayoutVersion++;
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            ClearInteractiveTree();
            _caretY = ContentTop();
            _footerHeight = 0f;
            DrawFooter(sprites);

            switch (AppConfig.DisplayMode)
            {
                case (int)DisplayMode.Legacy:
                    DrawList(sprites, _items);
                    break;
                case (int)DisplayMode.Grid:
                    DrawGrid(sprites, _items);
                    break;
            }

            QueueControlRenderIfNeeded();
            ClearDirtyAfterRender();
            return sprites;
        }

        protected virtual ItemViewModel GetOrCreateItemViewModel(KeyValuePair<MyItemType, double> item)
        {
            ItemViewModel model;

            if (!_models.TryGetValue(item.Key, out model))
            {
                model = new ItemViewModel(item.Key);
                _models.Add(item.Key, model);
            }

            if (model.LayoutVersion != _viewModelLayoutVersion)
                RefreshItemViewModelLayout(model);

            RefreshItemViewModelValue(model, item.Value);
            return model;
        }

        protected virtual void RefreshItemViewModelLayout(ItemViewModel model)
        {
            if (model == null)
                return;

            model.Icon = ResolveSprite(model.ItemType);
            model.DisplayName = ResolveDisplayName(model.ItemType);
            model.Cursor = CursorType.Hand;
            model.OnClick = OnItemClicked;
            model.LayoutVersion = _viewModelLayoutVersion;
        }

        protected virtual void RefreshItemViewModelValue(ItemViewModel model, double amount)
        {
            if (model == null)
                return;

            model.Amount = amount;
            var amountText = FormatingHelper.FormatItemQty(amount);
            model.ListTextColor = amount == 0 ? AppConfig.ErrorColor : Surface.ScriptForegroundColor;
            model.ListAmountColor = model.ListTextColor;
            model.ListIconColor = Color.White;
            model.IconBackgroundColor = amount == 0 ? AppConfig.ErrorColor : Color.White;
            var panelColor = AppConfig.HeaderColor;
            var panelTextColor = Surface.ScriptForegroundColor;
            model.GridTextColor = AppConfig.DrawLines && amount == 0
                ? new Color(96, 32, 32)
                : panelTextColor;
            model.GridAmountColor = model.GridTextColor;
            model.GridIconColor = Color.White;
            model.PanelColor = amount == 0 ? AppConfig.ErrorColor : panelColor;
            model.SetSimpleAmount(amountText);
        }

        void DrawList(List<MySprite> sprites, List<ItemViewModel> items)
        {
            var rowHeight = LINE_HEIGHT * Scale;
            if (items.Count <= 0)
                return;

            var contentBounds = GetScrollPanelBounds(CaretY, FooterHeight);
            _scrollPanel.SetContent(_listPanel);
            _listPanel.RowHeight = rowHeight;
            _listPanel.Gap = 0f;
            SyncPanelChildren(_listPanel, items, _listItemControls, RenderListItemControl);

            _scrollPanel.ConfigureAutomatic(
                contentBounds,
                ScrollPanel.DefaultScrollerWidthPixels * Scale,
                rowHeight);

            BeginInteractiveTree(_scrollPanel);
            PreviousType = items[0].TypeId;
            _scrollPanel.Render(sprites);

            CaretY = _scrollPanel.PanelBounds.Bottom;
        }

        void DrawGrid(List<MySprite> sprites, List<ItemViewModel> items)
        {
            var rowHeight = 3f * LINE_HEIGHT * Scale;
            if (items.Count <= 0)
                return;

            var contentBounds = GetScrollPanelBounds(CaretY, FooterHeight);
            _scrollPanel.SetContent(_gridPanel);
            _gridPanel.RowHeight = rowHeight;
            _gridPanel.MinimumColumnWidth = MINIMUM_COL_WIDTH * Scale;
            _gridPanel.HorizontalGap = 0f;
            _gridPanel.VerticalGap = 0f;
            SyncPanelChildren(_gridPanel, items, _gridItemControls, RenderGridItemControl);

            _scrollPanel.ConfigureAutomatic(
                contentBounds,
                ScrollPanel.DefaultScrollerWidthPixels * Scale,
                rowHeight);

            BeginInteractiveTree(_scrollPanel);
            PreviousType = items[0].TypeId;
            _scrollPanel.Render(sprites);

            CaretY = _scrollPanel.PanelBounds.Bottom;
        }

        RectangleF GetScrollPanelBounds(float contentTop, float footerHeight)
        {
            float viewportHeight = Math.Max(0f, ViewBox.Bottom - contentTop - Math.Max(0f, footerHeight));
            return new RectangleF(ViewBox.X, contentTop, ViewBox.Width, viewportHeight);
        }

        void RenderGridPanelContent(ControlTemplate control, List<MySprite> sprites)
        {
            var children = control?.Children;
            if (children == null)
                return;

            if (AppConfig.DrawLines)
            {
                var layout = WrapPanelLayout.Create(
                    control.Bounds,
                    3f * LINE_HEIGHT * Scale,
                    MINIMUM_COL_WIDTH * Scale,
                    children.Count);
                DrawWrapPanelLines(sprites, _scrollPanel, layout);
            }

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i] as ControlTemplate;
                child?.Render(sprites);
            }
        }

        void DrawWrapPanelLines(List<MySprite> sprites, ScrollPanel panel, WrapPanelLayout panelLayout)
        {
            var lineColor = AppConfig.HeaderColor;
            var contentStart = panel.ContentBounds.X;
            var contentEnd = panel.ContentBounds.Right;
            var gridHeight = panel.ContentBounds.Height;

            for (int row = 0; row <= panel.MaxVisibleRows; row++)
            {
                var y = panel.ContentBounds.Y + row * panelLayout.RowHeight;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2((contentStart + contentEnd) / 2f, y),
                    Size = new Vector2(contentEnd - contentStart, 2f),
                    Color = lineColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            for (int col = 0; col <= panelLayout.Columns; col++)
            {
                var x = col == panelLayout.Columns ? contentEnd : contentStart + col * panelLayout.ColumnWidth;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(x, panel.ContentBounds.Y + gridHeight / 2f),
                    Size = new Vector2(2f, gridHeight),
                    Color = lineColor,
                    Alignment = TextAlignment.CENTER
                });
            }
        }

        void RenderGridItemControl(ControlTemplate control, List<MySprite> frame)
        {
            var model = control.Model as ItemViewModel;
            if (model == null)
                return;

            var rect = control.Bounds;
            var cellPadding = (LINE_HEIGHT * Scale) / 2f;
            var cellViewBox = GetCellViewBox(rect.X, rect.Right, rect.Y, rect.Height, cellPadding);

            if (!AppConfig.DrawLines)
                DrawCellBackground(frame, model, rect.X, rect.Right, rect.Y, rect.Height, cellPadding);

            PreviousType = model.TypeId;
            var slots = GetCellSlots(cellViewBox.X, cellViewBox.Right, cellViewBox.Y, cellViewBox.Bottom, LINE_HEIGHT);
            DrawCellContent(frame, model, slots);
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

        void SyncPanelChildren(
            Panel panel,
            List<ItemViewModel> items,
            Dictionary<MyItemType, ItemControl> controls,
            InteractiveRenderHandler render)
        {
            if (panel == null)
                return;

            var desired = new List<Control>(items == null ? 0 : items.Count);
            var desiredTypes = new Dictionary<MyItemType, bool>();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item == null)
                        continue;

                    desiredTypes[item.ItemType] = true;
                    desired.Add(GetOrCreateItemControl(controls, item, default(RectangleF), render));
                }
            }

            RemoveStalePanelChildren(panel, controls, desiredTypes);
            EnsurePanelChildOrder(panel, desired);
        }

        void RemoveStalePanelChildren(
            Panel panel,
            Dictionary<MyItemType, ItemControl> controls,
            Dictionary<MyItemType, bool> desiredTypes)
        {
            var children = panel.Children;
            if (children == null)
                return;

            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i];
                var model = child?.DataContext as ItemViewModel;
                if (model == null || desiredTypes.ContainsKey(model.ItemType))
                    continue;

                panel.RemoveChild(child);
                if (controls != null)
                    controls.Remove(model.ItemType);
            }
        }

        void EnsurePanelChildOrder(Panel panel, List<Control> desired)
        {
            if (desired == null)
                return;

            var children = panel.Children;
            bool changed = false;
            for (int i = 0; i < desired.Count; i++)
            {
                var child = desired[i] as ControlTemplate;
                if (child == null)
                    continue;

                if (!ReferenceEquals(child.Parent, panel))
                {
                    panel.AddChild(child);
                    children = panel.Children;
                    changed = true;
                }

                if (children == null || i >= children.Count || ReferenceEquals(children[i], child))
                    continue;

                int currentIndex = IndexOfChild(children, child);
                if (currentIndex < 0)
                    continue;

                if (panel.MoveChild(child, i))
                    changed = true;
            }

            if (changed)
                panel.InvalidateLayout();
        }

        static int IndexOfChild(IReadOnlyList<Control> children, ControlTemplate child)
        {
            if (children == null || child == null)
                return -1;

            for (int i = 0; i < children.Count; i++)
            {
                if (ReferenceEquals(children[i], child))
                    return i;
            }

            return -1;
        }

        ItemControl GetOrCreateItemControl(
            Dictionary<MyItemType, ItemControl> controls,
            ItemViewModel item,
            RectangleF bounds,
            InteractiveRenderHandler render)
        {
            if (item == null)
                return null;

            ItemControl control;
            if (!controls.TryGetValue(item.ItemType, out control) || control == null)
            {
                control = new ItemControl(bounds, item, render);
                controls[item.ItemType] = control;
            }
            else
            {
                control.UpdateItem(item, render);
            }

            control.SetVisible(true);
            return control;
        }

        void OnItemClicked(object dataContext, object sender)
        {
            var item = dataContext as ItemViewModel;
            if (item == null)
                return;

            var interactiveHost = Host as InteractiveSurfaceScript;
            if (interactiveHost == null)
                return;

            interactiveHost.ShowDialog(new CraftDialog(
                this,
                GridLogic,
                item.ItemType,
                item.DisplayName,
                item.Icon,
                GetDefaultCraftAmount(item),
                delegate(Dialog dialog) { interactiveHost.ShowDialog(dialog); }));
        }

        protected virtual double GetDefaultCraftAmount(ItemViewModel item)
        {
            return 1d;
        }

        void RenderListItemControl(ControlTemplate control, List<MySprite> frame)
        {
            var model = control.Model as ItemViewModel;
            if (model == null)
                return;

            DrawListItemContent(frame, model, control.Bounds);
        }

        protected virtual void DrawListItemContent(List<MySprite> frame, ItemViewModel item, RectangleF bounds)
        {
            var margin = 0f;
            var xStart = bounds.X + margin;
            var xEnd = bounds.Right - margin;
            Vector2 position = bounds.Position;
            position.X = xStart;

            bool drawSeparatorLine = AppConfig.SortMethod == (int)SortMethod.Type && PreviousType != item.TypeId;

            if (AppConfig.DrawLines || drawSeparatorLine)
            {
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = new Vector2((xStart + xEnd) / 2f, position.Y),
                    Size = new Vector2(xEnd - xStart, 1),
                    Color = drawSeparatorLine ? AppConfig.HeaderColor : Surface.ScriptForegroundColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            PreviousType = item.TypeId;

            DrawItemIcon(frame,
                item.Icon,
                position + new Vector2(20f, 15) * Scale,
                new Vector2(LINE_HEIGHT * Scale),
                TextAlignment.CENTER,
                item.IconBackgroundColor);
            position.X += (xEnd - xStart) / 8f;

            var amountWidth = GetListAmountWidth(item);
            var nameWidth = Math.Max(0f, xEnd - position.X - amountWidth);
            var localizedName = TrimText(item.DisplayName, nameWidth);

            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = localizedName,
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = item.ListTextColor,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
            position.X = xEnd;
            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = item.AmountText,
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = item.ListAmountColor,
                Alignment = TextAlignment.RIGHT,
                FontId = TextFont
            });

        }

        float GetListAmountWidth(ItemViewModel item)
        {
            if (item == null || string.IsNullOrEmpty(item.AmountText))
                return 105f * Scale;

            var size = FormatingHelper.GetSizeInPixel(item.AmountText, TextFont, 1, Surface);
            return Math.Max(105f * Scale, size.X * Scale * FontScale + 8f * Scale);
        }

        protected virtual void DrawItemIcon(List<MySprite> frame, string icon, Vector2 position, Vector2 size,
            TextAlignment alignment, Color backgroundColor)
        {
            if (frame == null || size.X <= 0f || size.Y <= 0f)
                return;

            if (string.IsNullOrEmpty(icon))
            {
                frame.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Danger",
                    Position = position,
                    Size = size,
                    Alignment = alignment,
                    Color = backgroundColor
                });
                return;
            }

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = icon,
                Position = position,
                Size = size,
                Alignment = alignment,
                Color = Color.White
            });
        }

        protected virtual void DrawCellContent(List<MySprite> frame, ItemViewModel item,
            MyTuple<RectangleF, RectangleF, RectangleF> slots)
        {
            var iconRect = slots.Item1;
            var numberRect = slots.Item2;
            var nameRect = slots.Item3;

            DrawItemIcon(frame,
                item.Icon,
                new Vector2(iconRect.X, iconRect.Y + iconRect.Height / 2f),
                new Vector2(iconRect.Width),
                TextAlignment.LEFT,
                item.IconBackgroundColor);

            var localizedName = TrimText(item.DisplayName, nameRect.Width);

            Vector2 size = FormatingHelper.GetSizeInPixel(localizedName, TextFont, 1, Surface);
            float minProportion = Math.Min(nameRect.Width / size.X, nameRect.Height / size.Y);
            float fontSize = minProportion;
            float renderedHeight = size.Y * fontSize * FontScale;
            Vector2 pos = nameRect.Center;
            pos.Y -= renderedHeight * 0.5f;
            pos.X = nameRect.Right;

            frame.Add(new MySprite(
                SpriteType.TEXT,
                localizedName,
                pos,
                null,
                item.GridTextColor,
                TextFont,
                TextAlignment.RIGHT,
                fontSize * .95f * FontScale
            ));

            var qty = item.AmountText;
            size = FormatingHelper.GetSizeInPixel(qty, TextFont, 1, Surface);
            minProportion = Math.Min(numberRect.Width / size.X, numberRect.Height / size.Y);
            fontSize = minProportion;
            renderedHeight = size.Y * fontSize * FontScale;
            pos = numberRect.Center;
            pos.Y -= renderedHeight * 0.5f;
            pos.X = numberRect.Right;

            frame.Add(new MySprite(
                SpriteType.TEXT,
                qty,
                pos,
                null,
                item.GridAmountColor,
                TextFont,
                TextAlignment.RIGHT,
                fontSize * .95f * FontScale
            ));
        }

        protected virtual void DrawFooter(List<MySprite> frame)
        {
        }

        sealed class ItemControl : RectangleControl
        {
            public ItemControl(RectangleF bounds, ItemViewModel item, InteractiveRenderHandler render)
                : base(bounds, CursorType.Default, item)
            {
                CustomRender = render;
            }

            public void UpdateItem(ItemViewModel item, InteractiveRenderHandler render)
            {
                SetDataContext(item);
                CustomRender = render;
            }
        }

        void ClearInteractiveTree()
        {
            _scrollPanel.SetVisible(false);
            _children.Clear();
            SetItemControlsVisible(_listItemControls, false);
            SetItemControlsVisible(_gridItemControls, false);
        }

        static void SetItemControlsVisible(Dictionary<MyItemType, ItemControl> controls, bool visible)
        {
            if (controls == null)
                return;

            foreach (var kv in controls)
                kv.Value?.SetVisible(visible);
        }

        void BeginInteractiveTree(ScrollPanel panel)
        {
            panel.SetVisible(true);
            if (!_children.Contains(panel))
                _children.Add(panel);
        }

        public override bool HasVisibleItems()
        {
            return HasItems;
        }

        void OnScrollPanelChanged(ScrollPanel panel)
        {
            QueueControlRenderIfNeeded();
        }

        void QueueControlRenderIfNeeded()
        {
            if (_scrollRenderQueued || !_scrollPanel.IsDirty || !CanSelfRender())
                return;

            _scrollRenderQueued = true;
            LcdModClientComponent.RunNextFrame.Add(RunQueuedScrollRender);
        }

        void RunQueuedScrollRender()
        {
            _scrollRenderQueued = false;

            try
            {
                if (!_scrollPanel.IsDirty || !CanSelfRender())
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

        protected virtual RectangleF GetCellViewBox(float xStart, float xEnd, float yStart, float cellHeight,
            float cellPadding)
        {
            var innerLeft = xStart + cellPadding;
            var innerRight = xEnd - cellPadding;
            var innerTop = yStart + cellPadding;
            var innerBottom = yStart + cellHeight - cellPadding;
            return new RectangleF(innerLeft, innerTop, innerRight - innerLeft, innerBottom - innerTop);
        }

        protected virtual MyTuple<RectangleF, RectangleF, RectangleF> GetCellSlots(float innerLeft, float innerRight,
            float innerTop, float innerBottom, float spacing)
        {
            var topRowHeight = spacing * Scale;
            var bottomRowTop = innerTop + topRowHeight;
            var bottomRowHeight = Math.Max(0f, innerBottom - bottomRowTop);
            var iconSize = innerBottom - innerTop;
            var contentLeft = innerLeft + iconSize;
            var contentWidth = Math.Max(0f, innerRight - contentLeft);

            var iconRect = new RectangleF(innerLeft, innerTop, iconSize, iconSize);
            var numberRect = new RectangleF(contentLeft, innerTop, contentWidth, topRowHeight);
            var nameRect = new RectangleF(contentLeft, bottomRowTop, contentWidth, bottomRowHeight);
            return new MyTuple<RectangleF, RectangleF, RectangleF>(iconRect, numberRect, nameRect);
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

        protected virtual void DrawCellBackground(List<MySprite> frame, ItemViewModel item,
            float xStart, float xEnd, float yStart, float cellHeight, float cellPadding)
        {
            var rl = xStart + cellPadding / 2;
            var rr = xEnd - cellPadding / 2;
            var rt = yStart + cellPadding / 2;
            var rb = yStart + cellHeight - cellPadding / 2;

            var backgroundColor = item.PanelColor;
            var accent = backgroundColor.MulValue(0.2f);
            var cellRect = new RectangleF(rl, rt, rr - rl, rb - rt);
            var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
            Border.CreateSpritesFromRect(dropShadow, frame, accent,
                radiusScale: Scale);
            Border.CreateSpritesFromRect(cellRect, frame, backgroundColor,
                radiusScale: Scale);
        }

        protected Vector2 ToScreenMargin(Vector2 absoluteCenterInViewBox)
        {
            return new Vector2(absoluteCenterInViewBox.X, 512f - absoluteCenterInViewBox.Y);
        }

        protected class ItemViewModel : ControlModelBase
        {
            public ItemViewModel(MyItemType itemType)
            {
                ItemType = itemType;
            }

            public MyItemType ItemType { get; private set; }
            public double Amount { get; set; }
            public int LayoutVersion { get; set; }
            public string TypeId => ItemType.TypeId;

            public string Icon { get; set; }
            public string DisplayName { get; set; }
            public string AmountText { get; set; }
            public string PrimaryAmountText { get; set; }
            public string SecondaryAmountText { get; set; }
            public ItemAmountDisplayMode AmountDisplayMode { get; set; }
            public Color ListTextColor { get; set; }
            public Color ListAmountColor { get; set; }
            public Color ListIconColor { get; set; }
            public Color GridTextColor { get; set; }
            public Color GridAmountColor { get; set; }
            public Color GridIconColor { get; set; }
            public Color IconBackgroundColor { get; set; }
            public Color PanelColor { get; set; }

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

        protected enum ItemAmountDisplayMode
        {
            Simple,
            Quota
        }
    }


    sealed class ItemTypeComparer : IComparer<MyItemType>
    {
        public static readonly ItemTypeComparer Instance = new ItemTypeComparer();

        public int Compare(MyItemType a, MyItemType b)
        {
            int typeCmp = string.Compare(a.TypeId, b.TypeId, StringComparison.CurrentCulture);
            if (typeCmp != 0)
                return typeCmp;
            return string.Compare(a.SubtypeId, b.SubtypeId, StringComparison.CurrentCulture);
        }
    }

    sealed class DescendingDoubleComparer : IComparer<double>
    {
        public static readonly DescendingDoubleComparer Instance = new DescendingDoubleComparer();

        public int Compare(double a, double b) => b.CompareTo(a);
    }
}
