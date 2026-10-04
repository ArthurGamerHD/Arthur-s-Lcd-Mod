using System;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using Adk.Gui.Lcd;
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

        public abstract CursorType CursorType { get; protected set; }

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
                    _renderCursorOnly = !contentChanged && CanReuseContentFrame();
                    try
                    {
                        RenderSprites();
                    }
                    finally
                    {
                        _renderCursorOnly = false;
                    }
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

        bool UpdateCursor()
        {
            CursorType next = ResolveRetainedCursorType();
            bool changed = CursorType != next;
            CursorType = next;
            return changed;
        }

        protected virtual CursorType ResolveRetainedCursorType()
        {
            LcdGuiHost host = RetainedGuiHost;
            if (host == null)
                return CursorType.Default;
            switch (host.PointerCursor)
            {
                case PointerCursor.Hand:
                    return CursorType.Hand;
                case PointerCursor.DragMove:
                    return CursorType.Grab;
                case PointerCursor.Wait:
                    return CursorType.WaitCursor;
                case PointerCursor.Cross:
                    return CursorType.Cross;
                case PointerCursor.No:
                    return CursorType.No;
                case PointerCursor.None:
                    return CursorType.None;
                default:
                    return CursorType.Default;
            }
        }

        /// <summary>
        /// The last composed frame without the cursor. While the screen is only being looked at,
        /// the cursor is drawn over it instead of rebuilding unchanged content every frame.
        /// </summary>
        readonly List<MySprite> _contentFrame = new List<MySprite>();
        readonly List<MySprite> _cursorFrame = new List<MySprite>();
        bool _hasContentFrame;
        bool _renderCursorOnly;

        bool CanReuseContentFrame()
        {
            LcdGuiHost host = RetainedGuiHost;
            return _hasContentFrame && host != null && !host.NeedsRender;
        }

        protected override void LayoutChanged()
        {
            _hasContentFrame = false;
            base.LayoutChanged();
        }

        protected override List<MySprite> RenderFrame(Func<List<MySprite>> sprites)
        {
            UpdateCursor();

            if (_renderCursorOnly && _hasContentFrame)
            {
                _cursorFrame.Clear();
                _cursorFrame.AddRange(_contentFrame);
                AddCursorSprites(_cursorFrame);
                return _cursorFrame;
            }

            var spriteList = ComposeContentFrame(sprites);
            _contentFrame.Clear();
            _contentFrame.AddRange(spriteList);
            _hasContentFrame = true;
            AddCursorSprites(spriteList);
            return spriteList;
        }

        List<MySprite> ComposeContentFrame(Func<List<MySprite>> sprites)
        {
            var spriteList = base.RenderFrame(sprites);
            RaiseRenderOverlay(spriteList);
            return spriteList;
        }

        bool _cursorDrawn;

        public override void Run()
        {
            base.Run();
            if (_cursorDrawn && !HasRecentVisualContact)
                RenderSprites();
        }

        void AddCursorSprites(List<MySprite> spriteList)
        {
            _cursorDrawn = false;
            if (InteractionComponent.CursorScale == 0 || CursorType == CursorType.None)
                return;

            var cursor = CursorType;
            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                cursor = CursorType.None;
            _cursorDrawn = cursor != CursorType.None;

            Cursor.AddCursor(spriteList,
                cursor,
                position,
                new Vector2(32), // hardcoded size
                InteractionComponent.CursorScale);
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
                //SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ShouldPlay2D].ClearImmediate();
                //SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CueType].ClearImmediate();
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
