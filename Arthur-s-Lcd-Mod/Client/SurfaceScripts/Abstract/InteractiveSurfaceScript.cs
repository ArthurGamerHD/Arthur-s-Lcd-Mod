using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Utility;
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
        const long CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES = 6;
        const long HIDDEN_GLOBAL_MENU_TIMEOUT_FRAMES = 180;
        object _activeTooltipParentObject;
        RectangleF _tooltipRect;
        RectangleF _tooltipKeepOpenRect;
        bool _hasTooltipBounds;
        bool _showHiddenGlobalMenu;
        long _hiddenGlobalMenuVisibleUntilFrame = long.MinValue;
        long _lastVisualContactFrame = long.MinValue;

        protected override ConfigKind ConfigKind => ConfigKind.Interactive;

        protected InteractiveSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _hiddenGlobalMenuControl = new HiddenGlobalMenuControl(this);
        }

        public Vector2 CursorPosition { get; protected set; } = new Vector2(float.NaN, float.NaN);

        public virtual Vector2 HitTestOffset
        {
            get { return Vector2.Zero; }
        }

        protected Vector2 HitTestCursorPosition
        {
            get { return CursorPosition + HitTestOffset; }
        }

        RectangleF _baseViewBox;
        readonly HiddenGlobalMenuControl _hiddenGlobalMenuControl;

        readonly List<ControlBase> _interactiveEntriesWithOverlay = new List<ControlBase>();

        public ICollection<ControlBase> InteractiveEntries
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
                if (ShouldRenderGlobalMenu())
                {
                    _globalMenu?.AddInteractiveEntries(_interactiveEntriesWithOverlay);
                }
                else if (CanOpenHiddenGlobalMenu())
                {
                    _hiddenGlobalMenuControl.SetRect(_baseViewBox);
                    _hiddenGlobalMenuControl.SetVisible(true);
                    _interactiveEntriesWithOverlay.Insert(0, _hiddenGlobalMenuControl);
                }

                return _interactiveEntriesWithOverlay;
            }
        }

        public virtual List<ControlBase> InteractiveList { get; } = new List<ControlBase>();

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

            float reservedHeight = _globalMenu.GetReservedHeight(this, Scale, FontScale, Surface);
            reservedHeight += Math.Min(16 * Scale, Surface.SurfaceSize.Y * Surface.TextPadding / 100);
            
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
            bool handled = false;
            OnMouseScroll(delta, ref handled);
        }

        ControlBase _activeTooltipParentEntry;
        ControlBase _manualTooltipParentEntry;
        object _manualTooltipParentObject;

        MessageBox _messageBox;
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

        protected bool HasRecentVisualContact
        {
            get
            {
                return _lastVisualContactFrame != long.MinValue &&
                       MyAPIGateway.Session.GameplayFrameCounter - _lastVisualContactFrame <=
                       CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES;
            }
        }

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
            if (!HasRecentVisualContact)
            {
                CursorPosition = new Vector2(float.NaN, float.NaN);
                ClearTooltip();
            }

            UpdateHiddenGlobalMenuLifetime();
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

        ControlBase FindVisibleTooltipEntryByContext(object dataContext)
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

        ControlBase ResolveManualTooltipParent()
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

        ControlBase FindTooltipHitTarget()
        {
            var position = HitTestCursorPosition;
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
                (CursorInsideTooltip || CursorInsideTooltipKeepOpenArea || active.Hit(HitTestCursorPosition)))
            {
                return true;
            }

            return false;
        }

        public virtual bool TryHandleTooltipActivationClick(bool rightClick)
        {
            ControlBase tooltipParent;
            return TryHandleTooltipActivationClick(rightClick, out tooltipParent);
        }

        public virtual bool TryHandleTooltipActivationClick(bool rightClick, out ControlBase tooltipParent)
        {
            tooltipParent = null;

            var position = HitTestCursorPosition;
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

        ControlBase FindTooltipTarget()
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
            var panelColor = ColorableConfig?.HeaderColor ?? BackgroundColor;

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
                Scale,
                FontScale,
                Surface,
                ForegroundColor,
                ColorableConfig?.HeaderColor ?? BackgroundColor,
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

        void UpdateHiddenGlobalMenuLifetime()
        {
            if (!_showHiddenGlobalMenu)
                return;

            if (!CanOpenHiddenGlobalMenu())
            {
                CloseHiddenGlobalMenu();
                UpdateViewBox();
                return;
            }

            var session = MyAPIGateway.Session;
            if (session == null || !HasRecentVisualContact)
            {
                CloseHiddenGlobalMenu();
                UpdateViewBox();
                return;
            }

            if (_globalMenu != null && _globalMenu.Hit(CursorPosition))
            {
                RefreshHiddenGlobalMenuLifetime();
                return;
            }

            if (session.GameplayFrameCounter <= _hiddenGlobalMenuVisibleUntilFrame)
                return;

            CloseHiddenGlobalMenu();
            UpdateViewBox();
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

            public override bool Click(object sender)
            {
                return true;
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
            _messageBox = new MessageBox();
            _messageBox.Show(title, content, button1, button2, button1Callback, button2Callback, icon);
        }

        public virtual bool IsInsideContainer(ControlBase entry, Vector2 position)
        {
            if (entry == null || !entry.Visible || entry.Children == null || entry.Children.Count == 0)
                return false;

            return entry.Hit(position) || _hasTooltipBounds && ReferenceEquals(entry, _activeTooltipParentEntry);
        }

        void UpdateCursorFromTopHit()
        {
            var position = HitTestCursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return;

            var entries = InteractiveEntries as IList<ControlBase>;
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

        ControlBase ResolveTopHitEntry(ControlBase entry, Vector2 position)
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
            var spriteList = base.RenderFrame(sprites);
            if (!RendersInteractiveEntriesInGetSprites)
                RenderInteractiveEntryVisuals(spriteList);

            RenderAttachedTooltip(spriteList);

            _messageBox?.Render(
                this,
                spriteList,
                _baseViewBox,
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

        protected virtual bool RendersInteractiveEntriesInGetSprites
        {
            get { return false; }
        }

        protected void RenderInteractiveEntryVisuals(List<MySprite> sprites)
        {
            if (sprites == null || InteractiveList == null || InteractiveList.Count == 0)
                return;

            var context = new ControlRenderContext(
                Surface,
                Scale,
                FontScale,
                ForegroundColor,
                ColorableConfig?.HeaderColor ?? BackgroundColor,
                CursorPosition);

            for (int i = 0; i < InteractiveList.Count; i++)
            {
                var entry = InteractiveList[i];
                if (entry != null)
                {
                    entry.Render(context, sprites);
#if DEBUG
                    if (LocalConfigManager.DebugInteractive)
                        AddDebugInteractiveBounds(entry, sprites);
#endif
                }
            }
        }

#if DEBUG
        static void AddDebugInteractiveBounds(ControlBase entry, List<MySprite> sprites)
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
