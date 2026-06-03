#if EXPERIMENTAL
using LcdMod.Client.Terminal.Actions;
#endif
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
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
        const string TITLE = "Pick Target";
        const string SEARCH_TITLE = "Search Target";
        const string SEARCH_PLACEHOLDER = "Search targets";
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
        const float SCROLLER_WIDTH_PIXELS = 10f;

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
        readonly Action _cancelCallback;
        readonly Action _requestRedraw;
        readonly List<PickActionTargetResult> _allItems = new List<PickActionTargetResult>();
        readonly List<PickActionTargetResult> _filteredItems = new List<PickActionTargetResult>();
        readonly List<Button> _rowButtons = new List<Button>();
        readonly List<Button> _comboOptionButtons = new List<Button>();
        readonly ScrollPanel _scrollPanel = new ScrollPanel();
        readonly List<IMyBlockGroup> _groups = new List<IMyBlockGroup>();

        PickActionTargetKind _kind = PickActionTargetKind.Block;
        string _searchText = string.Empty;
        bool _comboOpen;
        bool _itemsLoaded;

        Button _comboButton;
        TextInput _searchInput;
        TextInputModel _searchInputModel;

        ControlStyle _comboStyle;
        ControlStyle _comboOptionStyle;
        ControlStyle _searchStyle;
        ControlStyle _rowStyle;

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
            _cancelCallback = cancelCallback;
            _requestRedraw = requestRedraw;
            OnClose = delegate
            {
                if (_cancelCallback != null)
                    _cancelCallback();
            };

            if (initialSelection != null)
                _kind = initialSelection.Kind;

            _scrollPanel.ManualScrollInertiaEnabled = false;
            _scrollPanel.ScrollChanged = OnScrollChanged;
        }

        protected override void RenderCore(
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

            var context = CreateRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);
            var layoutScale = scale * fontScale;
            var padding = new Vector2(18f * scale, 14f * scale);
            var spacing = 10f * scale;
            var titleScale = 0.82f * layoutScale;
            var titleHeight = FormatingHelper.LineHeight(titleScale, surface);
            var inputTextScale = 0.58f * layoutScale;
            var comboHeight = Math.Max(COMBO_HEIGHT_PIXELS * scale, FormatingHelper.LineHeight(inputTextScale, surface) + 10f * scale);
            var searchHeight = Math.Max(SEARCH_HEIGHT_PIXELS * scale, FormatingHelper.LineHeight(inputTextScale, surface) + 18f * scale);
            var cancelSize = GetDialogCloseButtonSize(scale);
            var headerHeight = Math.Max(titleHeight, cancelSize.Y);

            var maxCardWidth = Math.Max(1f, viewBox.Width - padding.X * 2f);
            var maxCardHeight = Math.Max(1f, viewBox.Height - padding.Y * 2f);
            var cardWidth = Math.Min(Math.Max(MIN_CARD_WIDTH_PIXELS * scale, viewBox.Width * CARD_WIDTH_PERCENT), maxCardWidth);
            var cardHeight = Math.Min(Math.Max(MIN_CARD_HEIGHT_PIXELS * scale, viewBox.Height * CARD_HEIGHT_PERCENT), maxCardHeight);
            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth,
                cardHeight);

            RegisterDialogCard(cardRect);

            DrawBackdrop(surface, scale, cardRect);

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = TITLE,
                Position = new Vector2(cardRect.Center.X, cardRect.Y + padding.Y + (headerHeight - titleHeight) * 0.5f),
                Color = GetThemeColor(Constants.ON_SURFACE),
                FontId = "White",
                RotationOrScale = titleScale,
                Alignment = TextAlignment.CENTER
            });

            var comboRect = new RectangleF(
                cardRect.X + padding.X,
                cardRect.Y + padding.Y + headerHeight + spacing,
                Math.Max(1f, cardRect.Width - padding.X * 2f),
                comboHeight);

            var searchRect = new RectangleF(
                cardRect.X + padding.X,
                comboRect.Bottom + spacing,
                Math.Max(1f, cardRect.Width - padding.X * 2f),
                searchHeight);

            var listRect = new RectangleF(
                cardRect.X + padding.X,
                searchRect.Bottom + spacing,
                Math.Max(1f, cardRect.Width - padding.X * 2f),
                Math.Max(0f, cardRect.Bottom - padding.Y - searchRect.Bottom - spacing));

            EnsureComboButton(comboRect);
            EnsureSearchInput(searchRect);

            ContainerControl.AddChild(_comboButton);
            ContainerControl.AddChild(_searchInput);

            _comboButton.Render(context, Sprites);
            _searchInput.Render(context, Sprites);

            RenderTargetList(context, listRect, scale, surface);

            if (_comboOpen)
                RenderComboOptions(context, comboRect, scale);
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

            Border.CreateSpritesFromRect(new RectangleF(cardRect.Position + 3f * scale, cardRect.Size), Sprites,
                GetThemeColor(Constants.SHADOW), radiusScale: scale);
            Border.CreateSpritesFromRect(cardRect, Sprites,
                GetThemeColor(Constants.SURFACE_CONTAINER_HIGH), radiusScale: scale);
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

            var blocks = _gridLogic.GetTerminalBlocks<IMyTerminalBlock>();
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
#if EXPERIMENTAL
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
#endif
        }

        void BuildBlockSubtypeItems()
        {
            foreach (var subtype in GridLogic.KnowSubtypes)
            {
                if (string.IsNullOrWhiteSpace(subtype))
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

        void EnsureComboButton(RectangleF rect)
        {
            if (_comboButton == null)
                _comboButton = new Button(rect, new ButtonModel { Text = GetKindLabel(_kind), Clicked = OnComboClicked });
            else
                _comboButton.SetRect(rect);

            var model = _comboButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = GetKindLabel(_kind);
                model.Enabled = true;
                model.Clicked = OnComboClicked;
            }

            _comboButton.SetStyle(GetComboStyle());
            _comboButton.CustomRender = RenderComboButton;
            _comboButton.SetCursor(CursorType.Hand);
            _comboButton.SetVisible(true);
        }

        void RenderComboButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var hovered = rect.Contains(context.CursorPosition);
            Border.CreateSpritesFromRect(rect, sprites, context.Style.GetPanelColor(hovered), radiusScale: context.Scale);

            var textScale = 0.56f * context.Scale * context.FontScale;
            var label = TrimText(GetKindLabel(_kind), Math.Max(0f, rect.Width - 32f * context.Scale), textScale, context.Surface);
            var textHeight = FormatingHelper.LineHeight(textScale, context.Surface);
            var textColor = context.Style.GetTextColor(hovered);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = label,
                Position = new Vector2(rect.X + 10f * context.Scale, rect.Center.Y - textHeight * 0.5f),
                Color = textColor,
                FontId = "White",
                RotationOrScale = textScale,
                Alignment = TextAlignment.LEFT
            });

            var arrowWidth = 9f * context.Scale;
            var arrowHeight = 5f * context.Scale;
            var arrowCenter = new Vector2(rect.Right - 14f * context.Scale, rect.Center.Y);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Triangle",
                Position = arrowCenter,
                Size = new Vector2(arrowWidth, arrowHeight),
                Color = textColor,
                RotationOrScale = _comboOpen ? 3.14159f : 0f,
                Alignment = TextAlignment.CENTER
            });
        }

        void RenderComboOptions(ControlRenderContext context, RectangleF comboRect, float scale)
        {
            var rowHeight = comboRect.Height;
            var listRect = new RectangleF(comboRect.X, comboRect.Bottom + 2f * scale, comboRect.Width, rowHeight * Kinds.Length);
            Border.CreateSpritesFromRect(listRect, Sprites, GetThemeColor(Constants.SURFACE_CONTAINER_HIGHEST), radiusScale: scale);

            for (var i = 0; i < Kinds.Length; i++)
            {
                var rect = new RectangleF(listRect.X, listRect.Y + i * rowHeight, listRect.Width, rowHeight);
                var button = GetComboOptionButton(i);
                ConfigureComboOptionButton(button, rect, Kinds[i]);
                ContainerControl.AddChild(button);
                button.Render(context, Sprites);
            }

            for (var i = Kinds.Length; i < _comboOptionButtons.Count; i++)
                _comboOptionButtons[i].SetVisible(false);
        }

        Button GetComboOptionButton(int index)
        {
            while (_comboOptionButtons.Count <= index)
            {
                var button = new Button(default(RectangleF), new ComboOptionButtonModel { Clicked = OnComboOptionClicked });
                button.CustomRender = RenderComboOption;
                _comboOptionButtons.Add(button);
            }

            return _comboOptionButtons[index];
        }

        void ConfigureComboOptionButton(Button button, RectangleF rect, PickActionTargetKind kind)
        {
            var model = button.DataContext as ComboOptionButtonModel;
            if (model == null)
            {
                model = new ComboOptionButtonModel();
                button.SetDataContext(model);
            }

            model.Kind = kind;
            model.Text = GetKindLabel(kind);
            model.Enabled = true;
            model.Clicked = OnComboOptionClicked;

            button.SetRect(rect);
            button.SetStyle(GetComboOptionStyle());
            button.SetCursor(CursorType.Hand);
            button.CustomRender = RenderComboOption;
            button.SetVisible(true);
        }

        void RenderComboOption(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var model = control.DataContext as ComboOptionButtonModel;
            if (model == null)
                return;

            var selected = model.Kind == _kind;
            var hovered = rect.Contains(context.CursorPosition);
            var panelColor = selected ? context.Style.GetPanelColor(true) : context.Style.GetPanelColor(hovered);
            var textColor = context.Style.GetTextColor(selected || hovered);

            Border.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: context.Scale);

            var textScale = 0.52f * context.Scale * context.FontScale;
            var textHeight = FormatingHelper.LineHeight(textScale, context.Surface);
            var text = TrimText(GetKindLabel(model.Kind), Math.Max(0f, rect.Width - 16f * context.Scale), textScale, context.Surface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.X + 8f * context.Scale, rect.Center.Y - textHeight * 0.5f),
                Color = textColor,
                FontId = "White",
                RotationOrScale = textScale,
                Alignment = TextAlignment.LEFT
            });
        }

        void EnsureSearchInput(RectangleF rect)
        {
            if (_searchInputModel == null)
            {
                _searchInputModel = new TextInputModel
                {
                    Title = SEARCH_TITLE,
                    Subtitle = "Filter targets containing this text",
                    Placeholder = SEARCH_PLACEHOLDER,
                    Value = _searchText,
                    ValueChanged = OnSearchChanged
                };
            }

            _searchInputModel.Title = SEARCH_TITLE;
            _searchInputModel.Subtitle = "Filter targets containing this text";
            _searchInputModel.Placeholder = SEARCH_PLACEHOLDER;
            _searchInputModel.Value = _searchText;
            _searchInputModel.Enabled = true;
            _searchInputModel.ValueChanged = OnSearchChanged;

            if (_searchInput == null)
                _searchInput = new TextInput(rect, _searchInputModel);
            else
                _searchInput.SetRect(rect);

            _searchInput.SetDataContext(_searchInputModel);
            _searchInput.SetStyle(GetSearchStyle());
            _searchInput.SetCursor(CursorType.Hand);
            _searchInput.SetVisible(true);
        }

        void RenderTargetList(ControlRenderContext context, RectangleF listRect, float scale, IMyTextSurface surface)
        {
            HideUnusedRows(0);

            if (listRect.Width <= 1f || listRect.Height <= 1f)
                return;

            Border.CreateSpritesFromRect(listRect, Sprites, GetThemeColor(Constants.SURFACE_CONTAINER_HIGH), radiusScale: scale);

            var rowHeight = GetRowHeight(scale);
            var scrollerWidth = Math.Min(SCROLLER_WIDTH_PIXELS * scale, Math.Max(0f, listRect.Width * 0.25f));

            _scrollPanel.ClearChildren();
            _scrollPanel.Configure(listRect, listRect.Y, 0f, rowHeight, _filteredItems.Count, scrollerWidth, 0f);
            _scrollPanel.SetScrollBarColors(GetThemeColor(Constants.SURFACE_CONTAINER_HIGH), GetThemeColor(Constants.ON_SURFACE));
            _scrollPanel.SetVisible(true);
            ContainerControl.AddChild(_scrollPanel);

            if (_filteredItems.Count == 0)
            {
                DrawEmptyMessage(listRect, scale, surface);
                _scrollPanel.Render(context, Sprites);
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
                button.Render(context, Sprites);
            }

            EndClip(Sprites);
            HideUnusedRows(usedControls);
            _scrollPanel.Render(context, Sprites);
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
            button.SetStyle(GetRowStyle());
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

        void RenderTargetRow(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var model = control.DataContext as TargetRowButtonModel;
            var target = model != null ? model.Target : null;
            if (target == null)
                return;

            var hovered = rect.Contains(context.CursorPosition);
            var selected = IsSelected(target);
            var panelColor = selected ? context.Style.GetPanelColor(true) : context.Style.GetPanelColor(hovered);
            var rowTextColor = context.Style.GetTextColor(hovered || selected);

            Border.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: context.Scale);

            var iconTargetSize = GetIconTargetSize(context.Scale);
            var iconSize = Math.Min(iconTargetSize, Math.Max(1f, Math.Min(rect.Height, rect.Width) - 4f * Math.Max(1f, context.Scale)));
            var textScale = 0.42f * context.Scale * context.FontScale;
            var minimumTextWidth = 72f * context.Scale;
            var iconOnly = rect.Width < iconSize + 10f * context.Scale + minimumTextWidth;
            var iconRect = iconOnly
                ? new RectangleF(rect.Center.X - iconSize * 0.5f, rect.Center.Y - iconSize * 0.5f, iconSize, iconSize)
                : new RectangleF(rect.X + 4f * context.Scale, rect.Center.Y - iconSize * 0.5f, iconSize, iconSize);

            DrawTargetIcon(target, iconRect, sprites);

            if (iconOnly)
                return;

            var textHeight = FormatingHelper.LineHeight(textScale, context.Surface);
            var textX = iconRect.Right + 6f * context.Scale;
            var textWidth = Math.Max(0f, rect.Right - textX - 6f * context.Scale);
            var displayName = TrimText(target.DisplayName, textWidth, textScale, context.Surface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = displayName,
                Position = new Vector2(textX, rect.Center.Y - textHeight * 0.5f),
                Color = rowTextColor,
                FontId = "White",
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
            var text = string.IsNullOrWhiteSpace(_searchText) ? "No targets" : "No matches";
            var textScale = 0.46f * scale * surface.FontSize;
            var textHeight = FormatingHelper.LineHeight(textScale, surface);

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textHeight * 0.5f),
                Color = GetThemeColor(Constants.ON_SURFACE),
                FontId = "White",
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
                if (cubeBlock != null && cubeBlock.BlockDefinition != null)
                    return TextureHelper.GetOrAddTextureForBlock(cubeBlock.BlockDefinition);
            }
            catch
            {
            }

            return MISSING_ICON_PLACEHOLDER;
        }

        string GetTypeSprite(Type type)
        {
            if (_gridLogic == null || type == null)
                return MISSING_ICON_PLACEHOLDER;

            var blocks = _gridLogic.GetTerminalBlocks<IMyTerminalBlock>();
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
                    return "Block";
                case PickActionTargetKind.Group:
                    return "Group";
                case PickActionTargetKind.BlockType:
                    return "Block Type";
                case PickActionTargetKind.BlockSubtype:
                    return "Block Subtype";
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

        static string TrimText(string text, float availableWidth, float fontSize, IMyTextSurface surface)
        {
            if (string.IsNullOrEmpty(text) || availableWidth <= 0f || surface == null)
                return string.Empty;

            var size = FormatingHelper.GetSizeInPixel(text, "White", fontSize, surface);
            if (size.X <= availableWidth)
                return text;

            return FormatingHelper.TrimName(text, Math.Max(1, (int)(text.Length * availableWidth / Math.Max(1f, size.X))));
        }


        ControlStyle GetComboStyle()
        {
            if (_comboStyle == null)
                _comboStyle = Button.CreatePrimaryButtonStyle(ParentTheme);
            else
                _comboStyle.ThemeColors = ParentTheme;

            return _comboStyle;
        }

        ControlStyle GetComboOptionStyle()
        {
            if (_comboOptionStyle == null)
                _comboOptionStyle = Button.CreatePrimaryButtonStyle(ParentTheme);
            else
                _comboOptionStyle.ThemeColors = ParentTheme;

            return _comboOptionStyle;
        }


        ControlStyle GetSearchStyle()
        {
            if (_searchStyle == null)
                _searchStyle = Button.CreatePrimaryButtonStyle(ParentTheme);
            else
                _searchStyle.ThemeColors = ParentTheme;

            return _searchStyle;
        }

        ControlStyle GetRowStyle()
        {
            if (_rowStyle == null)
                _rowStyle = Button.CreatePrimaryButtonStyle(ParentTheme);
            else
                _rowStyle.ThemeColors = ParentTheme;

            return _rowStyle;
        }

        void OnComboClicked(ButtonModel model, object sender)
        {
            _comboOpen = !_comboOpen;
            _requestRedraw?.Invoke();
        }

        void OnComboOptionClicked(ButtonModel model, object sender)
        {
            var option = model as ComboOptionButtonModel;
            if (option == null)
                return;

            if (_kind != option.Kind)
            {
                _kind = option.Kind;
                _itemsLoaded = false;
                _searchText = string.Empty;
                if (_searchInputModel != null)
                    _searchInputModel.Value = string.Empty;
            }

            _comboOpen = false;
            _requestRedraw?.Invoke();
        }

        void OnSearchChanged(string value)
        {
            _searchText = value ?? string.Empty;
            ApplyFilter();
            _requestRedraw?.Invoke();
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
            for (var i = 0; i < _comboOptionButtons.Count; i++)
                _comboOptionButtons[i].SetVisible(false);
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

        sealed class ComboOptionButtonModel : ButtonModel
        {
            public PickActionTargetKind Kind { get; set; }
        }
    }
}
