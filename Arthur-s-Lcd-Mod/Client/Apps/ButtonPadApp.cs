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
#if EXPERIMENTAL
using LcdMod.Client.Terminal.Actions;
using LcdMod.Client.Terminal.Models;
using LcdMod.Client.Terminal.Models.Actions;
using LcdMod.Client.Terminal.Models.Property;
using Sandbox.ModAPI.Interfaces;
#endif
using LcdMod.Common.Config.Models;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
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

namespace LcdMod.Client.Apps
{
    public sealed class ButtonPadApp : App, IApp
    {
        const float BUTTON_SIZE_PIXELS = 92f;
        const float BUTTON_SPACING_PIXELS = 3f;
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
#if EXPERIMENTAL
        readonly List<IMyBlockGroup> _actionGroups = new List<IMyBlockGroup>();
        readonly List<IMyIngameTerminalBlock> _actionGroupBlocks = new List<IMyIngameTerminalBlock>();
#endif

        int _lastLayoutColumns = 1;

        public ButtonPadApp(ScreenConfigButtonPanel config, IAppHost host) : base(config, host)
        {
            _scrollPanel = AddChild(new ScrollPanel());
            _scrollPanel.ManualScrollInertiaEnabled = false;
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _entryPanel.CreateControl = CreateEntryButton;
            _entryPanel.BindControl = BindEntryButton;
            LoadButtonPanelSettings();
        }

        public override IReadOnlyList<Control> Children => _children;

        new ScreenConfigButtonPanel AppConfig => (ScreenConfigButtonPanel)base.AppConfig;

        float Scale => AppConfig.Scale;

        float FontScale => Host.Surface.FontSize;

        float LayoutScale => Scale * FontScale;

        RectangleF ViewBox => Host.ViewBox;

        IMyTextSurface Surface => Host.Surface;

