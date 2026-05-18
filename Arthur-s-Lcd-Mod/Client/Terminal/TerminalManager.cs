using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Blueprint;
using LcdMod.Client.Terminal.Controls.Color;
using LcdMod.Client.Terminal.Controls.Filter;
using LcdMod.Client.Terminal.Controls.Filter.Buttons;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Client.Terminal.Controls.Interactive;
using LcdMod.Client.Terminal.Controls.Proxy;
using LcdMod.Client.Terminal.Controls.Scale;
using LcdMod.Common.Helpers;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace LcdMod.Client.Terminal
{
    public class TerminalManager
    {
        public readonly List<TerminalControlsWrapper> Controls = new List<TerminalControlsWrapper>();

        readonly LcdModSessionComponent _session;
        
        public static IMyTerminalControlButton CustomDataButton;
        public static IMyTerminalControlButton ShowTextPanelButton;

        public TerminalManager(LcdModSessionComponent session)
        {
            _session = session;
        }

        public void Initialize()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;
            
            Controls.Add(new ButtonShowConfig());


            TerminalControlsListbox source = new ListboxBlockCandidates();
            TerminalControlsListbox target = new ListboxBlockSelected();

            Controls.Add(new SliderFontSize());
            Controls.Add(new SliderPadding());
            Controls.Add(new SliderFov());
            Controls.Add(new SliderRadarRange());

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
            Controls.Add(new SwitchToggleLines());

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

            Controls.Add(new ComboboxSorting());
            Controls.Add(new SliderProxyX());
            Controls.Add(new SliderProxyY());
        }

        public void Unload()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlGetter;
            Controls.Clear();
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
            // no use for this right now, but it's a "nice to have" way to get a TextBox 
            if (CustomDataButton != null && ShowTextPanelButton != null)
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
            }
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
                controls.AddRange(visibleControls.Select(c => c.TerminalControl));
            }
            else if (provider.SurfaceCount > 0)
            {
                var index = controls.FindIndex(p => p.Id == "Script") + 3;

                foreach (var control in visibleControls)
                {
                    controls.AddOrInsert(control.TerminalControl, index);
                    index++;
                }
            }
        }
    }
}
