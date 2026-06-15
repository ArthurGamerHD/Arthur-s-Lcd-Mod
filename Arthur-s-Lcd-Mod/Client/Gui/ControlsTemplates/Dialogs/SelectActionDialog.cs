#if EXPERIMENTAL
using LcdMod.Client.Terminal.Actions;
using LcdMod.Client.Terminal.Models.Actions;
using LcdMod.Client.Terminal.Models.Property;
using Sandbox.ModAPI.Interfaces;
#endif
using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Models;
using LcdMod.Common.Helpers;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using IMyBlockGroup = Sandbox.ModAPI.Ingame.IMyBlockGroup;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using IMyIngameTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
{
    sealed class SelectActionDialog : Dialog
    {
        const string TITLE = "Select Action";
        const string SEARCH_TITLE = "Search Action";
        const string SEARCH_PLACEHOLDER = "Search actions";
        const string MISSING_ICON_PLACEHOLDER = "MissingIcon";
        const string STRING_INPUT_ICON = "StringInput";
        const string COLOR_INPUT_ICON = "ColorInput";
        const string NUMBER_INPUT_ICON = "NumberInput";
        const string BOOLEAN_INPUT_ICON = "BooleanInput";

        const float CARD_WIDTH_PERCENT = 0.76f;
        const float CARD_HEIGHT_PERCENT = 0.78f;
        const float MIN_CARD_WIDTH_PIXELS = 330f;
        const float MIN_CARD_HEIGHT_PIXELS = 275f;
        const float SEARCH_HEIGHT_PIXELS = 38f;
        const float ROW_HEIGHT_PIXELS = 40f;
        const float ROW_GAP_PIXELS = 3f;

        readonly GridLogic _gridLogic;
        readonly ButtonPanelTargetSettings _target;
        readonly ButtonPanelActionSettings _initialSelection;
        readonly Action<ButtonPanelActionSettings> _selectedCallback;
        readonly Action _cancelCallback;
        readonly Action _requestRedraw;
        readonly List<ButtonPanelActionSettings> _allItems = new List<ButtonPanelActionSettings>();
        readonly List<ButtonPanelActionSettings> _filteredItems = new List<ButtonPanelActionSettings>();
        readonly List<Button> _rowButtons = new List<Button>();
        readonly ScrollPanel _scrollPanel = new ScrollPanel();
        readonly List<IMyBlockGroup> _groups = new List<IMyBlockGroup>();
        readonly List<IMyIngameTerminalBlock> _groupBlocks = new List<IMyIngameTerminalBlock>();

        TextInput _searchInput;
        TextInputModel _searchInputModel;

        string _searchText = string.Empty;
        bool _itemsLoaded;

        public SelectActionDialog(
            IApp parentApp,
            GridLogic gridLogic,
            ButtonPanelTargetSettings target,
            ButtonPanelActionSettings initialSelection,
            Action<ButtonPanelActionSettings> selectedCallback,
            Action cancelCallback,
            Action requestRedraw)
            : base(parentApp)
        {
            _gridLogic = gridLogic;
            _target = target == null ? null : target.Clone();
            _initialSelection = initialSelection == null ? null : initialSelection.Clone();
            _selectedCallback = selectedCallback;
            _cancelCallback = cancelCallback;
            _requestRedraw = requestRedraw;
            OnClose = delegate
            {
                if (_cancelCallback != null)
                    _cancelCallback();
            };

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

            var context = CreateRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);
            var layoutScale = scale * fontScale;
            var padding = new Vector2(18f * scale, 14f * scale);
            var spacing = 10f * scale;
            var titleScale = 0.82f * layoutScale;
            var titleHeight = FormatingHelper.LineHeight(titleScale, surface);
            var inputTextScale = 0.58f * layoutScale;
            var searchHeight = Math.Max(SEARCH_HEIGHT_PIXELS * scale, FormatingHelper.LineHeight(inputTextScale, surface) + 18f * scale);
            var closeSize = GetDialogCloseButtonSize(scale);
            var headerHeight = Math.Max(titleHeight, closeSize.Y);

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
                Color = ResolveColor(ThemeResources.OnSurfaceColor),
                FontId = "White",
                RotationOrScale = titleScale,
                Alignment = TextAlignment.CENTER
            });

            var searchRect = new RectangleF(
                cardRect.X + padding.X,
                cardRect.Y + padding.Y + headerHeight + spacing,
                Math.Max(1f, cardRect.Width - padding.X * 2f),
                searchHeight);

            var listRect = new RectangleF(
                cardRect.X + padding.X,
                searchRect.Bottom + spacing,
                Math.Max(1f, cardRect.Width - padding.X * 2f),
                Math.Max(0f, cardRect.Bottom - padding.Y - searchRect.Bottom - spacing));

            EnsureSearchInput(searchRect);
            ContainerControl.AddChild(_searchInput);
            _searchInput.Render(context, Sprites);

            RenderActionList(context, listRect, scale, surface);
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
                ResolveColor(ThemeResources.ShadowColor), radiusScale: scale);
            Border.CreateSpritesFromRect(cardRect, Sprites,
                ResolveColor(ThemeResources.SurfaceContainerHighColor), radiusScale: scale);
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
            if (_target == null)
                return;

