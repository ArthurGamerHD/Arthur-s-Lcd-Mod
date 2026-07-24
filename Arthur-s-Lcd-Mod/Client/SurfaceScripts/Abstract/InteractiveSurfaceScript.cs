using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts.Abstract
{
    public abstract partial class InteractiveSurfaceScript : SurfaceScriptBase, IEyeTracking
    {
        public event Action<InteractiveSurfaceScript, List<Control>> OnCollectOverlayEntries;
        public event Action<InteractiveSurfaceScript, List<MySprite>> OnRenderOverlay;
        public event Action<InteractiveSurfaceScript, Vector2> OnVisualContact;

        const long CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES = 6;
        const long HIDDEN_GLOBAL_MENU_TIMEOUT_FRAMES = 180;
        object _activeTooltipParentObject;
        RectangleF _tooltipRect;
        RectangleF _tooltipKeepOpenRect;
        bool _hasTooltipBounds;
        bool _showHiddenGlobalMenu;
        long _hiddenGlobalMenuVisibleUntilFrame = long.MinValue;
        long _lastVisualContactFrame = long.MinValue;
        ControlTemplate _pointerOverControl;
        protected InteractiveSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _hiddenGlobalMenuControl = new HiddenGlobalMenuControl(this);
        }

        public Vector2 CursorPosition { get; protected set; } = new Vector2(float.NaN, float.NaN);

        public virtual Vector2 HitTestOffset => Vector2.Zero;

        protected Vector2 HitTestCursorPosition => CursorPosition + HitTestOffset;

        RectangleF _baseViewBox;
        readonly HiddenGlobalMenuControl _hiddenGlobalMenuControl;

        readonly List<Control> _interactiveEntriesWithOverlay = new List<Control>();
        int _controlOverlayStartIndex = -1;
        int _controlOverlayCount;
        int _externalOverlayStartIndex = -1;
        int _externalOverlayCount;

        public ICollection<Control> InteractiveEntries => GetInteractiveEntries();
        protected bool InteractiveVisualsDirty { get; private set; }

        public ICollection<Control> GetInteractiveEntries(bool includeDisabled = false)
        {
            _interactiveEntriesWithOverlay.Clear();
            ResetControlOverlayRange();
            ResetExternalOverlayRange();

            if (_dialog != null)
            {
                if (_dialog.Dismissed)
                {
                    _dialog = null;
                }
                else
                {
                    if (includeDisabled)
                        AddBaseInteractiveEntries(_interactiveEntriesWithOverlay);

                    _dialog.AddInteractiveEntries(_interactiveEntriesWithOverlay);
                    AddControlOverlayEntries(_interactiveEntriesWithOverlay);
                    RaiseCollectOverlayEntries(_interactiveEntriesWithOverlay);
                    return _interactiveEntriesWithOverlay;
                }
            }

            AddBaseInteractiveEntries(_interactiveEntriesWithOverlay);
            AddControlOverlayEntries(_interactiveEntriesWithOverlay);
            RaiseCollectOverlayEntries(_interactiveEntriesWithOverlay);
            return _interactiveEntriesWithOverlay;
        }

        void ResetControlOverlayRange()
        {
            _controlOverlayStartIndex = -1;
            _controlOverlayCount = 0;
        }

        void ResetExternalOverlayRange()
        {
            _externalOverlayStartIndex = -1;
            _externalOverlayCount = 0;
        }

        void AddControlOverlayEntries(List<Control> entries)
        {
            if (entries == null)
                return;

            var rootCount = entries.Count;
            _controlOverlayStartIndex = rootCount;
            for (var i = 0; i < rootCount; i++)
            {
                var root = entries[i] as ControlTemplate;
                if (root != null)
                    root.AddOverlayEntries(entries);
            }

            _controlOverlayCount = entries.Count - _controlOverlayStartIndex;
            if (_controlOverlayCount <= 0)
                ResetControlOverlayRange();
        }

        void AddBaseInteractiveEntries(List<Control> entries)
        {
            if (entries == null)
                return;

            entries.AddRange(InteractiveList);
            if (ShouldRenderGlobalMenu())
            {
                _globalMenu?.AddInteractiveEntries(entries);
            }
            else if (CanOpenHiddenGlobalMenu())
            {
                _hiddenGlobalMenuControl.SetRect(_baseViewBox);
                _hiddenGlobalMenuControl.SetVisible(true);
                entries.Insert(0, _hiddenGlobalMenuControl);
            }

            AddActiveTooltipContainer(entries);
        }

        void AddActiveTooltipContainer(List<Control> entries)
        {
            if (_activeTooltipParentEntry == null || !_activeTooltipParentEntry.Visible ||
                _activeTooltipParentEntry.Tooltip == null)
                return;

            var container = _activeTooltipParentEntry.Tooltip.TooltipContainer;
            if (container != null && container.Visible)
                entries.Add(container);
        }

        public virtual List<Control> InteractiveList { get; } = new List<Control>();

        protected override void UpdateViewBox()
        {
            var sizeOffset = (Surface.TextureSize - Surface.SurfaceSize) / 2;
            _userPadding = Surface.TextPadding;

            var padding = (Surface.TextPadding / 100f) * Surface.SurfaceSize;
            sizeOffset += padding / 2f;

            _baseViewBox = ApplyProxyOffsets(new RectangleF(
                sizeOffset.X,
                sizeOffset.Y,
                Surface.SurfaceSize.X - padding.X,
                Surface.SurfaceSize.Y - padding.Y));

            ViewBox = _baseViewBox;

            if (!ShouldRenderGlobalMenu())
                return;

            float reservedHeight = _globalMenu.GetReservedHeight(this, ConfiguredScale, FontScale, Surface);
            reservedHeight += Math.Min(16 * ConfiguredScale, Surface.SurfaceSize.Y * Surface.TextPadding / 100);
            
            ViewBox = new RectangleF(
                _baseViewBox.X,
                _baseViewBox.Y + reservedHeight,
                _baseViewBox.Width,
                Math.Max(0f, _baseViewBox.Height - reservedHeight));
        }

        public abstract CursorType CursorType { get; protected set; }

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
                bool pointerChanged = UpdateCursorFromTopHit();
                bool hoverHandled = TryHoverAtCursor(this);
                if (RenderContinuouslyWhileLookedAt ||
                    !hadRecentVisualContact ||
                    pointerChanged ||
                    hoverHandled ||
                    App != null && App.IsDirty)
                {
                    RenderSprites();
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
            if (TryScrollAtCursor(delta, this))
                handled = true;

            if (!handled)
                OnMouseScroll(delta, ref handled);
        }

        ControlTemplate _activeTooltipParentEntry;
        ControlTemplate _manualTooltipParentEntry;
        object _manualTooltipParentObject;

        Dialog _dialog;
        GlobalMenu _globalMenu;

        protected virtual void OnLookAt(Vector2 onScreenCoordinates)
        {
        }

        protected virtual void OnMouseScroll(int delta, ref bool handled)
        {
            if(!MyAPIGateway.Input.IsAnyShiftKeyPressed())
                return;
           
            float font = Surface.FontSize;
            float step = delta > 0 ? 1.1f : 1f / 1.1f;
            float nextFont = font * step;
            Surface.FontSize = (float)MathHelper.Clamp(nextFont, 0.1, 10);
            handled = true;
        }

        protected bool CursorInsideTooltip => _hasTooltipBounds && _tooltipRect.Contains(HitTestCursorPosition);

        protected bool CursorInsideTooltipKeepOpenArea =>
            _hasTooltipBounds && _tooltipKeepOpenRect.Contains(HitTestCursorPosition);

        protected bool HasRecentVisualContact =>
            _lastVisualContactFrame != long.MinValue &&
            MyAPIGateway.Session.GameplayFrameCounter - _lastVisualContactFrame <=
            CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES;

        protected void ClearTooltip()
        {
            HideAttachedTooltip();
        }

        protected void ClearAllTooltips()
        {
            HideAttachedTooltip();
        }

        public override void SafeRun()
        {
            InteractiveVisualsDirty = false;
            if (!HasRecentVisualContact)
            {
                CursorPosition = new Vector2(float.NaN, float.NaN);
                if (UpdateCursorFromTopHit())
                    InteractiveVisualsDirty = true;

                var tooltip = _activeTooltipParentEntry != null
                    ? _activeTooltipParentEntry.Tooltip
                    : null;
                if (tooltip != null && tooltip.HasBounds)
                    InteractiveVisualsDirty = true;

                ClearTooltip();
            }

            if (UpdateHiddenGlobalMenuLifetime())
                InteractiveVisualsDirty = true;
        }

        void HideAttachedTooltip()
        {
            if (_activeTooltipParentEntry != null && _activeTooltipParentEntry.Tooltip != null)
                _activeTooltipParentEntry.Tooltip.Hide();

            _hasTooltipBounds = false;
            //_cursorInsideClickableTooltipContent = false;
            _tooltipRect = default(RectangleF);
            _tooltipKeepOpenRect = default(RectangleF);

            // Keep tooltip controls alive between frames. Visibility gating keeps hidden
            // tooltip controls out of hit testing and cursor resolution.
        }


        void ClearManualTooltipState()
        {
            _manualTooltipParentEntry = null;
            _manualTooltipParentObject = null;
        }

        static bool TooltipButtonMatches(TooltipActivationMode mode, bool rightClick)
        {
            if (mode == TooltipActivationMode.Click)
                return !rightClick;

            if (mode == TooltipActivationMode.RightClick)
                return rightClick;

            return false;
        }

        ControlTemplate FindVisibleTooltipEntryByContext(object dataContext)
        {
            for (int i = InteractiveList.Count - 1; i >= 0; i--)
            {
                var entry = FindVisibleTooltipEntryByContext(InteractiveList[i] as ControlTemplate, dataContext);
                if (entry != null)
                    return entry;
            }

            return null;
        }

        ControlTemplate FindVisibleTooltipEntryByContext(ControlTemplate entry, object dataContext)
        {
            if (entry == null || !entry.Visible)
                return null;

            if (entry.Tooltip != null && Equals(entry.DataContext, dataContext))
                return entry;

            var children = entry.VisualChildren;
            if (children == null || children.Count == 0)
                return null;

            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = FindVisibleTooltipEntryByContext(children[i] as ControlTemplate, dataContext);
                if (child != null)
                    return child;
            }

            return null;
        }

        ControlTemplate ResolveManualTooltipParent()
        {
            if (_manualTooltipParentEntry != null &&
                _manualTooltipParentEntry.Visible &&
                _manualTooltipParentEntry.Tooltip != null)
            {
                return _manualTooltipParentEntry;
            }

            if (_manualTooltipParentObject != null)
            {
                var entry = FindVisibleTooltipEntryByContext(_manualTooltipParentObject);
                if (entry != null)
                {
                    _manualTooltipParentEntry = entry;
                    return entry;
                }
            }

            ClearManualTooltipState();
            return null;
        }

        ControlTemplate FindTooltipHitTarget()
        {
            var position = HitTestCursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return null;

            for (int i = InteractiveList.Count - 1; i >= 0; i--)
            {
                var root = InteractiveList[i] as ControlTemplate;
                ControlTemplate entry;
                if (root == null || !root.TryResolveTooltipTarget(position, out entry))
                    continue;

                if (entry.Tooltip != null)
                    return entry;
            }

            return null;
        }

        public virtual bool HasTooltipInputAtCursor(bool rightClick)
        {
            var target = FindTooltipHitTarget();
            if (target != null && target.Tooltip != null &&
                (TooltipButtonMatches(target.Tooltip.OpenMode, rightClick) ||
                 TooltipButtonMatches(target.Tooltip.CloseMode, rightClick)))
            {
                return true;
            }

            var active = ResolveManualTooltipParent() ?? _activeTooltipParentEntry;
            if (active != null && active.Visible && active.Tooltip != null && _hasTooltipBounds &&
                TooltipButtonMatches(active.Tooltip.CloseMode, rightClick) &&
                ViewBox.Contains(HitTestCursorPosition))
            {
                return true;
            }

            return false;
        }

        public virtual bool TryHandleTooltipActivationClick(bool rightClick)
        {
            ControlTemplate tooltipParent;
            return TryHandleTooltipActivationClick(rightClick, out tooltipParent);
        }

        public virtual bool TryHandleTooltipActivationClick(bool rightClick, out ControlTemplate tooltipParent)
        {
            tooltipParent = null;

            var position = HitTestCursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return false;

            var active = ResolveManualTooltipParent() ?? _activeTooltipParentEntry;
            if (active != null && active.Visible && active.Tooltip != null && _hasTooltipBounds &&
                TooltipButtonMatches(active.Tooltip.CloseMode, rightClick) &&
                ViewBox.Contains(position))
            {
                tooltipParent = active;
                HideAttachedTooltip();
                ClearManualTooltipState();
                return true;
            }

            var target = FindTooltipHitTarget();
            if (target == null || target.Tooltip == null)
                return false;

            if (!TooltipButtonMatches(target.Tooltip.OpenMode, rightClick))
                return false;

            if (_activeTooltipParentEntry != null && !ReferenceEquals(_activeTooltipParentEntry, target))
                HideAttachedTooltip();

            _manualTooltipParentEntry = target;
            _manualTooltipParentObject = target.DataContext;
            tooltipParent = target;
            return true;
        }

        ControlTemplate FindTooltipTarget()
        {
            var manualParent = ResolveManualTooltipParent();
            if (manualParent != null)
            {
                var manualTooltip = manualParent.Tooltip;
                if (manualTooltip != null && manualTooltip.CloseMode != TooltipActivationMode.Auto)
                    return manualParent;

                var positionForManual = HitTestCursorPosition;
                if (!float.IsNaN(positionForManual.X) && !float.IsNaN(positionForManual.Y) && HasRecentVisualContact &&
                    (manualParent.Hit(positionForManual) || CursorInsideTooltip || CursorInsideTooltipKeepOpenArea))
                {
                    return manualParent;
                }

                ClearManualTooltipState();
                HideAttachedTooltip();
            }

            if (_hasTooltipBounds && _activeTooltipParentEntry != null &&
                _activeTooltipParentEntry.Visible &&
                _activeTooltipParentEntry.Tooltip != null &&
                _activeTooltipParentEntry.Tooltip.CloseMode != TooltipActivationMode.Auto)
            {
                return _activeTooltipParentEntry;
            }

            var position = HitTestCursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return null;

            if (_hasTooltipBounds && (CursorInsideTooltip || CursorInsideTooltipKeepOpenArea))
            {
                var activeParent = _activeTooltipParentObject;
                for (int i = InteractiveList.Count - 1; i >= 0; i--)
                {
                    var entry = FindVisibleTooltipEntryByContext(InteractiveList[i] as ControlTemplate, activeParent);
                    if (entry != null &&
                        entry.Tooltip.OpenMode == TooltipActivationMode.Auto &&
                        Equals(entry.DataContext, activeParent))
                    {
                        return entry;
                    }
                }
            }

            for (int i = InteractiveList.Count - 1; i >= 0; i--)
            {
                var root = InteractiveList[i] as ControlTemplate;
                ControlTemplate entry;
                if (root == null || !root.TryResolveTooltipTarget(position, out entry))
                    continue;

                if (entry.Tooltip.OpenMode != TooltipActivationMode.Auto)
                    continue;

                return entry;
            }

            return null;
        }

        void RenderAttachedTooltip(List<MySprite> sprites)
        {
            //_cursorInsideClickableTooltipContent = false;

            var parentEntry = FindTooltipTarget();
            if (parentEntry == null || parentEntry.Tooltip == null)
            {
                HideAttachedTooltip();
                return;
            }

            if (_activeTooltipParentEntry != null && !ReferenceEquals(_activeTooltipParentEntry, parentEntry))
                HideAttachedTooltip();

            var tooltip = parentEntry.Tooltip;
            var textColor = ForegroundColor;
            var panelColor = ColorComponent.ResolveHeaderColor(Block as IMyTerminalBlock);

            var tooltipSprites = tooltip.Render(
                parentEntry,
                App,
                ViewBox,
                ConfiguredScale,
                FontScale,
                Surface,
                textColor,
                panelColor,
                CursorPosition);

            _activeTooltipParentEntry = parentEntry;
            _activeTooltipParentObject = parentEntry.DataContext;
            _hasTooltipBounds = tooltip.HasBounds;
            _tooltipRect = tooltip.Bounds;
            _tooltipKeepOpenRect = tooltip.KeepOpenBounds;

            sprites.AddRange(tooltipSprites);

            ConfigureTooltipContainer(tooltip);
        }

        void ConfigureTooltipContainer(InteractiveTooltip tooltip)
        {
            if (tooltip == null || tooltip.TooltipContainer == null)
                return;

            var container = tooltip.TooltipContainer;
            container.SetOnClick(null);
            container.OnSecondaryClick = null;

            if (tooltip.CloseMode == TooltipActivationMode.Click)
                container.SetOnClick(DismissTooltipFromContainer);
            else if (tooltip.CloseMode == TooltipActivationMode.RightClick)
                container.OnSecondaryClick = DismissTooltipFromContainer;
        }

        void DismissTooltipFromContainer(object dataContext, object sender)
        {
            HideAttachedTooltip();
            ClearManualTooltipState();
        }


        public void SetGlobalMenu(List<GlobalMenuEntry> entries)
        {
            if (_globalMenu != null)
                _globalMenu.HideEntries();

            _hiddenGlobalMenuControl.SetVisible(false);
            CloseHiddenGlobalMenu();
            _globalMenu = entries == null || entries.Count == 0 ? null : new GlobalMenu(entries);

            UpdateViewBox();
        }

        public virtual void SetGlobalMenu(params GlobalMenuEntry[] entries) => SetGlobalMenu(entries != null ? new List<GlobalMenuEntry>(entries) : null);

        public override void DrawTitle(List<MySprite> frame)
        {
            if (!ShouldRenderGlobalMenu())
            {
                _globalMenu?.HideEntries();
                _hiddenGlobalMenuControl.SetVisible(CanOpenHiddenGlobalMenu());
                base.DrawTitle(frame);
                return;
            }

            if (_globalMenu == null || !_globalMenu.Visible)
            {
                base.DrawTitle(frame);
                return;
            }

            CaretY = ViewBox.Y;
            _globalMenu.Render(
                this,
                frame,
                _baseViewBox,
                ConfiguredScale,
                FontScale,
                Surface,
                ForegroundColor,
                ColorComponent.ResolveHeaderColor(Block as IMyTerminalBlock),
                CursorPosition);
        }

        bool ShouldRenderGlobalMenu()
        {
            return (TitleVisible || _showHiddenGlobalMenu) && _globalMenu != null && _globalMenu.Visible;
        }

        bool CanOpenHiddenGlobalMenu()
        {
            return !TitleVisible && _globalMenu != null && _globalMenu.Visible;
        }

        void OpenHiddenGlobalMenu()
        {
            if (!CanOpenHiddenGlobalMenu())
                return;

            _showHiddenGlobalMenu = true;
            RefreshHiddenGlobalMenuLifetime();
            UpdateViewBox();
            RenderSprites();
        }

        void RefreshHiddenGlobalMenuLifetime()
        {
            var session = MyAPIGateway.Session;
            if (session == null)
                return;

            _hiddenGlobalMenuVisibleUntilFrame = session.GameplayFrameCounter + HIDDEN_GLOBAL_MENU_TIMEOUT_FRAMES;
        }

        bool UpdateHiddenGlobalMenuLifetime()
        {
            if (!_showHiddenGlobalMenu)
                return false;

            if (!CanOpenHiddenGlobalMenu())
            {
                CloseHiddenGlobalMenu();
                UpdateViewBox();
                return true;
            }

            var session = MyAPIGateway.Session;
            if (session == null || !HasRecentVisualContact)
            {
                CloseHiddenGlobalMenu();
                UpdateViewBox();
                return true;
            }

            if (_globalMenu != null && _globalMenu.Hit(CursorPosition))
            {
                RefreshHiddenGlobalMenuLifetime();
                return false;
            }

            if (session.GameplayFrameCounter <= _hiddenGlobalMenuVisibleUntilFrame)
                return false;

            CloseHiddenGlobalMenu();
            UpdateViewBox();
            return true;
        }

        void CloseHiddenGlobalMenu()
        {
            _showHiddenGlobalMenu = false;
            _hiddenGlobalMenuVisibleUntilFrame = long.MinValue;
        }

        sealed class HiddenGlobalMenuControl : RectangleControl
        {
            readonly InteractiveSurfaceScript _owner;

            public HiddenGlobalMenuControl(InteractiveSurfaceScript owner)
                : base(default(RectangleF), CursorType.Default, owner)
            {
                _owner = owner;
                OnSecondaryClick = OnRightClick;
                SetVisible(false);
            }

            public override bool CanClick => Visible;
            public override bool CanPrimaryClick => Visible;
            public override bool CanSecondaryClick => Visible;

            public override bool Click(object sender)
            {
                return true;
            }

            protected override void RenderDefault(List<MySprite> sprites)
            {
                // Hit-test surface only. Do not draw the default rectangle/text when the
                // hidden global-menu opener is part of the interactive-entry render pass.
            }

            void OnRightClick(object dataContext, object sender)
            {
                _owner.OpenHiddenGlobalMenu();
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
            var previousDialog = _dialog;
            var messageBox = new MessageBox(App);
            var wrappedButton2Callback = button2Callback != null || !string.IsNullOrWhiteSpace(button2)
                ? WrapMessageBoxCallback(messageBox, previousDialog, button2Callback)
                : null;
            messageBox.Show(
                title,
                content,
                button1,
                button2,
                WrapMessageBoxCallback(messageBox, previousDialog, button1Callback),
                wrappedButton2Callback,
                icon);
            ShowDialog(messageBox);
        }

        Action<object, object> WrapMessageBoxCallback(Dialog messageBox, Dialog previousDialog, Action<object, object> callback)
        {
            return delegate(object dataContext, object sender)
            {
                if (callback != null)
                    callback(dataContext, sender);

                if (previousDialog != null &&
                    !previousDialog.Dismissed &&
                    (_dialog == null || ReferenceEquals(_dialog, messageBox)))
                {
                    ShowDialog(previousDialog);
                    return;
                }

                RenderSprites();
            };
        }

        internal void ShowDialog(Dialog dialog)
        {
            if (dialog != null && _dialog != null && !_dialog.Dismissed && !ReferenceEquals(dialog, _dialog))
                dialog.SetStyleParent(_dialog);

            _dialog = dialog;
            RenderSprites();
        }

        public virtual bool IsInsideContainer(ControlTemplate entry, Vector2 position)
        {
            if (entry == null || !entry.Visible || entry.VisualChildren == null || entry.VisualChildren.Count == 0)
                return false;

            return entry.Hit(position) || _hasTooltipBounds && ReferenceEquals(entry, _activeTooltipParentEntry);
        }

        bool UpdateCursorFromTopHit()
        {
            ControlTemplate nextPointerOverControl = null;
            CursorType nextCursorType = CursorType.Default;
            var position = HitTestCursorPosition;
            if (!float.IsNaN(position.X) && !float.IsNaN(position.Y) && HasRecentVisualContact)
            {
                ControlTemplate entry;
                if (TryResolveHit(position, out entry))
                {
                    nextPointerOverControl = entry;
                    nextCursorType = entry.Cursor;
                }
            }

            bool pointerChanged = !ReferenceEquals(_pointerOverControl, nextPointerOverControl);
            bool cursorChanged = CursorType != nextCursorType;
            if (pointerChanged)
            {
                if (_pointerOverControl != null)
                    _pointerOverControl.SetPointerOver(false);

                _pointerOverControl = nextPointerOverControl;
                if (_pointerOverControl != null)
                    _pointerOverControl.SetPointerOver(true);
            }
            else if (_pointerOverControl != null)
            {
                _pointerOverControl.RestorePointerOverForRender();
            }

            CursorType = nextCursorType;
            return pointerChanged || cursorChanged;
        }

        public virtual bool TryResolveHitAtCursor(out ControlTemplate entry)
        {
            entry = null;
            var position = HitTestCursorPosition;
            return IsValidHitTestPosition(position) && TryResolveHit(position, out entry);
        }

        public virtual bool TryResolveClickableAtCursor(bool secondary, out ControlTemplate entry)
        {
            entry = null;
            var position = HitTestCursorPosition;
            return IsValidHitTestPosition(position) && TryResolveClickable(position, secondary, out entry);
        }

        public virtual bool TryResolveClickableAtCursor(out ControlTemplate entry)
        {
            entry = null;
            var position = HitTestCursorPosition;
            return IsValidHitTestPosition(position) && TryResolveClickable(position, out entry);
        }

        public virtual bool TryClickAtCursor(bool secondary, object sender, out ControlTemplate entry)
        {
            entry = null;
            var position = HitTestCursorPosition;
            if (!IsValidHitTestPosition(position) || !TryResolveClickable(position, secondary, out entry))
                return false;

            return secondary ? entry.SecondaryClickAt(position, sender) : entry.ClickAt(position, sender);
        }

        public virtual bool TryScrollAtCursor(int delta, object sender)
        {
            var position = HitTestCursorPosition;
            if (!IsValidHitTestPosition(position))
                return false;

            var entries = InteractiveEntries as IList<Control>;
            if (entries == null)
                return false;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var root = entries[i] as ControlTemplate;
                if (root != null && root.Scroll(position, sender, delta))
                    return true;
            }

            return false;
        }

        public virtual bool TryHoverAtCursor(object sender)
        {
            var position = HitTestCursorPosition;
            if (!IsValidHitTestPosition(position))
                return false;

            var entries = InteractiveEntries as IList<Control>;
            if (entries == null)
                return false;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var root = entries[i] as ControlTemplate;
                if (root != null && root.Hover(position, sender))
                    return true;
            }

            return false;
        }

        bool TryResolveHit(Vector2 position, out ControlTemplate entry)
        {
            entry = null;
            var entries = InteractiveEntries as IList<Control>;
            if (entries == null)
                return false;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var root = entries[i] as ControlTemplate;
                if (root != null && root.TryResolveHit(position, out entry))
                    return true;
            }

            return false;
        }

        bool TryResolveClickable(Vector2 position, bool secondary, out ControlTemplate entry)
        {
            entry = null;
            var entries = InteractiveEntries as IList<Control>;
            if (entries == null)
                return false;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var root = entries[i] as ControlTemplate;
                if (root == null)
                    continue;

                bool resolved = secondary
                    ? root.TryResolveSecondaryClickable(position, out entry)
                    : root.TryResolvePrimaryClickable(position, out entry);

                if (resolved)
                    return true;
            }

            return false;
        }

        bool TryResolveClickable(Vector2 position, out ControlTemplate entry)
        {
            entry = null;
            var entries = InteractiveEntries as IList<Control>;
            if (entries == null)
                return false;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var root = entries[i] as ControlTemplate;
                if (root != null && root.TryResolveClickable(position, out entry))
                    return true;
            }

            return false;
        }

        bool IsValidHitTestPosition(Vector2 position)
        {
            return !float.IsNaN(position.X) && !float.IsNaN(position.Y) && HasRecentVisualContact;
        }

        protected override List<MySprite> RenderFrame(Func<List<MySprite>> sprites)
        {
            UpdateCursorFromTopHit();

            var spriteList = base.RenderFrame(sprites);
            if (!RendersInteractiveEntriesInGetSprites)
                RenderInteractiveEntryVisuals(spriteList);

            if (_dialog != null && _dialog.Dismissed)
                _dialog = null;

            _dialog?.Render(
                this,
                spriteList,
                _baseViewBox,
                ConfiguredScale,
                FontScale,
                Surface,
                ForegroundColor,
                BackgroundColor,
                ColorComponent.ResolveHeaderColor(Block as IMyTerminalBlock),
                CursorPosition);

            if (RendersInteractiveEntriesInGetSprites)
                RenderControlOverlayVisuals(spriteList);

            RaiseRenderOverlay(spriteList);
            RenderAttachedTooltip(spriteList);

            if (InteractionComponent.CursorScale == 0 || CursorType == CursorType.None)
                return spriteList;

            var cursor = CursorType;
            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                cursor = CursorType.None;

            Cursor.AddCursor(spriteList,
                cursor,
                position,
                new Vector2(32), // hardcoded size
                InteractionComponent.CursorScale);

            return spriteList;
        }

        protected virtual bool RendersInteractiveEntriesInGetSprites => false;

        protected void RenderInteractiveEntryVisuals(List<MySprite> sprites)
        {
            var entries = InteractiveEntries as IList<Control>;
            if (sprites == null || entries == null || entries.Count == 0)
                return;

            int end = _externalOverlayStartIndex >= 0
                ? Math.Min(entries.Count, _externalOverlayStartIndex)
                : entries.Count;

            for (int i = 0; i < end; i++)
            {
                var entry = entries[i] as ControlTemplate;
                if (entry != null)
                {
                    entry.Render(sprites);
#if DEBUG
                    if (LocalConfigManager.DebugInteractive)
                        AddDebugInteractiveBounds(entry, sprites);
#endif
                }
            }
        }

        void RenderControlOverlayVisuals(List<MySprite> sprites)
        {
            var entries = GetInteractiveEntries() as IList<Control>;
            if (sprites == null || entries == null || _controlOverlayCount == 0)
                return;

            var end = Math.Min(entries.Count, _controlOverlayStartIndex + _controlOverlayCount);
            for (int i = _controlOverlayStartIndex; i < end; i++)
            {
                var entry = entries[i] as ControlTemplate;
                entry?.Render(sprites);
            }
        }

        void RaiseCollectOverlayEntries(List<Control> entries)
        {
            var handlers = OnCollectOverlayEntries;
            if (handlers == null || entries == null)
                return;

            _externalOverlayStartIndex = entries.Count;
            foreach (var @delegate in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<InteractiveSurfaceScript, List<Control>>)@delegate)(this, entries);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, this);
                }
            }

            _externalOverlayCount = entries.Count - _externalOverlayStartIndex;
            if (_externalOverlayCount <= 0)
                ResetExternalOverlayRange();
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

#if DEBUG
        static void AddDebugInteractiveBounds(ControlTemplate entry, List<MySprite> sprites)
        {
            var bounds = entry.Bounds;
            if (bounds.Width <= 0f || bounds.Height <= 0f)
                return;

            var random = new Random(entry.GetHashCode());
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = bounds.Center,
                Size = bounds.Size,
                Color = new Color(random.Next(256), random.Next(256), random.Next(256), 77),
                Alignment = TextAlignment.CENTER
            });
        }
#endif


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
            OnCollectOverlayEntries = null;
            OnRenderOverlay = null;
            OnVisualContact = null;
            SoundEmitter?.Cleanup();
            SoundEmitter = null;
            base.Dispose();
        }
    }
}
