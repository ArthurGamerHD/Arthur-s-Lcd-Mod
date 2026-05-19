using System.Collections.Generic;
using System;
using System.Linq;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Proxy;
using LcdMod.Common.Config.Models;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class RenderProxySurfaceScript : InteractiveSurfaceScript, 
        IUsesTerminalControl<SliderProxyX>,
        IUsesTerminalControl<SliderProxyY>,
        IReferenceBlockSelection
    {
        public const string ID = "LcdMod_RenderProxy";
        public const string TITLE = "LcdMod_RenderProxy";
        
        List<MySprite> _sprites = new List<MySprite>();

        protected override ConfigKind ConfigKind => ConfigKind.RenderProxy;

        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;

        readonly List<InteractiveEntry> _interactiveListFallback = new List<InteractiveEntry>();

        SurfaceScriptBase _parent;
        SurfaceScriptBase _registeredParent;
        SurfaceScriptBase _renderEventParent;
        long _resolvedReferenceBlockId;
        float _lastOffsetX;
        float _lastOffsetY;
        bool _hostScriptUnsupported;
        long _rebindBlockedUntilFrame = long.MinValue;
        long _lastObservedReferenceId;
        float _lastObservedOffsetX;
        float _lastObservedOffsetY;
        bool _hasObservedConfig;

        public override IApp App
        {
            get
            {
                if (!IsParentAlive(_parent))
                    return null;

                try
                {
                    return _parent.App;
                }
                catch
                {
                    return null;
                }
            }
        }

        IAppInteractive AppInteractive => App as IAppInteractive;

        public override List<InteractiveEntry> InteractiveList
        {
            get
            {
                var parentInteractive = _parent as InteractiveSurfaceScript;
                if (IsParentAlive(_parent) && parentInteractive != null)
                    return parentInteractive.InteractiveList;

                var appInteractive = AppInteractive;
                if (appInteractive != null)
                    return appInteractive.InteractiveList;

                var game = App as IGame;
                return game != null ? game.Interactive : _interactiveListFallback;
            }
        }

        public override Vector2 HitTestOffset
        {
            get { return IsParentAlive(_parent) ? GetCurrentProxyOffset() : Vector2.Zero; }
        }

        public RenderProxySurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block,
            size)
        {
        }

        public void SetParent(SurfaceScriptBase parent)
        {
            if (parent != null && parent.Block != null &&
                parent.Block.BlockDefinition.SubtypeName.Equals(Block.BlockDefinition.SubtypeName))
                _parent = parent;
        }

        public bool IsReferenceBlockCandidate(IMyTerminalBlock block)
        {
            if (!(block is IMyTextPanel) || block == null || block.MarkedForClose || block.Equals(Block))
                return false;

            return !ConfigManager.GetAppsForBlock(block).Any(a => a is RenderProxySurfaceScript);
        }

        bool IsParentAlive(SurfaceScriptBase parent)
        {
            return parent != null &&
                   parent.Block != null &&
                   !parent.Block.MarkedForClose &&
                   SurfaceScriptBase.Instances.Contains(parent);
        }

        long GetProxyRegistrationId()
        {
            unchecked
            {
                return (Block.EntityId * 397L) ^ RotationOrSurfaceIndex;
            }
        }

        Vector2 GetCurrentProxyOffset()
        {
            var appConfig = AppConfig as ScreenConfigRenderProxy;
            float xOffset = appConfig?.XAxisOffset ?? 0f;
            float yOffset = appConfig?.YAxisOffset ?? 0f;
            return new Vector2(Surface.TextureSize.X * xOffset, Surface.TextureSize.Y * yOffset);
        }

        void SyncProxyRegistration()
        {
            if (_registeredParent != null && _registeredParent != _parent)
            {
                _registeredParent.UnregisterProxy(GetProxyRegistrationId());
                _registeredParent = null;
                UnsubscribeFromParentRender();
            }

            if (!IsParentAlive(_parent))
            {
                UnsubscribeFromParentRender();
                return;
            }
            
            float x = AppConfig?.XAxisOffset ?? (float)0;
            float y = AppConfig?.YAxisOffset ?? (float)0;

            if (_registeredParent == _parent && _lastOffsetX.Equals(x)  && _lastOffsetY.Equals(y))
            {
                SubscribeToParentRender(_parent);
                return;
            }

            if (!_parent.RegisterProxy(GetProxyRegistrationId(),
                    new Vector2(Surface.TextureSize.X * x, Surface.TextureSize.Y * y)))
            {
                _hostScriptUnsupported = false;
                UnsubscribeFromParentRender();
                if (MyAPIGateway.Session != null)
                    _rebindBlockedUntilFrame = MyAPIGateway.Session.GameplayFrameCounter + 1;
                return;
            }

            _registeredParent = _parent;
            _lastOffsetX = x;
            _lastOffsetY = y;
            SubscribeToParentRender(_parent);
        }

        void SubscribeToParentRender(SurfaceScriptBase parent)
        {
            if (_renderEventParent == parent)
                return;

            UnsubscribeFromParentRender();

            _renderEventParent = parent;
            if (_renderEventParent != null)
                _renderEventParent.OnRender += HandleParentRender;
        }

        void UnsubscribeFromParentRender()
        {
            if (_renderEventParent == null)
                return;

            _renderEventParent.OnRender -= HandleParentRender;
            _renderEventParent = null;
        }

        void HandleParentRender(SurfaceScriptBase parent)
        {
            if (parent == null || parent != _registeredParent || !IsParentAlive(parent))
                return;

            RenderSprites(force: true);
        }

        void ResolveParentFromConfig()
        {
            if (MyAPIGateway.Session != null &&
                MyAPIGateway.Session.GameplayFrameCounter < _rebindBlockedUntilFrame)
            {
                _parent = null;
                return;
            }

            if (AppConfig == null)
            {
                _parent = null;
                _resolvedReferenceBlockId = 0L;
                _hostScriptUnsupported = false;
                return;
            }

            long referenceBlockId = AppConfig.ReferenceBlock;
            if (referenceBlockId == 0L)
            {
                _parent = null;
                _resolvedReferenceBlockId = 0L;
                _hostScriptUnsupported = false;
                return;
            }

            if (_resolvedReferenceBlockId == referenceBlockId && IsParentAlive(_parent))
            {
                _hostScriptUnsupported = false;
                return;
            }

            _parent = null;
            _resolvedReferenceBlockId = referenceBlockId;
            _hostScriptUnsupported = false;

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(referenceBlockId, out entity))
                return;

            var targetBlock = entity as IMyTerminalBlock;
            if (targetBlock == null || targetBlock.MarkedForClose || !IsReferenceBlockCandidate(targetBlock))
                return;

            // Text panels expose one surface, so pick a live non-proxy host script on that block.
            var scripts = ConfigManager.GetAppsForBlock(targetBlock).ToList();
            _parent = scripts
                .Where(a => !(a is RenderProxySurfaceScript))
                .FirstOrDefault(IsParentAlive);

            if (_parent != null)
            {
                _hostScriptUnsupported = false;
                return;
            }

            // Only mark unsupported when we can positively classify the host as unsupported.
            // During script swap/layout transitions there may be no live script for a frame.
            bool hasAnyNonProxyScript = scripts.Any(a => !(a is RenderProxySurfaceScript));
            _hostScriptUnsupported = !hasAnyNonProxyScript;
        }

        void UpdateRebindCooldown()
        {
            var appConfig = AppConfig as ScreenConfigRenderProxy;
            long referenceId = appConfig != null ? appConfig.ReferenceBlock : 0L;
            float x = appConfig != null ? appConfig.XAxisOffset : 0f;
            float y = appConfig != null ? appConfig.YAxisOffset : 0f;

            bool changed = !_hasObservedConfig ||
                           _lastObservedReferenceId != referenceId ||
                           !_lastObservedOffsetX.Equals(x) ||
                           !_lastObservedOffsetY.Equals(y);
            if (!changed || MyAPIGateway.Session == null)
                return;

            _lastObservedReferenceId = referenceId;
            _lastObservedOffsetX = x;
            _lastObservedOffsetY = y;
            _hasObservedConfig = true;
            _rebindBlockedUntilFrame = MyAPIGateway.Session.GameplayFrameCounter + 1;
        }

        public override void SafeRun()
        {
            base.SafeRun();
            UpdateRebindCooldown();
            ResolveParentFromConfig();
            SyncProxyRegistration();

            if (IsParentAlive(_registeredParent) && _registeredParent == _parent)
                return;

            RenderSprites();
        }

        protected override void OnLookAt(Vector2 onScreenCoordinates)
        {
            var parentInteractive = _parent as InteractiveSurfaceScript;
            if (IsParentAlive(_parent) && parentInteractive != null)
            {
                parentInteractive.LookAt(onScreenCoordinates + HitTestOffset);
                return;
            }

            base.OnLookAt(onScreenCoordinates);
        }

        public override bool HasTooltipInputAtCursor(bool rightClick)
        {
            var parentInteractive = _parent as InteractiveSurfaceScript;
            return IsParentAlive(_parent) && parentInteractive != null
                ? parentInteractive.HasTooltipInputAtCursor(rightClick)
                : base.HasTooltipInputAtCursor(rightClick);
        }

        public override bool TryHandleTooltipActivationClick(bool rightClick, out InteractiveEntry tooltipParent)
        {
            var parentInteractive = _parent as InteractiveSurfaceScript;
            return IsParentAlive(_parent) && parentInteractive != null
                ? parentInteractive.TryHandleTooltipActivationClick(rightClick, out tooltipParent)
                : base.TryHandleTooltipActivationClick(rightClick, out tooltipParent);
        }

        public override bool IsInsideContainer(InteractiveEntry entry, Vector2 position)
        {
            var parentInteractive = _parent as InteractiveSurfaceScript;
            return IsParentAlive(_parent) && parentInteractive != null
                ? parentInteractive.IsInsideContainer(entry, position)
                : base.IsInsideContainer(entry, position);
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();
            bool proxyingParent = _parent != null;
            if (!proxyingParent)
            {
                AddBackground(_sprites);
                DrawTitle(_sprites);
            }

            if (!(Block is Sandbox.ModAPI.IMyTextPanel))
            {
                var color = AppConfig?.ErrorColor ?? new Color(220, 80, 80);
                DrawMessage(_sprites, "Unsupported Screen", "Cross", color, 0.9f);
                return _sprites;
            }

            if (_parent == null)
            {
                if (_hostScriptUnsupported)
                {
                    var color = AppConfig?.ErrorColor ?? new Color(220, 80, 80);
                    DrawMessage(_sprites, "Unsuported script", "Cross", color, 0.9f);
                }
                else
                {
                    var color = AppConfig?.WarningColor ?? new Color(255, 216, 64);
                    DrawMessage(_sprites, "Screen Not Linked", "Warning", color, 0.9f);
                }
                return _sprites;
            }

            if (!IsParentAlive(_parent))
            {
                var color = AppConfig?.ErrorColor ?? new Color(220, 80, 80);
                DrawMessage(_sprites, "Unsuported script", "Cross", color, 0.9f);
                return _sprites;
            }

            var offset = GetCurrentProxyOffset();
            List<MySprite> parentFrame;
            try
            {
                parentFrame = _parent.GetCachedFrame();
            }
            catch
            {
                var color = AppConfig?.ErrorColor ?? new Color(220, 80, 80);
                DrawMessage(_sprites, "Unsuported script", "Cross", color, 0.9f);
                return _sprites;
            }

            foreach (var sprite in parentFrame)
            {
                _sprites.Add(new MySprite(
                    sprite.Type, 
                    sprite.Data, 
                    sprite.Position - offset, 
                    sprite.Size, 
                    sprite.Color,
                    sprite.FontId, 
                    sprite.Alignment, 
                    sprite.RotationOrScale));
            }
            return _sprites;
        }

        protected override void OnMouseScroll(int delta, ref bool handled)
        {
            var parentInteractive = _parent as InteractiveSurfaceScript;
            if (IsParentAlive(_parent) && parentInteractive != null)
            {
                parentInteractive.MouseScroll(delta);
                handled = true;
                return;
            }

            base.OnMouseScroll(delta, ref handled);
            AppInteractive?.OnMouseScroll(delta, ref handled);
        }

        protected override List<MySprite> RenderFrame(Func<List<MySprite>> sprites)
        {
            return sprites();
        }

        public override void Dispose()
        {
            UnsubscribeFromParentRender();

            if (_registeredParent != null)
            {
                _registeredParent.UnregisterProxy(GetProxyRegistrationId());
                _registeredParent = null;
            }

            base.Dispose();
        }
    }
}