        public override void Update()
        {
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

        void BuildRenderEntryIndices()
        {
            _renderEntryIndices.Clear();

            var populated = new List<int>();
            foreach (var entry in _entries)
            {
                if (entry.Value != null && entry.Value.HasContent())
                    populated.Add(entry.Key);
            }

            populated.Sort();
            _renderEntryIndices.AddRange(populated);
            _renderEntryIndices.Add(GetNextEntryIndex(populated));
        }

        static int GetNextEntryIndex(List<int> populated)
        {
            if (populated == null || populated.Count == 0)
                return 0;

            return populated[populated.Count - 1] + 1;
        }

        void DrawEntries(List<MySprite> sprites, float contentTop)
        {
            if (ViewBox.Width <= 0f || ViewBox.Height <= 0f)
                return;

            var availableHeight = Math.Max(0f, ViewBox.Bottom - contentTop);
            if (availableHeight <= 0f)
                return;

            BuildRenderEntryIndices();

            var scrollerWidth = Math.Min(ScrollPanel.DefaultScrollerWidthPixels * Scale, Math.Max(0f, ViewBox.Width * 0.25f));
            var layout = CreateButtonGridLayout(ViewBox.Width, availableHeight);
            var totalRows = GetRowsForEntryCount(_renderEntryIndices.Count, layout.Columns);
            var needsScroller = totalRows * layout.RowHeight > availableHeight + 0.001f;
            if (needsScroller && scrollerWidth > 0f)
            {
                layout = CreateButtonGridLayout(Math.Max(1f, ViewBox.Width - scrollerWidth), availableHeight);
                totalRows = GetRowsForEntryCount(_renderEntryIndices.Count, layout.Columns);
            }

            _lastLayoutColumns = Math.Max(1, layout.Columns);

            var contentWidth = Math.Max(1f, needsScroller ? ViewBox.Width - scrollerWidth : ViewBox.Width);
            var columnWidth = contentWidth / Math.Max(1, layout.Columns);

            _scrollPanel.SetContent(_entryPanel);
            _entryPanel.ItemsSource = _renderEntryIndices;
            _entryPanel.RowHeight = layout.RowHeight;
            _entryPanel.MinimumColumnWidth = Math.Max(1f, columnWidth - 0.01f);
            _entryPanel.HorizontalGap = Math.Max(0f, columnWidth - layout.ButtonSize);
            _entryPanel.VerticalGap = Math.Max(0f, layout.RowHeight - layout.ButtonSize);

            _scrollPanel.ConfigureAutomatic(
                new RectangleF(ViewBox.X, contentTop, ViewBox.Width, availableHeight),
                scrollerWidth,
                layout.RowHeight,
                0f);

            Color trackColor;
            Color thumbColor;
            _scrollPanel.ScrollBarTrackColor =
                ScopedResourceResolver.TryResolve(this, ThemeResources.SurfaceColor, out trackColor)
                    ? trackColor
                    : Color.Gray;
            _scrollPanel.ScrollBarThumbColor =
                ScopedResourceResolver.TryResolve(this, ThemeResources.FontColor, out thumbColor)
                    ? thumbColor
                    : Color.White;

            _scrollPanel.SetVisible(true);
            _children.Add(_scrollPanel);

            var renderContext = CreateControlRenderContext(Surface, Scale, FontScale, GetCursorPosition());

            _scrollPanel.Render(renderContext, sprites);
            ClearDirtyAfterRender();
        }

        int GetRowsForEntryCount(int entryCount, int columns)
        {
            columns = Math.Max(1, columns);
            entryCount = Math.Max(0, entryCount);

            if (entryCount == 0)
                return 1;

            return Math.Max(1, (entryCount + columns - 1) / columns);
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
        }

        void LoadButtonPanelSettings()
        {
            _entries.Clear();

            try
            {
                var data = AppConfig.GetCustomData(CUSTOM_DATA_KEY);
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
                EntryCount = GetSavedEntryCount(),
                Entries = list.ToArray()
            };

            AppConfig.SetCustomData(CUSTOM_DATA_KEY, MyAPIGateway.Utilities.SerializeToBinary(settings));

            var terminalBlock = Host.Block as IMyTerminalBlock;
            if (terminalBlock != null)
                ConfigManager.Sync(terminalBlock, Host.ProviderConfig);
        }

        int GetSavedEntryCount()
        {
            var max = -1;
            foreach (var entry in _entries)
            {
                if (entry.Value != null && entry.Value.HasContent() && entry.Key > max)
                    max = entry.Key;
            }

            return max + 1;
        }

        ButtonGridLayout CreateButtonGridLayout(float availableWidth, float availableHeight)
        {
            var preferredButtonSize = BUTTON_SIZE_PIXELS * Scale;
            var preferredSpacing = BUTTON_SPACING_PIXELS * Scale;
            var usableWidth = Math.Max(1f, availableWidth);
            var usableHeight = Math.Max(1f, availableHeight);

            var buttonSize = Math.Min(preferredButtonSize, Math.Min(usableWidth, usableHeight));
            var columns = Math.Max(1, (int)Math.Floor((usableWidth - preferredSpacing) / (buttonSize + preferredSpacing)));

            var requiredWidth = columns * buttonSize + (columns + 1) * preferredSpacing;
            if (requiredWidth > usableWidth)
            {
                var clampedSize = (usableWidth - (columns + 1) * preferredSpacing) / columns;
                buttonSize = clampedSize > 1f
                    ? Math.Min(buttonSize, clampedSize)
                    : Math.Max(1f, usableWidth / columns);
            }

            return new ButtonGridLayout
            {
                ButtonSize = buttonSize,
                Columns = columns,
                VerticalSpacing = preferredSpacing,
                RowHeight = buttonSize + preferredSpacing
            };
        }

        void ClearInteractiveTree()
        {
            _children.Clear();
            _scrollPanel.SetVisible(false);
        }

        ControlTemplate CreateEntryButton(int entryIndex)
        {
            return new Button(
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
            model.SpriteName = entry != null ? entry.SpriteName : null;
            model.Title = entry != null ? entry.Title : null;
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

        void RenderEntryButton(ControlTemplate control, ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var hovered = rect.Contains(context.CursorPosition);
            var button = control as Button;
            var defaultPanelColor = button != null ? button.BackgroundColor : control.BackgroundColor;
            var panelColor = hovered
                ? control.GetResourceColor(ThemeResources.AccentColor, defaultPanelColor)
                : defaultPanelColor;
            var plusColor = control.TextColor;
            var shadowColor = control.GetResourceColor(ThemeResources.ShadowColor, new Color(0, 0, 0, 160));

            Border.CreateSpritesFromRect(
                new RectangleF(rect.Position + 2f * context.Scale, rect.Size),
                sprites,
                shadowColor,
                radiusScale: context.Scale
            );

            Border.CreateSpritesFromRect(
                rect,
                sprites,
                panelColor,
                radiusScale: context.Scale
            );

            var model = control.DataContext as PadButtonModel;
            var spriteName = model != null ? model.SpriteName : null;
            var title = model != null ? model.Title : null;

            var hasTitle = !string.IsNullOrWhiteSpace(title);
            var titleAreaHeight = hasTitle ? Math.Max(12f, rect.Height * 0.24f) : 0f;
            var iconArea = new RectangleF(
                rect.X,
                rect.Y + titleAreaHeight,
                rect.Width,
                Math.Max(1f, rect.Height - titleAreaHeight)
            );

            if (hasTitle)
            {
                var titleScale = 0.34f * context.Scale * context.FontScale;
                var titleHeight = FormatingHelper.LineHeight(titleScale, context.Surface);
                var trimmedTitle = TrimText(title, Math.Max(0f, rect.Width - 8f * context.Scale), titleScale, context.Surface);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = trimmedTitle,
                    Position = new Vector2(rect.Center.X, rect.Y + Math.Max(1f, (titleAreaHeight - titleHeight) * 0.5f)),
                    Color = plusColor,
                    FontId = "White",
                    RotationOrScale = titleScale,
                    Alignment = TextAlignment.CENTER
                });
            }

            var iconSize = Math.Max(1f, Math.Min(iconArea.Width, iconArea.Height) * 0.68f);
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
                    Color = plusColor,
                    Alignment = TextAlignment.CENTER
                });
                return;
            }

