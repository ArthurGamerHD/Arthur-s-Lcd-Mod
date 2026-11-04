using System;
using System.Collections.Generic;
using Acrux.Core.Input;
using Acrux.Lcd;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Utility;
using LcdMod.Common.Helpers;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts.Abstract
{
    // ReSharper disable once PartialTypeWithSinglePart
    public abstract partial class InteractiveSurfaceScript : SurfaceScriptBase, IEyeTracking
    {
        public event Action<InteractiveSurfaceScript, List<MySprite>> OnRenderOverlay;

        // Overlay handlers (FTUE tips) keep shared state, so screens showing one render on the main thread.
        public event Action<InteractiveSurfaceScript, Vector2> OnVisualContact;

        const long CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES = 6;
        long _lastVisualContactFrame = long.MinValue;

        protected InteractiveSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        public Vector2 CursorPosition { get; protected set; } = new Vector2(float.NaN, float.NaN);

        public virtual Vector2 HitTestOffset => Vector2.Zero;

        protected bool InteractiveVisualsDirty { get; private set; }

        protected override void UpdateViewBox()
        {
            var sizeOffset = (Surface.TextureSize - Surface.SurfaceSize) / 2;
            _userPadding = Surface.TextPadding;

            var padding = (Surface.TextPadding / 100f) * Surface.SurfaceSize;
            sizeOffset += padding / 2f;

            ViewBox = ApplyProxyOffsets(new RectangleF(
                sizeOffset.X,
                sizeOffset.Y,
                Surface.SurfaceSize.X - padding.X,
                Surface.SurfaceSize.Y - padding.Y));
        }

        /// <summary>
        /// The cursor style to force on this surface. Inherit (the default) lets Acrux pick the one the control
        /// under the pointer asks for; None hides the cursor.
        /// </summary>
        protected virtual PointerCursor CursorOverride => PointerCursor.Inherit;

        protected virtual LcdGuiHost RetainedGuiHost => RetainedAppGuiHost;

        public virtual bool HandlesPointerButtonInput => RetainedGuiHost != null;

        /// <summary>The logical input owner shared by this surface and its render proxies.</summary>
        public virtual InteractiveSurfaceScript PointerInputOwner => this;

        public virtual void SetPointerButtonState(ControlClickButton button, bool pressed)
        {
            LcdGuiHost host = RetainedGuiHost;
            if (host == null)
                return;
            switch (button)
            {
                case ControlClickButton.Primary:
                    host.SetPrimaryButton(pressed);
                    if (!pressed)
                        LcdTextEntryScreen.TrackFocus(this, host);
                    break;
                case ControlClickButton.Secondary:
                    host.SetSecondaryButton(pressed);
                    break;
                case ControlClickButton.Middle:
                    host.SetMiddleButton(pressed);
                    break;
            }
        }

        public virtual void ResetPointerButtonInput()
        {
            LcdGuiHost host = RetainedGuiHost;
            if (host == null)
                return;
            // Cancel capture before releasing buttons; losing an input target is
            // not a user release/click at the last pointer position.
            host.ClearPointer();
            host.SetPrimaryButton(false);
            host.SetSecondaryButton(false);
            host.SetMiddleButton(false);
        }

        const long RENDER_REASON_LOG_INTERVAL = 300;

        long _lastRenderReasonLogFrame = -RENDER_REASON_LOG_INTERVAL;


        protected virtual bool RenderContinuouslyWhileLookedAt => true;

        public virtual void LookAt(Vector2 onScreenCoordinates)
        {
            bool hadRecentVisualContact = HasRecentVisualContact;
            _lastVisualContactFrame = MyAPIGateway.Session.GameplayFrameCounter;
            CursorPosition = onScreenCoordinates;
            OnLookAt(onScreenCoordinates);
            RaiseVisualContact(onScreenCoordinates);

            try
            {
                bool pointerChanged = UpdateCursor();
                bool contentChanged = !hadRecentVisualContact || pointerChanged ||
                                      App != null && App.IsDirty;
                if (RenderContinuouslyWhileLookedAt || contentChanged)
                {
                    bool cursorOnly = !contentChanged && CanReuseContentFrame();
                    if (!cursorOnly)
                        LogFullRenderReason(!hadRecentVisualContact, pointerChanged, App != null && App.IsDirty);
                    RequestRender(false, cursorOnly);
                }
            }
            catch (Exception e)
            {
                OnException(e);
            }
        }

        public void MouseScroll(int delta)
        {
            bool handled = false;
            OnMouseScroll(delta, ref handled);
        }

        protected virtual void OnLookAt(Vector2 onScreenCoordinates)
        {
            LcdGuiHost host = RetainedGuiHost;
            if (host == null)
                return;
            host.MovePointer(onScreenCoordinates);
        }

        protected virtual void OnMouseScroll(int delta, ref bool handled)
        {
            LcdGuiHost host = RetainedGuiHost;
            if (host != null && delta != 0 && host.Scroll(delta))
            {
                handled = true;
                return;
            }

            if(!MyAPIGateway.Input.IsAnyShiftKeyPressed())
                return;
           
            float font = Surface.FontSize;
            float step = delta > 0 ? 1.1f : 1f / 1.1f;
            float nextFont = font * step;
            Surface.FontSize = (float)MathHelper.Clamp(nextFont, 0.1, 10);
            handled = true;
        }

        protected bool HasRecentVisualContact =>
            _lastVisualContactFrame != long.MinValue &&
            MyAPIGateway.Session.GameplayFrameCounter - _lastVisualContactFrame <=
            CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES;

        public override void SafeRun()
        {
            InteractiveVisualsDirty = false;
            if (!HasRecentVisualContact)
            {
                CursorPosition = new Vector2(float.NaN, float.NaN);
                if (UpdateCursor())
                    InteractiveVisualsDirty = true;
            }
        }

        public void ShowMessageBox(
            string title,
            string content,
            string button1,
            string button2,
            Action<object, object> button1Callback,
            Action<object, object> button2Callback = null,
            string icon = null)
        {
            var app = RetainedViewApp;
            if (app == null)
                return;
            app.ShowDialog(new Apps.Dialogs.MessageBoxView(new Apps.Dialogs.MessageBoxViewModel(
                title,
                content,
                button1,
                Invoke(button1Callback),
                button2,
                Invoke(button2Callback),
                icon)));
        }

        internal void ShowMessageBox(string title, string content, params Apps.Dialogs.DialogButton[] buttons)
        {
            var app = RetainedViewApp;
            if (app != null)
                app.ShowMessage(title, content, buttons);
        }

        static Action Invoke(Action<object, object> callback)
        {
            return callback == null ? (Action)null : delegate { callback(null, null); };
        }

        LcdGuiHost _layersHost;
        PointerCursor _cursorOverride = PointerCursor.Inherit;
        PointerCursor _cursor = PointerCursor.Arrow;
        float _cursorScale = 1f;

        // Tells Acrux which retained UI and cursor override this surface has now; returns whether the cursor style it
        // will draw changed.
        bool UpdateCursor()
        {
            LcdSurfaceLayers layers = Layers;
            LcdGuiHost host = RetainedGuiHost;
            if (!ReferenceEquals(_layersHost, host))
            {
                _layersHost = host;
                layers.Host = host;
            }

            PointerCursor cursorOverride = CursorOverride;
            if (_cursorOverride != cursorOverride)
            {
                _cursorOverride = cursorOverride;
                layers.Cursor = cursorOverride;
            }

            PointerCursor next = layers.EffectiveCursor;
            bool changed = _cursor != next;
            _cursor = next;
            return changed;
        }

        readonly List<MySprite> _overlay = new List<MySprite>();

        // Logs, at most every few seconds, why a looked-at screen redraws its whole content instead of only the cursor.
        // A steady stream of these means something invalidates the UI every frame.
        void LogFullRenderReason(bool newContact, bool cursorChanged, bool appDirty)
        {
            long frame = MyAPIGateway.Session.GameplayFrameCounter;
            if (frame - _lastRenderReasonLogFrame < RENDER_REASON_LOG_INTERVAL)
                return;
            _lastRenderReasonLogFrame = frame;
            LcdGuiHost host = RetainedGuiHost;
            string reason = host == null ? "no retained host" : host.DescribeRenderReason() ?? "UI tree clean";
            string dirtySource = appDirty && App is Apps.Abstract.App ? ((Apps.Abstract.App)App).DescribeDirtySource() : "-";
            LogHelper.LogInfo("[Render] " + GetType().Name + " redraws while looked at (new contact=" + newContact +
                              ", cursor changed=" + cursorChanged + ", app dirty=" + appDirty + " from " + dirtySource +
                              "): tree " + reason);
        }

        bool CanReuseContentFrame()
        {
            return Layers.CanReuseContent;
        }

        /// <summary>
        /// Hands the frame's layers to Acrux, which draws them: this script's content, the overlay (FTUE tips)
        /// and the cursor. While the screen is only being looked at, Acrux keeps the content and overlay and
        /// moves the cursor over them instead of rebuilding unchanged content every frame.
        /// </summary>
        protected override List<MySprite> RenderFrame(Func<List<MySprite>> sprites)
        {
            UpdateCursor();
            LcdSurfaceLayers layers = Layers;

            // Rechecked here: the render may run later in the frame, after something else changed the content.
            if (!RenderingCursorOnly || !layers.CanReuseContent)
            {
                layers.SetContent(base.RenderFrame(sprites));
                _overlay.Clear();
                RaiseRenderOverlay(_overlay);
                layers.SetOverlay(_overlay);
            }

            float cursorScale = InteractionComponent.CursorScale;
            if (_cursorScale != cursorScale)
            {
                _cursorScale = cursorScale;
                layers.CursorScale = cursorScale;
            }
            layers.CursorPosition = CursorPosition;
            layers.CursorVisible = HasRecentVisualContact;

            List<MySprite> frame = layers.Compose();
            _cursorDrawn = layers.CursorDrawn;
            return frame;
        }

        bool _cursorDrawn;

        public override void Run()
        {
            base.Run();
            if (_cursorDrawn && !HasRecentVisualContact)
                RenderSprites();
        }

        void RaiseVisualContact(Vector2 onScreenCoordinates)
        {
            var handlers = OnVisualContact;
            if (handlers == null)
                return;

            foreach (var @delegate in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<InteractiveSurfaceScript, Vector2>)@delegate)(this, onScreenCoordinates);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, this);
                }
            }
        }

        void RaiseRenderOverlay(List<MySprite> sprites)
        {
            var handlers = OnRenderOverlay;
            if (handlers == null || sprites == null)
                return;

            foreach (var @delegate in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<InteractiveSurfaceScript, List<MySprite>>)@delegate)(this, sprites);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, this);
                }
            }
        }

        public MyEntity3DSoundEmitter SoundEmitter { get; set; }
        public bool RequiresAlt => InteractionComponent.RequiresAlt;

        public void PlaySounds(MySoundPair sound, bool playIn2D = false)
        {
            if (SoundEmitter == null)
            {
                SoundEmitter = new MyEntity3DSoundEmitter((MyEntity)Block, dopplerScaler: 0.0f)
                {
                    Force3D = true,
                    VolumeMultiplier = 1,
                    CustomVolume = 1.5f,
                    CustomMaxDistance = 30
                };
                SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CanHear].ClearImmediate();
                SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ImplicitEffect].ClearImmediate();
            }

            SoundEmitter.PlaySound(sound, force2D: playIn2D);
        }

        public override void Dispose()
        {
            OnRenderOverlay = null;
            OnVisualContact = null;
            SoundEmitter?.Cleanup();
            SoundEmitter = null;
            base.Dispose();
        }
    }
}
