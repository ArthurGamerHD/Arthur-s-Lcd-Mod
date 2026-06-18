using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LcdMod.Client.Extensions;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Blueprint;
using LcdMod.Client.Terminal.Controls.Cargo;
using LcdMod.Client.Terminal.Controls.Color;
using LcdMod.Client.Terminal.Controls.Filter;
using LcdMod.Client.Terminal.Controls.Filter.Buttons;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Client.Terminal.Controls.Interactive;
using LcdMod.Client.Terminal.Controls.Markdown;
using LcdMod.Client.Terminal.Controls.Proxy;
using LcdMod.Client.Terminal.Controls.Scale;
#if DEBUG
using LcdMod.Client.Terminal.Controls.VisibleTreeDebug;
#endif
using LcdMod.Common.Helpers;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.Game.Localization;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.ModAPI;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal
{
    public class TerminalManager
    {
        public readonly List<TerminalControlsWrapper> Controls = new List<TerminalControlsWrapper>();

        readonly LcdModSessionComponent _session;
        readonly TextSurfaceControlsVisibility _textSurfaceControlsVisibility = new TextSurfaceControlsVisibility();
        readonly TextAlignmentControlVisibility _textAlignmentControlVisibility = new TextAlignmentControlVisibility();
        
        public static IMyTerminalControlButton CustomDataButton;
        public static IMyTerminalControlButton ShowTextPanelButton;
        public static IMyTerminalControlTextbox SearchScriptTextBox;
        public static string SearchQuery = string.Empty;
        static readonly Dictionary<IMyTerminalControl, CapturedSurfaceControl> SurfaceControls =
            new Dictionary<IMyTerminalControl, CapturedSurfaceControl>();
        static readonly Dictionary<IMyTerminalControlListbox, CapturedScriptControl> ScriptControls =
            new Dictionary<IMyTerminalControlListbox, CapturedScriptControl>();
        static int _surfaceControlShadowId;
        static int _scriptControlShadowId;
        static readonly string[] SurfaceControlIds =
        {
            "Font",
            "FontSize",
            "TextPadding",
            "Alignment"
        };
        static readonly string[] SurfaceControlOrder =
        {
            "Font",
            "FontSize",
            "Alignment",
            "TextPadding"
        };
        public TerminalManager(LcdModSessionComponent session)
        {
            _session = session;
        }

        public void Initialize()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;
            SearchScriptTextBox = CreateSearchScriptTextBox();
            
            Controls.Add(new ButtonShowConfig());
            Controls.Add(new ButtonEditMarkdown());


            TerminalControlsListbox source = new ListboxBlockCandidates();
            TerminalControlsListbox target = new ListboxBlockSelected();

            Controls.Add(new SliderFov());
            Controls.Add(new SliderRadarRange());
            Controls.Add(new SliderNpcMarketMaxDistance());
            Controls.Add(new SliderNpcMarketPageSwitchDelay());

            Controls.Add(new SwitchToggleColors());
            Controls.Add(new ColorPickerAccent());
            Controls.Add(new ColorPickerWarning());
            Controls.Add(new ColorPickerError());

            Controls.Add(new SwitchToggleHeader());
            Controls.Add(new SliderScale());
            Controls.Add(new SliderCursorScale());
            Controls.Add(new SwitchToggleAlt());
            Controls.Add(new SliderRotation());

            Controls.Add(new SliderRaysPerTick());
            Controls.Add(new SliderRenderScale());

            Controls.Add(new ComboboxDisplayMode());
            Controls.Add(new ComboboxReferenceMode());
            Controls.Add(new ComboboxGraphWindow());
            Controls.Add(new ListboxReferenceBlockSelection());
#if DEBUG
            Controls.Add(new ListboxVisibleTreeDebugBlockSelection());
            Controls.Add(new ListboxVisibleTreeDebugScreenSelection());
#endif
            Controls.Add(new SwitchToggleLines());

            Controls.Add(new ComboboxLinkType());
            Controls.Add(new ListboxProjectorSelection());
            Controls.Add(new CheckboxHideEmpty());
            Controls.Add(new SeparatorFilter());
            Controls.Add(new LabelSeparator());
            Controls.Add(source);
            Controls.Add(new ButtonBlockAddToSelection(source, target));
            Controls.Add(target);
            Controls.Add(new ButtonBlockRemoveFromSelection(source, target));

            source = new ListboxItemsCandidates();
            target = new ListboxItemsSelected();

            Controls.Add(source);
            Controls.Add(new ButtonItemAddToSelection(source, target));
            Controls.Add(target);
            Controls.Add(new ButtonItemRemoveFromSelection(source, target));

            source = new ListboxSpriteCandidates();
            target = new ListboxSpriteSelected();

            Controls.Add(source);
            Controls.Add(new ButtonSpriteAddToSelection(source, target));
            Controls.Add(target);
            Controls.Add(new ButtonSpriteRemoveFromSelection(source, target));
            Controls.Add(new SliderImageChangeInterval());

            Controls.Add(new ComboboxSorting());

            Controls.Add(new SliderProxyX());
            Controls.Add(new SliderProxyY());
            Controls.Add(new SwitchProxyAutoAdjust());
            Controls.Add(new ButtonProxyAuto());

            Controls.Add(new SwitchShowConfigButton());
        }

        public void Unload()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlGetter;
            Controls.Clear();
            SurfaceControls.Clear();
            ScriptControls.Clear();
            SearchScriptTextBox = null;
            SearchQuery = string.Empty;
        }

        void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            CaptureTextButtonIfNeeded(controls);
            if (controls == null)
                return;

            try
            {
                SetupProviderTerminal(block, controls);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
        }

        void CaptureTextButtonIfNeeded(List<IMyTerminalControl> controls)
        {
            if (controls == null)
                return;

            foreach (var c in controls)
            {
                switch (c.Id)
                {
                    case "CustomData":
                        CustomDataButton = c as IMyTerminalControlButton;
                        break;
                    case "ShowTextPanel":
                        ShowTextPanelButton = c as IMyTerminalControlButton;
                        break;
                }

                CaptureSurfaceControl(c);
                CaptureScriptControl(c as IMyTerminalControlListbox);
            }
        }

        void CaptureScriptControl(IMyTerminalControlListbox control)
        {
            if (control == null || control.Id != "Script" || ScriptControls.ContainsKey(control))
                return;

            IMyTerminalControlListbox shadow = CreateScriptListBoxShadow(control);
            var captured = new CapturedScriptControl(control.Visible, shadow);

            shadow.Visible = block => ScriptControlShadowVisible(captured, block);
            shadow.Enabled = control.Enabled;
            shadow.SupportsMultipleBlocks = control.SupportsMultipleBlocks;
            control.Visible = block =>
            {
                bool originalVisible = captured.OriginalVisible == null || captured.OriginalVisible(block);
                return originalVisible && !ScriptControlShadowVisible(captured, block);
            };
            ScriptControls[control] = captured;
        }

        IMyTerminalControlListbox CreateScriptListBoxShadow(IMyTerminalControlListbox original)
        {
            var shadow = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyTerminalBlock>(
                MOD_PREFIX + ("Shadow_Script_" + _scriptControlShadowId++));

            shadow.Title = original.Title;
            shadow.Tooltip = original.Tooltip;
            shadow.Multiselect = original.Multiselect;
            shadow.VisibleRowsCount = original.VisibleRowsCount;
            shadow.ListContent = FillFilteredScriptsContent;
            shadow.ItemSelected = SelectFilteredScript;
            return shadow;
        }

        bool ScriptControlShadowVisible(CapturedScriptControl captured, IMyTerminalBlock block)
        {
            return captured.OriginalVisible == null || captured.OriginalVisible(block);
        }

        void CaptureSurfaceControl(IMyTerminalControl control)
        {
            string controlId = NormalizeSurfaceControlId(control != null ? control.Id : null);
            if (control == null || string.IsNullOrEmpty(controlId) || SurfaceControls.ContainsKey(control))
                return;

            IMyTerminalControl shadow = CreateSurfaceControlShadow(control, controlId);
            if (shadow == null)
                return;

            var captured = new CapturedSurfaceControl(controlId, control.Visible, shadow);
            shadow.Visible = block => SurfaceControlVisible(captured, block);
            shadow.Enabled = control.Enabled;
            shadow.SupportsMultipleBlocks = control.SupportsMultipleBlocks;
            SurfaceControls[control] = captured;
        }

        string NormalizeSurfaceControlId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            for (int i = 0; i < SurfaceControlIds.Length; i++)
                if (SurfaceControlIds[i] == id)
                    return id;

            switch (id)
            {
                case "TextPaddingSlider":
                    return "TextPadding";
                case "alignment":
                    return "Alignment";
                default:
                    return null;
            }
        }

        IMyTerminalControl CreateSurfaceControlShadow(IMyTerminalControl original, string logicalId)
        {
            var combo = original as IMyTerminalControlCombobox;
            if (combo != null)
                return CreateComboBoxShadow(combo, logicalId);

            var slider = original as IMyTerminalControlSlider;
            if (slider != null)
                return CreateSliderShadow(slider, logicalId);

            return null;
        }

        IMyTerminalControl CreateComboBoxShadow(IMyTerminalControlCombobox original, string logicalId)
        {
            var shadow = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyTerminalBlock>(
                MOD_PREFIX +("Shadow_" + logicalId + "_" + _surfaceControlShadowId++));

            shadow.Title = original.Title;
            shadow.Tooltip = original.Tooltip;
            shadow.Getter = original.Getter;
            shadow.Setter = original.Setter;
            shadow.ComboBoxContent = original.ComboBoxContent;
            return shadow;
        }

        IMyTerminalControl CreateSliderShadow(IMyTerminalControlSlider original, string logicalId)
        {
            var shadow = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyTerminalBlock>(
                MOD_PREFIX +("Shadow_" + logicalId + "_" + _surfaceControlShadowId++));

            shadow.Title = original.Title;
            shadow.Tooltip = original.Tooltip;
            shadow.Getter = original.Getter;
            shadow.Setter = original.Setter;
            shadow.Writer = original.Writer;
            SetSourceValidatedSliderLimits(shadow, logicalId);
            return shadow;
        }

        void SetSourceValidatedSliderLimits(IMyTerminalControlSlider shadow, string logicalId)
        {
            switch (logicalId)
            {
                case "FontSize":
                    shadow.SetLimits(0.1f, 10f);
                    break;
                case "TextPadding":
                    shadow.SetLimits(0f, 50f);
                    break;
            }
        }

        bool SurfaceControlVisible(CapturedSurfaceControl captured, IMyTerminalBlock block)
        {
            var provider = block as IMyTextSurfaceProvider;
            if (provider == null)
                return false;

            var multiTextPanel = block.Components.Get<MyMultiTextPanelComponent>();
            int surfaceIndex = multiTextPanel?.SelectedPanelIndex ?? 0;
            var surface = provider.GetSurface(surfaceIndex);
            if (surface == null)
                return false;

            bool vanillaVisible = captured.OriginalVisible != null && captured.OriginalVisible(block);

            return !vanillaVisible &&
                   surface.ContentType == ContentType.SCRIPT &&
                   !string.IsNullOrEmpty(surface.Script) &&
                   GetVisibilityWrapper(captured.LogicalId).VisibleForScript(surface.Script);
        }

        TerminalControlsWrapper GetVisibilityWrapper(string controlId)
        {
            return controlId == "Alignment"
                ? (TerminalControlsWrapper)_textAlignmentControlVisibility
                : _textSurfaceControlsVisibility;
        }

        void SetupProviderTerminal(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            var provider = block as IMyTextSurfaceProvider;
            if (provider == null)
                return;

            LcdModSessionComponent.LastSelectedBlock = block;

            var multiTextPanel = block.Components.Get<MyMultiTextPanelComponent>();
            int surfaceIndex = multiTextPanel?.SelectedPanelIndex ?? 0;
            string script = provider.GetSurface(surfaceIndex)?.Script;

            var visibleControls = string.IsNullOrEmpty(script)
                ? Enumerable.Empty<TerminalControlsWrapper>()
                : Controls.Where(c => c.VisibleForScript(script));

            if (provider is IMyTextPanel)
            {
                InsertSearchScriptTextBox(block, controls);
                InsertScriptListShadow(block, controls);
                ReorderSurfaceControls(controls, script);
                InsertVisibleControls(controls, visibleControls);
            }
            else if (provider.SurfaceCount > 0)
            {
                InsertSearchScriptTextBox(block, controls);
                InsertScriptListShadow(block, controls);
                ReorderSurfaceControls(controls, script);
                InsertVisibleControls(controls, visibleControls);
            }
        }

        IMyTerminalControlTextbox CreateSearchScriptTextBox()
        {
            var textBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyTerminalBlock>(
                MOD_PREFIX + "SearchScript");
            textBox.Title = MyStringId.GetOrCompute(MOD_PREFIX+"Search_Script");
            textBox.Tooltip = MyStringId.GetOrCompute(MOD_PREFIX+"Search_Script_Tooltip");
            textBox.Getter = GetScriptSearchText;
            textBox.Setter = SetScriptSearchText;
            textBox.Visible = block => true;
            return textBox;
        }

        StringBuilder GetScriptSearchText(IMyTerminalBlock block)
        {
            return new StringBuilder(SearchQuery ?? string.Empty);
        }

        void SetScriptSearchText(IMyTerminalBlock block, StringBuilder value)
        {
            string query = value != null ? value.ToString() : string.Empty;
            if (SearchQuery == query)
                return;

            SearchQuery = query;
            block.RefreshTerminal();
        }

        void InsertSearchScriptTextBox(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (SearchScriptTextBox == null ||
                controls == null ||
                controls.Contains(SearchScriptTextBox) ||
                !HasVisibleScriptControl(block, controls))
            {
                return;
            }

            int index = FindControlIndex(controls, "Script");
            if (index < 0)
                return;

            controls.AddOrInsert(SearchScriptTextBox, index);
        }

        void InsertScriptListShadow(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            RemoveScriptControlShadows(controls);

            CapturedScriptControl captured = FindScriptControl(controls);
            if (captured == null || !ScriptControlShadowVisible(captured, block))
                return;

            int index = FindControlIndex(controls, "Script");
            if (index < 0)
                return;

            controls.Insert(Math.Min(index + 1, controls.Count), captured.Shadow);
        }

        void InsertVisibleControls(List<IMyTerminalControl> controls, IEnumerable<TerminalControlsWrapper> visibleControls)
        {
            var index = GetScriptControlsInsertIndex(controls);

            foreach (var control in visibleControls)
            {
                controls.AddOrInsert(control.TerminalControl, index);
                index++;
            }
        }

        void ReorderSurfaceControls(List<IMyTerminalControl> controls, string script)
        {
            if (controls == null)
                return;

            RemoveSurfaceControlShadows(controls);

            if (string.IsNullOrEmpty(script) || !_textSurfaceControlsVisibility.VisibleForScript(script))
                return;

            var movedControls = new List<IMyTerminalControl>(SurfaceControlOrder.Length);
            for (int i = 0; i < SurfaceControlOrder.Length; i++)
            {
                CapturedSurfaceControl captured = FindSurfaceControl(controls, SurfaceControlOrder[i]);
                if (captured == null)
                    continue;

                movedControls.Add(captured.Shadow);
            }

            int insertIndex = FindScriptControlShadowIndex(controls);
            if (insertIndex < 0)
                insertIndex = FindControlIndex(controls, "Script");

            if (insertIndex < 0)
                return;

            insertIndex = Math.Min(insertIndex + 1, controls.Count);
            for (int i = 0; i < movedControls.Count; i++)
                controls.Insert(insertIndex + i, movedControls[i]);
        }

        void RemoveSurfaceControlShadows(List<IMyTerminalControl> controls)
        {
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                var control = controls[i];
                foreach (var captured in SurfaceControls.Values)
                {
                    if (captured.Shadow == control)
                    {
                        controls.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        void RemoveScriptControlShadows(List<IMyTerminalControl> controls)
        {
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                var control = controls[i];
                foreach (var captured in ScriptControls.Values)
                {
                    if (captured.Shadow == control)
                    {
                        controls.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        int GetScriptControlsInsertIndex(List<IMyTerminalControl> controls)
        {
            int insertIndex = FindSurfaceControlShadowIndex(controls);
            if (insertIndex < 0)
                insertIndex = FindScriptControlShadowIndex(controls);
            if (insertIndex < 0)
                insertIndex = FindControlIndex(controls, "Script");

            return insertIndex >= 0 ? insertIndex + 1 : controls.Count;
        }

        int FindControlIndex(List<IMyTerminalControl> controls, string id)
        {
            // SE can include duplicate IDs: inherited generic LCD controls first,
            // followed by the specialized MyTextPanel controls.
            // Prefer the most-derived control, which is added last.
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                IMyTerminalControl control = controls[i];

                if (control != null && control.Id == id)
                    return i;
            }

            return -1;
        }

        CapturedSurfaceControl FindSurfaceControl(List<IMyTerminalControl> controls, string logicalId)
        {
            // Same duplicate-ID rule as FindControlIndex(): LCD panels add their
            // specialized controls after the generic provider controls.
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                var control = controls[i];
                if (control != null && NormalizeSurfaceControlId(control.Id) == logicalId)
                {
                    CapturedSurfaceControl captured;
                    return SurfaceControls.TryGetValue(control, out captured) ? captured : null;
                }
            }

            return null;
        }

        CapturedScriptControl FindScriptControl(List<IMyTerminalControl> controls)
        {
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                var control = controls[i] as IMyTerminalControlListbox;
                CapturedScriptControl captured;
                if (control != null && control.Id == "Script" && ScriptControls.TryGetValue(control, out captured))
                    return captured;
            }

            return null;
        }

        int FindScriptControlShadowIndex(List<IMyTerminalControl> controls)
        {
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                var control = controls[i];
                foreach (var captured in ScriptControls.Values)
                    if (captured.Shadow == control)
                        return i;
            }

            return -1;
        }

        int FindSurfaceControlShadowIndex(List<IMyTerminalControl> controls)
        {
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                var control = controls[i];
                foreach (var captured in SurfaceControls.Values)
                    if (captured.Shadow == control)
                        return i;
            }

            return -1;
        }

        bool HasVisibleScriptControl(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            CapturedScriptControl captured = FindScriptControl(controls);
            return captured != null && ScriptControlShadowVisible(captured, block);
        }

        void FillFilteredScriptsContent(IMyTerminalBlock block,
            List<MyTerminalControlListBoxItem> content,
            List<MyTerminalControlListBoxItem> selected)
        {
            var provider = block as IMyTextSurfaceProvider;
            var factory = MyTextSurfaceScriptFactory.Instance;
            if (provider == null || factory == null)
                return;

            var multiTextPanel = block.Components.Get<MyMultiTextPanelComponent>();
            int surfaceIndex = multiTextPanel?.SelectedPanelIndex ?? 0;
            var surface = provider.GetSurface(surfaceIndex);
            if (surface == null)
                return;

            string query = SearchQuery ?? string.Empty;
            var none = new MyTerminalControlListBoxItem(MySpaceTexts.None, MyStringId.NullOrEmpty, string.Empty);
            if (string.IsNullOrEmpty(surface.Script) ||
                ScriptMatchesQuery(string.Empty, MyTexts.GetString(MySpaceTexts.None), query))
            {
                content.Add(none);
            }

            foreach (var script in factory.Scripts)
            {
                string displayName = MyTexts.GetString(script.Value.DisplayName);
                bool selectedScript = string.Equals(script.Key, surface.Script, StringComparison.InvariantCultureIgnoreCase);
                if (!selectedScript && !ScriptMatchesQuery(script.Key, displayName, query))
                    continue;

                var item = new MyTerminalControlListBoxItem(
                    MyStringId.GetOrCompute(displayName),
                    MyStringId.NullOrEmpty,
                    script.Key);
                content.Add(item);

                if (selectedScript)
                    selected.Add(item);
            }

            if (selected.Count == 0 && string.IsNullOrEmpty(surface.Script) && content.Contains(none))
                selected.Add(none);
        }

        bool ScriptMatchesQuery(string scriptId, string displayName, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            return (!string.IsNullOrEmpty(scriptId) &&
                    scriptId.IndexOf(query, StringComparison.InvariantCultureIgnoreCase) >= 0) ||
                   (!string.IsNullOrEmpty(displayName) &&
                    displayName.IndexOf(query, StringComparison.InvariantCultureIgnoreCase) >= 0);
        }

        void SelectFilteredScript(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selected)
        {
            if (selected == null || selected.Count == 0)
                return;

            var provider = block as IMyTextSurfaceProvider;
            if (provider == null)
                return;

            var multiTextPanel = block.Components.Get<MyMultiTextPanelComponent>();
            int surfaceIndex = multiTextPanel?.SelectedPanelIndex ?? 0;
            var surface = provider.GetSurface(surfaceIndex);
            if (surface != null)
                surface.Script = selected[0].UserData as string ?? string.Empty;
        }

        sealed class CapturedSurfaceControl
        {
            public readonly string LogicalId;
            public readonly Func<IMyTerminalBlock, bool> OriginalVisible;
            public readonly IMyTerminalControl Shadow;

            public CapturedSurfaceControl(string logicalId, Func<IMyTerminalBlock, bool> originalVisible, IMyTerminalControl shadow)
            {
                LogicalId = logicalId;
                OriginalVisible = originalVisible;
                Shadow = shadow;
            }
        }

        sealed class CapturedScriptControl
        {
            public readonly Func<IMyTerminalBlock, bool> OriginalVisible;
            public readonly IMyTerminalControlListbox Shadow;

            public CapturedScriptControl(Func<IMyTerminalBlock, bool> originalVisible, IMyTerminalControlListbox shadow)
            {
                OriginalVisible = originalVisible;
                Shadow = shadow;
            }
        }
    }
}
