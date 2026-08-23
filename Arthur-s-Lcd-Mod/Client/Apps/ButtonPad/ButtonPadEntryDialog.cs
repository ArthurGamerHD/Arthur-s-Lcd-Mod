using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal;
using LcdMod.Client.Terminal.Models;
using LcdMod.Client.Terminal.Models.Actions;
using LcdMod.Client.Terminal.Models.Property;
using Sandbox.ModAPI.Interfaces;
using LcdMod.Common.Helpers;
using LcdMod.Common.Layout;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using IMyBlockGroup = Sandbox.ModAPI.Ingame.IMyBlockGroup;
using IMyIngameTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

using LcdMod.Common.Config.Generation;
// ReSharper disable All
// todo: fix-me
namespace LcdMod.Client.Apps
{
    public sealed partial class ButtonPadApp
    {
        sealed partial class ButtonPadEntryDialog : Dialog
        {
            const float SPRITE_PREVIEW_SIZE_PIXELS = 78f;

            readonly GridLogic _gridLogic;
            readonly int _index;
            readonly Action<ButtonPanelEntrySettings> _apply;
            readonly Action<Dialog> _showDialog;
            readonly Action _requestRedraw;
            readonly ButtonPanelEntrySettings _draftEntry;

            TextInput _titleInput;
            TextInputModel _titleInputModel;
            Button _selectedSpriteButton;
            Button _colorButton;
            Button _pickTargetButton;
            Button _selectActionButton;
            Button _applyButton;
            Button _deleteButton;

            public ButtonPadEntryDialog(
                IApp parentApp,
                GridLogic gridLogic,
                int index,
                ButtonPanelEntrySettings initialEntry,
                Action<ButtonPanelEntrySettings> apply,
                Action<Dialog> showDialog,
                Action requestRedraw) : base(parentApp)
            {
                _gridLogic = gridLogic;
                _index = index;
                _draftEntry = initialEntry == null ? new ButtonPanelEntrySettings { Index = index } : initialEntry.Clone();
                _draftEntry.Index = index;
                _apply = apply;
                _showDialog = showDialog;
                _requestRedraw = requestRedraw;
                OnClose = delegate
                {
                    if (_requestRedraw != null)
                        _requestRedraw();
                };
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

                var compact = IsTinyDialogAspectRatio(viewBox);
                var layoutScale = scale * fontScale;
                var titleScale = 0.82f * layoutScale;
                var fieldTextScale = 0.56f * layoutScale;
                var padding = GetDialogPadding(viewBox, scale);
                var spacing = GetDialogSpacing(viewBox, scale);
                var smallSpacing = GetDialogSpacing(viewBox, scale, 6f, 3f);

                var titleHeight = MeasureLineHeight(titleScale, surface);
                var fieldHeight = Math.Max(34f * scale, MeasureLineHeight(fieldTextScale, surface) + 18f * scale);
                var closeIconSize = GetDialogCloseButtonSize(scale);
                var headerHeight = Math.Max(titleHeight, closeIconSize.Y);
                var previewSize = GetSpritePreviewTargetSize(scale);

                var outerPadding = GetDialogOuterPadding(viewBox, scale);
                var cardWidth = compact
                    ? Math.Max(1f, viewBox.Width - outerPadding * 2f)
                    : Math.Min(
                        Math.Max(400f * scale, viewBox.Width * 0.62f),
                        Math.Max(1f, viewBox.Width - outerPadding * 2f));

                var contentWidth = Math.Max(1f, cardWidth - padding.X * 2f);
                previewSize = Math.Min(previewSize, contentWidth);
                var narrowLayout = contentWidth < previewSize + spacing + 150f * scale;

                var stackHeight = fieldHeight * 4f + smallSpacing * 3f;
                var contentHeight = narrowLayout
                    ? previewSize + spacing + stackHeight + spacing + fieldHeight
                    : Math.Max(previewSize, stackHeight) + spacing + fieldHeight;

                var cardHeight = padding.Y * 2f + headerHeight + spacing + contentHeight;
                cardHeight = compact
                    ? Math.Max(1f, viewBox.Height - outerPadding * 2f)
                    : Math.Min(cardHeight, Math.Max(1f, viewBox.Height - outerPadding * 2f));

                var cardRect = new RectangleF(
                    viewBox.Center.X - cardWidth * 0.5f,
                    viewBox.Center.Y - cardHeight * 0.5f,
                    cardWidth,
                    cardHeight);

                RegisterDialogCard(cardRect);
                DrawDialogBackdrop(surface, scale, cardRect, 128, !compact);

                if (compact)
                {
                    RenderCompactEditor(cardRect, viewBox, scale);
                    return;
                }

                var dialogTitle = _index >= 0
                    ? ButtonPadLocalization.Button(_index + 1)
                    : ButtonPadLocalization.ButtonLabel;
                Sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = dialogTitle,
                    Position = new Vector2(cardRect.Center.X, cardRect.Y + padding.Y + (headerHeight - titleHeight) * 0.5f),
                    Color = ResolveColor(ThemeResources.OnSurfaceColor),
                    FontId = TextFont,
                    RotationOrScale = titleScale,
                    Alignment = TextAlignment.CENTER
                });