#if EXPERIMENTAL
            foreach (var entry in ActionHelper.CustomActions)
            {
                var action = entry.Value;
                if (action == null || string.IsNullOrEmpty(action.BaseId))
                    continue;

                if (!IsActionCompatible(action))
                    continue;

                _allItems.Add(new ButtonPanelActionSettings
                {
                    BaseId = action.BaseId,
                    DisplayName = string.IsNullOrWhiteSpace(action.Name) ? action.BaseId : action.Name,
                    ActionTypeName = action.GetType().FullName,
                    SpriteName = GetActionSpriteName(action)
                });
            }
#endif

            _allItems.Sort(CompareActions);
        }

#if EXPERIMENTAL
        static string GetActionSpriteName(ICustomAction action)
        {
            if (action is PropertyCustomAction<bool> ||
                action is OnOffAction)
                return BOOLEAN_INPUT_ICON;

            if (action is PropertyCustomAction<string> ||
                action is PropertyCustomAction<StringBuilder>)
                return STRING_INPUT_ICON;

            if (action is PropertyCustomAction<Color>)
                return COLOR_INPUT_ICON;

            if (IsNumericPropertyAction(action))
                return NUMBER_INPUT_ICON;

            return MISSING_ICON_PLACEHOLDER;
        }

        static bool IsNumericPropertyAction(ICustomAction action)
        {
            return action is PropertyCustomAction<byte> ||
                   action is PropertyCustomAction<sbyte> ||
                   action is PropertyCustomAction<short> ||
                   action is PropertyCustomAction<ushort> ||
                   action is PropertyCustomAction<int> ||
                   action is PropertyCustomAction<uint> ||
                   action is PropertyCustomAction<long> ||
                   action is PropertyCustomAction<ulong> ||
                   action is PropertyCustomAction<float> ||
                   action is PropertyCustomAction<double> ||
                   action is PropertyCustomAction<decimal>;
        }

        bool IsActionCompatible(ICustomAction action)
        {
            if (action == null || _target == null)
                return false;

            switch ((PickActionTargetKind)_target.Kind)
            {
                case PickActionTargetKind.Block:
                    return IsActionCompatibleWithBlock(action, FindBlock(_target.Id));
                case PickActionTargetKind.Group:
                    return IsActionCompatibleWithGroup(action, _target.Id);
                case PickActionTargetKind.BlockType:
                    return IsActionCompatibleWithTypeTarget(action, FindRegisteredType(_target.TypeName ?? _target.Id));
                case PickActionTargetKind.BlockSubtype:
                    return IsActionCompatibleWithSubtype(action, _target.Id);
                default:
                    return false;
            }
        }

        bool IsActionCompatibleWithGroup(ICustomAction action, string groupName)
        {
            if (_gridLogic == null || _gridLogic.Grid == null || string.IsNullOrEmpty(groupName))
                return false;

            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(_gridLogic.Grid);
            if (terminalSystem == null)
                return false;

            _groups.Clear();
            terminalSystem.GetBlockGroups(_groups);
            for (var i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                if (group == null || !string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase))
                    continue;

                _groupBlocks.Clear();
                group.GetBlocks(_groupBlocks);
                for (var blockIndex = 0; blockIndex < _groupBlocks.Count; blockIndex++)
                {
                    var groupBlock = _groupBlocks[blockIndex];
                    if (IsActionCompatibleWithBlock(action, groupBlock))
                        return true;
                }
            }

            return false;
        }

        bool IsActionCompatibleWithSubtype(ICustomAction action, string subtype)
        {
            if (_gridLogic == null || string.IsNullOrEmpty(subtype))
                return false;

            var blocks = _gridLogic.GetTerminalBlocks<IMyTerminalBlock>();
            if (blocks == null)
                return false;

            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null)
                    continue;

                var cubeBlock = block as MyCubeBlock;
                if (cubeBlock == null || cubeBlock.BlockDefinition == null)
                    continue;

                if (!string.Equals(cubeBlock.BlockDefinition.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsActionCompatibleWithBlock(action, block))
                    return true;
            }

            return false;
        }

        bool IsActionCompatibleWithBlock(ICustomAction action, IMyIngameTerminalBlock block)
        {
            if (block == null)
                return false;

            return IsActionCompatibleWithType(action, block.GetType()) && IsActionVisibleForBlock(action, block);
        }

        bool IsActionCompatibleWithTypeTarget(ICustomAction action, Type targetType)
        {
            if (action == null || targetType == null || !IsActionCompatibleWithType(action, targetType))
                return false;

            if (_gridLogic == null)
                return false;

            var blocks = _gridLogic.GetTerminalBlocks<IMyTerminalBlock>();
            if (blocks == null)
                return false;

            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null)
                    continue;

                var blockType = block.GetType();
                if (!IsTypeMatch(targetType, blockType))
                    continue;

                if (IsActionCompatibleWithBlock(action, block))
                    return true;
            }

            return false;
        }

        bool IsTypeMatch(Type expectedType, Type actualType)
        {
            if (expectedType == null || actualType == null)
                return false;

            if (string.Equals(expectedType.FullName, actualType.FullName, StringComparison.Ordinal))
                return true;

            return MyAPIGateway.Reflection.IsAssignableFrom(expectedType, actualType) ||
                   MyAPIGateway.Reflection.IsAssignableFrom(actualType, expectedType);
        }

        bool IsActionVisibleForBlock(ICustomAction action, IMyIngameTerminalBlock block)
        {
            if (action == null || block == null)
                return false;
            
            return action.Enabled(block);
        }
        
        bool IsActionCompatibleWithType(ICustomAction action, Type targetType)
        {
            if (action == null || targetType == null || action.Types == null)
                return false;

            foreach (var actionType in action.Types)
            {
                if (actionType == null)
                    continue;

                if (string.Equals(actionType.FullName, targetType.FullName, StringComparison.Ordinal))
                    return true;

                if (MyAPIGateway.Reflection.IsAssignableFrom(actionType, targetType) ||
                    MyAPIGateway.Reflection.IsAssignableFrom(targetType, actionType))
                    return true;
            }

            return false;
        }

        Type FindRegisteredType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            foreach (var type in ActionHelper.Types)
            {
                if (type == null)
                    continue;

                if (string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
                    string.Equals(type.Name, typeName, StringComparison.Ordinal))
                    return type;
            }

            return null;
        }
