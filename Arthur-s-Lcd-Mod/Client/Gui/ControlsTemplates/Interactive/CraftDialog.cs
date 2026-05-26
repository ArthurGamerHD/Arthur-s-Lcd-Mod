using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Lists;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
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
        // ReSharper disable once NotAccessedField.Local
        readonly MyItemType _itemType;
        readonly MyDefinitionId _itemDefinitionId;
        readonly string _itemName;
        readonly string _itemIcon;
        readonly Action<Dialog> _showDialog;
        readonly List<CraftAssemblerOption> _assemblerOptions = new List<CraftAssemblerOption>();
        readonly List<CraftAssemblerOption> _selectedAssemblers = new List<CraftAssemblerOption>();

        NumericUpDownModel _amountModel;
        NumericUpDown _amountControl;
        ControlStyle _amountControlStyle;
        RectangleControl _assemblerControl;
        Button _craftButton;
        Button _cancelButton;

        public CraftDialog(
            IApp parentApp,
            GridLogic gridLogic,
            MyItemType itemType,
            string itemName,
            string itemIcon,
            double defaultAmount,
            Action<Dialog> showDialog)
            : base(parentApp)
        {
            _gridLogic = gridLogic;
            _itemType = itemType;
            _itemDefinitionId = itemType;
            _itemName = string.IsNullOrEmpty(itemName) ? itemType.SubtypeId : itemName;
            _itemIcon = itemIcon;
            _showDialog = showDialog;

            BuildAssemblerOptions();
            SelectDefaultAssemblers();
            _amountModel = new NumericUpDownModel
            {
                Value = Math.Max(1d, Math.Ceiling(defaultAmount)),
                MinValue = 1d,
                MaxValue = 1000000d,
                Format = "0",
                Step = 1d,
                Title = "Craft",
                Subtitle = _itemName
            };
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

            var cardWidth = Math.Min(viewBox.Width - padding.X * 2f, Math.Max(360f * scale, viewBox.Width * 0.62f));
            var cardHeight = Math.Min(viewBox.Height - padding.Y * 2f, Math.Max(235f * scale, viewBox.Height * 0.52f));
            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth,
                cardHeight);

            var shadowColor = GetThemeColor(Constants.SHADOW);
            Border.CreateSpritesFromRect(new RectangleF(cardRect.Position + 2f, cardRect.Size), Sprites, shadowColor, 0.2f);
            Border.CreateSpritesFromRect(cardRect, Sprites, cardColor, 0.2f);

            var renderContext = CreateRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);

            var titleSize = FormatingHelper.GetSizeInPixel("Craft", "White", titleScale, surface);
            var currentY = cardRect.Y + padding.Y;
            DrawText("Craft", new Vector2(cardRect.Center.X, currentY), titleScale, cardTextColor, TextAlignment.CENTER);
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

            DrawItemIcon(iconRect);

            var nameText = TrimText(_itemName, rightWidth, nameScale, surface);
            var nameY = currentY + Math.Max(0f, (nameHeight - FormatingHelper.GetSizeInPixel(nameText, "White", nameScale, surface).Y) * 0.5f);
            DrawText(nameText, new Vector2(rightX, nameY), nameScale, cardTextColor, TextAlignment.LEFT);

            var amountTop = Math.Min(contentBottom - amountHeight, currentY + itemRowsHeight + spacing);
            var amountRect = new RectangleF(cardRect.X + padding.X, amountTop, contentWidth, amountHeight);
            EnsureAmountControl(amountRect);
            ConfigureAmountControlStyle();
            container.AddChild(_amountControl);
            _amountControl.Render(renderContext, Sprites);

            var buttonSpacing = 12f * scale;
            var buttonWidth = Math.Max(92f * scale, (cardRect.Width - padding.X * 2f - buttonSpacing) * 0.5f);
            var buttonsWidth = buttonWidth * 2f + buttonSpacing;
            var craftRect = new RectangleF(cardRect.Center.X - buttonsWidth * 0.5f, buttonsTop, buttonWidth, buttonHeight);
            var cancelRect = new RectangleF(craftRect.Right + buttonSpacing, buttonsTop, buttonWidth, buttonHeight);

            EnsureButtons(craftRect, cancelRect);
            container.AddChild(_craftButton);
            container.AddChild(_cancelButton);

            ConfigureButton(_craftButton, "Craft", buttonScale, panelColor, textColor, ThemedParentApp, owner, CanCraft());
            ConfigureButton(_cancelButton, "Cancel", buttonScale, panelColor, textColor, ThemedParentApp, owner, true);
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

        void DrawItemIcon(RectangleF rect)
        {
            if (string.IsNullOrEmpty(_itemIcon))
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
                Data = _itemIcon,
                Position = rect.Center,
                Size = rect.Size,
                Color = Color.White,
                Alignment = TextAlignment.CENTER
            });
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
                _amountControlStyle.BorderPercentage = 0.5f;
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

            Border.CreateSpritesFromRect(rect, sprites, fill, 0.2f);
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

            Border.CreateSpritesFromRect(rect, sprites, buttonColor, context.Style.BorderPercentage);

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
            return _selectedAssemblers.Count > 0 && _amountModel != null && _amountModel.Value > 0d;
        }

        void OnCraftClicked(ButtonModel model, object sender)
        {
            if (!CanCraft())
                return;

            try
            {
                var requestedItems = Math.Max(1, (int)Math.Ceiling(_amountModel.Value));
                QueueSplitCraft(requestedItems);
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

        double CalculateQueueAmount(MyBlueprintDefinitionBase blueprint, double requestedItems)
        {
            if (blueprint == null || blueprint.Results == null)
                return requestedItems;

            for (var i = 0; i < blueprint.Results.Length; i++)
            {
                if (!blueprint.Results[i].Id.Equals(_itemDefinitionId))
                    continue;

                var resultAmount = (double)blueprint.Results[i].Amount;
                if (resultAmount <= 0d)
                    return requestedItems;

                return Math.Ceiling(requestedItems / resultAmount);
            }

            return requestedItems;
        }

        void QueueSplitCraft(int requestedItems)
        {
            var selected = GetCraftableSelectedAssemblers();
            if (requestedItems <= 0 || selected.Count == 0)
                return;

            var count = selected.Count;
            var baseShare = requestedItems / count;
            var remainder = requestedItems % count;

            for (var i = 0; i < count; i++)
            {
                var itemShare = baseShare + (i < remainder ? 1 : 0);
                if (itemShare <= 0)
                    continue;

                var option = selected[i];
                var queueAmount = CalculateQueueAmount(option.Blueprint, itemShare);
                option.Assembler.AddQueueItem(option.Blueprint.Id, queueAmount);
            }
        }

        List<CraftAssemblerOption> GetCraftableSelectedAssemblers()
        {
            var selected = new List<CraftAssemblerOption>();

            for (var i = 0; i < _selectedAssemblers.Count; i++)
            {
                var option = _selectedAssemblers[i];
                if (option != null && option.Assembler != null && option.Blueprint != null)
                    selected.Add(option);
            }

            return selected;
        }

        void BuildAssemblerOptions()
        {
            _assemblerOptions.Clear();

            var assemblers = _gridLogic?.GetAssemblers();
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
            HashSet<MyDefinitionId> blueprintsByItem;
            if (!GridLogic.BlueprintsByCreatedItem.TryGetValue(_itemDefinitionId, out blueprintsByItem) ||
                blueprintsByItem == null || blueprintsByItem.Count == 0)
                return null;

            var assemblerSubtype = GridLogic.GetAssemblerSubtype(assembler);
            HashSet<MyDefinitionId> craftableBlueprints;
            if (string.IsNullOrEmpty(assemblerSubtype) ||
                !GridLogic.CraftableBlueprintsByAssemblerSubtype.TryGetValue(assemblerSubtype, out craftableBlueprints) ||
                craftableBlueprints == null)
                return null;

            CraftAssemblerOption best = null;
            foreach (var blueprintId in blueprintsByItem)
            {
                if (!craftableBlueprints.Contains(blueprintId))
                    continue;

                var blueprint = MyDefinitionManager.Static.GetBlueprintDefinition(blueprintId);
                if (blueprint == null)
                    continue;

                var option = new CraftAssemblerOption
                {
                    Assembler = assembler,
                    Blueprint = blueprint,
                    DisplayName = GetAssemblerName(assembler),
                    Idle = IsAssemblerIdle(assembler),
                    Speed = GetAssemblerSpeed(assembler)
                };

                if (best == null || CompareBlueprintChoice(option.Blueprint, best.Blueprint) < 0)
                    best = option;
            }

            return best;
        }

        void SelectDefaultAssemblers()
        {
            _selectedAssemblers.Clear();
            if (_assemblerOptions.Count > 0)
                _selectedAssemblers.Add(_assemblerOptions[0]);
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
                return "Assemblers: None";

            if (_selectedAssemblers.Count == 1)
                return "Assembler: " + _selectedAssemblers[0].DisplayName;

            return "Assemblers: " + _selectedAssemblers.Count.ToString() + " selected";
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
            return string.IsNullOrEmpty(subtype) ? "Assembler" : subtype;
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
                GetThemeColor(Constants.SHADOW), 0.2f);
            Border.CreateSpritesFromRect(cardRect, Sprites, cardColor, 0.2f);

            var title = "Select Assemblers";
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
                _listBoxStyle.BorderPercentage = 0;
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
                    Data = "No assembler found",
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
            CraftDialog.ConfigureButton(_selectButton, "Select", buttonScale, panelColor, textColor, ThemedParentApp, owner, HasSelection());
            CraftDialog.ConfigureButton(_cancelButton, "Cancel", buttonScale, panelColor, textColor, ThemedParentApp, owner, true);
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
                _listBox.Style.BorderPercentage = 0;
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

            var suffix = option.Idle ? " (Idle)" : string.Empty;
            return option.DisplayName + suffix + " x" + option.Speed.ToString("0.##");
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
        public MyBlueprintDefinitionBase Blueprint;
        public string DisplayName;
        public bool Idle;
        public float Speed;
    }
}