                var contentTop = cardRect.Y + padding.Y + headerHeight + spacing;
                RectangleF previewRect;
                RectangleF titleInputRect;
                RectangleF colorRect;
                RectangleF targetRect;
                RectangleF actionRect;
                RectangleF applyRect;
                RectangleF deleteRect;
                float footerTop;

                if (narrowLayout)
                {
                    previewRect = new RectangleF(
                        cardRect.Center.X - previewSize * 0.5f,
                        contentTop,
                        previewSize,
                        previewSize);

                    var y = previewRect.Bottom + spacing;
                    titleInputRect = new RectangleF(cardRect.X + padding.X, y, contentWidth, fieldHeight);
                    y = titleInputRect.Bottom + smallSpacing;
                    colorRect = new RectangleF(cardRect.X + padding.X, y, contentWidth, fieldHeight);
                    y = colorRect.Bottom + smallSpacing;
                    targetRect = new RectangleF(cardRect.X + padding.X, y, contentWidth, fieldHeight);
                    y = targetRect.Bottom + smallSpacing;
                    actionRect = new RectangleF(cardRect.X + padding.X, y, contentWidth, fieldHeight);
                    footerTop = actionRect.Bottom + spacing;
                }
                else
                {
                    previewRect = new RectangleF(
                        cardRect.X + padding.X,
                        contentTop,
                        previewSize,
                        previewSize);

                    var rightX = previewRect.Right + spacing;
                    var rightWidth = Math.Max(1f, cardRect.Right - padding.X - rightX);
                    titleInputRect = new RectangleF(rightX, contentTop, rightWidth, fieldHeight);
                    colorRect = new RectangleF(rightX, titleInputRect.Bottom + smallSpacing, rightWidth, fieldHeight);
                    targetRect = new RectangleF(rightX, colorRect.Bottom + smallSpacing, rightWidth, fieldHeight);
                    actionRect = new RectangleF(rightX, targetRect.Bottom + smallSpacing, rightWidth, fieldHeight);
                    footerTop = Math.Max(actionRect.Bottom + spacing, previewRect.Bottom + spacing);
                }

                var footerButtonWidth = Math.Max(1f, (contentWidth - smallSpacing) * 0.5f);
                var footerButtonsWidth = footerButtonWidth * 2f + smallSpacing;
                var footerButtonsX = cardRect.Center.X - footerButtonsWidth * 0.5f;

                applyRect = new RectangleF(
                    footerButtonsX,
                    footerTop,
                    footerButtonWidth,
                    fieldHeight);
                deleteRect = new RectangleF(
                    applyRect.Right + smallSpacing,
                    footerTop,
                    footerButtonWidth,
                    fieldHeight);

