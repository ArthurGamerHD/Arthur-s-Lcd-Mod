#if EXPERIMENTAL
using LcdMod.Client.Terminal.Models;
using LcdMod.Client.Terminal.Models.Actions;
using LcdMod.Client.Terminal.Models.Property;
#endif
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using IMyBlockGroup = Sandbox.ModAPI.Ingame.IMyBlockGroup;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using IMyIngameTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
{
    sealed class ActionConfigurationDialog : Dialog
    {
        const string TITLE = "Configure Action";
        const string TYPE_BOOLEAN = "Boolean";
        const string TYPE_STRING = "String";
        const string TYPE_INT64 = "Int64";
        const string TYPE_SINGLE = "Single";
        const string TYPE_COLOR = "Color";
        const string TYPE_STRING_BUILDER = "StringBuilder";
        const string TYPE_INCREASE_DECREASE = "IncreaseDecrease";
        const string BOOLEAN_ON = "on";
        const string BOOLEAN_OFF = "off";
        const string BOOLEAN_TOGGLE = "toggle";
        const string CLICK_INCREASE = "increase";
        const string CLICK_DECREASE = "decrease";
        const string SCROLL_NONE = "none";
        const string SCROLL_NORMAL = "normal";
        const string SCROLL_REVERSED = "reversed";
        static readonly string[] BooleanModes = { BOOLEAN_ON, BOOLEAN_OFF, BOOLEAN_TOGGLE };
        static readonly string[] ClickActions = { CLICK_INCREASE, CLICK_DECREASE };
        static readonly string[] ScrollModes = { SCROLL_NONE, SCROLL_NORMAL, SCROLL_REVERSED };

        readonly GridLogic _gridLogic;
        readonly ButtonPanelTargetSettings _target;
        readonly ButtonPanelActionSettings _action;
        readonly Action<ButtonPanelActionSettings> _selectedCallback;
        readonly Action _requestRedraw;
        readonly System.Collections.Generic.List<IMyBlockGroup> _groups = new System.Collections.Generic.List<IMyBlockGroup>();
        readonly System.Collections.Generic.List<IMyIngameTerminalBlock> _groupBlocks = new System.Collections.Generic.List<IMyIngameTerminalBlock>();

        NumericUpDown _numericInput;
        NumericUpDownModel _numericModel;
        TextInput _textInput;
        TextInputModel _textInputModel;
        Button _colorButton;
        readonly Button[] _booleanModeButtons = new Button[3];
        readonly Button[] _clickActionButtons = new Button[2];
        readonly System.Collections.Generic.List<Button> _scrollOptionButtons = new System.Collections.Generic.List<Button>();
        Button _scrollComboButton;
        Button _applyButton;


        string _parameterTypeName;
        string _parameterTitle;
        string _message;
        string _textValue;
        double _numberValue;
        double _numberMin;
        double _numberMax;
        double _numberStep = 1d;
        string _numberFormat = "0.###";
        Color _colorValue = Color.White;
        string _booleanMode = BOOLEAN_TOGGLE;
        string _clickAction = CLICK_INCREASE;
        string _scrollMode = SCROLL_NONE;
        bool _scrollComboOpen;
        RectangleF _scrollComboRect;
        bool _initialized;

        public ActionConfigurationDialog(
            IApp parentApp,
            GridLogic gridLogic,
            ButtonPanelTargetSettings target,
            ButtonPanelActionSettings action,
            Action<ButtonPanelActionSettings> selectedCallback,
            Action cancelCallback,
            Action requestRedraw)
            : base(parentApp)
        {
            _gridLogic = gridLogic;
            _target = target?.Clone();
            _action = action?.Clone();
            _selectedCallback = selectedCallback;
            var cancelCallback1 = cancelCallback;
            _requestRedraw = requestRedraw;
            OnClose = delegate
            {
                if (cancelCallback1 != null)
                    cancelCallback1();
            };
        }

        public static bool RequiresConfiguration(
            GridLogic gridLogic,
            ButtonPanelTargetSettings target,
            ButtonPanelActionSettings action)
        {
#if EXPERIMENTAL
            return GetParameterTypeName(ResolveCustomAction(action)) != null;
#else
            return false;
#endif
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
            EnsureInitialized();
            var layoutScale = scale * fontScale;
            var padding = new Vector2(18f * scale, 14f * scale);
            var spacing = 10f * scale;
            var smallSpacing = 6f * scale;
            var titleScale = 0.82f * layoutScale;
            var labelScale = 0.50f * layoutScale;
            var fieldTextScale = 0.56f * layoutScale;
            var titleHeight = MeasureLineHeight(titleScale, surface);
            var labelHeight = MeasureLineHeight(labelScale, surface);
            var fieldHeight = Math.Max(38f * scale, MeasureLineHeight(fieldTextScale, surface) + 18f * scale);
            var closeSize = GetDialogCloseButtonSize(scale);
            var headerHeight = Math.Max(titleHeight, closeSize.Y);

            var maxCardWidth = Math.Max(1f, viewBox.Width - padding.X * 2f);
            var cardWidth = Math.Min(Math.Max(370f * scale, viewBox.Width * 0.58f), maxCardWidth);
            var contentWidth = Math.Max(1f, cardWidth - padding.X * 2f);
            var controlRows = GetParameterControlRowCount();
            var contentHeight = labelHeight + smallSpacing +
                                controlRows * (labelHeight + fieldHeight) +
                                Math.Max(0, controlRows - 1) * smallSpacing +
                                spacing + fieldHeight;
            var cardHeight = Math.Min(
                padding.Y * 2f + headerHeight + spacing + contentHeight,
                Math.Max(1f, viewBox.Height - padding.Y * 2f));

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
                FontId = TextFont,
                RotationOrScale = titleScale,
                Alignment = TextAlignment.CENTER
            });

            var y = cardRect.Y + padding.Y + headerHeight + spacing;
            DrawInfoText(GetActionTitle(), cardRect.X + padding.X, y, contentWidth, labelScale, surface);
            y += labelHeight + smallSpacing;

            DrawInfoText(GetParameterCaption(), cardRect.X + padding.X, y, contentWidth, labelScale * 0.88f, surface);
            var fieldRect = new RectangleF(cardRect.X + padding.X, y + labelHeight, contentWidth, fieldHeight);
            RenderParameterControl(fieldRect, scale, surface);

            y = fieldRect.Bottom;
            if (NeedsScrollControl())
            {
                y += smallSpacing;
                DrawInfoText("Scroll", cardRect.X + padding.X, y, contentWidth, labelScale * 0.88f, surface);
                var scrollRect = new RectangleF(cardRect.X + padding.X, y + labelHeight, contentWidth, fieldHeight);
                RenderScrollCombo(scrollRect);
                y = scrollRect.Bottom;
            }

            var applyRect = new RectangleF(cardRect.X + padding.X, y + spacing, contentWidth, fieldHeight);
            EnsureApplyButton(applyRect);
            ContainerControl.AddChild(_applyButton);
            _applyButton.Render(Sprites);

            if (_scrollComboOpen)
                RenderScrollComboOptions(scale);
        }

        void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;
            _parameterTitle = _action == null ? string.Empty : (_action.DisplayName ?? _action.BaseId ?? string.Empty);
            _message = null;

