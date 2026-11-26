using Generated;
using LcdMod.Client.Config;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.GUI.TextPanel;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls
{
    /// <summary>
    ///     Wrapper around <see cref="IMyTerminalControl" />, contains meta-information about the controls,
    ///     the intended script to have it, and its required methods
    /// </summary>
    public abstract class TerminalControlsWrapper : IVisibleTerminalControl
    {
        /// <summary>
        ///     Controls to be displayed on the Terminal
        /// </summary>
        public abstract IMyTerminalControl TerminalControl { get; }

        protected virtual bool RequiresAdvancedTweakables => false;

        /// <summary>The slider's limits as set through <see cref="SetSliderLimits"/>; the ModAPI cannot read them back.</summary>
        public float SliderMinimum { get; private set; }
        public float SliderMaximum { get; private set; } = 1f;

        protected void SetSliderLimits(IMyTerminalControlSlider slider, float minimum, float maximum)
        {
            SliderMinimum = minimum;
            SliderMaximum = maximum;
            slider.SetLimits(minimum, maximum);
        }

        /// <summary>
        /// Whether the control applies to an app of the given script running inside another surface (a multi-function
        /// display page or window). Call it inside <see cref="ConfigManager.RunNested"/> for that app's config.
        /// </summary>
        public bool IsVisibleForHostedApp(IMyTerminalBlock block, string script)
        {
            if (RequiresAdvancedTweakables && !LocalConfigManager.AdvancedTweakables)
                return false;
            return VisibleForScript(script) && IsAvailableForCurrentConfig(block);
        }

        /// <summary>
        ///     Gets the current selected surface index from <see cref="block" />'s terminal
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public int GetThisSurfaceIndex(IMyTerminalBlock block)
        {
            var multiTextPanel = block.Components.Get<MyMultiTextPanelComponent>();
            return multiTextPanel?.SelectedPanelIndex ?? 0;
        }
        
        /// <summary>
        ///     Gets the current selected surface from <see cref="block" />'s terminal
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public IMyTextSurface GetThisSurface(IMyTerminalBlock block)
        {
            var index = GetThisSurfaceIndex(block);
            return (IMyTextSurface)((IMyTextSurfaceProvider)block).GetSurface(index);
        }

        /// <summary>
        ///     Getter for controls "Visible" property
        /// </summary>
        /// <param name="block">Reference block</param>
        /// <returns>Boolean indicating if the block is visible or not</returns>
        public virtual bool Visible(IMyTerminalBlock block)
        {
            if (RequiresAdvancedTweakables && !LocalConfigManager.AdvancedTweakables)
                return false;

            var sf = ((IMyTextSurfaceProvider)block).GetSurface(GetThisSurfaceIndex(block));
            return !string.IsNullOrEmpty(sf?.Script) && sf.ContentType == ContentType.SCRIPT &&
                   VisibleForScript(sf.Script) && IsAvailableForCurrentConfig(block);
        }

        /// <summary>
        /// Allows schema-driven controls to require an exact component and slot while preserving
        /// the existing script-interface visibility of handwritten controls.
        /// </summary>
        protected virtual bool IsAvailableForCurrentConfig(IMyTerminalBlock block)
        {
            return true;
        }

        public virtual bool VisibleForScript(string script)
        {
            return false;
        }


        /// <summary>
        ///     Create control for <see cref="TControlType" /> for <see cref="IMyTerminalBlock" />
        /// </summary>
        /// <param name="id"></param>
        /// <typeparam name="TControlType">Type of the control</typeparam>
        /// <returns></returns>
        protected TControlType CreateControl<TControlType>(string id)
        {
            return MyAPIGateway.TerminalControls.CreateControl<TControlType, IMyTerminalBlock>(
                MOD_PREFIX + id);
        }
    }
}