                EnsureSelectedSpriteButton(previewRect);
                EnsureTitleInput(titleInputRect);
                EnsureColorButton(colorRect);
                EnsurePickTargetButton(targetRect);
                EnsureSelectActionButton(actionRect);
                EnsureApplyButton(applyRect);
                EnsureDeleteButton(deleteRect);

                ContainerControl.AddChild(_selectedSpriteButton);
                ContainerControl.AddChild(_titleInput);
                ContainerControl.AddChild(_colorButton);
                ContainerControl.AddChild(_pickTargetButton);
                ContainerControl.AddChild(_selectActionButton);
                ContainerControl.AddChild(_applyButton);
                ContainerControl.AddChild(_deleteButton);

                _selectedSpriteButton.Render(Sprites);
                _titleInput.Render(Sprites);
                _colorButton.Render(Sprites);
                _pickTargetButton.Render(Sprites);
                _selectActionButton.Render(Sprites);
                _applyButton.Render(Sprites);
                _deleteButton.Render(Sprites);
            }

            void RenderCompactEditor(RectangleF cardRect, RectangleF viewBox, float scale)
            {
                var padding = GetDialogPadding(viewBox, scale);
                var spacing = GetDialogSpacing(viewBox, scale);
                var contentRect = GetDialogContentRect(cardRect, viewBox, scale, padding);
                var gap = Math.Min(spacing, contentRect.Width * 0.02f);
                var rowHeight = Math.Max(1f, (contentRect.Height - gap) * 0.5f);
                var previewSize = Math.Min(Math.Min(GetSpritePreviewTargetSize(scale), contentRect.Height),
                    Math.Max(1f, contentRect.Width * 0.12f));
                var actionButtonWidth = Math.Min(Math.Max(64f * scale, contentRect.Width * 0.09f), contentRect.Width * 0.14f);

                var previewRect = new RectangleF(contentRect.X, contentRect.Center.Y - previewSize * 0.5f,
                    previewSize, previewSize);
                var deleteRect = new RectangleF(contentRect.Right - actionButtonWidth, contentRect.Y,
                    actionButtonWidth, rowHeight);
                var applyRect = new RectangleF(deleteRect.X, contentRect.Y + rowHeight + gap,
                    actionButtonWidth, rowHeight);

                var fieldsLeft = previewRect.Right + gap;
                var fieldsRight = deleteRect.X - gap;
                var titleWidth = Math.Max(1f, Math.Min((fieldsRight - fieldsLeft) * 0.42f,
                    Math.Max(100f * scale, contentRect.Width * 0.22f)));
                var targetX = fieldsLeft + titleWidth + gap;
                var targetWidth = Math.Max(1f, fieldsRight - targetX);
                var titleInputRect = new RectangleF(fieldsLeft, contentRect.Y, titleWidth, rowHeight);
                var colorRect = new RectangleF(fieldsLeft, contentRect.Y + rowHeight + gap, titleWidth, rowHeight);
                var targetRect = new RectangleF(targetX, contentRect.Y, targetWidth, rowHeight);
                var actionRect = new RectangleF(targetX, contentRect.Y + rowHeight + gap, targetWidth, rowHeight);

                EnsureSelectedSpriteButton(previewRect);
                EnsureTitleInput(titleInputRect);
                EnsureColorButton(colorRect);
                EnsurePickTargetButton(targetRect);
                EnsureSelectActionButton(actionRect);
                EnsureApplyButton(applyRect);
                EnsureDeleteButton(deleteRect);

                ContainerControl.AddChild(_selectedSpriteButton);
                ContainerControl.AddChild(_titleInput);
                ContainerControl.AddChild(_colorButton);
                ContainerControl.AddChild(_pickTargetButton);
                ContainerControl.AddChild(_selectActionButton);
                ContainerControl.AddChild(_applyButton);
                ContainerControl.AddChild(_deleteButton);

                _selectedSpriteButton.Render(Sprites);
                _titleInput.Render(Sprites);
                _colorButton.Render(Sprites);
                _pickTargetButton.Render(Sprites);
                _selectActionButton.Render(Sprites);
                _applyButton.Render(Sprites);
                _deleteButton.Render(Sprites);
            }

