using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    /// <summary>
    /// Reusable control-based menu. It renders a root menu bar and nested popup levels from a
    /// GlobalMenuEntry tree, but every realized entry is a real child control.
    /// </summary>
    sealed class Menu : RectangleControl
    {
        sealed class Node
        {
            public GlobalMenuEntry Entry;
            public int Level;
            public RectangleF Rect;
            public MenuItemControl Control;
            public readonly List<Node> Children = new List<Node>();
        }

        readonly List<Node> _rootNodes = new List<Node>();
        readonly List<Node> _openPath = new List<Node>();
        readonly List<MenuItemControl> _interactiveEntries = new List<MenuItemControl>();

        RectangleF _menuBounds;
        RectangleF _renderViewBox;
        Sandbox.ModAPI.Ingame.IMyTextSurface _renderSurface;
        Vector2 _cursorPosition = new Vector2(float.NaN, float.NaN);
        float _popupMaxWidth;
        bool _hasMenuBounds;

        public Menu(List<GlobalMenuEntry> entries)
            : base(default(RectangleF), CursorType.Default)
        {
            SetClass("ControlBase Menu");

            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                    _rootNodes.Add(CreateNode(entries[i], 0));
            }

            SetVisible(_rootNodes.Count > 0);
        }

        public bool HasMenuBounds
        {
            get { return _hasMenuBounds; }
        }

        public RectangleF MenuBounds
        {
            get { return _menuBounds; }
        }

        public void Configure(
            RectangleF viewBox,
            float popupMaxWidth,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            Vector2 cursorPosition)
        {
            _renderViewBox = viewBox;
            _popupMaxWidth = popupMaxWidth;
            _renderSurface = surface;
            _cursorPosition = cursorPosition;
        }

        public float GetReservedHeight(
            InteractiveSurfaceScript owner,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            if (!Visible || _rootNodes.Count == 0)
                return 0f;

            if (surface == null)
                return 24f * scale;

            var rootScale = 0.58f * scale * fontScale;
            return Math.Max(24f * scale, FormatingHelper.LineHeight(rootScale, this, surface) + 10f * scale);
        }

        static Node CreateNode(GlobalMenuEntry entry, int level)
        {
            var node = new Node
            {
                Entry = entry,
                Level = level
            };

            if (entry != null)
                entry.Active = false;

            if (entry == null || entry.Children == null)
                return node;

            for (int i = 0; i < entry.Children.Count; i++)
                node.Children.Add(CreateNode(entry.Children[i], level + 1));

            return node;
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            HideEntries();

            if (!Visible || _rootNodes.Count == 0)
                return;

            var viewBox = _renderViewBox;
            var surface = _renderSurface ?? TextSurface;
            if (surface == null)
                return;

            var scale = LayoutScale;
            var fontScale = FontScale;
            var panelColor = BackgroundColor;
            var fontId = TextFont;
            var cursorPosition = _cursorPosition;
            var shadowColor = ResolveColor(ThemeResources.ShadowColor);
            var rootScale = 0.58f * scale * fontScale;
            var popupScale = 0.56f * scale * fontScale;
            var rootHeight = Math.Max(24f * scale, FormatingHelper.LineHeight(rootScale, this, surface) + 10f * scale);
            var itemHeight = Math.Max(22f * scale, FormatingHelper.LineHeight(rootScale, this, surface) + 8f * scale);
            var rootPaddingX = 12f * scale;

            DrawRootBar(viewBox, scale, rootScale, rootHeight, rootPaddingX, panelColor, fontId, cursorPosition, surface, sprites);
            DrawOpenPopups(viewBox, scale, popupScale, itemHeight, panelColor, shadowColor, fontId, cursorPosition, surface, sprites);

            SetRect(_hasMenuBounds ? _menuBounds : default(RectangleF));
        }

        void DrawRootBar(
            RectangleF viewBox,
            float scale,
            float rootScale,
            float rootHeight,
            float rootPaddingX,
            Color panelColor,
            string fontId,
            Vector2 cursorPosition,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            List<MySprite> sprites)
        {
            var barRect = new RectangleF(viewBox.X, viewBox.Y, viewBox.Width, rootHeight);
            Border.CreateSpritesFromRect(barRect, sprites, panelColor,
                radiusScale: surface.TextPadding == 0 ? 0 : scale);

            var x = viewBox.X;
            for (int i = 0; i < _rootNodes.Count; i++)
            {
                var node = _rootNodes[i];
                var entry = node.Entry;
                var text = GetText(entry);
                var size = FormatingHelper.GetSizeInPixel(text, fontId, rootScale, surface);
                var width = Math.Max(42f * scale, size.X + rootPaddingX * 2f);
                var rect = new RectangleF(x, viewBox.Y, width, rootHeight);

                var interactiveEntry = ShowNode(node, rect,
                    entry != null && entry.HasChildren ? CursorType.Hand : entry != null ? entry.Cursor : CursorType.Default,
                    cursorPosition,
                    true);

                if (interactiveEntry != null)
                {
                    GlobalMenuEntry entryForRender = entry;
                    interactiveEntry.CustomRender = delegate(ControlTemplate item, List<MySprite> itemSprites)
                    {
                        DrawItemVisual(item, entryForRender, rootScale, fontId, surface, true, itemSprites);
                    };
                    interactiveEntry.Render(sprites);
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
            string fontId,
            Vector2 cursorPosition,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            List<MySprite> sprites)
        {
            for (var level = 0; level < _openPath.Count; level++)
            {
                var parentNode = _openPath[level];
                if (parentNode == null || parentNode.Entry == null || !parentNode.Entry.HasChildren || parentNode.Children.Count == 0)
                    break;

                var parentRect = parentNode.Rect;
                var children = parentNode.Children;
                var popupWidth = CalculatePopupWidth(children, popupScale, fontId, surface, scale, _popupMaxWidth);
                var popupRect = level == 0
                    ? new RectangleF(parentRect.X, parentRect.Bottom, popupWidth, itemHeight * children.Count)
                    : new RectangleF(parentRect.Right - scale, parentRect.Y, popupWidth, itemHeight * children.Count);

                popupRect.X = MathHelper.Clamp(popupRect.X, viewBox.X, viewBox.Right - popupRect.Width);
                popupRect.Y = MathHelper.Clamp(popupRect.Y, viewBox.Y, viewBox.Bottom - popupRect.Height);

                var shadowRect = new RectangleF(popupRect.Position + 2f * scale, popupRect.Size);
                Border.CreateSpritesFromRect(shadowRect, sprites, shadowColor,
                    radiusScale: scale);
                Border.CreateSpritesFromRect(popupRect, sprites, panelColor,
                    radiusScale: scale);

                for (var i = 0; i < children.Count; i++)
                {
                    var childNode = children[i];
                    var child = childNode.Entry;
                    var rect = new RectangleF(popupRect.X, popupRect.Y + itemHeight * i, popupRect.Width, itemHeight);
                    var interactiveEntry = ShowNode(childNode, rect,
                        child != null && child.HasChildren ? CursorType.Hand : child != null ? child.Cursor : CursorType.Default,
                        cursorPosition,
                        false);

                    if (interactiveEntry != null)
                    {
                        GlobalMenuEntry childForRender = child;
                        interactiveEntry.CustomRender = delegate(ControlTemplate item, List<MySprite> itemSprites)
                        {
                            DrawItemVisual(item, childForRender, popupScale, fontId, surface, false, itemSprites);
                        };
                        interactiveEntry.Render(sprites);
                    }
                }
            }
        }

        static void DrawItemVisual(
            ControlTemplate control,
            GlobalMenuEntry entry,
            float textScale,
            string fontId,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            bool root,
            List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var fillColor = control.BackgroundColor;
            var itemTextColor = control.TextColor;
            var itemFontId = control.TextFont;
            if (string.IsNullOrEmpty(itemFontId))
                itemFontId = fontId;

            if (fillColor.A > 0)
            {
                Border.CreateSpritesFromRect(rect, sprites, fillColor,
                    radiusScale: control.LayoutScale);
            }

            var text = GetText(entry);
            var iconSpace = !root && entry != null && !string.IsNullOrEmpty(entry.Icon) ? rect.Height : 0f;
            var arrowSpace = !root && entry != null && entry.HasChildren ? 16f * textScale : 0f;

            if (iconSpace > 0f)
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = entry != null ? entry.Icon : null,
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
                Position = new Vector2(
                    rect.X + 8f * textScale + iconSpace,
                    rect.Center.Y - FormatingHelper.GetSizeInPixel(text, itemFontId, textScale, surface).Y * 0.5f),
                Color = itemTextColor,
                FontId = itemFontId,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = textScale
            });

            if (!root && entry != null && entry.HasChildren)
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "RightArrow",
                    Position = new Vector2(rect.Right - arrowSpace * 0.5f, rect.Center.Y),
                    Size = new Vector2(arrowSpace),
                    Color = itemTextColor,
                    Alignment = TextAlignment.CENTER
                });
            }
        }

        MenuItemControl ShowNode(Node node, RectangleF rect, CursorType cursor, Vector2 cursorPosition, bool root)
        {
            if (node == null)
                return null;

            node.Rect = rect;

            if (node.Control == null)
            {
                node.Control = new MenuItemControl(rect, cursor, node, OnEntryClick);
            }
            else
            {
                node.Control.SetRect(rect);
                node.Control.SetCursor(cursor);
            }

            node.Control.SetClass(root
                ? "ControlBase MenuItemControl MenuRootItem"
                : "ControlBase MenuItemControl MenuPopupItem");
            node.Control.SetActive(node.Entry != null && node.Entry.Active);
            node.Control.SetPointerOver(rect.Contains(cursorPosition));
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

            var x = Math.Min(_menuBounds.X, rect.X);
            var y = Math.Min(_menuBounds.Y, rect.Y);
            var right = Math.Max(_menuBounds.Right, rect.Right);
            var bottom = Math.Max(_menuBounds.Bottom, rect.Bottom);
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
                SyncActiveEntries();
            }
            else if (node.Entry.HasChildren)
            {
                while (_openPath.Count > node.Level)
                    _openPath.RemoveAt(_openPath.Count - 1);

                if (_openPath.Count == node.Level)
                    _openPath.Add(node);
                else
                    _openPath[node.Level] = node;

                SyncActiveEntries();
                return;
            }

            if (node.Entry.OnClick != null)
            {
                node.Entry.OnClick(node.Entry.DataContext ?? node.Entry, sender);
                _openPath.Clear();
                SyncActiveEntries();
            }
        }

        void SyncActiveEntries()
        {
            SetActive(_rootNodes, false);

            for (int i = 0; i < _openPath.Count; i++)
            {
                var node = _openPath[i];
                if (node != null && node.Entry != null)
                    node.Entry.Active = true;
            }
        }

        static void SetActive(List<Node> nodes, bool active)
        {
            if (nodes == null)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null)
                    continue;

                if (node.Entry != null)
                    node.Entry.Active = active;

                SetActive(node.Children, active);
            }
        }

        public void HideEntries()
        {
            for (int i = 0; i < _interactiveEntries.Count; i++)
            {
                var control = _interactiveEntries[i];
                if (control != null)
                    control.SetVisible(false);
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
            string fontId,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float maxWidth)
        {
            var width = 120f * scale;

            if (nodes == null)
                return width;

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var entry = node != null ? node.Entry : null;
                if (entry == null)
                    continue;

                var size = FormatingHelper.GetSizeInPixel(GetText(entry), fontId, textScale, surface);
                var candidate = size.X + 42f * scale;
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