            var plusLength = Math.Min(iconRect.Width, iconRect.Height) * 0.5f;
            var plusThickness = Math.Max(2f, 6f * context.Scale);

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

        void OnPadButtonClicked(ButtonModel model, object sender)
        {
            var padModel = model as PadButtonModel;
            var index = padModel != null ? padModel.Index : -1;
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
            OpenEntryEditor(padModel != null ? padModel.Index : -1);
        }

        bool OnPadButtonScrolled(object dataContext, object sender, int delta)
        {
            var padModel = dataContext as PadButtonModel;
            var entry = GetEntry(padModel != null ? padModel.Index : -1, false);

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
                initialEntry == null ? null : initialEntry.Clone(),
                entry =>
                {
                    ApplyEntry(index, entry);
                    Host.RenderSprites();
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
#if EXPERIMENTAL
            return TryRunConfiguredAction(entry, false, 0);
#else
            return false;
#endif
        }

        bool TryRunEntryScrollAction(ButtonPanelEntrySettings entry, int delta)
        {
#if EXPERIMENTAL
            if (delta == 0)
                return false;

            return TryRunConfiguredAction(entry, true, delta);
#else
            return false;
#endif
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

#if EXPERIMENTAL
        bool TryRunConfiguredAction(ButtonPanelEntrySettings entry, bool scroll, int delta)
        {
            if (!HasConfiguredAction(entry))
                return false;

            ICustomAction customAction;
            if (!ActionHelper.CustomActions.TryGetValue(entry.Action.BaseId, out customAction) || customAction == null)
            {
                NotifyActionFailure("Action unavailable");
                return true;
            }

            try
            {
                if (!ApplyActionToTarget(entry.Target, entry.Action, customAction, scroll, delta))
                    NotifyActionFailure("No compatible target");
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Error, "Button panel action failed: " + e);
                NotifyActionFailure("Action failed");
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

            var blocks = Host.GridLogic.GetTerminalBlocks<IMyTerminalBlock>();
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

            var blocks = Host.GridLogic.GetTerminalBlocks<IMyTerminalBlock>();
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

            var blocks = Host.GridLogic.GetTerminalBlocks<IMyTerminalBlock>();
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
                var clickAction = NormalizeClickAction(settings == null ? null : settings.ClickAction, CLICK_INCREASE);
                return ApplyTerminalAction(
                    clickAction == CLICK_DECREASE ? increaseDecreaseAction.Decrease : increaseDecreaseAction.Increase,
                    block);
            }

            var onOffAction = customAction as OnOffAction;
            if (onOffAction != null)
            {
                var mode = NormalizeBooleanMode(settings == null ? null : settings.ParameterValue, BOOLEAN_TOGGLE);
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
            var scrollMode = NormalizeScrollMode(settings == null ? null : settings.ScrollMode, SCROLL_NONE);
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

            var mode = NormalizeBooleanMode(settings == null ? null : settings.ParameterValue, BOOLEAN_TOGGLE);
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

            action.Property.SetValue(block, Clamp(value, GetSingleMinimum(action, block), GetSingleMaximum(action, block)));
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

            action.Property.SetValue(block, Clamp(value, min, max));
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

        static float Clamp(float value, float min, float max)
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
#endif

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
            return interactiveHost != null
                ? interactiveHost.CursorPosition
                : new Vector2(float.NaN, float.NaN);
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

        struct ButtonGridLayout
        {
            public float ButtonSize;
            public float RowHeight;
            public float VerticalSpacing;
            public int Columns;
        }

        sealed class PadButtonModel : ButtonModel
        {
            public int Index { get; set; }

            public int Row { get; set; }

            public int Column { get; set; }

            public string SpriteName { get; set; }

            public string Title { get; set; }
        }

        sealed class ButtonPadEntryDialog : Dialog
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

                var layoutScale = scale * fontScale;
                var titleScale = 0.82f * layoutScale;
                var fieldTextScale = 0.56f * layoutScale;
                var padding = new Vector2(18f * scale, 14f * scale);
                var spacing = 10f * scale;
                var smallSpacing = 6f * scale;

                var titleHeight = FormatingHelper.LineHeight(titleScale, surface);
                var fieldHeight = Math.Max(34f * scale, FormatingHelper.LineHeight(fieldTextScale, surface) + 18f * scale);
                var closeIconSize = GetDialogCloseButtonSize(scale);
                var headerHeight = Math.Max(titleHeight, closeIconSize.Y);
                var previewSize = GetSpritePreviewTargetSize(scale);

                var cardWidth = Math.Min(
                    Math.Max(400f * scale, viewBox.Width * 0.62f),
                    Math.Max(1f, viewBox.Width - padding.X * 2f));

                var contentWidth = Math.Max(1f, cardWidth - padding.X * 2f);
                previewSize = Math.Min(previewSize, contentWidth);
                var narrowLayout = contentWidth < previewSize + spacing + 150f * scale;

                var stackHeight = fieldHeight * 3f + smallSpacing * 2f;
                var contentHeight = narrowLayout
                    ? previewSize + spacing + stackHeight + spacing + fieldHeight
                    : Math.Max(previewSize, stackHeight) + spacing + fieldHeight;

                var cardHeight = padding.Y * 2f + headerHeight + spacing + contentHeight;
                cardHeight = Math.Min(cardHeight, Math.Max(1f, viewBox.Height - padding.Y * 2f));

                var cardRect = new RectangleF(
                    viewBox.Center.X - cardWidth * 0.5f,
                    viewBox.Center.Y - cardHeight * 0.5f,
                    cardWidth,
                    cardHeight);

                RegisterDialogCard(cardRect);
                DrawDialogBackdrop(surface, scale, cardRect, 128);

                var dialogTitle = _index >= 0 ? "Button " + (_index + 1) : "Button";
                Sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = dialogTitle,
                    Position = new Vector2(cardRect.Center.X, cardRect.Y + padding.Y + (headerHeight - titleHeight) * 0.5f),
                    Color = ResolveColor(ThemeResources.OnSurfaceColor),
                    FontId = "White",
                    RotationOrScale = titleScale,
                    Alignment = TextAlignment.CENTER
                });

                var contentTop = cardRect.Y + padding.Y + headerHeight + spacing;
                RectangleF previewRect;
                RectangleF titleInputRect;
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
                    targetRect = new RectangleF(rightX, titleInputRect.Bottom + smallSpacing, rightWidth, fieldHeight);
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

                var context = CreateRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);

