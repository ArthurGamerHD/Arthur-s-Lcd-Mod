using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Lists;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
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
        RectangleControl _assemblerControl;
        Button _craftButton;

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
                    Title = Loc(MOD_PREFIX + "CraftDialog_Title"),
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

        protected override void BuildDialogControls(
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
            var cardColor = ResolveColor(ThemeResources.SurfaceContainerHighColor);
            var cardTextColor = ResolveColor(ThemeResources.OnSurfaceColor);

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

            RegisterDialogCard(cardRect);

            var shadowColor = ResolveColor(ThemeResources.ShadowColor);
            Border.CreateSpritesFromRect(new RectangleF(cardRect.Position + 2f, cardRect.Size), Sprites, shadowColor,
                radiusScale: scale);
            Border.CreateSpritesFromRect(cardRect, Sprites, cardColor,
                radiusScale: scale);

            var title = _useRequestGrid ? "Craft all" : Loc(MOD_PREFIX + "CraftDialog_Title");
            var titleSize = MeasureText(title, titleScale, surface);
            var closeSize = GetDialogCloseButtonSize(scale);
            var headerHeight = Math.Max(titleSize.Y, closeSize.Y);
            var currentY = cardRect.Y + padding.Y;
            DrawText(
                title,
                new Vector2(cardRect.Center.X, currentY + (headerHeight - titleSize.Y) * 0.5f),
                titleScale,
                cardTextColor,
                TextAlignment.CENTER);
            currentY += headerHeight + spacing * 0.7f;

            var assemblerRect = new RectangleF(cardRect.X + padding.X, currentY, cardRect.Width - padding.X * 2f,
                Math.Max(24f * scale, MeasureLineHeight(labelScale, surface) + 8f * scale));
            EnsureAssemblerControl(assemblerRect);
            container.AddChild(_assemblerControl);
            _assemblerControl.Render(Sprites);

            currentY = assemblerRect.Bottom + spacing;

            var buttonHeight = Math.Max(26f * scale, MeasureLineHeight(buttonScale, surface) + 10f * scale);
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
                    Math.Max(56f * scale, MeasureLineHeight(nameScale, surface) * 2f + 4f * scale));

                var iconSize = Math.Min(Math.Max(44f * scale, itemRowsHeight), 72f * scale);
                var iconRect = new RectangleF(cardRect.X + padding.X, currentY + (itemRowsHeight - iconSize) * 0.5f,
                    iconSize, iconSize);
                var rightX = iconRect.Right + 16f * scale;
                var rightWidth = Math.Max(0f, cardRect.Right - padding.X - rightX);
                var nameHeight = itemRowsHeight;

                DrawItemIcon(iconRect, request.Icon);

                var nameText = TrimText(request.Name, rightWidth, nameScale, surface);
                var nameY = currentY + Math.Max(0f,
                    (nameHeight - MeasureText(nameText, nameScale, surface).Y) * 0.5f);
                DrawText(nameText, new Vector2(rightX, nameY), nameScale, cardTextColor, TextAlignment.LEFT);

                if (_amountModel != null)
                {
                    var amountTop = Math.Min(contentBottom - amountHeight, currentY + itemRowsHeight + spacing);
                    var amountRect = new RectangleF(cardRect.X + padding.X, amountTop, contentWidth, amountHeight);
                    EnsureAmountControl(amountRect);
                    ConfigureAmountControl();
                    container.AddChild(_amountControl);
                    _amountControl.Render(Sprites);
                }
            }

            var buttonWidth = Math.Min(
                Math.Max(120f * scale, (cardRect.Width - padding.X * 2f) * 0.55f),
                Math.Max(1f, cardRect.Width - padding.X * 2f));
            var craftRect = new RectangleF(cardRect.Center.X - buttonWidth * 0.5f, buttonsTop, buttonWidth, buttonHeight);

            EnsureButtons(craftRect);

            container.AddChild(_craftButton);

            ConfigureButton(_craftButton, _useRequestGrid ? "Craft all" : Loc(MOD_PREFIX + "CraftDialog_Button_Craft"),
                buttonScale, owner, CanCraft());
            _craftButton.Render(Sprites);
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
                    Data = "MissingIcon",
                    Position = rect.Center,
                    Size = rect.Size,
                    Color = ColorCorrection,
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
                Color = ColorCorrection,
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

            var grid = WrapPanelLayout.Create(rect, rowHeight, minColumnWidth, _requests.Count);
            var visibleCount = Math.Min(grid.VisibleCellCount, _requests.Count);
            var cellPadding = 4f * scale;
            var itemBackground = ResolveColor(ThemeResources.SurfaceContainerColor);
            var amountColor = ResolveColor(ThemeResources.OnSurfaceVariantColor);
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
                var nameHeight = MeasureLineHeight(nameScale, surface);
                var amountHeight = MeasureLineHeight(amountScale, surface);
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
                FontId = TextFont,
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

        void ConfigureAmountControl()
        {
            if (_amountControl == null)
                return;

            _amountControl.SetStyleId("Primary");
            _amountControl.BorderRadiusPixels = Border.DEFAULT_RADIUS_PIXELS;
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

        void RenderAssemblerControl(ControlTemplate control, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var hovered = _assemblerOptions.Count > 1 && rect.Contains(new Vector2(float.NaN, float.NaN));
            var label = GetAssemblerSelectionLabel();
            var textScale = 0.54f * control.LayoutScale * control.FontScale;
            var fill = hovered
                ? control.ResolveColor(ThemeResources.SurfaceContainerColor)
                : control.ResolveColor(ThemeResources.SurfaceContainerColor);
            var labelColor = control.ResolveColor(ThemeResources.OnSurfaceColor);

            Border.CreateSpritesFromRect(rect, sprites, fill,
                radiusScale: control.LayoutScale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = TrimText(label, rect.Width - 12f * control.LayoutScale, textScale, control.TextSurface),
                Position = new Vector2(rect.X + 6f * control.LayoutScale, rect.Center.Y -
                    FormatingHelper.GetSizeInPixel(label, control, textScale, control.TextSurface).Y * 0.5f),
                Color = labelColor,
                FontId = control.TextFont,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = textScale
            });
        }

        void EnsureButtons(RectangleF craftRect)
        {
            if (_craftButton == null)
                _craftButton = new Button(craftRect, new ButtonModel { Clicked = OnCraftClicked });
            else
                _craftButton.SetRect(craftRect);

            _craftButton.SetVisible(true);
        }

        internal static void ConfigureButton(
            Button button,
            string text,
            float textScale,
            InteractiveSurfaceScript owner,
            bool enabled)
        {
            var model = button.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = text;
                model.Enabled = enabled;
            }

            button.SetEnabled(enabled);
            button.SetCursor(enabled ? CursorType.Hand : CursorType.Default);
            button.SetStyleId(enabled ? "Primary" : "Disabled");
            button.CustomRender = null;
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


        double CalculateQueueAmount(MyBlueprintDefinitionBase blueprint, MyDefinitionId itemDefinitionId, double requestedItems)
        {
            if (blueprint?.Results == null)
                return requestedItems;

            for (var i = 0; i < blueprint.Results.Length; i++)
            {
                if (!blueprint.Results[i].Id.Equals(itemDefinitionId))
                    continue;

                var resultAmount = (double)blueprint.Results[i].Amount;
                return resultAmount <= 0d ? requestedItems : Math.Ceiling(requestedItems / resultAmount);
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

            int count = selected.Count;

            int baseShare = requestedItems / count;
            int remainder = requestedItems % count;

            for (int i = 0; i < count; i++)
            {
                int itemsForThisThread = baseShare;

                if (i < remainder)
                    itemsForThisThread++;

                if (itemsForThisThread == 0)
                    continue;

                var option = selected[i];
                MyBlueprintDefinitionBase blueprint;
                if (!option.BlueprintsByItem.TryGetValue(request.DefinitionId, out blueprint) || blueprint == null)
                    continue;

                var queueAmount = CalculateQueueAmount(blueprint, request.DefinitionId, itemsForThisThread);
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

            var assemblers = _gridLogic?.GetTerminalBlocks<IMyAssembler>(GridLinkTypeEnum.Physical);
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

                MyBlueprintDefinitionBase blueprint;
                if (!GridLogic.PrimaryBlueprintByCreatedItem.TryGetValue(request.DefinitionId, out blueprint))
                    continue;
                   

                option.BlueprintsByItem[request.DefinitionId] = blueprint;
            }

            return option.BlueprintsByItem.Count == 0 ? null : option;
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
                return Loc(MOD_PREFIX + "CraftDialog_Assemblers_None");

            if (_selectedAssemblers.Count == 1)
                return FormatLoc(MOD_PREFIX + "CraftDialog_Assemblers_Single", _selectedAssemblers[0].DisplayName);

            return FormatLoc(MOD_PREFIX + "CraftDialog_Assemblers_SelectedCount", _selectedAssemblers.Count);
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
            return string.IsNullOrEmpty(subtype) ? Loc(MOD_PREFIX + "CraftDialog_Assembler_FallbackName") : subtype;
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

        string TrimText(string text, float availableWidth, float fontSize,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            if (string.IsNullOrEmpty(text) || availableWidth <= 0f || surface == null)
                return string.Empty;

            var size = MeasureText(text, fontSize, surface);
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
        Button _selectButton;

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
            OnClose = delegate
            {
                if (_cancelCallback != null)
                    _cancelCallback();
            };
        }

        protected override void BuildDialogControls(
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
            var cardColor = ResolveColor(ThemeResources.SurfaceContainerHighColor);
            var cardTextColor = ResolveColor(ThemeResources.OnSurfaceColor);
            var cardWidth = Math.Min(viewBox.Width - padding.X * 2f, Math.Max(320f * scale, viewBox.Width * 0.58f));
            var cardHeight = Math.Min(viewBox.Height - padding.Y * 2f, Math.Max(230f * scale, viewBox.Height * 0.5f));
            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth,
                cardHeight);

            RegisterDialogCard(cardRect);

            Border.CreateSpritesFromRect(new RectangleF(cardRect.Position + 2f, cardRect.Size), Sprites,
                ResolveColor(ThemeResources.ShadowColor), radiusScale: scale);
            Border.CreateSpritesFromRect(cardRect, Sprites, cardColor,
                radiusScale: scale);

            var title = LocHelper.GetLoc(MOD_PREFIX + "CraftDialog_AssemblerSelection_Title");
            var titleSize = MeasureText(title, titleScale, surface);
            var closeSize = GetDialogCloseButtonSize(scale);
            var headerHeight = Math.Max(titleSize.Y, closeSize.Y);
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = title,
                Position = new Vector2(cardRect.Center.X, cardRect.Y + padding.Y + (headerHeight - titleSize.Y) * 0.5f),
                Color = cardTextColor,
                FontId = TextFont,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = titleScale
            });

            var buttonHeight = Math.Max(26f * scale, MeasureLineHeight(buttonScale, surface) + 10f * scale);
            var listTop = cardRect.Y + padding.Y + headerHeight + spacing;
            var buttonTop = cardRect.Bottom - padding.Y - buttonHeight;
            var listRect = new RectangleF(cardRect.X + padding.X, listTop,
                cardRect.Width - padding.X * 2f, Math.Max(0f, buttonTop - spacing - listTop));

            EnsureList(listRect, 30f * scale);
            ConfigureListBox();
            container.AddChild(_listBox);
            if (_options.Count > 0)
            {
                _listBox.Render(Sprites);
            }
            else
            {
                Sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = LocHelper.GetLoc(MOD_PREFIX + "CraftDialog_AssemblerSelection_Empty"),
                    Position = new Vector2(listRect.Center.X, listRect.Center.Y - titleSize.Y * 0.5f),
                    Color = cardTextColor,
                    FontId = TextFont,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = 0.58f * scale * fontScale
                });
            }

            var buttonWidth = Math.Min(
                Math.Max(120f * scale, (cardRect.Width - padding.X * 2f) * 0.55f),
                Math.Max(1f, cardRect.Width - padding.X * 2f));
            var selectRect = new RectangleF(cardRect.Center.X - buttonWidth * 0.5f, buttonTop, buttonWidth, buttonHeight);

            EnsureButtons(selectRect);

            container.AddChild(_selectButton);
            CraftDialog.ConfigureButton(_selectButton, LocHelper.GetLoc(MOD_PREFIX + "Common_Button_Select"), buttonScale, owner, HasSelection());
            _selectButton.Render(Sprites);
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
        }

        void ConfigureListBox()
        {
            if (_listBox == null)
                return;

            _listBox.BorderRadiusPixels = 0f;
            _listBox.BackgroundColor = ResolveColor(ThemeResources.SecondaryContainerColor);
            _listBox.TextColor = ResolveColor(ThemeResources.OnSecondaryContainerColor);
        }

        void EnsureButtons(RectangleF selectRect)
        {
            if (_selectButton == null)
                _selectButton = new Button(selectRect, new ButtonModel { Clicked = OnSelectClicked });
            else
                _selectButton.SetRect(selectRect);

            _selectButton.SetVisible(true);
        }

        static string FormatOption(CraftAssemblerOption option)
        {
            if (option == null)
                return string.Empty;

            var speedText = option.Speed.ToString("0.##", FormatingHelper.Culture);
            return option.Idle
                ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "CraftDialog_AssemblerOption_WithStatus"),
                    option.DisplayName, LocHelper.GetLoc(MOD_PREFIX + "CraftDialog_Assembler_Status_Idle"), speedText)
                : string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "CraftDialog_AssemblerOption"),
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
