using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.Controls.Interactive;
using LcdMod.Client.Utility;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.Apps.Abstract
{
    public abstract partial class InteractiveSurfaceScript : SurfaceScriptBase, IEyeTracking
    {
        const long CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES = 6;
        object _activeTooltipParentObject;
        RectangleF _tooltipRect;
        RectangleF _tooltipKeepOpenRect;
        bool _hasTooltipBounds;
        long _lastVisualContactFrame = long.MinValue;

        protected override ConfigKind ConfigKind => ConfigKind.Interactive;

        protected InteractiveSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        public Vector2 CursorPosition { get; protected set; }

        RectangleF _baseViewBox;

        readonly List<InteractiveEntry> _interactiveEntriesWithOverlay = new List<InteractiveEntry>();

        public ICollection<InteractiveEntry> InteractiveEntries
        {
            get
            {
                _interactiveEntriesWithOverlay.Clear();

                if (_messageBox != null)
                {
                    if (_messageBox.Dismissed)
                    {
                        _messageBox = null;
                    }
                    else
                    {
                        _messageBox.AddInteractiveEntries(_interactiveEntriesWithOverlay);
                        return _interactiveEntriesWithOverlay;
                    }
                }

                _interactiveEntriesWithOverlay.AddRange(InteractiveList);
                _globalMenu?.AddInteractiveEntries(_interactiveEntriesWithOverlay);
                return _interactiveEntriesWithOverlay;
            }
        }

        public virtual List<InteractiveEntry> InteractiveList { get; } = new List<InteractiveEntry>();

        protected override void UpdateViewBox()
        {
            var sizeOffset = (Surface.TextureSize - Surface.SurfaceSize) / 2;
            _userPadding = Surface.TextPadding;

            var padding = (Surface.TextPadding / 100f) * Surface.SurfaceSize;
            sizeOffset += padding / 2f;

            _baseViewBox = new RectangleF(
                sizeOffset.X,
                sizeOffset.Y,
                Surface.SurfaceSize.X - padding.X,
                Surface.SurfaceSize.Y - padding.Y);

            ViewBox = _baseViewBox;

            if (_globalMenu == null || !_globalMenu.Visible)
                return;

            float reservedHeight = _globalMenu.GetReservedHeight(this, Scale, FontScale, Surface);
            ViewBox = new RectangleF(
                _baseViewBox.X,
                _baseViewBox.Y + reservedHeight,
                _baseViewBox.Width,
                Math.Max(0f, _baseViewBox.Height - reservedHeight));
        }

        public abstract CursorType CursorType { get; protected set; }

        public void LookAt(Vector2 onScreenCoordinates)
        {
            _lastVisualContactFrame = MyAPIGateway.Session.GameplayFrameCounter;
            CursorPosition = onScreenCoordinates;
            OnLookAt(onScreenCoordinates);
            RenderSprites();
        }

        public void MouseScroll(int delta)
        {
            OnMouseScroll(delta);
        }

        InteractiveEntry _activeTooltipParentEntry;
        InteractiveEntry _manualTooltipParentEntry;
        object _manualTooltipParentObject;

        MessageBox _messageBox;
        GlobalMenu _globalMenu;

        protected virtual void OnLookAt(Vector2 onScreenCoordinates)
        {
        }

        protected virtual void OnMouseScroll(int delta)
        {
        }

        protected bool CursorInsideTooltip => _hasTooltipBounds && _tooltipRect.Contains(CursorPosition);

        protected bool CursorInsideTooltipKeepOpenArea =>
            _hasTooltipBounds && _tooltipKeepOpenRect.Contains(CursorPosition);

        protected bool HasRecentVisualContact => MyAPIGateway.Session.GameplayFrameCounter - _lastVisualContactFrame <=
                                                 CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES;

        protected void ClearTooltip()
        {
            HideAttachedTooltip();
        }

        protected void ClearAllTooltips()
        {
            HideAttachedTooltip();
        }

        public override void Run()
        {
            base.Run();
            if (!HasRecentVisualContact)
            {
                CursorPosition = new Vector2(float.NaN, float.NaN);
                ClearTooltip();
            }
        }

        void HideAttachedTooltip()
        {
            if (_activeTooltipParentEntry != null && _activeTooltipParentEntry.Tooltip != null)
                _activeTooltipParentEntry.Tooltip.Hide();

            _hasTooltipBounds = false;
            //_cursorInsideClickableTooltipContent = false;
            _tooltipRect = default(RectangleF);
            _tooltipKeepOpenRect = default(RectangleF);

            // Keep tooltip entries permanently attached to their parent entry.
            // Invisible entries are non-interactive because InteractiveEntry.Hit(),
            // InteractiveEntry.Click(), CanClick, and ResolveTopHitEntry() are visibility-gated.
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

        InteractiveEntry FindVisibleTooltipEntryByContext(object dataContext)
        {
            for (int i = InteractiveList.Count - 1; i >= 0; i--)
            {
                var entry = InteractiveList[i];
                if (entry != null &&
                    entry.Visible &&
                    entry.Tooltip != null &&
                    Equals(entry.DataContext, dataContext))
                {
                    return entry;
                }
            }

            return null;
        }

        InteractiveEntry ResolveManualTooltipParent()
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

        InteractiveEntry FindTooltipHitTarget()
        {
            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return null;

            for (int i = InteractiveList.Count - 1; i >= 0; i--)
            {
                var entry = InteractiveList[i];
                if (entry == null || !entry.Visible || entry.Tooltip == null)
                    continue;

                if (entry.Hit(position))
                    return entry;
            }

            return null;
        }

        public bool HasTooltipInputAtCursor(bool rightClick)
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
                (CursorInsideTooltip || CursorInsideTooltipKeepOpenArea || active.Hit(CursorPosition)))
            {
                return true;
            }

            return false;
        }

        public bool TryHandleTooltipActivationClick(bool rightClick)
        {
            InteractiveEntry tooltipParent;
            return TryHandleTooltipActivationClick(rightClick, out tooltipParent);
        }

        public bool TryHandleTooltipActivationClick(bool rightClick, out InteractiveEntry tooltipParent)
        {
            tooltipParent = null;

            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return false;

            var active = ResolveManualTooltipParent() ?? _activeTooltipParentEntry;
            if (active != null && active.Visible && active.Tooltip != null && _hasTooltipBounds &&
                TooltipButtonMatches(active.Tooltip.CloseMode, rightClick) &&
                (CursorInsideTooltip || CursorInsideTooltipKeepOpenArea || active.Hit(position)))
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

        InteractiveEntry FindTooltipTarget()
        {
            var manualParent = ResolveManualTooltipParent();
            if (manualParent != null)
            {
                var manualTooltip = manualParent.Tooltip;
                if (manualTooltip != null && manualTooltip.CloseMode != TooltipActivationMode.Auto)
                    return manualParent;

                var positionForManual = CursorPosition;
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

            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return null;

            if (_hasTooltipBounds && (CursorInsideTooltip || CursorInsideTooltipKeepOpenArea))
            {
                var activeParent = _activeTooltipParentObject;
                for (int i = InteractiveList.Count - 1; i >= 0; i--)
                {
                    var entry = InteractiveList[i];
                    if (entry != null &&
                        entry.Visible &&
                        entry.Tooltip != null &&
                        entry.Tooltip.OpenMode == TooltipActivationMode.Auto &&
                        Equals(entry.DataContext, activeParent))
                    {
                        return entry;
                    }
                }
            }

            for (int i = InteractiveList.Count - 1; i >= 0; i--)
            {
                var entry = InteractiveList[i];
                if (entry == null || !entry.Visible || entry.Tooltip == null)
                    continue;

                if (entry.Tooltip.OpenMode != TooltipActivationMode.Auto)
                    continue;

                if (entry.Hit(position))
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
            var panelColor = ColorableConfig != null ? ColorableConfig.HeaderColor : BackgroundColor;

            var tooltipSprites = tooltip.Render(
                parentEntry,
                ViewBox,
                Scale,
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

            parentEntry.AddChildren(tooltip.InteractiveEntries);
        }


        public void SetGlobalMenu(List<GlobalMenuEntry> entries)
        {
            if (_globalMenu != null)
                _globalMenu.HideEntries();

            _globalMenu = entries == null || entries.Count == 0 ? null : new GlobalMenu(entries);

            UpdateViewBox();
        }

        public virtual void SetGlobalMenu(params GlobalMenuEntry[] entries) => SetGlobalMenu(entries != null ? new List<GlobalMenuEntry>(entries) : null);

        public void ShowMessageBox(
            string title,
            string content,
            string button1,
            string button2,
            Action<object, object> button1Callback,
            Action<object, object> button2Callback = null,
            string icon = null)
        {
            _messageBox = new MessageBox();
            _messageBox.Show(title, content, button1, button2, button1Callback, button2Callback, icon);
        }

        public bool IsInsideContainer(InteractiveEntry entry, Vector2 position)
        {
            if (entry == null || !entry.Visible || entry.Children == null || entry.Children.Count == 0)
                return false;

            return entry.Hit(position) || _hasTooltipBounds && ReferenceEquals(entry, _activeTooltipParentEntry);
        }

        void UpdateCursorFromTopHit()
        {
            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return;

            var entries = InteractiveEntries as IList<InteractiveEntry>;
            if (entries == null)
                return;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = ResolveTopHitEntry(entries[i], position);
                if (entry == null)
                    continue;

                CursorType = entry.Cursor;
                return;
            }

            CursorType = CursorType.Default;
        }

        InteractiveEntry ResolveTopHitEntry(InteractiveEntry entry, Vector2 position)
        {
            if (entry == null || !entry.Visible)
                return null;

            bool selfHit = entry.Hit(position);

            // Tooltip child entries are allowed to resolve only through the currently
            // active tooltip parent. This prevents old parents that still reference
            // shared/reused tooltip children from producing ghost hitboxes or cursor styles.
            bool mayCheckChildren =
                entry.Children != null &&
                entry.Children.Count > 0 &&
                (selfHit || (_hasTooltipBounds && ReferenceEquals(entry, _activeTooltipParentEntry)));

            if (mayCheckChildren)
            {
                for (int i = entry.Children.Count - 1; i >= 0; i--)
                {
                    var childHit = ResolveTopHitEntry(entry.Children[i], position);
                    if (childHit != null)
                        return childHit;
                }
            }

            return selfHit ? entry : null;
        }

        protected override List<MySprite> RenderFrame(Func<List<MySprite>> sprites)
        {
            var baseViewBox = _baseViewBox;

            var spriteList = base.RenderFrame(sprites);
            RenderAttachedTooltip(spriteList);

            _globalMenu?.Render(
                this,
                spriteList,
                baseViewBox,
                Scale,
                FontScale,
                Surface,
                ForegroundColor,
                ColorableConfig?.HeaderColor ?? BackgroundColor,
                CursorPosition);

            _messageBox?.Render(
                this,
                spriteList,
                baseViewBox,
                Scale,
                FontScale,
                Surface,
                ForegroundColor,
                BackgroundColor,
                ColorableConfig?.HeaderColor ?? BackgroundColor,
                CursorPosition);

            UpdateCursorFromTopHit();

            if (AppConfig?.CursorScale == 0 || CursorType == CursorType.None)
                return spriteList;

            var cursor = CursorType;
            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                cursor = CursorType.None;

            Cursor.AddCursor(spriteList,
                cursor,
                position,
                new Vector2(32), // hardcoded size
                AppConfig?.CursorScale ?? 1f);

            return spriteList;
        }


        public MyEntity3DSoundEmitter SoundEmitter { get; set; }
        public bool RequiresAlt => AppConfig?.RequiresAlt ?? true;

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
            SoundEmitter?.Cleanup();
            SoundEmitter = null;
            base.Dispose();
        }
    }
}
