using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Ftue
{
    internal abstract class FtueTip
    {
        readonly List<Type> _appTypeWhitelist = new List<Type>();

        protected FtueTip(string id, params Type[] appTypeWhitelist)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A FTUE tip id is required.", nameof(id));

            Id = id;
            if (appTypeWhitelist == null)
                return;

            for (int i = 0; i < appTypeWhitelist.Length; i++)
            {
                var appType = appTypeWhitelist[i];
                if (appType != null && !_appTypeWhitelist.Contains(appType))
                    _appTypeWhitelist.Add(appType);
            }
        }

        public string Id { get; }
        public bool IsCompleted { get; private set; }
        public IReadOnlyList<Type> AppTypeWhitelist => _appTypeWhitelist;

        protected void SetCompleted(bool completed)
        {
            IsCompleted = completed;
        }

        internal virtual void OnHooked(InteractiveSurfaceScript surface, IApp app)
        {
        }

        internal virtual void OnUnhooked(InteractiveSurfaceScript surface, IApp app)
        {
        }

        internal virtual void OnUpdateStart(InteractiveSurfaceScript surface, IApp app)
        {
        }

        internal virtual void OnUpdateEnd(InteractiveSurfaceScript surface, IApp app)
        {
        }

        internal virtual void OnVisualContact(InteractiveSurfaceScript surface, IApp app, Vector2 coordinates)
        {
        }

        internal virtual void OnCollectInteractiveEntries(
            InteractiveSurfaceScript surface,
            IApp app,
            List<Control> entries)
        {
        }

        internal virtual void OnControlPointerEnter(
            InteractiveSurfaceScript surface,
            IApp app,
            ControlTemplate control)
        {
        }

        internal virtual void OnControlPointerLeave(
            InteractiveSurfaceScript surface,
            IApp app,
            ControlTemplate control)
        {
        }

        internal virtual void OnControlClick(
            InteractiveSurfaceScript surface,
            IApp app,
            ControlTemplate control,
            ControlClickButton button)
        {
        }

        internal virtual void OnRender(InteractiveSurfaceScript surface, IApp app, List<MySprite> frame)
        {
        }
    }
}
