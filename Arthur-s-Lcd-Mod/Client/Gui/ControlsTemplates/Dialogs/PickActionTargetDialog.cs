using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using IMyBlockGroup = Sandbox.ModAPI.Ingame.IMyBlockGroup;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
{
    enum PickActionTargetKind
    {
        Block,
        Group,
        BlockType,
        BlockSubtype
    }

    sealed class PickActionTargetResult
    {
        public PickActionTargetKind Kind { get; set; }
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string SpriteName { get; set; }
        public string TypeName { get; set; }

        public override string ToString()
        {
            return DisplayName ?? Id ?? string.Empty;
        }
    }

    sealed class PickActionTargetDialog : Dialog
    {
        const string MISSING_ICON_PLACEHOLDER = "MissingIcon";

        const float CARD_WIDTH_PERCENT = 0.76f;
        const float CARD_HEIGHT_PERCENT = 0.78f;
        const float MIN_CARD_WIDTH_PIXELS = 330f;
        const float MIN_CARD_HEIGHT_PIXELS = 275f;
        const float COMBO_HEIGHT_PIXELS = 34f;
        const float SEARCH_HEIGHT_PIXELS = 38f;
        const float ROW_HEIGHT_PIXELS = 40f;
        const float ROW_GAP_PIXELS = 3f;
        const float ICON_SIZE_PIXELS = 46f;

        static readonly PickActionTargetKind[] Kinds = new[]
        {
            PickActionTargetKind.Block,
            PickActionTargetKind.Group,
            PickActionTargetKind.BlockType,
            PickActionTargetKind.BlockSubtype
        };

        readonly GridLogic _gridLogic;
        readonly PickActionTargetResult _initialSelection;
        readonly Action<PickActionTargetResult> _selectedCallback;
        readonly Action _requestRedraw;
        readonly List<PickActionTargetResult> _allItems = new List<PickActionTargetResult>();
        readonly List<PickActionTargetResult> _filteredItems = new List<PickActionTargetResult>();
        readonly List<Button> _rowButtons = new List<Button>();
        readonly ScrollPanel _scrollPanel = new ScrollPanel();
        readonly List<IMyBlockGroup> _groups = new List<IMyBlockGroup>();

        PickActionTargetKind _kind = PickActionTargetKind.Block;
        string _searchText = string.Empty;
        bool _itemsLoaded;

        ComboBox<PickActionTargetKind> _comboButton;
        TextInput _searchInput;
        TextInputModel _searchInputModel;


        public PickActionTargetDialog(
            IApp parentApp,
            GridLogic gridLogic,
            PickActionTargetResult initialSelection,
            Action<PickActionTargetResult> selectedCallback,
            Action cancelCallback,
            Action requestRedraw)
            : base(parentApp)
        {
            _gridLogic = gridLogic;
            _initialSelection = initialSelection;
            _selectedCallback = selectedCallback;
            var cancelCallback1 = cancelCallback;
            _requestRedraw = requestRedraw;
            OnClose = delegate
            {
                if (cancelCallback1 != null)
                    cancelCallback1();
            };

            if (initialSelection != null)
                _kind = initialSelection.Kind;

            _scrollPanel.ManualScrollInertiaEnabled = false;
            _scrollPanel.ScrollChanged = OnScrollChanged;
        }

        protected override void BuildDialogControls(
            InteractiveSurfaceScript owner,
            RectangleF viewBox,
            float scale,
            float fontScale,
            IMyTextSurface surface,
            Color textColor,
            Color backgroundColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            EnsureContainer(viewBox);
            ContainerControl.ClearChildren();

            EnsureItemsLoaded();
            var layoutScale = scale * fontScale;
            var compact = IsTinyDialogAspectRatio(viewBox);
            var padding = GetDialogPadding(viewBox, scale);
            var spacing = GetDialogSpacing(viewBox, scale);
            var titleScale = 0.82f * layoutScale;
            var titleHeight = MeasureLineHeight(titleScale, surface);
            var inputTextScale = 0.58f * layoutScale;
            var comboHeight = Math.Max(COMBO_HEIGHT_PIXELS * scale, MeasureLineHeight(inputTextScale, surface) + 10f * scale);
            var searchHeight = Math.Max(SEARCH_HEIGHT_PIXELS * scale, MeasureLineHeight(inputTextScale, surface) + 18f * scale);
            var cancelSize = GetDialogCloseButtonSize(scale);
            var headerHeight = Math.Max(titleHeight, cancelSize.Y);

            var cardRect = GetDialogCardRect(
                viewBox,
                scale,
                CARD_WIDTH_PERCENT,
                CARD_HEIGHT_PERCENT,
                MIN_CARD_WIDTH_PIXELS,
                MIN_CARD_HEIGHT_PIXELS);

            RegisterDialogCard(cardRect);

            DrawBackdrop(surface, scale, cardRect);

            if (compact)
            {
                var contentRect = GetDialogContentRect(cardRect, viewBox, scale, padding);
                var compactComboWidth = Math.Max(72f * scale, Math.Min(contentRect.Width * .28f, 124f * scale));
                var compactRowHeight = Math.Max(1f, (contentRect.Height - spacing) * .5f);
                var compactSearchRect = new RectangleF(
                    contentRect.X,
                    contentRect.Y,
                    compactComboWidth,
                    compactRowHeight);
                var compactComboRect = new RectangleF(
                    contentRect.X,
                    compactSearchRect.Bottom + spacing,
                    compactComboWidth,
                    compactRowHeight);
                var compactListRect = new RectangleF(
                    compactComboRect.Right + spacing,
                    contentRect.Y,
                    Math.Max(1f, contentRect.Right - compactComboRect.Right - spacing),
                    contentRect.Height);

                EnsureSearchInput(compactSearchRect);
                _searchInput.SetClass("ControlBase Compact");
                ContainerControl.AddChild(_searchInput);
                _searchInput.Render(Sprites);

                EnsureComboButton(compactComboRect, viewBox);
                _comboButton.FullScreenRequested = delegate
                {
                    owner.ShowDialog(new ComboBoxSelectionDialog<PickActionTargetKind>(ParentApp, Kinds, _kind,
                        GetKindLabel, OnKindChanged));
                };
                ContainerControl.AddChild(_comboButton);
                _comboButton.Configure(compactComboRect, scale, viewBox);
                _comboButton.Render(Sprites);

                RenderTargetList(compactListRect, scale, surface);

                return;
            }

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = ButtonPadLocalization.TargetDialogTitle,
                Position = new Vector2(cardRect.Center.X, cardRect.Y + padding.Y + (headerHeight - titleHeight) * 0.5f),
                Color = ResolveColor(ThemeResources.OnSurfaceColor),
                FontId = TextFont,
                RotationOrScale = titleScale,
                Alignment = TextAlignment.CENTER
            });

            var contentTop = cardRect.Y + padding.Y + headerHeight + spacing;
            var contentHeight = Math.Max(0f, cardRect.Bottom - padding.Y - contentTop);
            var contentWidth = Math.Max(1f, cardRect.Width - padding.X * 2f);
            var controlsWidth = Math.Max(
                96f * scale,
                Math.Min(contentWidth * .28f, 160f * scale));

            var searchRect = new RectangleF(
                cardRect.X + padding.X,
                contentTop,
                controlsWidth,
                searchHeight);

            var comboRect = new RectangleF(
                cardRect.X + padding.X,
                searchRect.Bottom + spacing,
                controlsWidth,
                comboHeight);

            var listRect = new RectangleF(
                comboRect.Right + spacing,
                contentTop,
                Math.Max(1f, cardRect.Right - padding.X - comboRect.Right - spacing),
                contentHeight);

            EnsureComboButton(comboRect, viewBox);
            EnsureSearchInput(searchRect);
            _searchInput.SetClass("ControlBase");

            ContainerControl.AddChild(_comboButton);
            ContainerControl.AddChild(_searchInput);
            _comboButton.Configure(comboRect, scale, viewBox);

            _comboButton.Render(Sprites);
            _searchInput.Render(Sprites);

            RenderTargetList(listRect, scale, surface);

        }

        void DrawBackdrop(IMyTextSurface surface, float scale, RectangleF cardRect)
        {
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = surface.TextureSize / 2f,
                Size = surface.TextureSize,
                Color = new Color(0, 0, 0, 160),
                Alignment = TextAlignment.CENTER
            });

            if (!CurrentDialogIsTiny)
                BorderRenderer.CreateSpritesFromRect(new RectangleF(cardRect.Position + 3f * scale, cardRect.Size), Sprites,
                    ResolveColor(ThemeResources.ShadowColor), radiusScale: scale);
            BorderRenderer.CreateSpritesFromRect(cardRect, Sprites,
                ResolveColor(ThemeResources.SurfaceContainerHighColor), radiusPixels: DialogCardRadiusPixels,
                radiusScale: scale);
        }

        void EnsureItemsLoaded()
        {
            if (_itemsLoaded)
                return;

            BuildItems();
            _itemsLoaded = true;
            ApplyFilter();
        }

        void BuildItems()
        {
            _allItems.Clear();

            switch (_kind)
            {
                case PickActionTargetKind.Block:
                    BuildBlockItems();
                    break;
                case PickActionTargetKind.Group:
                    BuildGroupItems();
                    break;
                case PickActionTargetKind.BlockType:
                    BuildBlockTypeItems();
                    break;
                case PickActionTargetKind.BlockSubtype:
                    BuildBlockSubtypeItems();
                    break;
            }

            _allItems.Sort(CompareTargets);
        }

        void BuildBlockItems()
        {
            if (_gridLogic == null)
                return;

            var blocks = _gridLogic.Blocks.TerminalBlocks;
            if (blocks == null)
                return;

            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null)
                    continue;

                var displayName = GetBlockDisplayName(block);
                _allItems.Add(new PickActionTargetResult
                {
                    Kind = PickActionTargetKind.Block,
                    Id = block.EntityId.ToString(FormatingHelper.Culture),
                    DisplayName = displayName,
                    SpriteName = GetBlockSprite(block),
                    TypeName = block.GetType().FullName
                });
            }
        }

        void BuildGroupItems()
        {
            _groups.Clear();

            if (_gridLogic == null || _gridLogic.Grid == null)
                return;

            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(_gridLogic.Grid);
            if (terminalSystem == null)
                return;

            terminalSystem.GetBlockGroups(_groups);

            for (var i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                if (group == null || string.IsNullOrWhiteSpace(group.Name))
                    continue;

                _allItems.Add(new PickActionTargetResult
                {
                    Kind = PickActionTargetKind.Group,
                    Id = group.Name,
                    DisplayName = group.Name,
                    SpriteName = "SquareSimple",
                    TypeName = "Group"
                });
            }
        }

        void BuildBlockTypeItems()
        {
            foreach (var type in ActionHelper.Types)
            {
                if (type == null)
                    continue;

                _allItems.Add(new PickActionTargetResult
                {
                    Kind = PickActionTargetKind.BlockType,
                    Id = type.FullName ?? type.Name,
                    DisplayName = FormatTypeName(type),
                    SpriteName = GetTypeSprite(type),
                    TypeName = type.FullName ?? type.Name
                });
            }
        }

        void BuildBlockSubtypeItems()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var block in _gridLogic.Blocks.TerminalBlocks)
            {
                var subtype = block != null ? block.BlockDefinition.SubtypeName : null;
                if (string.IsNullOrWhiteSpace(subtype))
                    continue;
                if (!seen.Add(subtype))
                    continue;

                _allItems.Add(new PickActionTargetResult
                {
                    Kind = PickActionTargetKind.BlockSubtype,
                    Id = subtype,
                    DisplayName = subtype,
                    SpriteName = GetSubtypeSprite(subtype),
                    TypeName = subtype
                });
            }
        }

        void ApplyFilter()
        {
            _filteredItems.Clear();

            var query = (_searchText ?? string.Empty).Trim();
            if (query.Length == 0)
            {
                _filteredItems.AddRange(_allItems);
                return;
            }

            for (var i = 0; i < _allItems.Count; i++)
            {
                var item = _allItems[i];
                if (item == null)
                    continue;

                var displayName = item.DisplayName ?? string.Empty;
                var id = item.Id ?? string.Empty;
                if (displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _filteredItems.Add(item);
                }
            }
        }

        void EnsureComboButton(RectangleF rect, RectangleF viewBox)
        {
            if (_comboButton == null)
                _comboButton = new ComboBox<PickActionTargetKind>(Kinds, GetKindLabel, OnKindChanged, _requestRedraw);
            else
                _comboButton.SetRect(rect);

            _comboButton.SetClass(IsTinyDialogAspectRatio(viewBox) ? "ControlBase Compact" : "ControlBase");
            if (!IsTinyDialogAspectRatio(viewBox))
                _comboButton.FullScreenRequested = null;
            _comboButton.SetStyleId("Primary");
            _comboButton.SetSelectedValue(_kind);
            _comboButton.SetCursor(CursorType.Hand);
            _comboButton.SetVisible(true);
        }

        void EnsureSearchInput(RectangleF rect)
        {
            if (_searchInputModel == null)
            {
                _searchInputModel = new TextInputModel
                {
                    Title = ButtonPadLocalization.TargetDialogSearchTitle,
                    Subtitle = ButtonPadLocalization.TargetDialogSearchHelp,
                    Placeholder = ButtonPadLocalization.TargetDialogSearchPlaceholder,
                    Value = _searchText,
                    ValueChanged = OnSearchChanged
                };
            }

            _searchInputModel.Title = ButtonPadLocalization.TargetDialogSearchTitle;
            _searchInputModel.Subtitle = ButtonPadLocalization.TargetDialogSearchHelp;
            _searchInputModel.Placeholder = ButtonPadLocalization.TargetDialogSearchPlaceholder;
            _searchInputModel.Value = _searchText;
            _searchInputModel.Enabled = true;
            _searchInputModel.ValueChanged = OnSearchChanged;

            if (_searchInput == null)
                _searchInput = new TextInput(rect, _searchInputModel);
            else
                _searchInput.SetRect(rect);

            _searchInput.SetDataContext(_searchInputModel);
            _searchInput.SetStyleId("Primary");
            _searchInput.SetCursor(CursorType.Hand);
            _searchInput.SetVisible(true);
        }

        void RenderTargetList(RectangleF listRect, float scale, IMyTextSurface surface)
        {
            HideUnusedRows(0);

            if (listRect.Width <= 1f || listRect.Height <= 1f)
                return;

            BorderRenderer.CreateSpritesFromRect(listRect, Sprites, ResolveColor(ThemeResources.SurfaceContainerHighColor), radiusScale: scale);

            var borderInset = Math.Max(1f, 2f * scale);
            var scrollRect = new RectangleF(
                listRect.X + borderInset,
                listRect.Y + borderInset,
                Math.Max(1f, listRect.Width - borderInset * 2f),
                Math.Max(1f, listRect.Height - borderInset * 2f));

            var rowHeight = GetRowHeight(scale);
            var scrollerWidth = Math.Min(_scrollPanel.AutomaticScrollerWidthPixels * scale, Math.Max(0f, scrollRect.Width * 0.25f));

            _scrollPanel.ClearChildren();
            _scrollPanel.Configure(scrollRect, scrollRect.Y, 0f, rowHeight, _filteredItems.Count, scrollerWidth, 0f);
            _scrollPanel.SetScrollBarColors(ResolveColor(ThemeResources.SurfaceContainerHighColor), ResolveColor(ThemeResources.OnSurfaceColor));
            _scrollPanel.SetVisible(true);
            ContainerControl.AddChild(_scrollPanel);

            if (_filteredItems.Count == 0)
            {
                DrawEmptyMessage(scrollRect, scale, surface);
                _scrollPanel.Render(Sprites);
                return;
            }

            BeginClip(Sprites, _scrollPanel.ContentViewportBounds);

            var usedControls = 0;
            var startRow = _scrollPanel.StartRow;
            var endRow = Math.Min(_filteredItems.Count, startRow + _scrollPanel.RenderRows);
            for (var itemIndex = startRow; itemIndex < endRow; itemIndex++)
            {
                var visibleIndex = itemIndex - startRow;
                var rowRect = new RectangleF(
                    _scrollPanel.ContentViewportBounds.X,
                    _scrollPanel.ContentBounds.Y + visibleIndex * rowHeight,
                    _scrollPanel.ContentViewportBounds.Width,
                    Math.Max(1f, rowHeight - ROW_GAP_PIXELS * scale));

                var button = GetRowButton(usedControls++);
                ConfigureRowButton(button, rowRect, _filteredItems[itemIndex]);
                _scrollPanel.AddChild(button);
                button.Render(Sprites);
            }

            EndClip(Sprites);
            HideUnusedRows(usedControls);
            _scrollPanel.Render(Sprites);
        }

        Button GetRowButton(int index)
        {
            while (_rowButtons.Count <= index)
            {
                var button = new Button(default(RectangleF), new TargetRowButtonModel { Clicked = OnTargetClicked });
                button.CustomRender = RenderTargetRow;
                _rowButtons.Add(button);
            }

            return _rowButtons[index];
        }

        void ConfigureRowButton(Button button, RectangleF rect, PickActionTargetResult item)
        {
            var model = button.DataContext as TargetRowButtonModel;
            if (model == null)
            {
                model = new TargetRowButtonModel();
                button.SetDataContext(model);
            }

            model.Target = item;
            model.Text = string.Empty;
            model.Enabled = item != null;
            model.Clicked = OnTargetClicked;

            button.SetRect(rect);
            var selected = IsSelected(item);
            button.SetClass(selected ? "ControlBase Button Row Selected" : "ControlBase Button Row");
            button.SetStyleId(null);
            button.SetCursor(CursorType.Hand);
            button.CustomRender = RenderTargetRow;
            button.SetVisible(true);
        }


        static float GetIconTargetSize(float scale)
        {
            return Math.Max(ICON_SIZE_PIXELS, ICON_SIZE_PIXELS * scale);
        }

        static float GetRowHeight(float scale)
        {
            return Math.Max(ROW_HEIGHT_PIXELS * scale, GetIconTargetSize(scale) + 8f * Math.Max(1f, scale));
        }

        void RenderTargetRow(ControlTemplate control, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var model = control.DataContext as TargetRowButtonModel;
            var target = model?.Target;
            if (target == null)
                return;

            rect.Contains(new Vector2(float.NaN, float.NaN));
            var panelColor = control.BackgroundColor;
            var rowTextColor = control.TextColor;

            BorderRenderer.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: control.LayoutScale);

            var iconTargetSize = GetIconTargetSize(control.LayoutScale);
            var iconSize = Math.Min(iconTargetSize, Math.Max(1f, Math.Min(rect.Height, rect.Width) - 4f * Math.Max(1f, control.LayoutScale)));
            var textScale = 0.42f * control.LayoutScale * control.FontScale;
            var minimumTextWidth = 72f * control.LayoutScale;
            var iconOnly = rect.Width < iconSize + 10f * control.LayoutScale + minimumTextWidth;
            var iconRect = iconOnly
                ? new RectangleF(rect.Center.X - iconSize * 0.5f, rect.Center.Y - iconSize * 0.5f, iconSize, iconSize)
                : new RectangleF(rect.X + 4f * control.LayoutScale, rect.Center.Y - iconSize * 0.5f, iconSize, iconSize);

            DrawTargetIcon(target, iconRect, sprites);

            if (iconOnly)
                return;

            var textHeight = FormatingHelper.LineHeight(textScale, control, control.TextSurface);
            var textX = iconRect.Right + 6f * control.LayoutScale;
            var textWidth = Math.Max(0f, rect.Right - textX - 6f * control.LayoutScale);
            var displayName = TrimText(target.DisplayName, textWidth, textScale, control.TextSurface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = displayName,
                Position = new Vector2(textX, rect.Center.Y - textHeight * 0.5f),
                Color = rowTextColor,
                FontId = control.TextFont,
                RotationOrScale = textScale,
                Alignment = TextAlignment.LEFT
            });
        }

        void DrawTargetIcon(PickActionTargetResult target, RectangleF iconRect, List<MySprite> sprites)
        {
            var spriteName = string.IsNullOrEmpty(target.SpriteName) ? MISSING_ICON_PLACEHOLDER : target.SpriteName;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = spriteName,
                Position = iconRect.Center,
                Size = iconRect.Size,
                Color = Constants.ColorCorrection,
                Alignment = TextAlignment.CENTER
            });
        }


        void HideUnusedRows(int usedControls)
        {
            for (var i = usedControls; i < _rowButtons.Count; i++)
                _rowButtons[i].SetVisible(false);
        }

        void DrawEmptyMessage(RectangleF rect, float scale, IMyTextSurface surface)
        {
            var text = string.IsNullOrWhiteSpace(_searchText)
                ? ButtonPadLocalization.TargetDialogNoTargets
                : ButtonPadLocalization.NoMatches;
            var textScale = 0.46f * scale * surface.FontSize;
            var textHeight = MeasureLineHeight(textScale, surface);

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textHeight * 0.5f),
                Color = ResolveColor(ThemeResources.OnSurfaceColor),
                FontId = TextFont,
                RotationOrScale = textScale,
                Alignment = TextAlignment.CENTER
            });
        }

        bool IsSelected(PickActionTargetResult target)
        {
            return _initialSelection != null && target != null &&
                   _initialSelection.Kind == target.Kind &&
                   string.Equals(_initialSelection.Id, target.Id, StringComparison.OrdinalIgnoreCase);
        }

        string GetBlockSprite(IMyTerminalBlock block)
        {
            try
            {
                var cubeBlock = block as MyCubeBlock;
                if (cubeBlock?.BlockDefinition != null)
                    return TextureHelper.GetOrAddTextureForBlock(cubeBlock.BlockDefinition);
            }
            catch
            {
                // ignored, using missing icon instead
            }

            return MISSING_ICON_PLACEHOLDER;
        }

        string GetTypeSprite(Type type)
        {
            if (_gridLogic == null || type == null)
                return MISSING_ICON_PLACEHOLDER;

            var blocks = _gridLogic.Blocks.TerminalBlocks;
            if (blocks == null)
                return MISSING_ICON_PLACEHOLDER;

            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null)
                    continue;

                if (MyAPIGateway.Reflection.IsAssignableFrom(type, block.GetType()))
                    return GetBlockSprite(block);
            }

            return MISSING_ICON_PLACEHOLDER;
        }

        string GetSubtypeSprite(string subtype)
        {
            if (string.IsNullOrEmpty(subtype))
                return MISSING_ICON_PLACEHOLDER;

            foreach (var definitionBase in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var blockDefinition = definitionBase as MyCubeBlockDefinition;
                if (blockDefinition == null)
                    continue;

                if (!string.Equals(blockDefinition.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    continue;

                return TextureHelper.GetOrAddTextureForBlock(blockDefinition);
            }

            string textureName;
            return TextureHelper.TryGetOrAddTextureForBlockName(subtype, out textureName) ? textureName : "Danger";
        }

        static string GetBlockDisplayName(IMyTerminalBlock block)
        {
            if (block == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(block.CustomName))
                return block.CustomName;

            if (!string.IsNullOrWhiteSpace(block.DisplayNameText))
                return block.DisplayNameText;

            return block.EntityId.ToString(FormatingHelper.Culture);
        }

        static string FormatTypeName(Type type)
        {
            if (type == null)
                return string.Empty;

            var name = type.Name;
            if (name.StartsWith("My", StringComparison.Ordinal) && name.Length > 2)
                name = name.Substring(2);
            if (name.StartsWith("IMy", StringComparison.Ordinal) && name.Length > 3)
                name = name.Substring(3);

            return name;
        }

        static string GetKindLabel(PickActionTargetKind kind)
        {
            switch (kind)
            {
                case PickActionTargetKind.Block:
                    return ButtonPadLocalization.TargetKindBlock;
                case PickActionTargetKind.Group:
                    return ButtonPadLocalization.TargetKindGroup;
                case PickActionTargetKind.BlockType:
                    return ButtonPadLocalization.TargetKindBlockType;
                case PickActionTargetKind.BlockSubtype:
                    return ButtonPadLocalization.TargetKindBlockSubtype;
                default:
                    return kind.ToString();
            }
        }

        static int CompareTargets(PickActionTargetResult a, PickActionTargetResult b)
        {
            if (a == null && b == null)
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
        }

        string TrimText(string text, float availableWidth, float fontSize, IMyTextSurface surface)
        {
            if (string.IsNullOrEmpty(text) || availableWidth <= 0f || surface == null)
                return string.Empty;

            var size = MeasureText(text, fontSize, surface);
            if (size.X <= availableWidth)
                return text;

            return FormatingHelper.TrimName(text, Math.Max(1, (int)(text.Length * availableWidth / Math.Max(1f, size.X))));
        }


        void OnKindChanged(PickActionTargetKind kind)
        {
            if (_kind != kind)
            {
                _kind = kind;
                _itemsLoaded = false;
                _searchText = string.Empty;
                if (_searchInputModel != null)
                    _searchInputModel.Value = string.Empty;
            }

            _requestRedraw?.Invoke();
            RequestRender();
        }

        void OnSearchChanged(string value)
        {
            _searchText = value ?? string.Empty;
            ApplyFilter();
            _requestRedraw?.Invoke();
            RequestRender();
        }

        void OnTargetClicked(ButtonModel model, object sender)
        {
            var rowModel = model as TargetRowButtonModel;
            if (rowModel == null || rowModel.Target == null)
                return;

            Dismiss();
            if (_selectedCallback != null)
                _selectedCallback(rowModel.Target);
        }


        void OnScrollChanged(ScrollPanel panel)
        {
            _requestRedraw?.Invoke();
        }

        protected override void OnDismiss()
        {
            base.OnDismiss();
            _scrollPanel.ClearChildren();
            for (var i = 0; i < _rowButtons.Count; i++)
                _rowButtons[i].SetVisible(false);
        }

        static void BeginClip(List<MySprite> sprites, RectangleF bounds)
        {
            sprites.Add(MySprite.CreateClipRect(new Rectangle(
                (int)Math.Floor(bounds.X),
                (int)Math.Floor(bounds.Y),
                (int)Math.Ceiling(bounds.Width),
                (int)Math.Ceiling(bounds.Height)
            )));
        }

        static void EndClip(List<MySprite> sprites)
        {
            sprites.Add(MySprite.CreateClearClipRect());
        }

        sealed class TargetRowButtonModel : ButtonModel
        {
            public PickActionTargetResult Target { get; set; }
        }

    }
}