                EnsureSelectedSpriteButton(previewRect);
                EnsureTitleInput(titleInputRect);
                EnsurePickTargetButton(targetRect);
                EnsureSelectActionButton(actionRect);
                EnsureApplyButton(applyRect);
                EnsureDeleteButton(deleteRect);

                ContainerControl.AddChild(_selectedSpriteButton);
                ContainerControl.AddChild(_titleInput);
                ContainerControl.AddChild(_pickTargetButton);
                ContainerControl.AddChild(_selectActionButton);
                ContainerControl.AddChild(_applyButton);
                ContainerControl.AddChild(_deleteButton);

                _selectedSpriteButton.Render(context, Sprites);
                _titleInput.Render(context, Sprites);
                _pickTargetButton.Render(context, Sprites);
                _selectActionButton.Render(context, Sprites);
                _applyButton.Render(context, Sprites);
                _deleteButton.Render(context, Sprites);
            }

            void DrawDialogBackdrop(IMyTextSurface surface, float scale, RectangleF cardRect, byte overlayAlpha)
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

                Border.CreateSpritesFromRect(new RectangleF(cardRect.Position + 3f * scale, cardRect.Size), Sprites,
                    ResolveColor(ThemeResources.ShadowColor), radiusScale: scale);
                Border.CreateSpritesFromRect(cardRect, Sprites,
                    ResolveColor(ThemeResources.SurfaceContainerHighColor), radiusScale: scale);
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

                model.SpriteName = _draftEntry.SpriteName;
                model.Text = string.Empty;
                model.Enabled = true;
                model.Clicked = OnSelectedSpriteButtonClicked;

                _selectedSpriteButton.SetStyleId("Primary");
                _selectedSpriteButton.CustomRender = RenderSelectedSprite;
                _selectedSpriteButton.SetCursor(CursorType.Hand);
                _selectedSpriteButton.SetVisible(true);
            }

            void RenderSelectedSprite(ControlTemplate control, ControlRenderContext context, List<MySprite> sprites)
            {
                var rect = control.Bounds;
                var model = control.DataContext as SelectedSpriteButtonModel;
                var spriteName = model != null ? model.SpriteName : null;
                var hovered = rect.Contains(context.CursorPosition);
                var button = control as Button;
                var defaultPanelColor = button != null ? button.BackgroundColor : control.BackgroundColor;
                var panelColor = hovered
                    ? control.GetResourceColor(ThemeResources.AccentColor, defaultPanelColor)
                    : defaultPanelColor;

                Border.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: context.Scale);

                var foregroundColor = control.TextColor;
                var iconSize = Math.Max(1f, Math.Min(rect.Width, rect.Height) * 0.74f);
                var iconRect = new RectangleF(rect.Center.X - iconSize * 0.5f, rect.Center.Y - iconSize * 0.5f, iconSize, iconSize);

                if (string.IsNullOrEmpty(spriteName))
                {
                    DrawPlus(iconRect, foregroundColor, context.Scale, sprites);
                    return;
                }

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = spriteName,
                    Position = iconRect.Center,
                    Size = iconRect.Size,
                    Color = foregroundColor,
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
                        Title = "Title",
                        Subtitle = "Button title displayed above the icon",
                        Placeholder = "Title",
                        Value = _draftEntry.Title ?? string.Empty,
                        ValueChanged = OnTitleChanged
                    };

                _titleInputModel.Title = "Title";
                _titleInputModel.Subtitle = "Button title displayed above the icon";
                _titleInputModel.Placeholder = "Title";
                _titleInputModel.Value = _draftEntry.Title ?? string.Empty;
                _titleInputModel.Enabled = true;
                _titleInputModel.ValueChanged = OnTitleChanged;

                if (_titleInput == null)
                    _titleInput = new TextInput(rect, _titleInputModel);
                else
                    _titleInput.SetRect(rect);

                _titleInput.SetDataContext(_titleInputModel);
                _titleInput.SetStyleId("Primary");
                _titleInput.SetCursor(CursorType.Hand);
                _titleInput.SetVisible(true);
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
                _selectActionButton.SetCursor(enabled ? CursorType.Hand : CursorType.Default);
                _selectActionButton.SetVisible(true);
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
                _applyButton.CustomRender = RenderTextButton;
                _applyButton.SetCursor(CursorType.Hand);
                _applyButton.SetVisible(true);
            }

            void EnsureDeleteButton(RectangleF rect)
            {
                if (_deleteButton == null)
                    _deleteButton = new Button(rect, new ButtonModel { Text = "Delete", Clicked = OnDeleteClicked });
                else
                    _deleteButton.SetRect(rect);

                var model = _deleteButton.DataContext as ButtonModel;
                if (model != null)
                {
                    model.Text = "Delete";
                    model.Enabled = true;
                    model.Clicked = OnDeleteClicked;
                }

                _deleteButton.SetStyleId("Danger");
                _deleteButton.CustomRender = RenderTextButton;
                _deleteButton.SetCursor(CursorType.Hand);
                _deleteButton.SetVisible(true);
            }

            void RenderTextButton(ControlTemplate control, ControlRenderContext context, List<MySprite> sprites)
            {
                var rect = control.Bounds;
                var buttonModel = control.DataContext as ButtonModel;
                var enabled = buttonModel == null || buttonModel.Enabled;
                var hovered = enabled && rect.Contains(context.CursorPosition);
                var button = control as Button;
                var defaultPanelColor = button != null ? button.BackgroundColor : control.BackgroundColor;
                var panelColor = hovered
                    ? control.GetResourceColor(ThemeResources.AccentColor, defaultPanelColor)
                    : defaultPanelColor;
                Border.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: context.Scale);

                var text = buttonModel == null ? string.Empty : buttonModel.Text;
                var textScale = 0.52f * context.Scale * context.FontScale;
                var availableWidth = Math.Max(0f, rect.Width - 12f * context.Scale);
                var trimmed = TrimText(text, availableWidth, textScale, context.Surface);
                var textHeight = FormatingHelper.LineHeight(textScale, context.Surface);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = trimmed,
                    Position = new Vector2(rect.Center.X, rect.Center.Y - textHeight * 0.5f),
                    Color = control.TextColor,
                    FontId = "White",
                    RotationOrScale = textScale,
                    Alignment = TextAlignment.CENTER
                });
            }

            string GetPickTargetButtonText()
            {
                if (_draftEntry.Target == null || string.IsNullOrEmpty(_draftEntry.Target.DisplayName))
                    return "Select Target";
                return "Target: " + _draftEntry.Target.DisplayName;
            }

            string GetSelectActionButtonText()
            {
                if (_draftEntry.Target == null)
                    return "Select Action";
                if (_draftEntry.Action == null || string.IsNullOrEmpty(_draftEntry.Action.DisplayName))
                    return "Select Action";
                if (!string.IsNullOrEmpty(_draftEntry.Action.ParameterDisplayValue))
                    return "Action: " + _draftEntry.Action.DisplayName + " = " + _draftEntry.Action.ParameterDisplayValue;
                return "Action: " + _draftEntry.Action.DisplayName;
            }

            void OnSelectedSpriteButtonClicked(ButtonModel model, object sender)
            {
                if (_showDialog == null)
                    return;

                bool restored = false;
                Action restoreEditor = delegate
                {
                    if (restored)
                        return;

                    restored = true;
                    if (_showDialog != null)
                        _showDialog(this);
                    if (_requestRedraw != null)
                        _requestRedraw();
                };

                _showDialog(new SpritePicker(
                    ParentApp,
                    delegate(string spriteName)
                    {
                        _draftEntry.SpriteName = spriteName;
                        restoreEditor();
                    },
                    _requestRedraw,
                    restoreEditor));
            }

            void OnPickTargetClicked(ButtonModel model, object sender)
            {
                if (_showDialog == null || _gridLogic == null)
                    return;
                _showDialog(new PickActionTargetDialog(
                    ParentApp,
                    _gridLogic,
                    _draftEntry.Target == null ? null : _draftEntry.Target.ToPickResult(),
                    OnPickTargetSelected,
                    OnPickTargetCancelled,
                    _requestRedraw));
            }

            void OnPickTargetSelected(PickActionTargetResult target)
            {
                var oldKey = _draftEntry.Target == null ? null : _draftEntry.Target.CompatibilityKey;
                _draftEntry.Target = ButtonPanelTargetSettings.FromPickResult(target);
                var newKey = _draftEntry.Target == null ? null : _draftEntry.Target.CompatibilityKey;
                if (!string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
                    _draftEntry.Action = null;

                if (_showDialog != null)
                    _showDialog(this);
                _requestRedraw?.Invoke();
            }

            void OnPickTargetCancelled()
            {
                if (_showDialog != null)
                    _showDialog(this);
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
                var selectedAction = action == null ? null : action.Clone();
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
                if (_showDialog != null)
                    _showDialog(this);
                _requestRedraw?.Invoke();
            }

            void OnActionConfigured(ButtonPanelActionSettings action)
            {
                _draftEntry.Action = action == null ? null : action.Clone();
                if (_showDialog != null)
                    _showDialog(this);
                _requestRedraw?.Invoke();
            }

            void OnActionConfigurationCancelled()
            {
                if (_showDialog != null)
                    _showDialog(this);
                _requestRedraw?.Invoke();
            }

            void OnActionCancelled()
            {
                if (_showDialog != null)
                    _showDialog(this);
                _requestRedraw?.Invoke();
            }

            void OnTitleChanged(string value)
            {
                _draftEntry.Title = value ?? string.Empty;
                _requestRedraw?.Invoke();
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

            static string TrimText(string text, float availableWidth, float fontSize, IMyTextSurface surface)
            {
                if (string.IsNullOrEmpty(text) || availableWidth <= 0f || surface == null)
                    return string.Empty;
                var size = FormatingHelper.GetSizeInPixel(text, "White", fontSize, surface);
                if (size.X <= availableWidth)
                    return text;
                return FormatingHelper.TrimName(text, Math.Max(1, (int)(text.Length * availableWidth / Math.Max(1f, size.X))));
            }

            sealed class SelectedSpriteButtonModel : ButtonModel
            {
                public string SpriteName { get; set; }
            }
        }

    }
}
