using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
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
using IMyBlockGroup = Sandbox.ModAPI.Ingame.IMyBlockGroup;
using IMyIngameTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;
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
        const string BOOLEAN_ON = "on";
        const string BOOLEAN_OFF = "off";
        const string BOOLEAN_TOGGLE = "toggle";
        const string CLICK_INCREASE = "increase";
        const string CLICK_DECREASE = "decrease";
        const string SCROLL_NONE = "none";
        const string SCROLL_NORMAL = "normal";
        const string SCROLL_REVERSED = "reversed";

        readonly List<int> _renderEntryIndices = new List<int>();
        readonly Dictionary<int, ButtonPanelEntrySettings> _entries = new Dictionary<int, ButtonPanelEntrySettings>();
        readonly List<IMyBlockGroup> _actionGroups = new List<IMyBlockGroup>();
        readonly List<IMyIngameTerminalBlock> _actionGroupBlocks = new List<IMyIngameTerminalBlock>();
        readonly StringBuilder _actionStatusBuilder = new StringBuilder();
        readonly ButtonPad.ButtonPadViewModel _viewModel;

        public ButtonPadApp(IAppHost host) : base(host)
        {
            _viewModel = new ButtonPad.ButtonPadViewModel(this);
            SetContent(new ButtonPad.ButtonPadView { DataContext = _viewModel });
            NeedGridData(GridCapability.Blocks);
            LoadButtonPanelSettings();
            EnsureButtonTexturesLoaded();
        }

        float Scale => GeneralComponent.GetScale();

        float FontScale => Host.Surface.FontSize;

        float LayoutScale => Scale * FontScale;

        RectangleF ViewBox => Host.ViewBox;

        public override void Update()
        {
            _viewModel.Refresh();
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            LoadButtonPanelSettings();
            EnsureButtonTexturesLoaded();
            _viewModel.Refresh();
            MarkDirty();
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        internal void BuildNativeEntries(
            List<ButtonPad.ActionButtonModel> destination,
            out float buttonSize,
            out float cellWidth,
            out float cellHeight)
        {
            float spacing = ButtonPanelLayout.SpacingPixels * Scale;

            float availableHeight = Math.Max(0f, ViewBox.Bottom - GetContentTop());
            var layout = ButtonPanelLayout.Create(
                ButtonPanelComponent.ButtonCount,
                ViewBox.Width,
                availableHeight,
                ButtonPanelLayout.PreferredButtonSizePixels * Scale,
                spacing,
                GetMinimumConfiguredButtonCount());
            // Each button is centered in its cell, so the leftover space is shared evenly around every button.
            buttonSize = Math.Max(1f, layout.ButtonSize);
            cellWidth = Math.Max(buttonSize, layout.CellWidth);
            cellHeight = Math.Max(buttonSize, layout.CellHeight);

            string styleClass = GetNativeStyleClass();
            int count = 0;
            for (int index = 0; index < layout.ButtonCount; index++)
            {
                var entry = GetEntry(index, false);
                bool configured = entry != null && entry.HasContent();
                if (ButtonPanelComponent.HideEmpty && !configured)
                    continue;

                int capturedIndex = index;
                if (count == destination.Count)
                    destination.Add(new ButtonPad.ActionButtonModel());
                var model = destination[count++];
                string icon = GetEntrySpriteName(entry);
                model.Size = buttonSize;
                model.Title = entry != null ? entry.Title ?? string.Empty : string.Empty;
                model.Icon = !configured ? "Plus" : string.IsNullOrWhiteSpace(icon) ? "MissingIcon" : icon;
                model.Status = GetEntryStatus(entry) ?? string.Empty;
                model.Variant = styleClass;
                model.IsConfigured = configured;
                Color panelColor;
                model.PanelColor = entry != null && Extensions.ColorExtensions.TryParseHexColor(entry.BackgroundColor, out panelColor)
                    ? panelColor
                    : Color.Transparent;
                model.Clicked = delegate { OnNativeButtonClicked(capturedIndex); };
                model.SecondaryClicked = delegate { OpenEntryEditor(capturedIndex); };
            }
            if (destination.Count > count)
                destination.RemoveRange(count, destination.Count - count);
        }

        string GetNativeStyleClass()
        {
            switch (GetButtonPanelStyle())
            {
                case ButtonPanelStyle.Classic:
                    return "classic";
                case ButtonPanelStyle.Transparent:
                    return "transparent";
                case ButtonPanelStyle.Border:
                    return "border";
                default:
                    return "default";
            }
        }

        void OnNativeButtonClicked(int index)
        {
            OnPadButtonClicked(index);
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
            _viewModel.Refresh();
            MarkDirty();
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

        void OnPadButtonClicked(int index)
        {
            var initialEntry = GetEntry(index, false);

            if (HasConfiguredAction(initialEntry) && TryRunEntryAction(initialEntry))
            {
                Host.RenderSprites();
                return;
            }

            OpenEntryEditor(index);
        }

        void OpenEntryEditor(int index)
        {
            var catalog = new ButtonPad.ButtonPadActionCatalog(Host.GridLogic);
            var surface = Host.Surface;
            var services = new ButtonPad.ButtonPadEntryServices
            {
                PickSprite = picked => Dialogs.Launchers.SpritePickerLauncher.PickOne(this, surface, picked),
                PickTarget = (current, picked) => ShowDialog(new ButtonPad.TargetPickerView(
                    new ButtonPad.TargetPickerViewModel(catalog.Targets, current, picked))),
                PickAction = (target, current, picked) => ShowDialog(new ButtonPad.ActionPickerView(
                    new ButtonPad.ActionPickerViewModel(catalog.Actions(target), current, picked))),
                NeedsConfiguration = (target, action) => catalog.Parameter(target, action) != null,
                Configure = (target, action, configured) => ShowDialog(new ButtonPad.ActionConfigurationView(
                    new ButtonPad.ActionConfigurationViewModel(catalog.Parameter(target, action), action, configured)))
            };
            var initialEntry = GetEntry(index, false);
            ShowDialog(new ButtonPad.ButtonPadEntryView(new ButtonPad.ButtonPadEntryViewModel(
                index, initialEntry == null ? null : initialEntry.Clone(), entry => ApplyEntry(index, entry), services)));
        }

        static bool HasConfiguredAction(ButtonPanelEntrySettings entry)
        {
            return entry != null &&
                   entry.Target != null &&
                   entry.Action != null &&
                   !string.IsNullOrEmpty(entry.Action.BaseId);
        }

        bool TryRunEntryAction(ButtonPanelEntrySettings entry)
        {
            return TryRunConfiguredAction(entry, false, 0);
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
            MyAPIGateway.Utilities.ShowNotification(message, 1500);
        }

        float GetContentTop()
        {
            return Host.TitleVisible
                ? ViewBox.Y + 40f * LayoutScale
                : ViewBox.Y;
        }

    }
}