#if EXPERIMENTAL
            var customAction = ResolveCustomAction(_action);
            _parameterTypeName = GetParameterTypeName(customAction);
            if (_parameterTypeName == null)
            {
                _message = "No configurable parameters";
                return;
            }

            var block = FindRepresentativeBlock(customAction);
            InitializeParameter(customAction, block);
#else
            _parameterTypeName = null;
            _message = "No configurable parameters";
#endif
        }

#if EXPERIMENTAL
        void InitializeParameter(ICustomAction customAction, IMyIngameTerminalBlock block)
        {
            var increaseDecreaseAction = customAction as IncreaseDecreaseAction;
            if (increaseDecreaseAction != null)
            {
                InitializeIncreaseDecreaseAction();
                return;
            }

            var onOffAction = customAction as OnOffAction;
            if (onOffAction != null)
            {
                InitializeOnOffAction();
                return;
            }

            var boolAction = customAction as PropertyCustomAction<bool>;
            if (boolAction != null)
            {
                InitializeBoolean();
                return;
            }

            var stringAction = customAction as PropertyCustomAction<string>;
            if (stringAction != null)
            {
                InitializeString(stringAction, block);
                return;
            }

            var int64Action = customAction as PropertyCustomAction<long>;
            if (int64Action != null)
            {
                InitializeInt64(int64Action, block);
                return;
            }

            var singleAction = customAction as PropertyCustomAction<float>;
            if (singleAction != null)
            {
                InitializeSingle(singleAction, block);
                return;
            }

            var colorAction = customAction as PropertyCustomAction<Color>;
            if (colorAction != null)
            {
                InitializeColor(colorAction, block);
                return;
            }

            var stringBuilderAction = customAction as PropertyCustomAction<StringBuilder>;
            if (stringBuilderAction != null)
            {
                InitializeStringBuilder(stringBuilderAction, block);
            }
        }

        void InitializeBoolean()
        {
            _parameterTypeName = TYPE_BOOLEAN;
            _booleanMode = HasExistingParameter(TYPE_BOOLEAN) ? NormalizeBooleanMode(_action.ParameterValue, BOOLEAN_TOGGLE) : BOOLEAN_TOGGLE;
        }

        void InitializeOnOffAction()
        {
            _parameterTypeName = TYPE_BOOLEAN;
            _booleanMode = HasExistingParameter(TYPE_BOOLEAN)
                ? NormalizeBooleanMode(_action.ParameterValue, BOOLEAN_TOGGLE)
                : BOOLEAN_TOGGLE;
        }

        void InitializeIncreaseDecreaseAction()
        {
            _parameterTypeName = TYPE_INCREASE_DECREASE;
            _clickAction = NormalizeClickAction(_action?.ClickAction, CLICK_INCREASE);
            _scrollMode = NormalizeScrollMode(_action?.ScrollMode, SCROLL_NONE);
        }

        void InitializeString(PropertyCustomAction<string> action, IMyIngameTerminalBlock block)
        {
            _parameterTypeName = TYPE_STRING;
            _textValue = HasExistingParameter(TYPE_STRING)
                ? _action.ParameterValue ?? string.Empty
                : GetValue(action, block, GetDefault(action, block, string.Empty));
        }

        void InitializeStringBuilder(PropertyCustomAction<StringBuilder> action, IMyIngameTerminalBlock block)
        {
            _parameterTypeName = TYPE_STRING_BUILDER;
            _textValue = HasExistingParameter(TYPE_STRING_BUILDER)
                ? _action.ParameterValue ?? string.Empty
                : GetValue(action, block, GetDefault(action, block, new StringBuilder())).ToString();
        }

        void InitializeInt64(PropertyCustomAction<long> action, IMyIngameTerminalBlock block)
        {
            _parameterTypeName = TYPE_INT64;
            _scrollMode = NormalizeScrollMode(_action?.ScrollMode, SCROLL_NONE);
            _numberFormat = "0";
            _numberStep = 1d;

            var min = GetMinimum(action, block, long.MinValue);
            var max = GetMaximum(action, block, long.MaxValue);
            SetNumberRange(min, max);

            long parsed;
            if (HasExistingParameter(TYPE_INT64) && _action != null &&
                long.TryParse(_action.ParameterValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                _numberValue = Clamp(parsed, _numberMin, _numberMax);
                return;
            }

            var fallback = ToInt64(Clamp(0d, _numberMin, _numberMax));
            _numberValue = Clamp(GetValue(action, block, GetDefault(action, block, fallback)), _numberMin, _numberMax);
        }

        void InitializeSingle(PropertyCustomAction<float> action, IMyIngameTerminalBlock block)
        {
            _parameterTypeName = TYPE_SINGLE;
            _scrollMode = NormalizeScrollMode(_action?.ScrollMode, SCROLL_NONE);
            _numberFormat = "0.###";

            var min = GetMinimum(action, block, float.MinValue);
            var max = GetMaximum(action, block, float.MaxValue);
            SetNumberRange(min, max);
            _numberStep = GetSingleStep(_numberMin, _numberMax);

            double parsed;
            if (HasExistingParameter(TYPE_SINGLE) &&
                _action != null &&
                double.TryParse(_action.ParameterValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                _numberValue = Clamp(parsed, _numberMin, _numberMax);
                return;
            }

            var fallback = (float)Clamp(0d, _numberMin, _numberMax);
            _numberValue = Clamp(GetValue(action, block, GetDefault(action, block, fallback)), _numberMin, _numberMax);
        }

        void InitializeColor(PropertyCustomAction<Color> action, IMyIngameTerminalBlock block)
        {
            _parameterTypeName = TYPE_COLOR;

            Color parsed;
            if (HasExistingParameter(TYPE_COLOR) && Extensions.ColorExtensions.TryParseHexColor(_action.ParameterValue, out parsed))
            {
                _colorValue = parsed;
                return;
            }

            _colorValue = GetValue(action, block, GetDefault(action, block, Color.White));
        }
#endif

        void RenderParameterControl(RectangleF fieldRect, float scale, IMyTextSurface surface)
        {
            if (string.IsNullOrEmpty(_parameterTypeName))
            {
                DrawMessage(fieldRect, scale, surface);
                return;
            }

            switch (_parameterTypeName)
            {
                case TYPE_INT64:
                case TYPE_SINGLE:
                    EnsureNumericInput(fieldRect);
                    ContainerControl.AddChild(_numericInput);
                    _numericInput.Render(Sprites);
                    return;
                case TYPE_STRING:
                case TYPE_STRING_BUILDER:
                    EnsureTextInput(fieldRect);
                    ContainerControl.AddChild(_textInput);
                    _textInput.Render(Sprites);
                    return;
                case TYPE_COLOR:
                    EnsureColorButton(fieldRect);
                    ContainerControl.AddChild(_colorButton);
                    _colorButton.Render(Sprites);
                    return;
                case TYPE_BOOLEAN:
                    RenderBooleanModeButtons(fieldRect, scale);
                    return;
                case TYPE_INCREASE_DECREASE:
                    RenderClickActionButtons(fieldRect, scale);
                    return;
                default:
                    DrawMessage(fieldRect, scale, surface);
                    break;
            }
        }

        void EnsureNumericInput(RectangleF rect)
        {
            if (_numericModel == null)
                _numericModel = new NumericUpDownModel { ValueChanged = OnNumberChanged };

            _numericModel.Title = GetParameterTitle();
            _numericModel.Subtitle = "Enter value";
            _numericModel.Enabled = true;
            _numericModel.MinValue = _numberMin;
            _numericModel.MaxValue = _numberMax;
            _numericModel.Step = _numberStep;
            _numericModel.Format = string.IsNullOrEmpty(_numberFormat) ? "0.###" : _numberFormat;
            _numericModel.Value = Clamp(_numberValue, _numberMin, _numberMax);
            _numericModel.ValueChanged = OnNumberChanged;

            if (_numericInput == null)
                _numericInput = new NumericUpDown(rect, _numericModel);
            else
                _numericInput.SetRect(rect);

            _numericInput.SetDataContext(_numericModel);
            _numericInput.SetStyleId("Primary");
            _numericInput.SetCursor(CursorType.Hand);
            _numericInput.SetVisible(true);
        }

        void EnsureTextInput(RectangleF rect)
        {
            if (_textInputModel == null)
                _textInputModel = new TextInputModel { ValueChanged = OnTextChanged };

            _textInputModel.Title = GetParameterTitle();
            _textInputModel.Subtitle = "Enter value";
            _textInputModel.Placeholder = GetParameterTitle();
            _textInputModel.Value = _textValue ?? string.Empty;
            _textInputModel.Enabled = true;
            _textInputModel.ValueChanged = OnTextChanged;

            if (_textInput == null)
                _textInput = new TextInput(rect, _textInputModel);
            else
                _textInput.SetRect(rect);

            _textInput.SetDataContext(_textInputModel);
            _textInput.SetStyleId("Primary");
            _textInput.SetCursor(CursorType.Hand);
            _textInput.SetVisible(true);
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
                model.Text = _colorValue.ToHex();
                model.Enabled = true;
                model.Clicked = OnColorClicked;
            }

            _colorButton.SetStyleId("Primary");
            _colorButton.SetClass("ControlBase Button Input");
            _colorButton.CustomRender = RenderColorButton;
            _colorButton.SetCursor(CursorType.Hand);
            _colorButton.SetVisible(true);
        }

        void EnsureApplyButton(RectangleF rect)
        {
            if (_applyButton == null)
                _applyButton = new Button(rect, new ButtonModel { Text = "Apply", Clicked = OnApplyClicked });
            else
                _applyButton.SetRect(rect);

            var model = _applyButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = "Apply";
                model.Enabled = true;
                model.Clicked = OnApplyClicked;
            }

            _applyButton.SetStyleId("Primary");
            _applyButton.SetClass("ControlBase Button");
            _applyButton.CustomRender = RenderTextButton;
            _applyButton.SetCursor(CursorType.Hand);
            _applyButton.SetVisible(true);
        }

        void RenderColorButton(ControlTemplate control, System.Collections.Generic.List<MySprite> sprites)
        {
            var rect = control.Bounds;
            rect.Contains(new Vector2(float.NaN, float.NaN));
            BorderRenderer.CreateSpritesFromRect(rect, sprites, control.BackgroundColor, radiusScale: control.LayoutScale);

            var padding = 6f * control.LayoutScale;
            var swatchSize = Math.Max(1f, rect.Height - padding * 2f);
            var swatchRect = new RectangleF(rect.X + padding, rect.Center.Y - swatchSize * 0.5f, swatchSize, swatchSize);
            BorderRenderer.CreateSpritesFromRect(swatchRect, sprites, _colorValue, radiusScale: control.LayoutScale);

            var textScale = 0.54f * control.LayoutScale * control.FontScale;
            var textHeight = FormatingHelper.LineHeight(textScale, control, control.TextSurface);
            var textX = swatchRect.Right + 8f * control.LayoutScale;
            var textWidth = Math.Max(0f, rect.Right - textX - padding);
            var text = TrimText(_colorValue.ToHex(), textWidth, textScale, control.TextSurface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(textX, rect.Center.Y - textHeight * 0.5f),
                Color = control.TextColor,
                FontId = control.TextFont,
                RotationOrScale = textScale,
                Alignment = TextAlignment.LEFT
            });
        }

        void RenderBooleanModeButtons(RectangleF rect, float scale)
        {
            var gap = 4f * scale;
            var buttonWidth = Math.Max(1f, (rect.Width - gap * 2f) / 3f);

            for (var i = 0; i < BooleanModes.Length; i++)
            {
                var buttonRect = new RectangleF(
                    rect.X + i * (buttonWidth + gap),
                    rect.Y,
                    i == BooleanModes.Length - 1 ? Math.Max(1f, rect.Right - (rect.X + i * (buttonWidth + gap))) : buttonWidth,
                    rect.Height);

                var button = EnsureBooleanModeButton(i, buttonRect, BooleanModes[i]);
                ContainerControl.AddChild(button);
                button.Render(Sprites);
            }
        }

        Button EnsureBooleanModeButton(int index, RectangleF rect, string mode)
        {
            var button = _booleanModeButtons[index];
            if (button == null)
            {
                button = new Button(rect, new BooleanModeButtonModel { Clicked = OnBooleanModeClicked });
                _booleanModeButtons[index] = button;
            }
            else
                button.SetRect(rect);

            var model = button.DataContext as BooleanModeButtonModel;
            if (model == null)
            {
                model = new BooleanModeButtonModel();
                button.SetDataContext(model);
            }

            model.Mode = mode;
            model.Text = GetBooleanModeLabel(mode);
            model.Enabled = true;
            model.Clicked = OnBooleanModeClicked;

            button.SetStyleId(string.Equals(_booleanMode, mode, StringComparison.OrdinalIgnoreCase) ? "Primary" : null);
            button.SetClass("ControlBase Button Choice");
            button.CustomRender = RenderBooleanModeButton;
            button.SetCursor(CursorType.Hand);
            button.SetVisible(true);
            return button;
        }

        void RenderBooleanModeButton(ControlTemplate control, System.Collections.Generic.List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var model = control.DataContext as BooleanModeButtonModel;
            var mode = model == null ? string.Empty : model.Mode;
            var selected = string.Equals(_booleanMode, mode, StringComparison.OrdinalIgnoreCase);
            rect.Contains(new Vector2(float.NaN, float.NaN));
            control.SetStyleId(selected ? "Primary" : null);
            var panelColor = selected
                ? ResolveColor(ThemeResources.AccentContainerColor)
                : control.BackgroundColor;
            var textColor = selected
                ? ResolveColor(ThemeResources.OnAccentContainerColor)
                : control.TextColor;

            BorderRenderer.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: control.LayoutScale);

            var textScale = 0.50f * control.LayoutScale * control.FontScale;
            var availableWidth = Math.Max(0f, rect.Width - 8f * control.LayoutScale);
            var trimmed = TrimText(GetBooleanModeLabel(mode), availableWidth, textScale, control.TextSurface);
            var textHeight = FormatingHelper.LineHeight(textScale, control, control.TextSurface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = trimmed,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textHeight * 0.5f),
                Color = textColor,
                FontId = control.TextFont,
                RotationOrScale = textScale,
                Alignment = TextAlignment.CENTER
            });
        }

        void RenderClickActionButtons(RectangleF rect, float scale)
        {
            var gap = 4f * scale;
            var buttonWidth = Math.Max(1f, (rect.Width - gap) / 2f);

            for (var i = 0; i < ClickActions.Length; i++)
            {
                var buttonRect = new RectangleF(
                    rect.X + i * (buttonWidth + gap),
                    rect.Y,
                    i == ClickActions.Length - 1 ? Math.Max(1f, rect.Right - (rect.X + i * (buttonWidth + gap))) : buttonWidth,
                    rect.Height);

                var button = EnsureClickActionButton(i, buttonRect, ClickActions[i]);
                ContainerControl.AddChild(button);
                button.Render(Sprites);
            }
        }

        Button EnsureClickActionButton(int index, RectangleF rect, string clickAction)
        {
            var button = _clickActionButtons[index];
            if (button == null)
            {
                button = new Button(rect, new ClickActionButtonModel { Clicked = OnClickActionClicked });
                _clickActionButtons[index] = button;
            }
            else
                button.SetRect(rect);

            var model = button.DataContext as ClickActionButtonModel;
            if (model == null)
            {
                model = new ClickActionButtonModel();
                button.SetDataContext(model);
            }

            model.ClickAction = clickAction;
            model.Text = GetClickActionLabel(clickAction);
            model.Enabled = true;
            model.Clicked = OnClickActionClicked;

            button.SetStyleId("Primary");
            button.SetClass("ControlBase Button Choice");
            button.CustomRender = RenderClickActionButton;
            button.SetCursor(CursorType.Hand);
            button.SetVisible(true);
            return button;
        }

        void RenderClickActionButton(ControlTemplate control, System.Collections.Generic.List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var model = control.DataContext as ClickActionButtonModel;
            var clickAction = model == null ? string.Empty : model.ClickAction;
            var selected = string.Equals(_clickAction, clickAction, StringComparison.OrdinalIgnoreCase);
            RenderChoiceButton(control, rect, GetClickActionLabel(clickAction), selected, sprites);
        }

        void RenderScrollCombo(RectangleF rect)
        {
            _scrollComboRect = rect;
            EnsureScrollComboButton(rect);
            ContainerControl.AddChild(_scrollComboButton);
            _scrollComboButton.Render(Sprites);
        }

        void EnsureScrollComboButton(RectangleF rect)
        {
            if (_scrollComboButton == null)
                _scrollComboButton = new Button(rect, new ButtonModel { Clicked = OnScrollComboClicked });
            else
                _scrollComboButton.SetRect(rect);

            var model = _scrollComboButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = GetScrollModeLabel(_scrollMode);
                model.Enabled = true;
                model.Clicked = OnScrollComboClicked;
            }

            _scrollComboButton.SetStyleId("Primary");
            _scrollComboButton.SetClass("ControlBase Button Combo");
            _scrollComboButton.CustomRender = RenderScrollComboButton;
            _scrollComboButton.SetCursor(CursorType.Hand);
            _scrollComboButton.SetVisible(true);
        }

        void RenderScrollComboButton(ControlTemplate control, System.Collections.Generic.List<MySprite> sprites)
        {
            var rect = control.Bounds;
            rect.Contains(new Vector2(float.NaN, float.NaN));
            BorderRenderer.CreateSpritesFromRect(rect, sprites, control.BackgroundColor, radiusScale: control.LayoutScale);

            var textScale = 0.54f * control.LayoutScale * control.FontScale;
            var label = TrimText(GetScrollModeLabel(_scrollMode), Math.Max(0f, rect.Width - 34f * control.LayoutScale), textScale, control.TextSurface);
            var textHeight = FormatingHelper.LineHeight(textScale, control, control.TextSurface);
            var textColor = control.TextColor;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = label,
                Position = new Vector2(rect.X + 10f * control.LayoutScale, rect.Center.Y - textHeight * 0.5f),
                Color = textColor,
                FontId = control.TextFont,
                RotationOrScale = textScale,
                Alignment = TextAlignment.LEFT
            });

            var arrowWidth = 9f * control.LayoutScale;
            var arrowHeight = 5f * control.LayoutScale;
            var arrowCenter = new Vector2(rect.Right - 14f * control.LayoutScale, rect.Center.Y);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Triangle",
                Position = arrowCenter,
                Size = new Vector2(arrowWidth, arrowHeight),
                Color = textColor,
                RotationOrScale = _scrollComboOpen ? 3.14159f : 0f,
                Alignment = TextAlignment.CENTER
            });
        }

        void RenderScrollComboOptions(float scale)
        {
            if (_scrollComboRect.Width <= 1f || _scrollComboRect.Height <= 1f)
                return;

            var rowHeight = _scrollComboRect.Height;
            var listRect = new RectangleF(_scrollComboRect.X, _scrollComboRect.Bottom + 2f * scale, _scrollComboRect.Width, rowHeight * ScrollModes.Length);
            BorderRenderer.CreateSpritesFromRect(listRect, Sprites, ResolveColor(ThemeResources.SurfaceContainerHighestColor), radiusScale: scale);

            for (var i = 0; i < ScrollModes.Length; i++)
            {
                var rect = new RectangleF(listRect.X, listRect.Y + i * rowHeight, listRect.Width, rowHeight);
                var button = EnsureScrollOptionButton(i, rect, ScrollModes[i]);
                ContainerControl.AddChild(button);
                button.Render(Sprites);
            }

            for (var i = ScrollModes.Length; i < _scrollOptionButtons.Count; i++)
                _scrollOptionButtons[i].SetVisible(false);
        }

        Button EnsureScrollOptionButton(int index, RectangleF rect, string scrollMode)
        {
            while (_scrollOptionButtons.Count <= index)
            {
                var button = new Button(default(RectangleF), new ScrollModeButtonModel { Clicked = OnScrollOptionClicked })
                    {
                        CustomRender = RenderScrollOptionButton
                    };
                _scrollOptionButtons.Add(button);
            }

            var optionButton = _scrollOptionButtons[index];
            var model = optionButton.DataContext as ScrollModeButtonModel;
            if (model == null)
            {
                model = new ScrollModeButtonModel();
                optionButton.SetDataContext(model);
            }

            model.ScrollMode = scrollMode;
            model.Text = GetScrollModeLabel(scrollMode);
            model.Enabled = true;
            model.Clicked = OnScrollOptionClicked;

            optionButton.SetRect(rect);
            optionButton.SetStyleId(string.Equals(_scrollMode, scrollMode, StringComparison.OrdinalIgnoreCase) ? "Primary" : null);
            optionButton.SetClass("ControlBase Button Choice");
            optionButton.CustomRender = RenderScrollOptionButton;
            optionButton.SetCursor(CursorType.Hand);
            optionButton.SetVisible(true);
            return optionButton;
        }

        void RenderScrollOptionButton(ControlTemplate control, System.Collections.Generic.List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var model = control.DataContext as ScrollModeButtonModel;
            var scrollMode = model == null ? string.Empty : model.ScrollMode;
            var selected = string.Equals(_scrollMode, scrollMode, StringComparison.OrdinalIgnoreCase);
            RenderChoiceButton(control, rect, GetScrollModeLabel(scrollMode), selected, sprites);
        }

        void RenderChoiceButton(ControlTemplate control, RectangleF rect, string text, bool selected, System.Collections.Generic.List<MySprite> sprites)
        {
            rect.Contains(new Vector2(float.NaN, float.NaN));
            control.SetStyleId(selected ? "Primary" : null);
            var panelColor = selected
                ? ResolveColor(ThemeResources.AccentContainerColor)
                : control.BackgroundColor;
            var textColor = selected
                ? ResolveColor(ThemeResources.OnAccentContainerColor)
                : control.TextColor;

            BorderRenderer.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: control.LayoutScale);

            var textScale = 0.50f * control.LayoutScale * control.FontScale;
            var availableWidth = Math.Max(0f, rect.Width - 8f * control.LayoutScale);
            var trimmed = TrimText(text, availableWidth, textScale, control.TextSurface);
            var textHeight = FormatingHelper.LineHeight(textScale, control, control.TextSurface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = trimmed,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textHeight * 0.5f),
                Color = textColor,
                FontId = control.TextFont,
                RotationOrScale = textScale,
                Alignment = TextAlignment.CENTER
            });
        }

        void RenderTextButton(ControlTemplate control, System.Collections.Generic.List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var buttonModel = control.DataContext as ButtonModel;
            BorderRenderer.CreateSpritesFromRect(rect, sprites, control.BackgroundColor, radiusScale: control.LayoutScale);

            var text = buttonModel == null ? string.Empty : buttonModel.Text;
            var textScale = 0.54f * control.LayoutScale * control.FontScale;
            var availableWidth = Math.Max(0f, rect.Width - 12f * control.LayoutScale);
            var trimmed = TrimText(text, availableWidth, textScale, control.TextSurface);
            var textHeight = FormatingHelper.LineHeight(textScale, control, control.TextSurface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = trimmed,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textHeight * 0.5f),
                Color = control.TextColor,
                FontId = control.TextFont,
                RotationOrScale = textScale,
                Alignment = TextAlignment.CENTER
            });
        }

        void OnNumberChanged(double value)
        {
            _numberValue = Clamp(value, _numberMin, _numberMax);
            if (_requestRedraw != null)
                _requestRedraw();
        }

        void OnTextChanged(string value)
        {
            _textValue = value ?? string.Empty;
            if (_requestRedraw != null)
                _requestRedraw();
        }

        void OnColorClicked(ButtonModel model, object sender)
        {
            TextInputHelper.SpawnForLocalPlayer(
                "Color",
                OnColorTextChanged,
                _colorValue.ToHex(),
                "Hex color, for example #ff8800");
        }

        void OnColorTextChanged(string value)
        {
            Color parsed;
            if (Extensions.ColorExtensions.TryParseHexColor(value, out parsed))
            {
                _colorValue = parsed;
                if (_requestRedraw != null)
                    _requestRedraw();
                return;
            }

            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.ShowNotification("Invalid color. Use #RRGGBB.", 2500);
        }

        void OnBooleanModeClicked(ButtonModel model, object sender)
        {
            var booleanModel = model as BooleanModeButtonModel;
            if (booleanModel == null)
                return;

            _booleanMode = NormalizeBooleanMode(booleanModel.Mode, BOOLEAN_TOGGLE);
            if (_requestRedraw != null)
                _requestRedraw();
        }

        void OnClickActionClicked(ButtonModel model, object sender)
        {
            var clickModel = model as ClickActionButtonModel;
            if (clickModel == null)
                return;

            _clickAction = NormalizeClickAction(clickModel.ClickAction, CLICK_INCREASE);
            if (_requestRedraw != null)
                _requestRedraw();
        }

        void OnScrollComboClicked(ButtonModel model, object sender)
        {
            _scrollComboOpen = !_scrollComboOpen;
            if (_requestRedraw != null)
                _requestRedraw();
        }

        void OnScrollOptionClicked(ButtonModel model, object sender)
        {
            var scrollModel = model as ScrollModeButtonModel;
            if (scrollModel == null)
                return;

            _scrollMode = NormalizeScrollMode(scrollModel.ScrollMode, SCROLL_NONE);
            _scrollComboOpen = false;
            if (_requestRedraw != null)
                _requestRedraw();
        }

        void OnApplyClicked(ButtonModel model, object sender)
        {
            var result = _action == null ? new ButtonPanelActionSettings() : _action.Clone();
            ApplyParameter(result);

            Dismiss();
            if (_selectedCallback != null)
                _selectedCallback(result);
        }

        void ApplyParameter(ButtonPanelActionSettings result)
        {
            if (result == null)
                return;

            result.ParameterTypeName = _parameterTypeName;
            result.ClickAction = null;
            result.ScrollMode = null;

            if (_parameterTypeName == TYPE_INT64)
            {
                var value = ToInt64(Clamp(_numberValue, _numberMin, _numberMax));
                result.ParameterValue = value.ToString(CultureInfo.InvariantCulture);
                result.ScrollMode = NormalizeScrollMode(_scrollMode, SCROLL_NONE);
                result.ParameterDisplayValue = FormatDisplayWithScroll(result.ParameterValue);
                return;
            }

            if (_parameterTypeName == TYPE_SINGLE)
            {
                var value = (float)Clamp(_numberValue, _numberMin, _numberMax);
                result.ParameterValue = value.ToString("R", CultureInfo.InvariantCulture);
                result.ScrollMode = NormalizeScrollMode(_scrollMode, SCROLL_NONE);
                result.ParameterDisplayValue = FormatDisplayWithScroll(value.ToString(string.IsNullOrEmpty(_numberFormat) ? "0.###" : _numberFormat, CultureInfo.InvariantCulture));
                return;
            }

            if (_parameterTypeName == TYPE_STRING || _parameterTypeName == TYPE_STRING_BUILDER)
            {
                result.ParameterValue = _textValue ?? string.Empty;
                result.ParameterDisplayValue = result.ParameterValue;
                return;
            }

            if (_parameterTypeName == TYPE_COLOR)
            {
                result.ParameterValue = _colorValue.ToHex();
                result.ParameterDisplayValue = result.ParameterValue;
                return;
            }

            if (_parameterTypeName == TYPE_BOOLEAN)
            {
                result.ParameterValue = NormalizeBooleanMode(_booleanMode, BOOLEAN_TOGGLE);
                result.ParameterDisplayValue = GetBooleanModeLabel(result.ParameterValue);
                return;
            }

            if (_parameterTypeName == TYPE_INCREASE_DECREASE)
            {
                result.ClickAction = NormalizeClickAction(_clickAction, CLICK_INCREASE);
                result.ScrollMode = NormalizeScrollMode(_scrollMode, SCROLL_NONE);
                result.ParameterValue = result.ClickAction;
                result.ParameterDisplayValue = FormatDisplayWithScroll(GetClickActionLabel(result.ClickAction));
                return;
            }

            result.ParameterValue = null;
            result.ParameterDisplayValue = null;
        }

        void DrawBackdrop(IMyTextSurface surface, float scale, RectangleF cardRect)
        {
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = surface.TextureSize / 2f,
                Size = surface.TextureSize,
                Color = new Color(0, 0, 0, 150),
                Alignment = TextAlignment.CENTER
            });

            BorderRenderer.CreateSpritesFromRect(new RectangleF(cardRect.Position + 3f * scale, cardRect.Size), Sprites,
                ResolveColor(ThemeResources.ShadowColor), radiusScale: scale);
            BorderRenderer.CreateSpritesFromRect(cardRect, Sprites,
                ResolveColor(ThemeResources.SurfaceContainerHighColor), radiusScale: scale);
        }

        void DrawInfoText(string text, float x, float y, float width, float textScale, IMyTextSurface surface)
        {
            var trimmed = TrimText(text, width, textScale, surface);
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = trimmed,
                Position = new Vector2(x, y),
                Color = ResolveColor(ThemeResources.OnSurfaceColor),
                FontId = TextFont,
                RotationOrScale = textScale,
                Alignment = TextAlignment.LEFT
            });
        }

        void DrawMessage(RectangleF rect, float scale, IMyTextSurface surface)
        {
            BorderRenderer.CreateSpritesFromRect(rect, Sprites, ResolveColor(ThemeResources.SurfaceContainerColor), radiusScale: scale);

            var text = string.IsNullOrEmpty(_message) ? "No configurable parameters" : _message;
            var textScale = 0.46f * scale * surface.FontSize;
            var textHeight = MeasureLineHeight(textScale, surface);
            var trimmed = TrimText(text, Math.Max(0f, rect.Width - 12f * scale), textScale, surface);

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = trimmed,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textHeight * 0.5f),
                Color = ResolveColor(ThemeResources.OnSurfaceColor),
                FontId = TextFont,
                RotationOrScale = textScale,
                Alignment = TextAlignment.CENTER
            });
        }

        string GetActionTitle()
        {
            if (_action == null)
                return string.Empty;

            return string.IsNullOrWhiteSpace(_action.DisplayName) ? _action.BaseId ?? string.Empty : _action.DisplayName;
        }

        string GetParameterTitle()
        {
            if (!string.IsNullOrWhiteSpace(_parameterTitle))
                return _parameterTitle;

            return string.IsNullOrWhiteSpace(_parameterTypeName) ? "Parameter" : _parameterTypeName;
        }

        string GetParameterCaption()
        {
            if (_parameterTypeName == TYPE_INT64 || _parameterTypeName == TYPE_SINGLE)
                return "Number value";
            if (_parameterTypeName == TYPE_STRING || _parameterTypeName == TYPE_STRING_BUILDER)
                return "Text value";
            if (_parameterTypeName == TYPE_COLOR)
                return "Color value";
            if (_parameterTypeName == TYPE_BOOLEAN)
                return "Boolean mode";
            if (_parameterTypeName == TYPE_INCREASE_DECREASE)
                return "Click Action";

            return "Parameter";
        }

        int GetParameterControlRowCount()
        {
            return NeedsScrollControl() ? 2 : 1;
        }

        bool NeedsScrollControl()
        {
            return _parameterTypeName == TYPE_INCREASE_DECREASE ||
                   _parameterTypeName == TYPE_INT64 ||
                   _parameterTypeName == TYPE_SINGLE;
        }

        bool HasExistingParameter(string typeName)
        {
            return _action != null &&
                   string.Equals(_action.ParameterTypeName, typeName, StringComparison.OrdinalIgnoreCase) &&
                   _action.ParameterValue != null;
        }

        void SetNumberRange(double min, double max)
        {
            if (double.IsNaN(min))
                min = double.MinValue;
            if (double.IsNaN(max))
                max = double.MaxValue;

            if (min > max)
            {
                var temp = min;
                min = max;
                max = temp;
            }

            _numberMin = min;
            _numberMax = max;
        }

        static double GetSingleStep(double min, double max)
        {
            if (!IsReasonableFinite(min) || !IsReasonableFinite(max) || max <= min)
                return 1d;

            return Math.Max(0.001d, (max - min) / 100d);
        }

        static bool IsReasonableFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) < 1000000000000d;
        }

        static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        static long ToInt64(double value)
        {
            if (value <= long.MinValue)
                return long.MinValue;
            if (value >= long.MaxValue)
                return long.MaxValue;
            return (long)Math.Round(value);
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

        static string NormalizeBooleanMode(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            if (string.Equals(value, BOOLEAN_ON, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                return BOOLEAN_ON;

            if (string.Equals(value, BOOLEAN_OFF, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                return BOOLEAN_OFF;

            if (string.Equals(value, BOOLEAN_TOGGLE, StringComparison.OrdinalIgnoreCase))
                return BOOLEAN_TOGGLE;

            return fallback;
        }

        static string GetBooleanModeLabel(string mode)
        {
            mode = NormalizeBooleanMode(mode, BOOLEAN_TOGGLE);
            if (mode == BOOLEAN_ON)
                return "On";
            if (mode == BOOLEAN_OFF)
                return "Off";
            return "Toggle";
        }

        static string NormalizeClickAction(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            if (string.Equals(value, CLICK_INCREASE, StringComparison.OrdinalIgnoreCase))
                return CLICK_INCREASE;

            if (string.Equals(value, CLICK_DECREASE, StringComparison.OrdinalIgnoreCase))
                return CLICK_DECREASE;

            return fallback;
        }

        static string GetClickActionLabel(string clickAction)
        {
            clickAction = NormalizeClickAction(clickAction, CLICK_INCREASE);
            return clickAction == CLICK_DECREASE ? "Decrease" : "Increase";
        }

        static string NormalizeScrollMode(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            if (string.Equals(value, SCROLL_NONE, StringComparison.OrdinalIgnoreCase))
                return SCROLL_NONE;

            if (string.Equals(value, SCROLL_NORMAL, StringComparison.OrdinalIgnoreCase))
                return SCROLL_NORMAL;

            if (string.Equals(value, SCROLL_REVERSED, StringComparison.OrdinalIgnoreCase))
                return SCROLL_REVERSED;

            return fallback;
        }

        static string GetScrollModeLabel(string scrollMode)
        {
            scrollMode = NormalizeScrollMode(scrollMode, SCROLL_NONE);
            if (scrollMode == SCROLL_NORMAL)
                return "Normal";
            if (scrollMode == SCROLL_REVERSED)
                return "Reversed";
            return "None";
        }

        string FormatDisplayWithScroll(string primary)
        {
            var scrollMode = NormalizeScrollMode(_scrollMode, SCROLL_NONE);
            if (scrollMode == SCROLL_NONE)
                return primary ?? string.Empty;

            return (primary ?? string.Empty) + ", Scroll: " + GetScrollModeLabel(scrollMode);
        }

        sealed class BooleanModeButtonModel : ButtonModel
        {
            public string Mode { get; set; }
        }

        sealed class ClickActionButtonModel : ButtonModel
        {
            public string ClickAction { get; set; }
        }

        sealed class ScrollModeButtonModel : ButtonModel
        {
            public string ScrollMode { get; set; }
        }

#if EXPERIMENTAL
        static ICustomAction ResolveCustomAction(ButtonPanelActionSettings action)
        {
            if (action == null || string.IsNullOrEmpty(action.BaseId))
                return null;

            ICustomAction customAction;
            return ActionHelper.CustomActions.TryGetValue(action.BaseId, out customAction) ? customAction : null;
        }

        static string GetParameterTypeName(ICustomAction customAction)
        {
            if (customAction is IncreaseDecreaseAction)
                return TYPE_INCREASE_DECREASE;
            if (customAction is OnOffAction)
                return TYPE_BOOLEAN;
            if (customAction is PropertyCustomAction<bool>)
                return TYPE_BOOLEAN;
            if (customAction is PropertyCustomAction<string>)
                return TYPE_STRING;
            if (customAction is PropertyCustomAction<long>)
                return TYPE_INT64;
            if (customAction is PropertyCustomAction<float>)
                return TYPE_SINGLE;
            if (customAction is PropertyCustomAction<Color>)
                return TYPE_COLOR;
            if (customAction is PropertyCustomAction<StringBuilder>)
                return TYPE_STRING_BUILDER;

            return null;
        }

        IMyIngameTerminalBlock FindRepresentativeBlock(ICustomAction action)
        {
            if (action == null || _target == null)
                return null;

            switch ((PickActionTargetKind)_target.Kind)
            {
                case PickActionTargetKind.Block:
                    return FindBlock(_target.Id);
                case PickActionTargetKind.Group:
                    return FindGroupBlock(action, _target.Id);
                case PickActionTargetKind.BlockType:
                    return FindTypeBlock(action, FindRegisteredType(_target.TypeName ?? _target.Id));
                case PickActionTargetKind.BlockSubtype:
                    return FindSubtypeBlock(action, _target.Id);
                default:
                    return null;
            }
        }

        IMyIngameTerminalBlock FindBlock(string id)
        {
            long entityId;
            if (_gridLogic == null || !long.TryParse(id, out entityId))
                return null;

            var blocks = _gridLogic.GetTerminalBlocks<IMyTerminalBlock>();
            return blocks?.FirstOrDefault(block => block != null && block.EntityId == entityId);
        }

        IMyIngameTerminalBlock FindGroupBlock(ICustomAction action, string groupName)
        {
            if (_gridLogic == null || _gridLogic.Grid == null || string.IsNullOrEmpty(groupName))
                return null;

            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(_gridLogic.Grid);
            if (terminalSystem == null)
                return null;

            _groups.Clear();
            terminalSystem.GetBlockGroups(_groups);
            foreach (var group in _groups.Where(group => group != null && string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase)))
            {
                _groupBlocks.Clear();
                group.GetBlocks(_groupBlocks);
                foreach (var block in _groupBlocks.Where(block => IsActionCompatibleWithBlock(action, block)))
                    return block;
            }

            return null;
        }

        IMyIngameTerminalBlock FindTypeBlock(ICustomAction action, Type targetType)
        {
            if (_gridLogic == null || targetType == null)
                return null;

            var blocks = _gridLogic.GetTerminalBlocks<IMyTerminalBlock>();
            return blocks?.Where(block => block != null && IsTypeMatch(targetType, block.GetType())).FirstOrDefault(block => IsActionCompatibleWithBlock(action, block));
        }

        IMyIngameTerminalBlock FindSubtypeBlock(ICustomAction action, string subtype)
        {
            if (_gridLogic == null || string.IsNullOrEmpty(subtype))
                return null;

            var blocks = _gridLogic.GetTerminalBlocks<IMyTerminalBlock>();
            if (blocks == null)
                return null;

            return (from block in blocks where block != null let cubeBlock = block as MyCubeBlock where cubeBlock?.BlockDefinition != null where string.Equals(cubeBlock.BlockDefinition.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase) select block).FirstOrDefault(block => IsActionCompatibleWithBlock(action, block));
        }

        bool IsActionCompatibleWithBlock(ICustomAction action, IMyIngameTerminalBlock block)
        {
            if (action == null || block == null)
                return false;

            return IsActionCompatibleWithType(action, block.GetType()) && action.Enabled(block);
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

        bool IsTypeMatch(Type expectedType, Type actualType)
        {
            if (expectedType == null || actualType == null)
                return false;

            if (string.Equals(expectedType.FullName, actualType.FullName, StringComparison.Ordinal))
                return true;

            return MyAPIGateway.Reflection.IsAssignableFrom(expectedType, actualType) ||
                   MyAPIGateway.Reflection.IsAssignableFrom(actualType, expectedType);
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
        
        static T GetMinimum<T>(PropertyCustomAction<T> action, IMyIngameTerminalBlock block, T fallback)
        {
            try
            {
                if (action?.Property != null && block != null)
                    return action.Property.GetMinimum(block);
            }
            catch
            {
                // fallback
            }

            return fallback;
        }
        
        static T GetMaximum<T>(PropertyCustomAction<T> action, IMyIngameTerminalBlock block, T fallback)
        {
            try
            {
                if (action?.Property != null && block != null)
                    return action.Property.GetMaximum(block);
            }
            catch
            {
                // fallback
            }

            return fallback;
        }
        
        static T GetValue<T>(PropertyCustomAction<T> action, IMyIngameTerminalBlock block, T fallback)
        {
            try
            {
                if (action?.Property != null && block != null)
                    return action.Property.GetValue(block);
            }
            catch
            {
                // fallback
            }

            return fallback;
        }

        static T GetDefault<T>(PropertyCustomAction<T> action, IMyIngameTerminalBlock block, T fallback)
        {
            try
            {
                if (action?.Property != null && block != null)
                    return action.Property.GetDefaultValue(block);
            }
            catch
            {
                // fallback
            }

            return fallback;
        }
#endif
    }
}
