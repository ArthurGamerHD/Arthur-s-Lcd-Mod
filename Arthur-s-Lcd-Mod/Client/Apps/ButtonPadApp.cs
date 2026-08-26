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
    [LcdApp(23, Name = "ButtonPanel")]
    [ConfigComponent(Constants.APP, typeof(ButtonPanelConfigComponent), PropertyName = "ButtonPanelComponent")]
    public sealed partial class ButtonPadApp : App, IApp
    {
        const string CUSTOM_DATA_KEY = "Buttonpanel";
        const string TYPE_BOOLEAN = "Boolean";
        const string TYPE_INT64 = "Int64";
        const string TYPE_SINGLE = "Single";
        const string TYPE_INCREASE_DECREASE = "IncreaseDecrease";
        const string BOOLEAN_ON = "on";
        const string BOOLEAN_OFF = "off";
        const string BOOLEAN_TOGGLE = "toggle";
        const string CLICK_INCREASE = "increase";
        const string CLICK_DECREASE = "decrease";
        const string SCROLL_NONE = "none";
        const string SCROLL_NORMAL = "normal";
        const string SCROLL_REVERSED = "reversed";

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<Control> _children = new List<Control>();
        readonly List<int> _renderEntryIndices = new List<int>();
        readonly Dictionary<int, ButtonPanelEntrySettings> _entries = new Dictionary<int, ButtonPanelEntrySettings>();
        readonly ScrollPanel _scrollPanel;
        readonly VirtualizedWrapPanel<int> _entryPanel = new VirtualizedWrapPanel<int>();
        readonly List<IMyBlockGroup> _actionGroups = new List<IMyBlockGroup>();
        readonly List<IMyIngameTerminalBlock> _actionGroupBlocks = new List<IMyIngameTerminalBlock>();
        readonly StringBuilder _actionStatusBuilder = new StringBuilder();

        int _lastLayoutColumns = 1;
        float _lastButtonSize = ButtonPanelLayout.PreferredButtonSizePixels;

        public ButtonPadApp(IAppHost host) : base(host)
        {
            NeedGridData(GridCapability.Blocks);
            _scrollPanel = AddLogicalChild(new ScrollPanel());
            _scrollPanel.ManualScrollInertiaEnabled = false;
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _entryPanel.CreateControl = CreateEntryButton;
            _entryPanel.BindControl = BindEntryButton;
            LoadButtonPanelSettings();
            EnsureButtonTexturesLoaded();
        }

        public override IReadOnlyList<Control> VisualChildren => _children;

        float Scale => GeneralComponent.GetScale();

        float FontScale => Host.Surface.FontSize;

        float LayoutScale => Scale * FontScale;

        RectangleF ViewBox => Host.ViewBox;

        public override void Update()
        {
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            LoadButtonPanelSettings();
            EnsureButtonTexturesLoaded();
            _entryPanel.InvalidateLayout();
            MarkDirty();
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();
            ClearInteractiveTree();

            var contentTop = GetContentTop();
            DrawEntries(_sprites, contentTop);

            return _sprites;
        }

        public override void OnMouseScroll(int delta, ref bool handled)
        {
            if (handled || !_scrollPanel.CanScroll)
                return;

            handled = _scrollPanel.Scroll(this, delta);
        }

        void BuildRenderEntryIndices(int buttonCount)
        {
            _renderEntryIndices.Clear();

            for (var index = 0; index < Math.Max(1, buttonCount); index++)
                _renderEntryIndices.Add(index);
        }

        int GetMinimumConfiguredButtonCount()
        {
            var highestConfiguredIndex = -1;
            foreach (var entry in _entries)
            {
                if (entry.Key > highestConfiguredIndex && entry.Value != null && entry.Value.HasContent())
                    highestConfiguredIndex = entry.Key;
            }

            return Math.Max(ButtonPanelLayout.MinimumButtonCount, highestConfiguredIndex + 1);
        }

        void DrawEntries(List<MySprite> sprites, float contentTop)
        {
            if (ViewBox.Width <= 0f || ViewBox.Height <= 0f)
                return;

            var availableHeight = Math.Max(0f, ViewBox.Bottom - contentTop);
            if (availableHeight <= 0f)
                return;

            var spacing = ButtonPanelLayout.SpacingPixels * Scale;
            var layout = ButtonPanelLayout.Create(
                ButtonPanelComponent.ButtonCount,
                ViewBox.Width,
                availableHeight,
                ButtonPanelLayout.PreferredButtonSizePixels * Scale,
                spacing,
                GetMinimumConfiguredButtonCount());
            BuildRenderEntryIndices(layout.ButtonCount);

            _lastLayoutColumns = Math.Max(1, layout.Columns);
            _lastButtonSize = Math.Max(1f, layout.ButtonSize);
            var panelRect = new RectangleF(
                ViewBox.X,
                contentTop,
                ViewBox.Width,
                availableHeight);

            _scrollPanel.SetContent(_entryPanel);
            _entryPanel.ItemsSource = _renderEntryIndices;
            _entryPanel.RowHeight = layout.CellHeight;
            _entryPanel.MinimumColumnWidth = Math.Max(1f, layout.CellWidth - 0.01f);
            _entryPanel.HorizontalGap = 0f;
            _entryPanel.VerticalGap = 0f;
            _entryPanel.InvalidateLayout();

            _scrollPanel.AutoScrollSecondsPerStep = 0f;
            _scrollPanel.ConfigureAutomatic(panelRect, 0f, layout.CellHeight);

            _scrollPanel.SetVisible(true);
            _children.Add(_scrollPanel);

            _scrollPanel.Render(sprites);
            ClearDirtyAfterRender();
        }

        ButtonPanelEntrySettings GetEntry(int index, bool create)
        {
            if (index < 0)
                return null;

            ButtonPanelEntrySettings entry;
            if (_entries.TryGetValue(index, out entry))
                return entry;

            if (!create)
                return null;

            entry = new ButtonPanelEntrySettings { Index = index };
            _entries[index] = entry;
            return entry;
        }

        static string GetEntrySpriteName(ButtonPanelEntrySettings entry)
        {
            if (entry == null)
                return null;

            return !string.IsNullOrEmpty(entry.SpriteName)
                ? entry.SpriteName
                : entry.Target?.SpriteName;
        }

        void ApplyEntry(int index, ButtonPanelEntrySettings entry)
        {
            if (index < 0)
                return;

            if (entry == null || !entry.HasContent())
                _entries.Remove(index);
            else
            {
                var copy = entry.Clone();
                copy.Index = index;
                _entries[index] = copy;
            }

            SaveButtonPanelSettings();
            MarkDirty();
            _entryPanel.InvalidateLayout();
            Host.RenderSprites();
        }

        void LoadButtonPanelSettings()
        {
            _entries.Clear();

            try
            {
                var data = GeneralComponent.GetCustomData(CUSTOM_DATA_KEY);
                if (data == null || data.Length == 0)
                    return;

                var settings = MyAPIGateway.Utilities.SerializeFromBinary<ButtonPanelSettings>(data);
                if (settings == null)
                    return;

                if (settings.Entries == null)
                    return;

                for (var i = 0; i < settings.Entries.Length; i++)
                {
                    var entry = settings.Entries[i];
                    if (entry == null || entry.Index < 0 || !entry.HasContent())
                        continue;

                    _entries[entry.Index] = entry.Clone();
                }
            }
            catch
            {
                _entries.Clear();
            }
        }

        void EnsureButtonTexturesLoaded()
        {
            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                if (entry == null)
                    continue;

                EnsureBlockTextureLoaded(entry.SpriteName);

                var targetSpriteName = entry.Target?.SpriteName;
                if (!string.Equals(entry.SpriteName, targetSpriteName, StringComparison.Ordinal))
                    EnsureBlockTextureLoaded(targetSpriteName);
            }
        }

        static void EnsureBlockTextureLoaded(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName))
                return;

            string registeredSpriteName;
            TextureHelper.TryGetOrAddTextureForBlockName(spriteName, out registeredSpriteName);
        }

        void SaveButtonPanelSettings()
        {
            var list = new List<ButtonPanelEntrySettings>();
            foreach (var entry in _entries)
            {
                if (entry.Value == null || !entry.Value.HasContent())
                    continue;

                var copy = entry.Value.Clone();
                copy.Index = entry.Key;
                list.Add(copy);
            }

            var settings = new ButtonPanelSettings
            {
                EntryCount = Math.Max(ButtonPanelLayout.MinimumButtonCount, _renderEntryIndices.Count),
                Entries = list.ToArray()
            };

            GeneralComponent.SetCustomData(CUSTOM_DATA_KEY, MyAPIGateway.Utilities.SerializeToBinary(settings));

            var terminalBlock = Host.Block as IMyTerminalBlock;
            if (terminalBlock != null)
                ConfigManager.Sync(terminalBlock, Host.ProviderConfig);
        }

        void ClearInteractiveTree()
        {
            _children.Clear();
            _scrollPanel.SetVisible(false);
        }

        ControlTemplate CreateEntryButton(int entryIndex)
        {
            return new PadButton(
                default(RectangleF),
                new PadButtonModel
                {
                    Text = string.Empty,
                    Clicked = OnPadButtonClicked
                }
            );
        }

        void BindEntryButton(ControlTemplate control, int entryIndex, int visibleIndex)
        {
            var button = control as Button;
            if (button == null)
                return;

            var columns = Math.Max(1, _lastLayoutColumns);
            ConfigureEntryButton(
                button,
                default(RectangleF),
                entryIndex,
                visibleIndex / columns,
                visibleIndex % columns);
        }

        void ConfigureEntryButton(Button button, RectangleF rect, int index, int row, int column)
        {
            var model = button.DataContext as PadButtonModel;
            if (model == null)
            {
                model = new PadButtonModel();
                button.SetDataContext(model);
            }

            model.Index = index;
            model.Row = row;
            model.Column = column;
            var entry = GetEntry(index, false);
            model.Configured = entry != null && entry.HasContent();
            model.SpriteName = GetEntrySpriteName(entry);
            model.Title = entry?.Title;
            model.BackgroundColor = entry?.BackgroundColor;
            model.Status = GetEntryStatus(entry);
            model.Text = string.Empty;
            model.Enabled = true;
            model.Clicked = OnPadButtonClicked;
            model.OnSecondaryClick = null;
            model.OnScroll = null;

            button.SetRect(rect);
            button.SetVisible(true);
            button.SetCursor(CursorType.Hand);
            button.SetStyleId("Primary");
            button.CustomRender = RenderEntryButton;
            button.OnSecondaryClick = OnPadButtonSecondaryClicked;
            button.OnScroll = IsEntryScrollEnabled(entry) ? (ControlScrollHandler)OnPadButtonScrolled : null;
        }

        void RenderEntryButton(ControlTemplate control, List<MySprite> sprites)
        {
            var rect = CenterSquare(control.Bounds, _lastButtonSize);
            var hovered = control.IsPointerOver;
            var button = control as Button;
            var model = control.DataContext as PadButtonModel;
            var buttonStyle = GetButtonPanelStyle();
            var isConfigured = model?.Configured ?? false;
            Color customPanelColor;
            var hasCustomPanelColor = Extensions.ColorExtensions.TryParseHexColor(model?.BackgroundColor, out customPanelColor);
            var defaultPanelColor = hasCustomPanelColor
                ? customPanelColor
                : button?.BackgroundColor ?? control.BackgroundColor;
            var panelColor = hovered
                ? hasCustomPanelColor
                    ? GetHoverColor(defaultPanelColor)
                    : control.GetResourceColor(ThemeResources.AccentColor, defaultPanelColor)
                : defaultPanelColor;
            var useDisabledColor = !isConfigured &&
                                   (buttonStyle == ButtonPanelStyle.Default ||
                                    buttonStyle == ButtonPanelStyle.Border);
            if (useDisabledColor)
            {
                panelColor = control.GetResourceColor(
                    ThemeResources.SurfaceContainerLowColor,
                    defaultPanelColor);
            }

            var titleColor = buttonStyle == ButtonPanelStyle.Default
                ? hasCustomPanelColor ? GetContrastingTextColor(panelColor) : control.TextColor
                : panelColor.DeriveAccentColor();

            RenderButtonBackground(
                control,
                sprites,
                rect,
                panelColor,
                buttonStyle,
                hovered);

            var spriteName = model?.SpriteName;
            var title = model?.Title;
            var status = model?.Status;

            var showText = buttonStyle != ButtonPanelStyle.Transparent;
            var hasTitle = showText && !string.IsNullOrWhiteSpace(title);
            var hasStatus = showText && !string.IsNullOrWhiteSpace(status);
            var titleAreaHeight = hasTitle ? Math.Max(12f, rect.Height * 0.22f) : 0f;
            var statusAreaHeight = hasStatus ? Math.Max(12f, rect.Height * 0.22f) : 0f;
            var iconArea = new RectangleF(
                rect.X,
                rect.Y + titleAreaHeight,
                rect.Width,
                Math.Max(1f, rect.Height - titleAreaHeight - statusAreaHeight)
            );

            var iconSize = Math.Max(1f, Math.Min(iconArea.Width, iconArea.Height) * BUTTON_OCUPANCY);
            var iconRect = new RectangleF(
                iconArea.Center.X - iconSize * 0.5f,
                iconArea.Center.Y - iconSize * 0.5f,
                iconSize,
                iconSize
            );

            if (!string.IsNullOrEmpty(spriteName))
            {
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
            else if (buttonStyle == ButtonPanelStyle.Transparent)
            {
                var plusLength = Math.Min(iconRect.Width, iconRect.Height) * 0.5f;
                var plusThickness = Math.Max(2f, 6f * control.LayoutScale);
                var plusColor = control.GetResourceColor(ThemeResources.DisabledColor, Color.Gray);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = iconRect.Center,
                    Size = new Vector2(plusLength, plusThickness),
                    Color = plusColor,
                    Alignment = TextAlignment.CENTER
                });

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = iconRect.Center,
                    Size = new Vector2(plusThickness, plusLength),
                    Color = plusColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            var padButton = control as PadButton;
            if (padButton == null)
                return;

            var borderInset = buttonStyle == ButtonPanelStyle.Border
                ? 2f * control.LayoutScale
                : 0f;
            var textInset = 4f * control.LayoutScale + borderInset;
            var textWidth = Math.Max(0f, rect.Width - textInset * 2f);
            RenderAdaptiveButtonText(
                padButton.TitleText,
                hasTitle ? title : null,
                new RectangleF(
                    rect.X + textInset,
                    rect.Y + textInset,
                    textWidth,
                    Math.Max(0f, titleAreaHeight - textInset)),
                titleColor,
                control,
                sprites);
            RenderAdaptiveButtonText(
                padButton.StatusText,
                hasStatus ? status : null,
                new RectangleF(
                    rect.X + textInset,
                    rect.Bottom - statusAreaHeight,
                    textWidth,
                    Math.Max(0f, statusAreaHeight - textInset)),
                titleColor,
                control,
                sprites);
        }

        static void RenderAdaptiveButtonText(
            FitTextControl textControl,
            string text,
            RectangleF rect,
            Color color,
            ControlTemplate owner,
            List<MySprite> sprites)
        {
            if (textControl == null)
                return;

            var clampedText = ClampButtonText(text);
            var hasText = clampedText.Length > 0;
            textControl.Text = clampedText;
            textControl.TextColor = color;
            textControl.MinFontScale = 0.01f;
            textControl.MaxFontScale = 6f * owner.FontScale;
            textControl.SetRect(hasText ? rect : default(RectangleF));
            textControl.Render(sprites);
        }

        static string ClampButtonText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var trimmed = text.Trim();
            if (trimmed.Length <= BUTTON_TEXT_MAX_LENGTH)
                return trimmed;

            return trimmed.Substring(0, BUTTON_TEXT_MAX_LENGTH - 1) + FormatingHelper.ELLIPSIS;
        }

        ButtonPanelStyle GetButtonPanelStyle()
        {
            switch ((ButtonPanelStyle)ButtonPanelComponent.ButtonStyle)
            {
                case ButtonPanelStyle.Classic:
                    return ButtonPanelStyle.Classic;
                case ButtonPanelStyle.Transparent:
                    return ButtonPanelStyle.Transparent;
                case ButtonPanelStyle.Border:
                    return ButtonPanelStyle.Border;
                default:
                    return ButtonPanelStyle.Default;
            }
        }

        static void RenderButtonBackground(
            ControlTemplate control,
            List<MySprite> sprites,
            RectangleF rect,
            Color panelColor,
            ButtonPanelStyle buttonStyle,
            bool hovered)
        {
            switch (buttonStyle)
            {
                case ButtonPanelStyle.Classic:
                    var classicBackground = new Color(
                        panelColor.R,
                        panelColor.G,
                        panelColor.B,
                        hovered ? (byte)255 : (byte)220);
                    AddSquare(sprites, rect, classicBackground);

                    var indicatorColor = panelColor.DeriveAccentColor(0.8f, 1.5);
                    indicatorColor.A = byte.MaxValue;
                    var indicatorHeight = Math.Min(rect.Height, Math.Max(1f, 1f * control.LayoutScale));
                    AddSquare(sprites, new RectangleF(rect.X, rect.Y, rect.Width, indicatorHeight), indicatorColor);
                    return;

                case ButtonPanelStyle.Transparent:
                    return;

                case ButtonPanelStyle.Border:
                    RenderSquareBorder(sprites, rect, panelColor, 2f * control.LayoutScale);
                    return;

                default:
                    var shadowColor = control.GetResourceColor(
                        ThemeResources.ShadowColor,
                        new Color(0, 0, 0, 160));
                    BorderRenderer.CreateSpritesFromRect(
                        new RectangleF(rect.Position + 2f * control.LayoutScale, rect.Size),
                        sprites,
                        shadowColor,
                        radiusScale: control.LayoutScale);
                    BorderRenderer.CreateSpritesFromRect(
                        rect,
                        sprites,
                        panelColor,
                        radiusScale: control.LayoutScale);
                    return;
            }
        }

        static void RenderSquareBorder(
            List<MySprite> sprites,
            RectangleF rect,
            Color color,
            float requestedThickness)
        {
            var thickness = Math.Min(
                Math.Min(rect.Width, rect.Height) * 0.5f,
                Math.Max(1f, requestedThickness));
            AddSquare(sprites, new RectangleF(rect.X, rect.Y, rect.Width, thickness), color);
            AddSquare(sprites, new RectangleF(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
            AddSquare(sprites, new RectangleF(rect.X, rect.Y, thickness, rect.Height), color);
            AddSquare(sprites, new RectangleF(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
        }

        static void AddSquare(List<MySprite> sprites, RectangleF rect, Color color)
        {
            if (rect.Width <= 0f || rect.Height <= 0f || color.A == 0)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = rect.Center,
                Size = rect.Size,
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        const int BUTTON_TEXT_MAX_LENGTH = 16;
        const float BUTTON_OCUPANCY = 0.9f;

        void OnPadButtonClicked(ButtonModel model, object sender)
        {
            var padModel = model as PadButtonModel;
            var index = padModel?.Index ?? -1;
            var initialEntry = GetEntry(index, false);

            if (HasConfiguredAction(initialEntry) && TryRunEntryAction(initialEntry))
            {
                Host.RenderSprites();
                return;
            }

            OpenEntryEditor(index);
        }

        void OnPadButtonSecondaryClicked(object dataContext, object sender)
        {
            var padModel = dataContext as PadButtonModel;
            OpenEntryEditor(padModel?.Index ?? -1);
        }

        bool OnPadButtonScrolled(object dataContext, object sender, int delta)
        {
            var padModel = dataContext as PadButtonModel;
            var entry = GetEntry(padModel?.Index ?? -1, false);

            if (!IsEntryScrollEnabled(entry))
                return false;

            var handled = TryRunEntryScrollAction(entry, delta);
            if (handled)
                Host.RenderSprites();

            return handled;
        }

        void OpenEntryEditor(int index)
        {
            var interactiveHost = Host as InteractiveSurfaceScript;
            if (interactiveHost == null)
                return;

            var initialEntry = GetEntry(index, false);

            interactiveHost.ShowDialog(new ButtonPadEntryDialog(
                this,
                Host.GridLogic,
                index,
                initialEntry?.Clone(),
                entry =>
                {
                    ApplyEntry(index, entry);
                },
                dialog => interactiveHost.ShowDialog(dialog),
                Host.RenderSprites
            ));
        }

        static bool HasConfiguredAction(ButtonPanelEntrySettings entry)
        {
            return entry != null &&
                   entry.Target != null &&
                   entry.Action != null &&
                   !string.IsNullOrEmpty(entry.Action.BaseId);
        }

        static bool IsEntryScrollEnabled(ButtonPanelEntrySettings entry)
        {
            if (!HasConfiguredAction(entry) || entry.Action == null)
                return false;

            var scrollMode = NormalizeScrollMode(entry.Action.ScrollMode, SCROLL_NONE);
            if (scrollMode == SCROLL_NONE)
                return false;

            return string.Equals(entry.Action.ParameterTypeName, TYPE_INCREASE_DECREASE, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entry.Action.ParameterTypeName, TYPE_INT64, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entry.Action.ParameterTypeName, TYPE_SINGLE, StringComparison.OrdinalIgnoreCase);
        }

        bool TryRunEntryAction(ButtonPanelEntrySettings entry)
        {
            return TryRunConfiguredAction(entry, false, 0);
        }

        bool TryRunEntryScrollAction(ButtonPanelEntrySettings entry, int delta)
        {
            if (delta == 0)
                return false;

            return TryRunConfiguredAction(entry, true, delta);
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

        string GetEntryStatus(ButtonPanelEntrySettings entry)
        {
            if (!HasConfiguredAction(entry))
                return null;

            ICustomAction customAction;
            if (!ActionHelper.CustomActions.TryGetValue(entry.Action.BaseId, out customAction))
                return null;

            var valueWriter = customAction as ICustomActionValueWriter;
            if (valueWriter == null)
                return null;

            var block = FindStatusBlock(entry.Target, customAction);
            if (block == null)
                return null;

            try
            {
                _actionStatusBuilder.Clear();
                valueWriter.WriteValue(block, _actionStatusBuilder);
                return NormalizeStatusText(_actionStatusBuilder);
            }
            catch
            {
                _actionStatusBuilder.Clear();
                return null;
            }
        }

        static string NormalizeStatusText(StringBuilder text)
        {
            if (text == null || text.Length == 0)
                return null;

            var value = text.ToString().Trim();
            if (value.Length == 0)
                return null;

            var carriageReturn = value.IndexOf('\r');
            var lineFeed = value.IndexOf('\n');
            var lineEnd = carriageReturn < 0
                ? lineFeed
                : lineFeed < 0
                    ? carriageReturn
                    : Math.Min(carriageReturn, lineFeed);
            return lineEnd < 0 ? value : value.Substring(0, lineEnd).Trim();
        }

        IMyIngameTerminalBlock FindStatusBlock(
            ButtonPanelTargetSettings target,
            ICustomAction customAction)
        {
            if (target == null || customAction == null)
                return null;

            switch ((PickActionTargetKind)target.Kind)
            {
                case PickActionTargetKind.Block:
                {
                    var block = FindBlock(target.Id);
                    return CanWriteStatus(customAction, block) ? block : null;
                }
                case PickActionTargetKind.Group:
                    return FindGroupStatusBlock(target.Id, customAction);
                case PickActionTargetKind.BlockType:
                    return FindTypeStatusBlock(
                        FindRegisteredType(target.TypeName ?? target.Id),
                        customAction);
                case PickActionTargetKind.BlockSubtype:
                    return FindSubtypeStatusBlock(target.Id, customAction);
                default:
                    return null;
            }
        }

        IMyIngameTerminalBlock FindGroupStatusBlock(string groupName, ICustomAction customAction)
        {
            if (Host.GridLogic == null || Host.GridLogic.Grid == null || string.IsNullOrEmpty(groupName))
                return null;

            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(Host.GridLogic.Grid);
            if (terminalSystem == null)
                return null;

            _actionGroups.Clear();
            terminalSystem.GetBlockGroups(_actionGroups);
            for (var groupIndex = 0; groupIndex < _actionGroups.Count; groupIndex++)
            {
                var group = _actionGroups[groupIndex];
                if (group == null || !string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase))
                    continue;

                _actionGroupBlocks.Clear();
                group.GetBlocks(_actionGroupBlocks);
                for (var blockIndex = 0; blockIndex < _actionGroupBlocks.Count; blockIndex++)
                {
                    var block = _actionGroupBlocks[blockIndex];
                    if (CanWriteStatus(customAction, block))
                        return block;
                }
            }

            return null;
        }

        IMyIngameTerminalBlock FindTypeStatusBlock(Type targetType, ICustomAction customAction)
        {
            if (Host.GridLogic == null || targetType == null)
                return null;

            var blocks = Host.GridLogic.Blocks.TerminalBlocks;
            if (blocks == null)
                return null;

            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block != null && IsTypeMatch(targetType, block.GetType()) && CanWriteStatus(customAction, block))
                    return block;
            }

            return null;
        }

        IMyIngameTerminalBlock FindSubtypeStatusBlock(string subtype, ICustomAction customAction)
        {
            if (Host.GridLogic == null || string.IsNullOrEmpty(subtype))
                return null;

            var blocks = Host.GridLogic.Blocks.TerminalBlocks;
            if (blocks == null)
                return null;

            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                var cubeBlock = block as MyCubeBlock;
                if (cubeBlock == null || cubeBlock.BlockDefinition == null ||
                    !string.Equals(cubeBlock.BlockDefinition.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (CanWriteStatus(customAction, block))
                    return block;
            }

            return null;
        }

        bool CanWriteStatus(ICustomAction customAction, IMyIngameTerminalBlock block)
        {
            return customAction != null && block != null &&
                   IsActionCompatibleWithType(customAction, block.GetType());
        }

        bool TryRunConfiguredAction(ButtonPanelEntrySettings entry, bool scroll, int delta)
        {
            if (!HasConfiguredAction(entry))
                return false;

            ICustomAction customAction;
            if (!ActionHelper.CustomActions.TryGetValue(entry.Action.BaseId, out customAction) || customAction == null)
            {
                NotifyActionFailure(ButtonPadLocalization.ActionUnavailable);
                return true;
            }

            try
            {
                if (!ApplyActionToTarget(entry.Target, entry.Action, customAction, scroll, delta))
                    NotifyActionFailure(ButtonPadLocalization.NoCompatibleTarget);
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Error, "Button panel action failed: " + e);
                NotifyActionFailure(ButtonPadLocalization.ActionFailed);
            }

            return true;
        }

        bool ApplyActionToTarget(
            ButtonPanelTargetSettings target,
            ButtonPanelActionSettings settings,
            ICustomAction customAction,
            bool scroll,
            int delta)
        {
            if (target == null || customAction == null)
                return false;

            switch ((PickActionTargetKind)target.Kind)
            {
                case PickActionTargetKind.Block:
                    return TryApplyActionToBlock(customAction, settings, FindBlock(target.Id), scroll, delta);
                case PickActionTargetKind.Group:
                    return ApplyActionToGroup(target.Id, customAction, settings, scroll, delta);
                case PickActionTargetKind.BlockType:
                    return ApplyActionToType(FindRegisteredType(target.TypeName ?? target.Id), customAction, settings, scroll, delta);
                case PickActionTargetKind.BlockSubtype:
                    return ApplyActionToSubtype(target.Id, customAction, settings, scroll, delta);
                default:
                    return false;
            }
        }

        bool ApplyActionToGroup(
            string groupName,
            ICustomAction customAction,
            ButtonPanelActionSettings settings,
            bool scroll,
            int delta)
        {
            if (Host.GridLogic == null || Host.GridLogic.Grid == null || string.IsNullOrEmpty(groupName))
                return false;

            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(Host.GridLogic.Grid);
            if (terminalSystem == null)
                return false;

            var applied = false;
            _actionGroups.Clear();
            terminalSystem.GetBlockGroups(_actionGroups);
            for (var i = 0; i < _actionGroups.Count; i++)
            {
                var group = _actionGroups[i];
                if (group == null || !string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase))
                    continue;

                _actionGroupBlocks.Clear();
                group.GetBlocks(_actionGroupBlocks);
                for (var blockIndex = 0; blockIndex < _actionGroupBlocks.Count; blockIndex++)
                {
                    if (TryApplyActionToBlock(customAction, settings, _actionGroupBlocks[blockIndex], scroll, delta))
                        applied = true;
                }
            }

            return applied;
        }

        bool ApplyActionToType(
            Type targetType,
            ICustomAction customAction,
            ButtonPanelActionSettings settings,
            bool scroll,
            int delta)
        {
            if (Host.GridLogic == null || targetType == null)
                return false;

            var blocks = Host.GridLogic.Blocks.TerminalBlocks;
            if (blocks == null)
                return false;

            var applied = false;
            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null || !IsTypeMatch(targetType, block.GetType()))
                    continue;

                if (TryApplyActionToBlock(customAction, settings, block, scroll, delta))
                    applied = true;
            }

            return applied;
        }

        bool ApplyActionToSubtype(
            string subtype,
            ICustomAction customAction,
            ButtonPanelActionSettings settings,
            bool scroll,
            int delta)
        {
            if (Host.GridLogic == null || string.IsNullOrEmpty(subtype))
                return false;

            var blocks = Host.GridLogic.Blocks.TerminalBlocks;
            if (blocks == null)
                return false;

            var applied = false;
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

                if (TryApplyActionToBlock(customAction, settings, block, scroll, delta))
                    applied = true;
            }

            return applied;
        }

        IMyIngameTerminalBlock FindBlock(string id)
        {
            long entityId;
            if (Host.GridLogic == null || !long.TryParse(id, out entityId))
                return null;

            var blocks = Host.GridLogic.Blocks.TerminalBlocks;
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

        bool TryApplyActionToBlock(
            ICustomAction customAction,
            ButtonPanelActionSettings settings,
            IMyIngameTerminalBlock block,
            bool scroll,
            int delta)
        {
            if (!IsActionCompatibleWithBlock(customAction, block))
                return false;

            return scroll
                ? ApplyScrollActionToBlock(customAction, settings, block, delta)
                : ApplyClickActionToBlock(customAction, settings, block);
        }

        bool ApplyClickActionToBlock(ICustomAction customAction, ButtonPanelActionSettings settings, IMyIngameTerminalBlock block)
        {
            var increaseDecreaseAction = customAction as IncreaseDecreaseAction;
            if (increaseDecreaseAction != null)
            {
                var clickAction = NormalizeClickAction(settings?.ClickAction, CLICK_INCREASE);
                return ApplyTerminalAction(
                    clickAction == CLICK_DECREASE ? increaseDecreaseAction.Decrease : increaseDecreaseAction.Increase,
                    block);
            }

            var onOffAction = customAction as OnOffAction;
            if (onOffAction != null)
            {
                var mode = NormalizeBooleanMode(settings?.ParameterValue, BOOLEAN_TOGGLE);
                if (mode == BOOLEAN_ON)
                    return ApplyTerminalAction(onOffAction.On, block);
                if (mode == BOOLEAN_OFF)
                    return ApplyTerminalAction(onOffAction.Off, block);
                return ApplyTerminalAction(onOffAction.Action, block);
            }

            var boolProperty = customAction as PropertyCustomAction<bool>;
            if (boolProperty != null)
                return SetBooleanProperty(boolProperty, settings, block);

            var stringProperty = customAction as PropertyCustomAction<string>;
            if (stringProperty != null)
                return SetStringProperty(stringProperty, settings, block);

            var stringBuilderProperty = customAction as PropertyCustomAction<StringBuilder>;
            if (stringBuilderProperty != null)
                return SetStringBuilderProperty(stringBuilderProperty, settings, block);

            var int64Property = customAction as PropertyCustomAction<long>;
            if (int64Property != null)
                return SetInt64Property(int64Property, settings, block);

            var singleProperty = customAction as PropertyCustomAction<float>;
            if (singleProperty != null)
                return SetSingleProperty(singleProperty, settings, block);

            var colorProperty = customAction as PropertyCustomAction<Color>;
            if (colorProperty != null)
                return SetColorProperty(colorProperty, settings, block);

            var terminalAction = customAction as CustomAction;
            return terminalAction != null && ApplyTerminalAction(terminalAction.Action, block);
        }

        bool ApplyScrollActionToBlock(
            ICustomAction customAction,
            ButtonPanelActionSettings settings,
            IMyIngameTerminalBlock block,
            int delta)
        {
            var scrollMode = NormalizeScrollMode(settings?.ScrollMode, SCROLL_NONE);
            if (scrollMode == SCROLL_NONE || delta == 0)
                return false;

            var direction = delta > 0 ? 1 : -1;
            if (scrollMode == SCROLL_REVERSED)
                direction = -direction;

            var increaseDecreaseAction = customAction as IncreaseDecreaseAction;
            if (increaseDecreaseAction != null)
                return ApplyTerminalAction(direction < 0 ? increaseDecreaseAction.Decrease : increaseDecreaseAction.Increase, block);

            var int64Property = customAction as PropertyCustomAction<long>;
            if (int64Property != null)
                return ScrollInt64Property(int64Property, block, direction);

            var singleProperty = customAction as PropertyCustomAction<float>;
            if (singleProperty != null)
                return ScrollSingleProperty(singleProperty, block, direction);

            return false;
        }

        static bool ApplyTerminalAction(ITerminalAction action, IMyIngameTerminalBlock block)
        {
            if (action == null || block == null || !action.IsEnabled(block))
                return false;

            action.Apply(block);
            return true;
        }

        static bool SetBooleanProperty(
            PropertyCustomAction<bool> action,
            ButtonPanelActionSettings settings,
            IMyIngameTerminalBlock block)
        {
            if (action == null || action.Property == null || block == null)
                return false;

            var mode = NormalizeBooleanMode(settings?.ParameterValue, BOOLEAN_TOGGLE);
            var value = mode == BOOLEAN_TOGGLE
                ? !action.Property.GetValue(block)
                : mode == BOOLEAN_ON;

            action.Property.SetValue(block, value);
            return true;
        }

        static bool SetStringProperty(
            PropertyCustomAction<string> action,
            ButtonPanelActionSettings settings,
            IMyIngameTerminalBlock block)
        {
            if (action == null || action.Property == null || block == null)
                return false;

            action.Property.SetValue(block, settings == null ? string.Empty : settings.ParameterValue ?? string.Empty);
            return true;
        }

        static bool SetStringBuilderProperty(
            PropertyCustomAction<StringBuilder> action,
            ButtonPanelActionSettings settings,
            IMyIngameTerminalBlock block)
        {
            if (action == null || action.Property == null || block == null)
                return false;

            action.Property.SetValue(block, new StringBuilder(settings == null ? string.Empty : settings.ParameterValue ?? string.Empty));
            return true;
        }

        static bool SetInt64Property(
            PropertyCustomAction<long> action,
            ButtonPanelActionSettings settings,
            IMyIngameTerminalBlock block)
        {
            if (action == null || action.Property == null || block == null)
                return false;

            long value;
            if (settings == null ||
                !long.TryParse(settings.ParameterValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return false;

            action.Property.SetValue(block, Clamp(value, GetInt64Minimum(action, block), GetInt64Maximum(action, block)));
            return true;
        }

        static bool SetSingleProperty(
            PropertyCustomAction<float> action,
            ButtonPanelActionSettings settings,
            IMyIngameTerminalBlock block)
        {
            if (action == null || action.Property == null || block == null)
                return false;

            float value;
            if (settings == null ||
                !float.TryParse(settings.ParameterValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;

            action.Property.SetValue(block, MathHelper.Clamp(value, GetSingleMinimum(action, block), GetSingleMaximum(action, block)));
            return true;
        }

        static bool SetColorProperty(
            PropertyCustomAction<Color> action,
            ButtonPanelActionSettings settings,
            IMyIngameTerminalBlock block)
        {
            if (action == null || action.Property == null || block == null || settings == null)
                return false;

            Color color;
            if (!Extensions.ColorExtensions.TryParseHexColor(settings.ParameterValue, out color))
                return false;

            action.Property.SetValue(block, color);
            return true;
        }

        static bool ScrollInt64Property(PropertyCustomAction<long> action, IMyIngameTerminalBlock block, int direction)
        {
            if (action == null || action.Property == null || block == null)
                return false;

            var min = GetInt64Minimum(action, block);
            var max = GetInt64Maximum(action, block);
            var value = action.Property.GetValue(block);
            var nextValue = direction < 0
                ? value <= min ? min : value - 1L
                : value >= max ? max : value + 1L;

            action.Property.SetValue(block, Clamp(nextValue, min, max));
            return true;
        }

        static bool ScrollSingleProperty(PropertyCustomAction<float> action, IMyIngameTerminalBlock block, int direction)
        {
            if (action == null || action.Property == null || block == null)
                return false;

            var min = GetSingleMinimum(action, block);
            var max = GetSingleMaximum(action, block);
            var step = GetSingleStep(min, max);
            var value = action.Property.GetValue(block) + (direction < 0 ? -step : step);

            action.Property.SetValue(block, MathHelper.Clamp(value, min, max));
            return true;
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

        static long GetInt64Minimum(PropertyCustomAction<long> action, IMyIngameTerminalBlock block)
        {
            try
            {
                if (action != null && action.Property != null && block != null)
                    return action.Property.GetMinimum(block);
            }
            catch
            {
            }

            return long.MinValue;
        }

        static long GetInt64Maximum(PropertyCustomAction<long> action, IMyIngameTerminalBlock block)
        {
            try
            {
                if (action != null && action.Property != null && block != null)
                    return action.Property.GetMaximum(block);
            }
            catch
            {
            }

            return long.MaxValue;
        }

        static float GetSingleMinimum(PropertyCustomAction<float> action, IMyIngameTerminalBlock block)
        {
            try
            {
                if (action != null && action.Property != null && block != null)
                    return action.Property.GetMinimum(block);
            }
            catch
            {
            }

            return float.MinValue;
        }

        static float GetSingleMaximum(PropertyCustomAction<float> action, IMyIngameTerminalBlock block)
        {
            try
            {
                if (action != null && action.Property != null && block != null)
                    return action.Property.GetMaximum(block);
            }
            catch
            {
            }

            return float.MaxValue;
        }

        static float GetSingleStep(float min, float max)
        {
            if (!IsReasonableFinite(min) || !IsReasonableFinite(max) || max <= min)
                return 1f;

            return Math.Max(0.001f, (max - min) / 100f);
        }

        static bool IsReasonableFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) < 1000000000000f;
        }

        static long Clamp(long value, long min, long max)
        {
            if (min > max)
            {
                var temp = min;
                min = max;
                max = temp;
            }

            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        static void NotifyActionFailure(string message)
        {
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.ShowNotification(message, 1500);
        }

        void OnScrollPanelChanged(ScrollPanel panel)
        {
            Host.RenderSprites();
        }

        float GetContentTop()
        {
            return Host.TitleVisible
                ? ViewBox.Y + 40f * LayoutScale
                : ViewBox.Y;
        }

        Vector2 GetCursorPosition()
        {
            var interactiveHost = Host as InteractiveSurfaceScript;
            return interactiveHost?.CursorPosition ?? new Vector2(float.NaN, float.NaN);
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

        static Color GetHoverColor(Color color)
        {
            return Color.White.ContrastRatio(color) >= Color.Black.ContrastRatio(color)
                ? color.MulValue(1.18)
                : color.MulValue(0.82);
        }

        static Color GetContrastingTextColor(Color background)
        {
            return Color.White.ContrastRatio(background) >= Color.Black.ContrastRatio(background)
                ? Color.White
                : Color.Black;
        }

        static RectangleF CenterSquare(RectangleF bounds, float size)
        {
            var safeSize = Math.Max(1f, Math.Min(size, Math.Min(bounds.Width, bounds.Height)));
            return new RectangleF(
                bounds.Center.X - safeSize * 0.5f,
                bounds.Center.Y - safeSize * 0.5f,
                safeSize,
                safeSize);
        }

    }
}
