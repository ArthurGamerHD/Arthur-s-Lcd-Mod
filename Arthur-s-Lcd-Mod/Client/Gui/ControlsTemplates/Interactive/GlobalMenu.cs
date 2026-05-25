using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    sealed class GlobalMenu : RectangleControl
    {
        sealed class Node
        {
            public GlobalMenuEntry Entry;
            public int Level;
            public RectangleF Rect;
            public RectangleControl Control;
            public readonly List<Node> Children = new List<Node>();
        }

        readonly List<Node> _rootNodes = new List<Node>();
        readonly List<Node> _openPath = new List<Node>();
        readonly List<ControlBase> _interactiveEntries = new List<ControlBase>();
        readonly List<MySprite> _sprites = new List<MySprite>();
        RectangleF _menuBounds;
        RectangleF _renderViewBox;
        float _popupMaxWidth;
        bool _hasMenuBounds;

        public GlobalMenu(List<GlobalMenuEntry> entries)
            : base(default(RectangleF), CursorType.Default)
        {
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                    _rootNodes.Add(CreateNode(entries[i], 0));
            }

            SetVisible(_rootNodes.Count > 0);
        }

        static Node CreateNode(GlobalMenuEntry entry, int level)
        {
            var node = new Node
            {
                Entry = entry,
                Level = level
            };

            if (entry != null && entry.Children != null)
            {
                for (int i = 0; i < entry.Children.Count; i++)
                    node.Children.Add(CreateNode(entry.Children[i], level + 1));
            }

            return node;
        }

        public void AddInteractiveEntries(List<ControlBase> entries)
        {
            if (!Visible || entries == null || !_hasMenuBounds)
                return;

            entries.Add(this);
        }

        public float GetReservedHeight(
            InteractiveSurfaceScript owner,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            if (!Visible || _rootNodes.Count == 0)
                return 0f;

            float rootScale = 0.58f * scale * fontScale;
            return Math.Max(24f * scale, FormatingHelper.LineHeight(rootScale, surface) + 10f * scale);
        }

        public void Render(
            InteractiveSurfaceScript owner,
            List<MySprite> targetSprites,
            RectangleF viewBox,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            Color textColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            _sprites.Clear();
            HideEntries();

            if (!Visible || _rootNodes.Count == 0)
                return;

            SetDataContext(owner != null ? owner.App : null);
            _renderViewBox = viewBox;
            _popupMaxWidth = owner != null ? owner.ViewBox.Width * 0.65f : viewBox.Width * 0.65f;
            var themedApp = owner != null ? owner.App as IThemedApp : null;
            var renderContext = themedApp != null
                ? themedApp.CreateControlRenderContext(surface, scale, fontScale, cursorPosition)
                : new ControlRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);

            base.Render(renderContext, targetSprites);
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            _sprites.Clear();

            if (!Visible || _rootNodes.Count == 0)
                return;

            var viewBox = _renderViewBox;
            var surface = context.Surface;
            var scale = context.Scale;
            var fontScale = context.FontScale;
            var panelColor = context.PanelColor;
            var cursorPosition = context.CursorPosition;
            var shadowColor = context.GetThemeColor(Constants.SHADOW);
            float rootScale = 0.58f * scale * fontScale;
            float popupScale = 0.56f * scale * fontScale;
            float rootHeight = Math.Max(24f * scale, FormatingHelper.LineHeight(rootScale, surface) + 10f * scale);
            float itemHeight = Math.Max(22f * scale, FormatingHelper.LineHeight(rootScale, surface) + 8f * scale);
            float rootPaddingX = 12f * scale;

            DrawRootBar(viewBox, scale, rootScale, rootHeight, rootPaddingX, panelColor, cursorPosition, surface, context);
            DrawOpenPopups(viewBox, scale, popupScale, itemHeight, panelColor, shadowColor, cursorPosition, surface, context);

            SetRect(_hasMenuBounds ? _menuBounds : default(RectangleF));

            sprites.AddRange(_sprites);
        }

        void DrawRootBar(RectangleF viewBox,
            float scale,
            float rootScale,
            float rootHeight,
            float rootPaddingX,
            Color panelColor,
            Vector2 cursorPosition,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            ControlRenderContext renderContext)
        {
            var barRect = new RectangleF(viewBox.X, viewBox.Y, viewBox.Width, rootHeight);
            Border.CreateSpritesFromRect(barRect, _sprites, panelColor, surface.TextPadding == 0 ? 0 : 0.5f);

            float x = viewBox.X;
            for (int i = 0; i < _rootNodes.Count; i++)
            {
                var node = _rootNodes[i];
                var entry = node.Entry;
                string text = GetText(entry);
                var size = FormatingHelper.GetSizeInPixel(text, "White", rootScale, surface);
                float width = Math.Max(42f * scale, size.X + rootPaddingX * 2f);
                var rect = new RectangleF(x, viewBox.Y, width, rootHeight);

                var interactiveEntry = ShowNode(node, rect, entry != null && entry.HasChildren ? CursorType.Hand : entry?.Cursor ?? CursorType.Default);
                if (interactiveEntry != null)
                {
                    interactiveEntry.CustomRender = delegate(ControlBase item, ControlRenderContext context, List<MySprite> sprites)
                    {
                        DrawItemVisual(item.Bounds, entry, rootScale, context, cursorPosition, surface, true, sprites);
                    };
                    interactiveEntry.Render(renderContext, _sprites);
                }

                x += width;
            }
        }

        void DrawOpenPopups(
            RectangleF viewBox,
            float scale,
            float popupScale,
            float itemHeight,
            Color panelColor,
            Color shadowColor,
            Vector2 cursorPosition,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            ControlRenderContext renderContext)
        {
            for (int level = 0; level < _openPath.Count; level++)
            {
                var parentNode = _openPath[level];
                if (parentNode == null || parentNode.Entry == null || !parentNode.Entry.HasChildren || parentNode.Children.Count == 0)
                    break;

                var parentRect = parentNode.Rect;
                var children = parentNode.Children;
                float popupWidth = CalculatePopupWidth(children, popupScale, surface, scale, _popupMaxWidth);
                var popupRect = level == 0
                    ? new RectangleF(parentRect.X, parentRect.Bottom, popupWidth, itemHeight * children.Count)
                    : new RectangleF(parentRect.Right - scale, parentRect.Y, popupWidth, itemHeight * children.Count);

                popupRect.X = MathHelper.Clamp(popupRect.X, viewBox.X, viewBox.Right - popupRect.Width);
                popupRect.Y = MathHelper.Clamp(popupRect.Y, viewBox.Y, viewBox.Bottom - popupRect.Height);

                var shadowRect = new RectangleF(popupRect.Position + 2f * scale, popupRect.Size);
                Border.CreateSpritesFromRect(shadowRect, _sprites, shadowColor, 0.16f);
                Border.CreateSpritesFromRect(popupRect, _sprites, panelColor, 0.16f);

                for (int i = 0; i < children.Count; i++)
                {
                    var childNode = children[i];
                    var child = childNode.Entry;
                    var rect = new RectangleF(popupRect.X, popupRect.Y + itemHeight * i, popupRect.Width, itemHeight);
                    var interactiveEntry = ShowNode(childNode, rect, child != null && child.HasChildren ? CursorType.Hand : child?.Cursor ?? CursorType.Default);
                    if (interactiveEntry != null)
                    {
                        interactiveEntry.CustomRender = delegate(ControlBase item, ControlRenderContext context, List<MySprite> sprites)
                        {
                            DrawItemVisual(item.Bounds, child, popupScale, context, cursorPosition, surface, false, sprites);
                        };
                        interactiveEntry.Render(renderContext, _sprites);
                    }
                }
            }
        }

        static void DrawItemVisual(RectangleF rect,
            GlobalMenuEntry entry,
            float textScale,
            ControlRenderContext context,
            Vector2 cursorPosition,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            bool root,
            List<MySprite> sprites)
        {
            bool hover = rect.Contains(cursorPosition);
            var fillColor = hover
                ? context.GetThemeColor(Constants.PRIMARY + Constants.HOVER)
                : context.GetThemeColor(Constants.PRIMARY);
            var itemTextColor = context.GetThemeColor(Constants.ON_PRIMARY);
            Border.CreateSpritesFromRect(rect, sprites, fillColor, 0.5f);

            string text = GetText(entry);
            float iconSpace = !root && entry != null && !string.IsNullOrEmpty(entry.Icon) ? rect.Height : 0f;
            float arrowSpace = !root && entry != null && entry.HasChildren ? 16f * textScale : 0f;

            if (iconSpace > 0f)
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = entry?.Icon,
                    Position = new Vector2(rect.X + rect.Height * 0.5f, rect.Center.Y),
                    Size = new Vector2(rect.Height * 0.62f),
                    Color = itemTextColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.X + 8f * textScale + iconSpace, rect.Center.Y - FormatingHelper.GetSizeInPixel(text, "White", textScale, surface).Y * 0.5f),
                Color = itemTextColor,
                FontId = "White",
                Alignment = TextAlignment.LEFT,
                RotationOrScale = textScale
            });

            if (!root && entry != null && entry.HasChildren)
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = ">",
                    Position = new Vector2(rect.Right - arrowSpace, rect.Center.Y - FormatingHelper.GetSizeInPixel(">", "White", textScale, surface).Y * 0.5f),
                    Color = itemTextColor,
                    FontId = "White",
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = textScale
                });
            }
        }

        RectangleControl ShowNode(Node node, RectangleF rect, CursorType cursor)
        {
            if (node == null)
                return null;

            node.Rect = rect;

            if (node.Control == null)
            {
                node.Control = new RectangleControl(rect, cursor, node, OnEntryClick);
            }
            else
            {
                node.Control.SetRect(rect);
                node.Control.SetCursor(cursor);
            }

            node.Control.SetVisible(true);
            _interactiveEntries.Add(node.Control);
            AddChild(node.Control);
            AddMenuBounds(rect);
            return node.Control;
        }

        void AddMenuBounds(RectangleF rect)
        {
            if (!_hasMenuBounds)
            {
                _menuBounds = rect;
                _hasMenuBounds = true;
                return;
            }

            float x = Math.Min(_menuBounds.X, rect.X);
            float y = Math.Min(_menuBounds.Y, rect.Y);
            float right = Math.Max(_menuBounds.Right, rect.Right);
            float bottom = Math.Max(_menuBounds.Bottom, rect.Bottom);
            _menuBounds = new RectangleF(x, y, right - x, bottom - y);
        }

        void OnEntryClick(object dataContext, object sender)
        {
            var node = dataContext as Node;
            if (node == null || node.Entry == null)
                return;

            if (_rootNodes.Contains(node) && _openPath.Contains(node))
            {
                _openPath.Clear();
            }
            else if (node.Entry.HasChildren)
            {
                while (_openPath.Count > node.Level)
                    _openPath.RemoveAt(_openPath.Count - 1);

                if (_openPath.Count == node.Level)
                    _openPath.Add(node);
                else
                    _openPath[node.Level] = node;

                return;
            }

            if (node.Entry.OnClick != null)
            {
                node.Entry.OnClick(node.Entry.DataContext ?? node.Entry, sender);
                _openPath.Clear();
            }
        }

        public void HideEntries()
        {
            for (int i = 0; i < _interactiveEntries.Count; i++)
            {
                if (_interactiveEntries[i] != null)
                    _interactiveEntries[i].SetVisible(false);
            }

            _interactiveEntries.Clear();
            ClearChildren();
            SetRect(default(RectangleF));
            _menuBounds = default(RectangleF);
            _hasMenuBounds = false;
        }

        static float CalculatePopupWidth(
            List<Node> nodes,
            float textScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float maxWidth)
        {
            float width = 120f * scale;

            if (nodes == null)
                return width;

            for (int i = 0; i < nodes.Count; i++)
            {
                var entry = nodes[i] != null ? nodes[i].Entry : null;
                if (entry == null)
                    continue;

                var size = FormatingHelper.GetSizeInPixel(GetText(entry), "White", textScale, surface);
                float candidate = size.X + 42f * scale;
                if (!string.IsNullOrEmpty(entry.Icon))
                    candidate += 20f * scale;
                if (entry.HasChildren)
                    candidate += 20f * scale;

                if (candidate > width)
                    width = candidate;
            }

            return maxWidth > 0f ? Math.Min(width, maxWidth) : width;
        }

        static string GetText(GlobalMenuEntry entry)
        {
            return entry != null && !string.IsNullOrEmpty(entry.MenuItem) ? entry.MenuItem : string.Empty;
        }
    }
}
