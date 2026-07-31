using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Modules.EyeTracking;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Ftue
{
    internal sealed class FtueService
    {
        sealed class AppBinding
        {
            public readonly IApp App;
            public readonly List<FtueTip> Tips;

            public AppBinding(IApp app, List<FtueTip> tips)
            {
                App = app;
                Tips = tips;
            }
        }

        readonly List<FtueTip> _tips = new List<FtueTip>();
        readonly Dictionary<InteractiveSurfaceScript, AppBinding> _bindings =
            new Dictionary<InteractiveSurfaceScript, AppBinding>();
        bool _loaded;

        public void Load()
        {
            if (_loaded)
                return;

            _tips.Clear();
            _tips.AddRange(FtueTipCatalog.CreateTips());
            _loaded = true;

            SurfaceScriptBase.Instances.AppRegistered += HandleAppRegistered;
            SurfaceScriptBase.Instances.AppUnregistered += HandleAppUnregistered;
            EyeTrackingModule.OnControlPointerEnter += HandleControlPointerEnter;
            EyeTrackingModule.OnControlPointerLeave += HandleControlPointerLeave;
            EyeTrackingModule.OnControlClick += HandleControlClick;
            HookEveryApp();
        }

        public void Unload()
        {
            if (!_loaded)
                return;

            _loaded = false;
            SurfaceScriptBase.Instances.AppRegistered -= HandleAppRegistered;
            SurfaceScriptBase.Instances.AppUnregistered -= HandleAppUnregistered;
            EyeTrackingModule.OnControlPointerEnter -= HandleControlPointerEnter;
            EyeTrackingModule.OnControlPointerLeave -= HandleControlPointerLeave;
            EyeTrackingModule.OnControlClick -= HandleControlClick;
            UnhookEveryApp();
            _tips.Clear();
            FtueTipSlotScheduler.Clear();
        }

        public void ResetCommand(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Usage: /lcdmod resetftue");
                return;
            }

            try
            {
                int cleared = Reset();
                MyAPIGateway.Utilities.ShowMessage(
                    "lcdMod",
                    "FTUE reset. Cleared " + cleared + " completed tip" + (cleared == 1 ? "." : "s."));
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Unable to reset FTUE. See the log for details.");
            }
        }

        int Reset()
        {
            Unload();
            try
            {
                return LocalConfigManager.ClearCompletedFtueTips();
            }
            finally
            {
                Load();
            }
        }

        void HandleAppRegistered(SurfaceScriptBase surface, IApp app)
        {
            TryHookApp(surface, app);
        }

        void HandleAppUnregistered(SurfaceScriptBase surface, IApp app)
        {
            UnhookApp(surface, app);
        }

        void HookEveryApp()
        {
            if (!_loaded)
                return;

            foreach (var surface in SurfaceScriptBase.Instances)
                TryHookApp(surface, GetApp(surface));
        }

        void UnhookEveryApp()
        {
            if (_bindings.Count == 0)
                return;

            var surfaces = new List<InteractiveSurfaceScript>(_bindings.Keys);
            for (int i = 0; i < surfaces.Count; i++)
                UnhookApp(surfaces[i], null);
        }

        void TryHookApp(SurfaceScriptBase surface, IApp app)
        {
            if (!_loaded || app == null)
                return;

            var interactive = surface as InteractiveSurfaceScript;
            if (interactive == null)
                return;

            AppBinding existing;
            if (_bindings.TryGetValue(interactive, out existing))
            {
                if (ReferenceEquals(existing.App, app))
                    return;

                UnhookApp(interactive, existing.App);
            }

            var applicableTips = GetApplicableTips(app.GetType());
            if (applicableTips.Count == 0)
                return;

            interactive.OnUpdateStart += HandleUpdateStart;
            interactive.OnUpdateEnd += HandleUpdateEnd;
            interactive.OnVisualContact += HandleVisualContact;
            interactive.OnCollectOverlayEntries += HandleCollectOverlayEntries;
            interactive.OnRenderOverlay += HandleRenderOverlay;

            var binding = new AppBinding(app, applicableTips);
            _bindings[interactive] = binding;
            for (int i = 0; i < binding.Tips.Count; i++)
            {
                try
                {
                    binding.Tips[i].OnHooked(interactive, app);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, interactive);
                }
            }
        }

        void UnhookApp(SurfaceScriptBase surface, IApp app)
        {
            var interactive = surface as InteractiveSurfaceScript;
            if (interactive == null)
                return;

            AppBinding binding;
            if (!_bindings.TryGetValue(interactive, out binding))
                return;

            if (app != null && !ReferenceEquals(binding.App, app))
                return;

            for (int i = 0; i < binding.Tips.Count; i++)
            {
                try
                {
                    binding.Tips[i].OnUnhooked(interactive, binding.App);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, interactive);
                }
            }

            interactive.OnUpdateStart -= HandleUpdateStart;
            interactive.OnUpdateEnd -= HandleUpdateEnd;
            interactive.OnVisualContact -= HandleVisualContact;
            interactive.OnCollectOverlayEntries -= HandleCollectOverlayEntries;
            interactive.OnRenderOverlay -= HandleRenderOverlay;
            _bindings.Remove(interactive);
        }

        List<FtueTip> GetApplicableTips(Type appType)
        {
            var applicable = new List<FtueTip>();
            for (int i = 0; i < _tips.Count; i++)
            {
                var tip = _tips[i];
                if (!tip.IsCompleted && TipMatchesWhitelist(tip, appType))
                    applicable.Add(tip);
            }

            return applicable;
        }

        bool TryGetActiveBinding(InteractiveSurfaceScript surface, out AppBinding binding)
        {
            binding = null;
            if (surface == null || !_bindings.TryGetValue(surface, out binding))
                return false;

            for (int i = 0; i < binding.Tips.Count; i++)
            {
                if (!binding.Tips[i].IsCompleted)
                    return true;
            }

            UnhookApp(surface, null);
            binding = null;
            return false;
        }

        void HandleUpdateStart(SurfaceScriptBase surface)
        {
            DispatchUpdate(surface, true);
        }

        void HandleUpdateEnd(SurfaceScriptBase surface)
        {
            DispatchUpdate(surface, false);
        }

        void DispatchUpdate(SurfaceScriptBase surface, bool starting)
        {
            var interactive = surface as InteractiveSurfaceScript;
            AppBinding binding;
            if (!TryGetActiveBinding(interactive, out binding))
                return;

            for (int i = 0; i < binding.Tips.Count; i++)
            {
                var tip = binding.Tips[i];
                if (tip.IsCompleted)
                    continue;

                try
                {
                    if (starting)
                        tip.OnUpdateStart(interactive, binding.App);
                    else
                        tip.OnUpdateEnd(interactive, binding.App);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, interactive);
                }
            }
        }

        void HandleVisualContact(InteractiveSurfaceScript surface, Vector2 coordinates)
        {
            AppBinding binding;
            if (!TryGetActiveBinding(surface, out binding))
                return;

            for (int i = 0; i < binding.Tips.Count; i++)
            {
                var tip = binding.Tips[i];
                if (tip.IsCompleted)
                    continue;

                try
                {
                    tip.OnVisualContact(surface, binding.App, coordinates);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, surface);
                }
            }
        }

        void HandleCollectOverlayEntries(InteractiveSurfaceScript surface, List<Control> entries)
        {
            AppBinding binding;
            if (entries == null || !TryGetActiveBinding(surface, out binding))
                return;

            for (int i = 0; i < binding.Tips.Count; i++)
            {
                var tip = binding.Tips[i];
                if (tip.IsCompleted)
                    continue;

                try
                {
                    tip.OnCollectInteractiveEntries(surface, binding.App, entries);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, surface);
                }
            }
        }

        void HandleControlPointerEnter(InteractiveSurfaceScript surface, ControlTemplate control)
        {
            DispatchControlPointer(surface, control, true);
        }

        void HandleControlPointerLeave(InteractiveSurfaceScript surface, ControlTemplate control)
        {
            DispatchControlPointer(surface, control, false);
        }

        void DispatchControlPointer(
            InteractiveSurfaceScript surface,
            ControlTemplate control,
            bool entered)
        {
            AppBinding binding;
            if (control == null || !TryGetActiveBinding(surface, out binding))
                return;

            for (int i = 0; i < binding.Tips.Count; i++)
            {
                var tip = binding.Tips[i];
                if (tip.IsCompleted)
                    continue;

                try
                {
                    if (entered)
                        tip.OnControlPointerEnter(surface, binding.App, control);
                    else
                        tip.OnControlPointerLeave(surface, binding.App, control);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, surface);
                }
            }
        }

        void HandleControlClick(
            InteractiveSurfaceScript surface,
            ControlTemplate control,
            ControlClickButton button)
        {
            AppBinding binding;
            if (control == null || !TryGetActiveBinding(surface, out binding))
                return;

            for (int i = 0; i < binding.Tips.Count; i++)
            {
                var tip = binding.Tips[i];
                if (tip.IsCompleted)
                    continue;

                try
                {
                    tip.OnControlClick(surface, binding.App, control, button);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, surface);
                }
            }
        }

        void HandleRenderOverlay(InteractiveSurfaceScript surface, List<MySprite> frame)
        {
            AppBinding binding;
            if (frame == null || !TryGetActiveBinding(surface, out binding))
                return;

            for (int i = 0; i < binding.Tips.Count; i++)
            {
                var tip = binding.Tips[i];
                if (tip.IsCompleted)
                    continue;

                try
                {
                    tip.OnRender(surface, binding.App, frame);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, surface);
                }
            }
        }

        static IApp GetApp(SurfaceScriptBase surface)
        {
            if (surface == null)
                return null;

            try
            {
                return surface.App;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, surface);
                return null;
            }
        }

        static bool TipMatchesWhitelist(FtueTip tip, Type appType)
        {
            if (tip == null || appType == null || MyAPIGateway.Reflection == null)
                return false;

            var whitelist = tip.AppTypeWhitelist;
            for (int i = 0; i < whitelist.Count; i++)
            {
                var whitelistedType = whitelist[i];
                if (whitelistedType != null &&
                    MyAPIGateway.Reflection.IsAssignableFrom(whitelistedType, appType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