            void DrawDialogBackdrop(IMyTextSurface surface, float scale, RectangleF cardRect, byte overlayAlpha,
                bool drawShadow)
            {
                Sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = surface.TextureSize / 2f,
                    Size = surface.TextureSize,
                    Color = new Color(0, 0, 0, overlayAlpha),
                    Alignment = TextAlignment.CENTER
                });

                if (drawShadow)
                    BorderRenderer.CreateSpritesFromRect(new RectangleF(cardRect.Position + 3f * scale, cardRect.Size), Sprites,
                        ResolveColor(ThemeResources.ShadowColor), radiusScale: scale);
                BorderRenderer.CreateSpritesFromRect(cardRect, Sprites,
                    ResolveColor(ThemeResources.SurfaceContainerHighColor),
                    radiusPixels: DialogCardRadiusPixels, radiusScale: scale);
            }

            void EnsureSelectedSpriteButton(RectangleF rect)
            {
                if (_selectedSpriteButton == null)
                    _selectedSpriteButton = new Button(rect, new SelectedSpriteButtonModel { Clicked = OnSelectedSpriteButtonClicked });
                else
                    _selectedSpriteButton.SetRect(rect);

                var model = _selectedSpriteButton.DataContext as SelectedSpriteButtonModel;
                if (model == null)
                {
                    model = new SelectedSpriteButtonModel();
                    _selectedSpriteButton.SetDataContext(model);
                }

                model.SpriteName = GetEntrySpriteName(_draftEntry);
                model.Text = string.Empty;
                model.Enabled = true;
                model.Clicked = OnSelectedSpriteButtonClicked;

                _selectedSpriteButton.SetStyleId("Primary");
                _selectedSpriteButton.CustomRender = RenderSelectedSprite;
                _selectedSpriteButton.OnSecondaryClick = OnSpriteUnassigned;
                _selectedSpriteButton.SetCursor(CursorType.Hand);
                _selectedSpriteButton.SetVisible(true);
            }

            void RenderSelectedSprite(ControlTemplate control, List<MySprite> sprites)
            {
                var rect = control.Bounds;
                var model = control.DataContext as SelectedSpriteButtonModel;
                var spriteName = model?.SpriteName;
                var hovered = rect.Contains(new Vector2(float.NaN, float.NaN));
                var button = control as Button;
                Color customPanelColor;
                var hasCustomPanelColor = Extensions.ColorExtensions.TryParseHexColor(
                    _draftEntry.BackgroundColor,
                    out customPanelColor);
                var defaultPanelColor = hasCustomPanelColor
                    ? customPanelColor
                    : button?.BackgroundColor ?? control.BackgroundColor;
                var panelColor = hovered
                    ? hasCustomPanelColor
                        ? GetHoverColor(defaultPanelColor)
                        : control.GetResourceColor(ThemeResources.AccentColor, defaultPanelColor)
                    : defaultPanelColor;

                BorderRenderer.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: control.LayoutScale);

                var iconSize = Math.Max(1f, Math.Min(rect.Width, rect.Height) * 0.74f);
                var iconRect = new RectangleF(rect.Center.X - iconSize * 0.5f, rect.Center.Y - iconSize * 0.5f, iconSize, iconSize);

                if (string.IsNullOrEmpty(spriteName))
                {
                    DrawPlus(iconRect, Color.White, control.LayoutScale, sprites);
                    return;
                }

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = spriteName,
                    Position = iconRect.Center,
                    Size = iconRect.Size,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER
                });
            }

            static void DrawPlus(RectangleF rect, Color color, float scale, List<MySprite> sprites)
            {
                var plusLength = Math.Min(rect.Width, rect.Height) * 0.62f;
                var plusThickness = Math.Max(2f, 5f * scale);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = rect.Center,
                    Size = new Vector2(plusLength, plusThickness),
                    Color = color,
                    Alignment = TextAlignment.CENTER
                });
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = rect.Center,
                    Size = new Vector2(plusThickness, plusLength),
                    Color = color,
                    Alignment = TextAlignment.CENTER
                });
            }

            void EnsureTitleInput(RectangleF rect)
            {
                if (_titleInputModel == null)
                    _titleInputModel = new TextInputModel
                    {
                        Title = ButtonPadLocalization.Title,
                        Subtitle = ButtonPadLocalization.TitleHelp,
                        Placeholder = ButtonPadLocalization.Title,
                        Value = _draftEntry.Title ?? string.Empty,
                        ValueChanged = OnTitleChanged
                    };

                _titleInputModel.Title = ButtonPadLocalization.Title;
                _titleInputModel.Subtitle = ButtonPadLocalization.TitleHelp;
                _titleInputModel.Placeholder = ButtonPadLocalization.Title;
                _titleInputModel.Value = _draftEntry.Title ?? string.Empty;
                _titleInputModel.Enabled = true;
                _titleInputModel.ValueChanged = OnTitleChanged;

                if (_titleInput == null)
                    _titleInput = new TextInput(rect, _titleInputModel);
                else
                    _titleInput.SetRect(rect);

                _titleInput.SetDataContext(_titleInputModel);
                _titleInput.SetStyleId("Primary");
                _titleInput.OnSecondaryClick = OnTitleUnassigned;
                _titleInput.SetCursor(CursorType.Hand);
                _titleInput.SetVisible(true);
            }

            void EnsureColorButton(RectangleF rect)
            {
                if (_colorButton == null)
                    _colorButton = new Button(rect, new ButtonModel { Clicked = OnColorClicked });
                else
                    _colorButton.SetRect(rect);

                var model = _colorButton.DataContext as ButtonModel;
                if (model != null)
                {
                    model.Text = GetDraftBackgroundColor().ToHex();
                    model.Enabled = true;
                    model.Clicked = OnColorClicked;
                }

                _colorButton.SetStyleId("Primary");
                _colorButton.SetClass(CurrentDialogIsTiny
                    ? "ControlBase Button Input Compact"
                    : "ControlBase Button Input");
                _colorButton.CustomRender = RenderColorButton;
                _colorButton.OnSecondaryClick = OnColorUnassigned;
                _colorButton.SetCursor(CursorType.Hand);
                _colorButton.SetVisible(true);
            }

            void RenderColorButton(ControlTemplate control, List<MySprite> sprites)
            {
                var rect = control.Bounds;
                var hovered = rect.Contains(new Vector2(float.NaN, float.NaN));
                var button = control as Button;
                var defaultPanelColor = button?.BackgroundColor ?? control.BackgroundColor;
                var panelColor = hovered
                    ? control.GetResourceColor(ThemeResources.AccentColor, defaultPanelColor)
                    : defaultPanelColor;
                BorderRenderer.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: control.LayoutScale);

                var padding = 6f * control.LayoutScale;
                var swatchSize = Math.Max(1f, Math.Min(rect.Height - padding * 2f, 22f * control.LayoutScale));
                var swatchCenter = new Vector2(rect.X + padding + swatchSize * 0.5f, rect.Center.Y);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = swatchCenter,
                    Size = new Vector2(swatchSize, swatchSize),
                    Color = control.TextColor,
                    Alignment = TextAlignment.CENTER
                });
                var swatchInset = Math.Max(1f, 2f * control.LayoutScale);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = swatchCenter,
                    Size = new Vector2(
                        Math.Max(1f, swatchSize - swatchInset * 2f),
                        Math.Max(1f, swatchSize - swatchInset * 2f)),
                    Color = GetDraftBackgroundColor(),
                    Alignment = TextAlignment.CENTER
                });

                var textScale = 0.52f * control.LayoutScale * control.FontScale;
                var textHeight = FormatingHelper.LineHeight(textScale, control, control.TextSurface);
                var textX = swatchCenter.X + swatchSize * 0.5f + 7f * control.LayoutScale;
                var availableWidth = Math.Max(0f, rect.Right - textX - padding);
                var text = TrimText(GetDraftBackgroundColor().ToHex(), availableWidth, textScale, control.TextSurface);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = text,
                    Position = new Vector2(textX, rect.Center.Y - textHeight * 0.5f),
                    Color = control.TextColor,
                    FontId = TextFont,
                    RotationOrScale = textScale,
                    Alignment = TextAlignment.LEFT
                });
            }

            Color GetDraftBackgroundColor()
            {
                Color color;
                return Extensions.ColorExtensions.TryParseHexColor(_draftEntry.BackgroundColor, out color)
                    ? color
                    : ResolveColor(ThemeResources.AccentContainerColor);
            }

            void EnsurePickTargetButton(RectangleF rect)
            {
                if (_pickTargetButton == null)
                    _pickTargetButton = new Button(rect, new ButtonModel { Clicked = OnPickTargetClicked });
                else
                    _pickTargetButton.SetRect(rect);

                var model = _pickTargetButton.DataContext as ButtonModel;
                if (model != null)
                {
                    model.Text = GetPickTargetButtonText();
                    model.Enabled = _gridLogic != null;
                    model.Clicked = OnPickTargetClicked;
                }

                _pickTargetButton.SetStyleId("Primary");
                _pickTargetButton.CustomRender = RenderTextButton;
                _pickTargetButton.OnSecondaryClick = OnTargetUnassigned;
                _pickTargetButton.SetCursor(_gridLogic != null ? CursorType.Hand : CursorType.Default);
                _pickTargetButton.SetVisible(true);
            }

            void EnsureSelectActionButton(RectangleF rect)
            {
                if (_selectActionButton == null)
                    _selectActionButton = new Button(rect, new ButtonModel { Clicked = OnSelectActionClicked });
                else
                    _selectActionButton.SetRect(rect);

                var enabled = _draftEntry.Target != null;
                var model = _selectActionButton.DataContext as ButtonModel;
                if (model != null)
                {
                    model.Text = GetSelectActionButtonText();
                    model.Enabled = enabled;
                    model.Clicked = OnSelectActionClicked;
                }

                _selectActionButton.SetStyleId(enabled ? "Primary" : "Disabled");
                _selectActionButton.SetEnabled(enabled);
                _selectActionButton.CustomRender = RenderTextButton;
                _selectActionButton.OnSecondaryClick = OnActionUnassigned;
                _selectActionButton.SetCursor(enabled ? CursorType.Hand : CursorType.Default);
                _selectActionButton.SetVisible(true);
            }

            void EnsureApplyButton(RectangleF rect)
            {
                if (_applyButton == null)
                    _applyButton = new Button(rect, new ButtonModel { Text = ButtonPadLocalization.Apply, Clicked = OnApplyClicked });
                else
                    _applyButton.SetRect(rect);

                var model = _applyButton.DataContext as ButtonModel;
                if (model != null)
                {
                    model.Text = ButtonPadLocalization.Apply;
                    model.Enabled = true;
                    model.Clicked = OnApplyClicked;
                }

                _applyButton.SetStyleId("Primary");
                _applyButton.CustomRender = RenderTextButton;
                _applyButton.SetCursor(CursorType.Hand);
                _applyButton.SetVisible(true);
            }

            void EnsureDeleteButton(RectangleF rect)
            {
                if (_deleteButton == null)
                    _deleteButton = new Button(rect, new ButtonModel { Text = ButtonPadLocalization.Delete, Clicked = OnDeleteClicked });
                else
                    _deleteButton.SetRect(rect);

                var model = _deleteButton.DataContext as ButtonModel;
                if (model != null)
                {
                    model.Text = ButtonPadLocalization.Delete;
                    model.Enabled = true;
                    model.Clicked = OnDeleteClicked;
                }

                _deleteButton.SetStyleId("Danger");
                _deleteButton.CustomRender = RenderTextButton;
                _deleteButton.SetCursor(CursorType.Hand);
                _deleteButton.SetVisible(true);
            }

            void RenderTextButton(ControlTemplate control, List<MySprite> sprites)
            {
                var rect = control.Bounds;
                var buttonModel = control.DataContext as ButtonModel;
                var enabled = buttonModel == null || buttonModel.Enabled;
                var hovered = enabled && rect.Contains(new Vector2(float.NaN, float.NaN));
                var button = control as Button;
                var defaultPanelColor = button?.BackgroundColor ?? control.BackgroundColor;
                var panelColor = hovered
                    ? control.GetResourceColor(ThemeResources.AccentColor, defaultPanelColor)
                    : defaultPanelColor;
                BorderRenderer.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: control.LayoutScale);

                var text = buttonModel == null ? string.Empty : buttonModel.Text;
                var textScale = 0.52f * control.LayoutScale * control.FontScale;
                var availableWidth = Math.Max(0f, rect.Width - 12f * control.LayoutScale);
                var trimmed = TrimText(text, availableWidth, textScale, control.TextSurface);
                var textHeight = FormatingHelper.LineHeight(textScale, control, control.TextSurface);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = trimmed,
                    Position = new Vector2(rect.Center.X, rect.Center.Y - textHeight * 0.5f),
                    Color = control.TextColor,
                    FontId = TextFont,
                    RotationOrScale = textScale,
                    Alignment = TextAlignment.CENTER
                });
            }

            string GetPickTargetButtonText()
            {
                if (_draftEntry.Target == null || string.IsNullOrEmpty(_draftEntry.Target.DisplayName))
                    return ButtonPadLocalization.SelectTarget;
                return ButtonPadLocalization.Target(_draftEntry.Target.DisplayName);
            }

            string GetSelectActionButtonText()
            {
                if (_draftEntry.Target == null)
                    return ButtonPadLocalization.SelectAction;
                if (_draftEntry.Action == null || string.IsNullOrEmpty(_draftEntry.Action.DisplayName))
                    return ButtonPadLocalization.SelectAction;
                if (!string.IsNullOrEmpty(_draftEntry.Action.ParameterDisplayValue))
                    return ButtonPadLocalization.ActionValue(_draftEntry.Action.DisplayName, _draftEntry.Action.ParameterDisplayValue);
                return ButtonPadLocalization.Action(_draftEntry.Action.DisplayName);
            }

            void OnSelectedSpriteButtonClicked(ButtonModel model, object sender)
            {
                if (_showDialog == null)
                    return;

                _showDialog(new SpritePicker(
                    ParentApp,
                    delegate(string spriteName)
                    {
                        _draftEntry.SpriteName = spriteName;
                        RequestRender();
                    },
                    _requestRedraw,
                    _requestRedraw));
            }

            void OnSpriteUnassigned(object dataContext, object sender)
            {
                _draftEntry.SpriteName = null;
                RequestRender();
            }

            void OnTitleUnassigned(object dataContext, object sender)
            {
                _draftEntry.Title = string.Empty;
                if (_titleInputModel != null)
                    _titleInputModel.Value = string.Empty;
                RequestRender();
            }

            void OnColorUnassigned(object dataContext, object sender)
            {
                _draftEntry.BackgroundColor = null;
                RequestRender();
            }

            void OnTargetUnassigned(object dataContext, object sender)
            {
                _draftEntry.Target = null;
                _draftEntry.Action = null;
                RequestRender();
            }

            void OnActionUnassigned(object dataContext, object sender)
            {
                _draftEntry.Action = null;
                RequestRender();
            }

            void OnPickTargetClicked(ButtonModel model, object sender)
            {
                if (_showDialog == null || _gridLogic == null)
                    return;
                _showDialog(new PickActionTargetDialog(
                    ParentApp,
                    _gridLogic,
                    _draftEntry.Target?.ToPickResult(),
                    OnPickTargetSelected,
                    OnPickTargetCancelled,
                    _requestRedraw));
            }

            void OnPickTargetSelected(PickActionTargetResult target)
            {
                var oldKey = _draftEntry.Target?.CompatibilityKey;
                _draftEntry.Target = ButtonPanelTargetSettings.FromPickResult(target);
                var newKey = _draftEntry.Target?.CompatibilityKey;
                if (!string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
                    _draftEntry.Action = null;

                RequestRender();
            }

            void OnPickTargetCancelled()
            {
                _requestRedraw?.Invoke();
            }

            void OnSelectActionClicked(ButtonModel model, object sender)
            {
                if (_showDialog == null || _gridLogic == null || _draftEntry.Target == null)
                    return;
                _showDialog(new SelectActionDialog(
                    ParentApp,
                    _gridLogic,
                    _draftEntry.Target,
                    _draftEntry.Action,
                    OnActionSelected,
                    OnActionCancelled,
                    _requestRedraw));
            }

            void OnActionSelected(ButtonPanelActionSettings action)
            {
                var selectedAction = action?.Clone();
                if (selectedAction != null &&
                    _draftEntry.Action != null &&
                    string.Equals(selectedAction.BaseId, _draftEntry.Action.BaseId, StringComparison.OrdinalIgnoreCase))
                {
                    selectedAction.CopyParametersFrom(_draftEntry.Action);
                }

                if (selectedAction != null &&
                    ActionConfigurationDialog.RequiresConfiguration(_gridLogic, _draftEntry.Target, selectedAction))
                {
                    if (_showDialog != null)
                    {
                        _showDialog(new ActionConfigurationDialog(
                            ParentApp,
                            _gridLogic,
                            _draftEntry.Target,
                            selectedAction,
                            OnActionConfigured,
                            OnActionConfigurationCancelled,
                            _requestRedraw));
                    }
                    return;
                }

                _draftEntry.Action = selectedAction;
                RequestRender();
            }

            void OnActionConfigured(ButtonPanelActionSettings action)
            {
                _draftEntry.Action = action?.Clone();
                RequestRender();
            }

            void OnActionConfigurationCancelled()
            {
                _requestRedraw?.Invoke();
            }

            void OnActionCancelled()
            {
                _requestRedraw?.Invoke();
            }

            void OnTitleChanged(string value)
            {
                _draftEntry.Title = value ?? string.Empty;
                RequestRender();
            }

            void OnColorClicked(ButtonModel model, object sender)
            {
                TextInputHelper.SpawnForLocalPlayer(
                    ButtonPadLocalization.ButtonColor,
                    OnColorChanged,
                    GetDraftBackgroundColor().ToHex(),
                    ButtonPadLocalization.ColorHexHelp);
            }

            void OnColorChanged(string value)
            {
                Color color;
                if (Extensions.ColorExtensions.TryParseHexColor(value, out color))
                {
                    _draftEntry.BackgroundColor = color.ToHex();
                    RequestRender();
                    return;
                }

                if (MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.ShowNotification(ButtonPadLocalization.InvalidColor, 2500);
            }

            void OnApplyClicked(ButtonModel model, object sender)
            {
                Dismiss();
                if (_apply != null)
                    _apply(_draftEntry.Clone());
            }

            void OnDeleteClicked(ButtonModel model, object sender)
            {
                Dismiss();
                if (_apply != null)
                    _apply(null);
            }

            static float GetSpritePreviewTargetSize(float scale)
            {
                return Math.Max(32f, SPRITE_PREVIEW_SIZE_PIXELS * scale);
            }

            string TrimText(string text, float availableWidth, float fontSize, IMyTextSurface surface)
            {
                if (string.IsNullOrEmpty(text) || availableWidth <= 0f || surface == null)
                    return string.Empty;
                var size = FormatingHelper.GetSizeInPixel(text, TextFont, fontSize, surface);
                if (size.X <= availableWidth)
                    return text;
                return FormatingHelper.TrimName(text, Math.Max(1, (int)(text.Length * availableWidth / Math.Max(1f, size.X))));
            }

        }
    }
}
