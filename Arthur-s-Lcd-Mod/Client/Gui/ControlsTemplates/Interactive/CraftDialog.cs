using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Lists;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.WrappedGrid;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    sealed class CraftDialog : Dialog
    {
        readonly GridLogic _gridLogic;
        readonly Action<Dialog> _showDialog;
        readonly List<CraftRequest> _requests = new List<CraftRequest>();
        readonly List<CraftAssemblerOption> _assemblerOptions = new List<CraftAssemblerOption>();
        readonly List<CraftAssemblerOption> _selectedAssemblers = new List<CraftAssemblerOption>();
        readonly bool _useRequestGrid;

        NumericUpDownModel _amountModel;
        NumericUpDown _amountControl;
        ControlStyle _amountControlStyle;
        RectangleControl _assemblerControl;
        Button _craftButton;
        Button _cancelButton;

        public sealed class CraftRequest
        {
            public readonly MyItemType ItemType;
            public readonly MyDefinitionId DefinitionId;
            public readonly string Name;
            public readonly string Icon;
            public readonly int Amount;

            public CraftRequest(MyItemType itemType, string name, string icon, double amount)
            {
                ItemType = itemType;
                DefinitionId = itemType;
                Name = string.IsNullOrEmpty(name) ? itemType.SubtypeId : name;
                Icon = icon;
                Amount = Math.Max(1, (int)Math.Ceiling(amount));
            }
        }

        public CraftDialog(
            IApp parentApp,
            GridLogic gridLogic,
            MyItemType itemType,
            string itemName,
            string itemIcon,
            double defaultAmount,
            Action<Dialog> showDialog)
            : this(parentApp, gridLogic, CreateSingleRequest(itemType, itemName, itemIcon, defaultAmount),
                showDialog, false)
        {
        }

        public CraftDialog(
            IApp parentApp,
            GridLogic gridLogic,
            IEnumerable<CraftRequest> requests,
            Action<Dialog> showDialog)
            : this(parentApp, gridLogic, requests, showDialog, true)
        {
        }

        CraftDialog(
            IApp parentApp,
            GridLogic gridLogic,
            IEnumerable<CraftRequest> requests,
            Action<Dialog> showDialog,
            bool useRequestGrid)
            : base(parentApp)
        {
            _gridLogic = gridLogic;
            _showDialog = showDialog;
            _useRequestGrid = useRequestGrid;
            AddRequests(requests);

            BuildAssemblerOptions();
            SelectDefaultAssemblers();
            if (!_useRequestGrid && _requests.Count > 0)
            {
                var request = _requests[0];
                _amountModel = new NumericUpDownModel
                {
                    Value = request.Amount,
                    MinValue = 1d,
                    MaxValue = 1000000d,
                    Format = "0",
                    Step = 1d,
                    Title = Loc("LcdMod_CraftDialog_Title"),
                    Subtitle = request.Name
                };
            }
        }

        static List<CraftRequest> CreateSingleRequest(MyItemType itemType, string itemName, string itemIcon,
            double defaultAmount)
        {
            return new List<CraftRequest> { new CraftRequest(itemType, itemName, itemIcon, defaultAmount) };
        }

        void AddRequests(IEnumerable<CraftRequest> requests)
        {
            if (requests == null)
                return;

            var seen = new HashSet<MyDefinitionId>();
            foreach (var request in requests)
            {
                if (request == null || request.Amount <= 0 || seen.Contains(request.DefinitionId))
                    continue;

                seen.Add(request.DefinitionId);
                _requests.Add(request);
            }
        }

        protected override void RenderCore(
            InteractiveSurfaceScript owner,
            RectangleF viewBox,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            Color textColor,
            Color backgroundColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            var container = EnsureContainer(viewBox);
            container.ClearChildren();

            DrawBackdrop(surface);

            var padding = new Vector2(18f, 14f) * scale;
            var spacing = 10f * scale;
            var titleScale = 0.82f * scale * fontScale;
            var labelScale = 0.54f * scale * fontScale;
            var nameScale = 0.66f * scale * fontScale;
            var buttonScale = 0.58f * scale * fontScale;
            var cardColor = GetThemeColor(Constants.SURFACE_CONTAINER_HIGH);
            var cardTextColor = GetThemeColor(Constants.ON_SURFACE);

            var cardWidth = _useRequestGrid
                ? Math.Min(viewBox.Width - padding.X * 2f, Math.Max(460f * scale, viewBox.Width * 0.82f))
                : Math.Min(viewBox.Width - padding.X * 2f, Math.Max(360f * scale, viewBox.Width * 0.62f));
            var cardHeight = _useRequestGrid
                ? Math.Min(viewBox.Height - padding.Y * 2f, Math.Max(310f * scale, viewBox.Height * 0.72f))
                : Math.Min(viewBox.Height - padding.Y * 2f, Math.Max(235f * scale, viewBox.Height * 0.52f));
            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth,
                cardHeight);

            var shadowColor = GetThemeColor(Constants.SHADOW);
            Border.CreateSpritesFromRect(new RectangleF(cardRect.Position + 2f, cardRect.Size), Sprites, shadowColor,
                radiusScale: scale);
            Border.CreateSpritesFromRect(cardRect, Sprites, cardColor,
                radiusScale: scale);

            var renderContext = CreateRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);

            var title = _useRequestGrid ? "Craft all" : Loc("LcdMod_CraftDialog_Title");
            var titleSize = FormatingHelper.GetSizeInPixel(title, "White", titleScale, surface);
            var currentY = cardRect.Y + padding.Y;
            DrawText(title, new Vector2(cardRect.Center.X, currentY), titleScale, cardTextColor, TextAlignment.CENTER);
            currentY += titleSize.Y + spacing * 0.7f;

            var assemblerRect = new RectangleF(cardRect.X + padding.X, currentY, cardRect.Width - padding.X * 2f,
                Math.Max(24f * scale, FormatingHelper.LineHeight(labelScale, surface) + 8f * scale));
            EnsureAssemblerControl(assemblerRect);
            container.AddChild(_assemblerControl);
            _assemblerControl.Render(renderContext, Sprites);

            currentY = assemblerRect.Bottom + spacing;

            var buttonHeight = Math.Max(26f * scale, FormatingHelper.LineHeight(buttonScale, surface) + 10f * scale);
            var buttonsTop = cardRect.Bottom - padding.Y - buttonHeight;
            var contentBottom = buttonsTop - spacing;
            var contentHeight = Math.Max(0f, contentBottom - currentY);
            var contentWidth = cardRect.Width - padding.X * 2f;
            if (_useRequestGrid)
            {
                var gridRect = new RectangleF(cardRect.X + padding.X, currentY, contentWidth, contentHeight);
                DrawRequestGrid(gridRect, scale, fontScale, surface, cardTextColor);
            }
            else if (_requests.Count > 0)
            {
                var request = _requests[0];
                var amountHeight = Math.Max(30f * scale, Math.Min(42f * scale, contentHeight * 0.35f));
                var itemRowsHeight = Math.Max(0f, contentHeight - amountHeight - spacing);
                itemRowsHeight = Math.Min(itemRowsHeight,
                    Math.Max(56f * scale, FormatingHelper.LineHeight(nameScale, surface) * 2f + 4f * scale));

                var iconSize = Math.Min(Math.Max(44f * scale, itemRowsHeight), 72f * scale);
                var iconRect = new RectangleF(cardRect.X + padding.X, currentY + (itemRowsHeight - iconSize) * 0.5f,
                    iconSize, iconSize);
                var rightX = iconRect.Right + 16f * scale;
                var rightWidth = Math.Max(0f, cardRect.Right - padding.X - rightX);
                var nameHeight = itemRowsHeight;

                DrawItemIcon(iconRect, request.Icon);

                var nameText = TrimText(request.Name, rightWidth, nameScale, surface);
                var nameY = currentY + Math.Max(0f,
                    (nameHeight - FormatingHelper.GetSizeInPixel(nameText, "White", nameScale, surface).Y) * 0.5f);
                DrawText(nameText, new Vector2(rightX, nameY), nameScale, cardTextColor, TextAlignment.LEFT);

                if (_amountModel != null)
                {
                    var amountTop = Math.Min(contentBottom - amountHeight, currentY + itemRowsHeight + spacing);
                    var amountRect = new RectangleF(cardRect.X + padding.X, amountTop, contentWidth, amountHeight);
                    EnsureAmountControl(amountRect);
                    ConfigureAmountControlStyle();
                    container.AddChild(_amountControl);
                    _amountControl.Render(renderContext, Sprites);
                }
            }

            var buttonSpacing = 12f * scale;
            var buttonWidth = Math.Max(92f * scale, (cardRect.Width - padding.X * 2f - buttonSpacing) * 0.5f);
            var buttonsWidth = buttonWidth * 2f + buttonSpacing;
            var craftRect = new RectangleF(cardRect.Center.X - buttonsWidth * 0.5f, buttonsTop, buttonWidth, buttonHeight);
            var cancelRect = new RectangleF(craftRect.Right + buttonSpacing, buttonsTop, buttonWidth, buttonHeight);

            EnsureButtons(craftRect, cancelRect);
            container.AddChild(_craftButton);
            container.AddChild(_cancelButton);

            ConfigureButton(_craftButton, _useRequestGrid ? "Craft all" : Loc("LcdMod_CraftDialog_Button_Craft"),
                buttonScale, panelColor, textColor, ThemedParentApp, owner, CanCraft());
            ConfigureButton(_cancelButton, Loc("LcdMod_Common_Button_Cancel"), buttonScale, panelColor, textColor, ThemedParentApp, owner, true);
            _craftButton.Render(renderContext, Sprites);
            _cancelButton.Render(renderContext, Sprites);
        }

        void DrawBackdrop(Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            Sprites.Add(new MySprite(SpriteType.TEXTURE,
                "SquareSimple",
                surface.TextureSize / 2f,
                surface.TextureSize,
                new Color(0, 0, 0, 128)));
        }

        void DrawItemIcon(RectangleF rect, string icon)
        {
            if (string.IsNullOrEmpty(icon))
            {
                Sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Danger",
                    Position = rect.Center,
                    Size = rect.Size,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER
                });
                return;
            }

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = icon,
                Position = rect.Center,
                Size = rect.Size,
                Color = Color.White,
                Alignment = TextAlignment.CENTER
            });
        }

        void DrawRequestGrid(RectangleF rect, float scale, float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface, Color textColor)
        {
            if (_requests.Count == 0 || rect.Width <= 0f || rect.Height <= 0f)
                return;

            var minColumnWidth = Math.Max(90f * scale, 1f);
            var columns = Math.Max(1, (int)Math.Floor(rect.Width / minColumnWidth));
            columns = Math.Min(columns, _requests.Count);

            var rows = Math.Max(1, (_requests.Count + columns - 1) / columns);
            var rowHeight = rect.Height / rows;
            rowHeight = Math.Max(1f, Math.Min(58f * scale, rowHeight));

            var grid = WrappedGrid.Create(rect, rowHeight, minColumnWidth, _requests.Count);
            var visibleCount = Math.Min(grid.VisibleCellCount, _requests.Count);
            var cellPadding = 4f * scale;
            var itemBackground = GetThemeColor(Constants.SURFACE_CONTAINER);
            var amountColor = GetThemeColor(Constants.ON_SURFACE_VARIANT);
            var nameScale = 0.42f * scale * fontScale;
            var amountScale = 0.36f * scale * fontScale;

            for (var i = 0; i < visibleCount; i++)
            {
                var cell = grid.GetCell(i);
                if (cell.ItemIndex < 0 || cell.ItemIndex >= _requests.Count)
                    continue;

                var request = _requests[cell.ItemIndex];
                var cellRect = new RectangleF(
                    cell.Bounds.X + cellPadding,
                    cell.Bounds.Y + cellPadding,
                    Math.Max(0f, cell.Bounds.Width - cellPadding * 2f),
                    Math.Max(0f, cell.Bounds.Height - cellPadding * 2f));

                if (cellRect.Width <= 0f || cellRect.Height <= 0f)
                    continue;

                Border.CreateSpritesFromRect(cellRect, Sprites, itemBackground, radiusScale: scale);

                var iconSize = Math.Max(0f, Math.Min(30f * scale, cellRect.Height - 8f * scale));
                var iconRect = new RectangleF(
                    cellRect.X + 6f * scale,
                    cellRect.Center.Y - iconSize * 0.5f,
                    iconSize,
                    iconSize);
                if (iconSize > 4f)
                    DrawItemIcon(iconRect, request.Icon);

                var textX = iconSize > 4f ? iconRect.Right + 6f * scale : cellRect.X + 6f * scale;
                var textWidth = Math.Max(0f, cellRect.Right - textX - 6f * scale);
                var name = TrimText(request.Name, textWidth, nameScale, surface);
                var amount = TrimText("x " + FormatingHelper.FormatItemQty(request.Amount), textWidth, amountScale, surface);
                var nameHeight = FormatingHelper.LineHeight(nameScale, surface);
                var amountHeight = FormatingHelper.LineHeight(amountScale, surface);
                var textTop = cellRect.Center.Y - (nameHeight + amountHeight) * 0.5f;

                DrawText(name, new Vector2(textX, textTop), nameScale, textColor, TextAlignment.LEFT);
                DrawText(amount, new Vector2(textX, textTop + nameHeight), amountScale, amountColor, TextAlignment.LEFT);
            }
        }

        void DrawText(string text, Vector2 position, float scale, Color color, TextAlignment alignment)
        {
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text ?? string.Empty,
                Position = position,
                Color = color,
                FontId = "White",
                Alignment = alignment,
                RotationOrScale = scale
            });
        }

        void EnsureAmountControl(RectangleF rect)
        {
            if (_amountControl == null)
                _amountControl = new NumericUpDown(rect, _amountModel);
            else
                _amountControl.SetRect(rect);

            _amountControl.SetVisible(true);
        }

        void ConfigureAmountControlStyle()
        {
            if (_amountControlStyle == null)
            {
                _amountControlStyle = ControlStyle.FromThemeRoles(
                    Constants.ON_PRIMARY_CONTAINER,
                    Constants.PRIMARY_CONTAINER,
                    Constants.PRIMARY_CONTAINER + Constants.HOVER,
                    Constants.ON_PRIMARY_CONTAINER,
                    ParentTheme);
                _amountControlStyle.BorderRadiusPixels = Border.DEFAULT_RADIUS_PIXELS;
            }

            _amountControlStyle.ThemeColors = ParentTheme;

            _amountControl.SetStyle(_amountControlStyle);
        }

        void EnsureAssemblerControl(RectangleF rect)
        {
            if (_assemblerControl == null)
            {
                _assemblerControl = new RectangleControl(rect, CursorType.Hand, this, OnAssemblerClicked);
            }
            else
            {
                _assemblerControl.SetRect(rect);
                _assemblerControl.SetDataContext(this);
            }

            _assemblerControl.SetCursor(_assemblerOptions.Count > 1 ? CursorType.Hand : CursorType.Default);
            _assemblerControl.SetOnClick(_assemblerOptions.Count > 1 ? (Action<object, object>)OnAssemblerClicked : null);
            _assemblerControl.SetVisible(true);
            _assemblerControl.CustomRender = RenderAssemblerControl;
        }

        void RenderAssemblerControl(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var hovered = _assemblerOptions.Count > 1 && rect.Contains(context.CursorPosition);
            var label = GetAssemblerSelectionLabel();
            var textScale = 0.54f * context.Scale * context.FontScale;
            var fill = hovered
                ? context.GetThemeColor(Constants.SURFACE + Constants.HOVER)
                : context.GetThemeColor(Constants.SURFACE_CONTAINER);
            var labelColor = context.GetThemeColor(Constants.ON_SURFACE);

            Border.CreateSpritesFromRect(rect, sprites, fill,
                radiusScale: context.Scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = TrimText(label, rect.Width - 12f * context.Scale, textScale, context.Surface),
                Position = new Vector2(rect.X + 6f * context.Scale, rect.Center.Y -
                    FormatingHelper.GetSizeInPixel(label, "White", textScale, context.Surface).Y * 0.5f),
                Color = labelColor,
                FontId = "White",
                Alignment = TextAlignment.LEFT,
                RotationOrScale = textScale
            });
        }

        void EnsureButtons(RectangleF craftRect, RectangleF cancelRect)
        {
            if (_craftButton == null)
                _craftButton = new Button(craftRect, new ButtonModel { Clicked = OnCraftClicked });
            else
                _craftButton.SetRect(craftRect);

            if (_cancelButton == null)
                _cancelButton = new Button(cancelRect, new ButtonModel { Clicked = OnCancelClicked });
            else
                _cancelButton.SetRect(cancelRect);

            _craftButton.SetVisible(true);
            _cancelButton.SetVisible(true);
        }

        internal static void ConfigureButton(
            Button button,
            string text,
            float textScale,
            Color panelColor,
            Color textColor,
            IThemedApp themedParentApp,
            InteractiveSurfaceScript owner,
            bool enabled)
        {
            var model = button.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = text;
                model.Enabled = enabled;
            }

            button.SetCursor(enabled ? CursorType.Hand : CursorType.Default);
            button.SetStyle(enabled
                ? Button.CreatePrimaryButtonStyle(themedParentApp?.Theme)
                : Button.CreateDisabledButtonStyle(themedParentApp?.Theme));
            button.CustomRender = delegate(ControlBase renderEntry, ControlRenderContext context, List<MySprite> sprites)
            {
                DrawButton(renderEntry.Bounds, owner, sprites, text, textScale, context, enabled);
            };
        }

        static void DrawButton(
            RectangleF rect,
            InteractiveSurfaceScript owner,
            List<MySprite> sprites,
            string text,
            float textScale,
            ControlRenderContext context,
            bool enabled)
        {
            var hover = enabled && rect.Contains(context.CursorPosition);
            var buttonColor = context.Style.GetPanelColor(hover);
            var buttonTextColor = context.Style.GetTextColor(hover);

            Border.CreateSpritesFromRect(rect, sprites, buttonColor,
                radiusScale: context.Scale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, rect.Center.Y -
                    FormatingHelper.GetSizeInPixel(text, "White", textScale, owner.Surface).Y * 0.5f),
                Color = buttonTextColor,
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });
        }

        void OnAssemblerClicked(object dataContext, object sender)
        {
            if (_showDialog == null || _assemblerOptions.Count <= 1)
                return;

            _showDialog(new AssemblerSelectionDialog(ParentApp, _assemblerOptions, _selectedAssemblers,
                delegate(List<CraftAssemblerOption> options)
                {
                    SetSelectedAssemblers(options);
                    _showDialog(this);
                },
                delegate { _showDialog(this); }));
        }

        bool CanCraft()
        {
            if (_requests.Count == 0 || _selectedAssemblers.Count == 0)
                return false;

            return _useRequestGrid || (_amountModel != null && _amountModel.Value > 0d);
        }

        void OnCraftClicked(ButtonModel model, object sender)
        {
            if (!CanCraft())
                return;

            try
            {
                if (_useRequestGrid)
                {
                    QueueAllCraft();
                }
                else if (_requests.Count > 0)
                {
                    var requestedItems = Math.Max(1, (int)Math.Ceiling(_amountModel.Value));
                    QueueSplitCraft(_requests[0], requestedItems);
                }

                Dismiss();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(CraftDialog));
            }
        }

        void OnCancelClicked(ButtonModel model, object sender)
        {
            Dismiss();
        }

        double CalculateQueueAmount(MyBlueprintDefinitionBase blueprint, MyDefinitionId itemDefinitionId,
            double requestedItems)
        {
            if (blueprint == null || blueprint.Results == null)
                return requestedItems;

            for (var i = 0; i < blueprint.Results.Length; i++)
            {
                if (!blueprint.Results[i].Id.Equals(itemDefinitionId))
                    continue;

                var resultAmount = (double)blueprint.Results[i].Amount;
                if (resultAmount <= 0d)
                    return requestedItems;

                return Math.Ceiling(requestedItems / resultAmount);
            }

            return requestedItems;
        }

        void QueueAllCraft()
        {
            for (var i = 0; i < _requests.Count; i++)
                QueueSplitCraft(_requests[i], _requests[i].Amount);
        }

        void QueueSplitCraft(CraftRequest request, int requestedItems)
        {
            var selected = GetCraftableSelectedAssemblers(request);
            if (requestedItems <= 0 || selected.Count == 0)
                return;

            var requestCount = Math.Round((double)requestedItems, MidpointRounding.AwayFromZero);
            
            var count = selected.Count;
            var baseShare = requestCount / count;
            var remainder = requestCount % count;

            for (var i = 0; i < count; i++)
            {
                var itemShare = baseShare + (i < remainder ? 1 : 0);
                if (itemShare <= 0)
                    continue;

                var option = selected[i];
                MyBlueprintDefinitionBase blueprint;
                if (!option.BlueprintsByItem.TryGetValue(request.DefinitionId, out blueprint) || blueprint == null)
                    continue;

                var queueAmount = CalculateQueueAmount(blueprint, request.DefinitionId, itemShare);
                option.Assembler.AddQueueItem(blueprint.Id, queueAmount);
            }
        }

        List<CraftAssemblerOption> GetCraftableSelectedAssemblers(CraftRequest request)
        {
            var selected = new List<CraftAssemblerOption>();
            if (request == null)
                return selected;

            for (var i = 0; i < _selectedAssemblers.Count; i++)
            {
                var option = _selectedAssemblers[i];
                if (option != null && option.Assembler != null &&
                    option.BlueprintsByItem.ContainsKey(request.DefinitionId))
                    selected.Add(option);
            }

            return selected;
        }

        void BuildAssemblerOptions()
        {
            _assemblerOptions.Clear();

            var assemblers = _gridLogic?.GetTerminalBlocks<IMyAssembler>();
            if (assemblers == null || assemblers.Count == 0)
                return;

            for (var i = 0; i < assemblers.Count; i++)
            {
                var assembler = assemblers[i];
                if (assembler == null)
                    continue;

                GridLogic.EnsureAssemblerBlueprintDatabase(assembler);
                var option = CreateAssemblerOption(assembler);
                if (option != null)
                    _assemblerOptions.Add(option);
            }

            _assemblerOptions.Sort(CompareAssemblerOptions);
        }

        CraftAssemblerOption CreateAssemblerOption(IMyAssembler assembler)
        {
            var assemblerSubtype = GridLogic.GetAssemblerSubtype(assembler);
            HashSet<MyDefinitionId> craftableBlueprints;
            if (string.IsNullOrEmpty(assemblerSubtype) ||
                !GridLogic.CraftableBlueprintsByAssemblerSubtype.TryGetValue(assemblerSubtype, out craftableBlueprints) ||
                craftableBlueprints == null)
                return null;

            var option = new CraftAssemblerOption
            {
                Assembler = assembler,
                DisplayName = GetAssemblerName(assembler),
                Idle = IsAssemblerIdle(assembler),
                Speed = GetAssemblerSpeed(assembler)
            };

            for (var i = 0; i < _requests.Count; i++)
            {
                var request = _requests[i];
                if (option.BlueprintsByItem.ContainsKey(request.DefinitionId))
                    continue;

                var blueprint = FindBestBlueprint(request.DefinitionId, craftableBlueprints);
                if (blueprint == null)
                    continue;

                option.BlueprintsByItem[request.DefinitionId] = blueprint;
            }

            return option.BlueprintsByItem.Count == 0 ? null : option;
        }

        static MyBlueprintDefinitionBase FindBestBlueprint(MyDefinitionId itemDefinitionId,
            HashSet<MyDefinitionId> craftableBlueprints)
        {
            HashSet<MyDefinitionId> blueprintsByItem;
            if (!GridLogic.BlueprintsByCreatedItem.TryGetValue(itemDefinitionId, out blueprintsByItem) ||
                blueprintsByItem == null || blueprintsByItem.Count == 0)
                return null;

            MyBlueprintDefinitionBase best = null;
            foreach (var blueprintId in blueprintsByItem)
            {
                if (!craftableBlueprints.Contains(blueprintId))
                    continue;

                var blueprint = MyDefinitionManager.Static.GetBlueprintDefinition(blueprintId);
                if (blueprint == null)
                    continue;

                if (best == null || CompareBlueprintChoice(blueprint, best) < 0)
                    best = blueprint;
            }

            return best;
        }

        void SelectDefaultAssemblers()
        {
            _selectedAssemblers.Clear();
            if (_useRequestGrid)
            {
                _selectedAssemblers.AddRange(_assemblerOptions.Where(a => a.Speed >= 1));
            }
            else if (_assemblerOptions.Count > 0)
            {
                _selectedAssemblers.Add(_assemblerOptions[0]);
            }
        }

        void SetSelectedAssemblers(List<CraftAssemblerOption> options)
        {
            _selectedAssemblers.Clear();

            if (options == null)
                return;

            for (var i = 0; i < _assemblerOptions.Count; i++)
            {
                var option = _assemblerOptions[i];
                if (option != null && options.Contains(option))
                    _selectedAssemblers.Add(option);
            }
        }

        string GetAssemblerSelectionLabel()
        {
            if (_selectedAssemblers.Count == 0)
                return Loc("LcdMod_CraftDialog_Assemblers_None");

            if (_selectedAssemblers.Count == 1)
                return FormatLoc("LcdMod_CraftDialog_Assemblers_Single", _selectedAssemblers[0].DisplayName);

            return FormatLoc("LcdMod_CraftDialog_Assemblers_SelectedCount", _selectedAssemblers.Count);
        }

        static int CompareAssemblerOptions(CraftAssemblerOption a, CraftAssemblerOption b)
        {
            if (a == null && b == null)
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            var idle = b.Idle.CompareTo(a.Idle);
            if (idle != 0)
                return idle;

            var speed = b.Speed.CompareTo(a.Speed);
            if (speed != 0)
                return speed;

            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture);
        }

        static int CompareBlueprintChoice(MyBlueprintDefinitionBase a, MyBlueprintDefinitionBase b)
        {
            if (a == null && b == null)
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            var primary = b.IsPrimary.CompareTo(a.IsPrimary);
            if (primary != 0)
                return primary;

            var priority = a.Priority.CompareTo(b.Priority);
            if (priority != 0)
                return priority;

            return string.Compare(a.Id.SubtypeName, b.Id.SubtypeName, StringComparison.CurrentCulture);
        }

        static string GetAssemblerName(IMyAssembler assembler)
        {
            var terminal = assembler as IMyTerminalBlock;
            if (terminal != null)
            {
                if (!string.IsNullOrWhiteSpace(terminal.CustomName))
                    return terminal.CustomName;
                if (!string.IsNullOrWhiteSpace(terminal.DisplayNameText))
                    return terminal.DisplayNameText;
            }

            var subtype = GridLogic.GetAssemblerSubtype(assembler);
            return string.IsNullOrEmpty(subtype) ? Loc("LcdMod_CraftDialog_Assembler_FallbackName") : subtype;
        }

        static bool IsAssemblerIdle(IMyAssembler assembler)
        {
            try
            {
                return assembler != null && assembler.IsQueueEmpty && !assembler.IsProducing;
            }
            catch
            {
                return false;
            }
        }

        static float GetAssemblerSpeed(IMyAssembler assembler)
        {
            try
            {
                var definition = MyDefinitionManager.Static.GetCubeBlockDefinition(assembler.BlockDefinition) as MyAssemblerDefinition;
                return definition?.AssemblySpeed ?? 1f;
            }
            catch
            {
                return 1f;
            }
        }

        static string TrimText(string text, float availableWidth, float fontSize,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            if (string.IsNullOrEmpty(text) || availableWidth <= 0f || surface == null)
                return string.Empty;

            var size = FormatingHelper.GetSizeInPixel(text, "White", fontSize, surface);
            if (size.X <= availableWidth)
                return text;

            return FormatingHelper.TrimName(text, Math.Max(1, (int)(text.Length * availableWidth / Math.Max(1f, size.X))));
        }

        static string Loc(string key)
        {
            return LocHelper.GetLoc(key);
        }

        static string FormatLoc(string key, object arg)
        {
            return string.Format(FormatingHelper.Culture, Loc(key), arg);
        }
    }

    sealed class AssemblerSelectionDialog : Dialog
    {
        readonly List<CraftAssemblerOption> _options;
        readonly List<CraftAssemblerOption> _selected;
        readonly Action<List<CraftAssemblerOption>> _selectedCallback;
        readonly Action _cancelCallback;

        ListBoxModel<CraftAssemblerOption> _listModel;
        ListBox<CraftAssemblerOption> _listBox;
        ControlStyle _listBoxStyle;
        Button _selectButton;
        Button _cancelButton;

        public AssemblerSelectionDialog(
            IApp parentApp,
            List<CraftAssemblerOption> options,
            List<CraftAssemblerOption> selected,
            Action<List<CraftAssemblerOption>> selectedCallback,
            Action cancelCallback)
            : base(parentApp)
        {
            _options = options ?? new List<CraftAssemblerOption>();
            _selected = selected == null
                ? new List<CraftAssemblerOption>()
                : new List<CraftAssemblerOption>(selected);
            _selectedCallback = selectedCallback;
            _cancelCallback = cancelCallback;
        }

        protected override void RenderCore(
            InteractiveSurfaceScript owner,
            RectangleF viewBox,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            Color textColor,
            Color backgroundColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            var container = EnsureContainer(viewBox);
            container.ClearChildren();

            Sprites.Add(new MySprite(SpriteType.TEXTURE,
                "SquareSimple",
                surface.TextureSize / 2f,
                surface.TextureSize,
                new Color(0, 0, 0, 128)));

            var padding = new Vector2(18f, 14f) * scale;
            var spacing = 10f * scale;
            var titleScale = 0.78f * scale * fontScale;
            var buttonScale = 0.58f * scale * fontScale;
            var cardColor = GetThemeColor(Constants.SURFACE_CONTAINER_HIGH);
            var cardTextColor = GetThemeColor(Constants.ON_SURFACE);
            var cardWidth = Math.Min(viewBox.Width - padding.X * 2f, Math.Max(320f * scale, viewBox.Width * 0.58f));
            var cardHeight = Math.Min(viewBox.Height - padding.Y * 2f, Math.Max(230f * scale, viewBox.Height * 0.5f));
            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth,
                cardHeight);

            Border.CreateSpritesFromRect(new RectangleF(cardRect.Position + 2f, cardRect.Size), Sprites,
                GetThemeColor(Constants.SHADOW), radiusScale: scale);
            Border.CreateSpritesFromRect(cardRect, Sprites, cardColor,
                radiusScale: scale);

            var title = LocHelper.GetLoc("LcdMod_CraftDialog_AssemblerSelection_Title");
            var titleSize = FormatingHelper.GetSizeInPixel(title, "White", titleScale, surface);
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = title,
                Position = new Vector2(cardRect.Center.X, cardRect.Y + padding.Y),
                Color = cardTextColor,
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = titleScale
            });

            var buttonHeight = Math.Max(26f * scale, FormatingHelper.LineHeight(buttonScale, surface) + 10f * scale);
            var listTop = cardRect.Y + padding.Y + titleSize.Y + spacing;
            var buttonTop = cardRect.Bottom - padding.Y - buttonHeight;
            var listRect = new RectangleF(cardRect.X + padding.X, listTop,
                cardRect.Width - padding.X * 2f, Math.Max(0f, buttonTop - spacing - listTop));

            EnsureList(listRect, 30f * scale);
            if (_listBoxStyle == null)
            {
                _listBoxStyle = ControlStyle.FromThemeRoles(
                    Constants.ON_SECONDARY_CONTAINER,
                    Constants.SECONDARY_CONTAINER,
                    Constants.SECONDARY_CONTAINER + Constants.HOVER,
                    Constants.ON_SECONDARY_CONTAINER,
                    ParentTheme);
                _listBoxStyle.BorderRadiusPixels = 0;
                _listBox.SetStyle(_listBoxStyle);
            }
            else
            {
                _listBoxStyle.ThemeColors = ParentTheme;
            }
            
            container.AddChild(_listBox);

            var renderContext = CreateRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);
            if (_options.Count > 0)
            {
                _listBox.Render(renderContext, Sprites);
            }
            else
            {
                Sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = LocHelper.GetLoc("LcdMod_CraftDialog_AssemblerSelection_Empty"),
                    Position = new Vector2(listRect.Center.X, listRect.Center.Y - titleSize.Y * 0.5f),
                    Color = cardTextColor,
                    FontId = "White",
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = 0.58f * scale * fontScale
                });
            }

            var buttonSpacing = 12f * scale;
            var buttonWidth = Math.Max(92f * scale, (cardRect.Width - padding.X * 2f - buttonSpacing) * 0.5f);
            var buttonsWidth = buttonWidth * 2f + buttonSpacing;
            var selectRect = new RectangleF(cardRect.Center.X - buttonsWidth * 0.5f, buttonTop, buttonWidth, buttonHeight);
            var cancelRect = new RectangleF(selectRect.Right + buttonSpacing, buttonTop, buttonWidth, buttonHeight);

            EnsureButtons(selectRect, cancelRect);
            container.AddChild(_selectButton);
            container.AddChild(_cancelButton);
            CraftDialog.ConfigureButton(_selectButton, LocHelper.GetLoc("LcdMod_Common_Button_Select"), buttonScale, panelColor, textColor, ThemedParentApp, owner, HasSelection());
            CraftDialog.ConfigureButton(_cancelButton, LocHelper.GetLoc("LcdMod_Common_Button_Cancel"), buttonScale, panelColor, textColor, ThemedParentApp, owner, true);
            _selectButton.Render(renderContext, Sprites);
            _cancelButton.Render(renderContext, Sprites);
        }

        void EnsureList(RectangleF rect, float rowHeight)
        {
            if (_listModel == null)
            {
                _listModel = new ListBoxModel<CraftAssemblerOption>
                {
                    Items = _options,
                    SelectedEntries = new List<CraftAssemblerOption>(_selected),
                    MultiSelect = true,
                    TextSelector = FormatOption,
                };
            }

            _listModel.RowHeight = rowHeight;

            if (_listBox == null)
                _listBox = new ListBox<CraftAssemblerOption>(rect, _listModel);
            else
                _listBox.SetRect(rect);
            
            _listBox.SetVisible(_options.Count > 0);
            if (_listBox.Style != null) 
                _listBox.Style.BorderRadiusPixels = 0;
        }

        void EnsureButtons(RectangleF selectRect, RectangleF cancelRect)
        {
            if (_selectButton == null)
                _selectButton = new Button(selectRect, new ButtonModel { Clicked = OnSelectClicked });
            else
                _selectButton.SetRect(selectRect);

            if (_cancelButton == null)
                _cancelButton = new Button(cancelRect, new ButtonModel { Clicked = OnCancelClicked });
            else
                _cancelButton.SetRect(cancelRect);

            _selectButton.SetVisible(true);
            _cancelButton.SetVisible(true);
        }

        static string FormatOption(CraftAssemblerOption option)
        {
            if (option == null)
                return string.Empty;

            var speedText = option.Speed.ToString("0.##", FormatingHelper.Culture);
            return option.Idle
                ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc("LcdMod_CraftDialog_AssemblerOption_WithStatus"),
                    option.DisplayName, LocHelper.GetLoc("LcdMod_CraftDialog_Assembler_Status_Idle"), speedText)
                : string.Format(FormatingHelper.Culture, LocHelper.GetLoc("LcdMod_CraftDialog_AssemblerOption"),
                    option.DisplayName, speedText);
        }

        bool HasSelection()
        {
            return _listModel != null && _listModel.SelectedEntries != null &&
                   _listModel.SelectedEntries.Count > 0;
        }

        void OnSelectClicked(ButtonModel model, object sender)
        {
            if (!HasSelection())
                return;

            Dismiss();

            if (_selectedCallback != null)
                _selectedCallback(new List<CraftAssemblerOption>(_listModel.SelectedEntries));
        }

        void OnCancelClicked(ButtonModel model, object sender)
        {
            if (_cancelCallback != null)
                _cancelCallback();

            Dismiss();
        }
    }

    sealed class CraftAssemblerOption
    {
        public IMyAssembler Assembler;
        public readonly Dictionary<MyDefinitionId, MyBlueprintDefinitionBase> BlueprintsByItem =
            new Dictionary<MyDefinitionId, MyBlueprintDefinitionBase>();
        public string DisplayName;
        public bool Idle;
        public float Speed;
    }
}