#endif

        IMyTerminalBlock FindBlock(string id)
        {
            long entityId;
            if (_gridLogic == null || !long.TryParse(id, out entityId))
                return null;

            var blocks = _gridLogic.GetTerminalBlocks<IMyTerminalBlock>();
            if (blocks == null)
                return null;

            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block != null && block.EntityId == entityId)
                    return block;
            }

            return null;
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
                var id = item.BaseId ?? string.Empty;
                if (displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _filteredItems.Add(item);
                }
            }
        }

        void EnsureSearchInput(RectangleF rect)
        {
            if (_searchInputModel == null)
                _searchInputModel = new TextInputModel
                {
                    Title = SEARCH_TITLE,
                    Subtitle = "Filter actions containing this text",
                    Placeholder = SEARCH_PLACEHOLDER,
                    Value = _searchText,
                    ValueChanged = OnSearchChanged
                };

            _searchInputModel.Title = SEARCH_TITLE;
            _searchInputModel.Subtitle = "Filter actions containing this text";
            _searchInputModel.Placeholder = SEARCH_PLACEHOLDER;
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

        void RenderActionList(ControlRenderContext context, RectangleF listRect, float scale, IMyTextSurface surface)
        {
            HideUnusedRows(0);
            if (listRect.Width <= 1f || listRect.Height <= 1f)
                return;

            Border.CreateSpritesFromRect(listRect, Sprites, ResolveColor(ThemeResources.SurfaceContainerHighColor), radiusScale: scale);

            var rowHeight = GetRowHeight(scale);
            var scrollerWidth = Math.Min(_scrollPanel.AutomaticScrollerWidthPixels * scale, Math.Max(0f, listRect.Width * 0.25f));
            _scrollPanel.ClearChildren();
            _scrollPanel.Configure(listRect, listRect.Y, 0f, rowHeight, _filteredItems.Count, scrollerWidth, 0f);
            _scrollPanel.SetScrollBarColors(ResolveColor(ThemeResources.SurfaceContainerHighColor), ResolveColor(ThemeResources.OnSurfaceColor));
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

        static float GetRowHeight(float scale)
        {
            return Math.Max(ROW_HEIGHT_PIXELS * scale, 32f + 8f * Math.Max(1f, scale));
        }

        Button GetRowButton(int index)
        {
            while (_rowButtons.Count <= index)
            {
                var button = new Button(default(RectangleF), new ActionRowButtonModel { Clicked = OnActionClicked });
                button.CustomRender = RenderActionRow;
                _rowButtons.Add(button);
            }

            return _rowButtons[index];
        }

        void ConfigureRowButton(Button button, RectangleF rect, ButtonPanelActionSettings action)
        {
            var model = button.DataContext as ActionRowButtonModel;
            if (model == null)
            {
                model = new ActionRowButtonModel();
                button.SetDataContext(model);
            }

            model.Action = action;
            model.Text = action == null ? string.Empty : action.DisplayName;
            model.Enabled = true;
            model.Clicked = OnActionClicked;

            button.SetRect(rect);
            button.SetClass("ControlBase Button Row");
            button.SetStyleId(null);
            button.SetCursor(CursorType.Hand);
            button.CustomRender = RenderActionRow;
            button.SetVisible(true);
        }

        void RenderActionRow(ControlTemplate control, ControlRenderContext context, List<MySprite> sprites)
        {
            var model = control.DataContext as ActionRowButtonModel;
            var action = model == null ? null : model.Action;
            if (action == null)
                return;

            var rect = control.Bounds;
            var hovered = rect.Contains(context.CursorPosition);
            var selected = _initialSelection != null &&
                           string.Equals(_initialSelection.BaseId, action.BaseId, StringComparison.OrdinalIgnoreCase);
            control.SetStyleId(selected ? "Primary" : null);
            var panelColor = selected
                ? ResolveColor(ThemeResources.AccentContainerColor)
                : control.BackgroundColor;
            var textColor = selected
                ? ResolveColor(ThemeResources.OnAccentContainerColor)
                : control.TextColor;

            Border.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: context.Scale);

            var iconSize = Math.Min(64f, Math.Max(1f, rect.Height - 1f * Math.Max(1f, context.Scale)));
            iconSize = Math.Max(1f, Math.Min(iconSize, rect.Width));
            var iconRect = new RectangleF(rect.X + 4f * context.Scale, rect.Center.Y - iconSize * 0.5f, iconSize, iconSize);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = string.IsNullOrEmpty(action.SpriteName) ? MISSING_ICON_PLACEHOLDER : action.SpriteName,
                Position = iconRect.Center,
                Size = iconRect.Size,
                Color = Constants.ColorCorrection,
                Alignment = TextAlignment.CENTER
            });

            var textScale = 0.46f * context.Scale * context.FontScale;
            var textHeight = FormatingHelper.LineHeight(textScale, context.Surface);
            var textX = iconRect.Right + 8f * context.Scale;
            var textWidth = Math.Max(0f, rect.Right - textX - 6f * context.Scale);
            var displayName = TrimText(action.DisplayName ?? action.BaseId, textWidth, textScale, context.Surface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = displayName,
                Position = new Vector2(textX, rect.Center.Y - textHeight * 0.5f),
                Color = textColor,
                FontId = "White",
                RotationOrScale = textScale,
                Alignment = TextAlignment.LEFT
            });
        }

        void HideUnusedRows(int usedControls)
        {
            for (var i = usedControls; i < _rowButtons.Count; i++)
                _rowButtons[i].SetVisible(false);
        }

        void DrawEmptyMessage(RectangleF rect, float scale, IMyTextSurface surface)
        {
            var text = string.IsNullOrWhiteSpace(_searchText) ? "No compatible actions" : "No matches";
            var textScale = 0.46f * scale * surface.FontSize;
            var textHeight = FormatingHelper.LineHeight(textScale, surface);
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textHeight * 0.5f),
                Color = ResolveColor(ThemeResources.OnSurfaceColor),
                FontId = "White",
                RotationOrScale = textScale,
                Alignment = TextAlignment.CENTER
            });
        }

        void OnSearchChanged(string value)
        {
            _searchText = value ?? string.Empty;
            ApplyFilter();
            _requestRedraw?.Invoke();
        }

        void OnActionClicked(ButtonModel model, object sender)
        {
            var rowModel = model as ActionRowButtonModel;
            if (rowModel == null || rowModel.Action == null)
                return;

            Dismiss();
            if (_selectedCallback != null)
                _selectedCallback(rowModel.Action.Clone());
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

        static int CompareActions(ButtonPanelActionSettings a, ButtonPanelActionSettings b)
        {
            if (a == null && b == null)
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;
            return string.Compare(a.DisplayName ?? a.BaseId, b.DisplayName ?? b.BaseId, StringComparison.CurrentCultureIgnoreCase);
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

        static void BeginClip(List<MySprite> sprites, RectangleF bounds)
        {
            sprites.Add(MySprite.CreateClipRect(new Rectangle(
                (int)Math.Floor(bounds.X),
                (int)Math.Floor(bounds.Y),
                (int)Math.Ceiling(bounds.Width),
                (int)Math.Ceiling(bounds.Height))));
        }

        static void EndClip(List<MySprite> sprites)
        {
            sprites.Add(MySprite.CreateClearClipRect());
        }

        sealed class ActionRowButtonModel : ButtonModel
        {
            public ButtonPanelActionSettings Action { get; set; }
        }
    }
}
