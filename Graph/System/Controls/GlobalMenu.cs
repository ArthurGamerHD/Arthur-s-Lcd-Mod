using System;
using System.Collections.Generic;
using Graph.Apps.Abstract;
using Graph.Apps.Utility;
using Graph.Extensions;
using Graph.Helpers;
using Graph.Panels;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Graph.System.Controls
{
    sealed class GlobalMenu
    {
        sealed class Node
        {
            public GlobalMenuEntry Entry;
            public int Level;
            public int Index;
            public RectangleF Rect;
            public InteractiveRectangleEntry InteractiveEntry;
            public readonly List<Node> Children = new List<Node>();
        }

        readonly List<Node> _rootNodes = new List<Node>();
        readonly List<Node> _openPath = new List<Node>();
        readonly List<InteractiveEntry> _interactiveEntries = new List<InteractiveEntry>();
        readonly List<MySprite> _sprites = new List<MySprite>();

        public bool Visible { get; private set; }

        public GlobalMenu(List<GlobalMenuEntry> entries)
        {
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                    _rootNodes.Add(CreateNode(entries[i], 0, i));
            }

            Visible = _rootNodes.Count > 0;
        }

        static Node CreateNode(GlobalMenuEntry entry, int level, int index)
        {
            var node = new Node
            {
                Entry = entry,
                Level = level,
                Index = index
            };

            if (entry != null && entry.Children != null)
            {
                for (int i = 0; i < entry.Children.Count; i++)
                    node.Children.Add(CreateNode(entry.Children[i], level + 1, i));
            }

            return node;
        }

        public void AddInteractiveEntries(List<InteractiveEntry> entries)
        {
            if (!Visible)
                return;

            for (int i = 0; i < _interactiveEntries.Count; i++)
            {
                var entry = _interactiveEntries[i];
                if (entry != null && entry.Visible)
                    entries.Add(entry);
            }
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
            return Math.Max(24f * scale, FormatingHelper.GetSizeInPixel("Ag", "White", rootScale, surface).Y + 10f * scale);
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

            var shadowColor = panelColor.MulValue(0.2f);
            float rootScale = 0.58f * scale * fontScale;
            float popupScale = 0.56f * scale * fontScale;
            float rootHeight = Math.Max(24f * scale, FormatingHelper.GetSizeInPixel("Ag", "White", rootScale, surface).Y + 10f * scale);
            float itemHeight = Math.Max(22f * scale, FormatingHelper.GetSizeInPixel("Ag", "White", popupScale, surface).Y + 9f * scale);
            float rootPaddingX = 12f * scale;

            DrawRootBar(owner, viewBox, scale, rootScale, rootHeight, rootPaddingX, panelColor, textColor, shadowColor, cursorPosition, surface);
            DrawOpenPopups(owner, viewBox, scale, popupScale, itemHeight, panelColor, textColor, shadowColor, cursorPosition, surface);

            targetSprites.AddRange(_sprites);
        }

        void DrawRootBar(
            InteractiveSurfaceScript owner,
            RectangleF viewBox,
            float scale,
            float rootScale,
            float rootHeight,
            float rootPaddingX,
            Color panelColor,
            Color textColor,
            Color shadowColor,
            Vector2 cursorPosition,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            var barRect = new RectangleF(viewBox.X, viewBox.Y, viewBox.Width, rootHeight);
            RectanglePanel.CreateSpritesFromRect(barRect, _sprites, panelColor, surface.TextPadding == 0 ? 0 : 0.5f);

            float x = viewBox.X;
            for (int i = 0; i < _rootNodes.Count; i++)
            {
                var node = _rootNodes[i];
                var entry = node.Entry;
                string text = GetText(entry);
                var size = FormatingHelper.GetSizeInPixel(text, "White", rootScale, surface);
                float width = Math.Max(42f * scale, size.X + rootPaddingX * 2f);
                var rect = new RectangleF(x, viewBox.Y, width, rootHeight);

                DrawItem(owner, rect, entry, rootScale, panelColor, textColor, cursorPosition, surface, true);
                ShowNode(node, rect, entry != null && entry.HasChildren ? CursorType.Hand : entry != null ? entry.Cursor : CursorType.Default);
                x += width;
            }
        }

        void DrawOpenPopups(
            InteractiveSurfaceScript owner,
            RectangleF viewBox,
            float scale,
            float popupScale,
            float itemHeight,
            Color panelColor,
            Color textColor,
            Color shadowColor,
            Vector2 cursorPosition,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            for (int level = 0; level < _openPath.Count; level++)
            {
                var parentNode = _openPath[level];
                if (parentNode == null || parentNode.Entry == null || !parentNode.Entry.HasChildren || parentNode.Children.Count == 0)
                    break;

                var parentRect = parentNode.Rect;
                var children = parentNode.Children;
                float popupWidth = CalculatePopupWidth(owner, children, popupScale, surface, scale);
                var popupRect = level == 0
                    ? new RectangleF(parentRect.X, parentRect.Bottom, popupWidth, itemHeight * children.Count)
                    : new RectangleF(parentRect.Right - scale, parentRect.Y, popupWidth, itemHeight * children.Count);

                popupRect.X = MathHelper.Clamp(popupRect.X, viewBox.X, viewBox.Right - popupRect.Width);
                popupRect.Y = MathHelper.Clamp(popupRect.Y, viewBox.Y, viewBox.Bottom - popupRect.Height);

                var shadowRect = new RectangleF(popupRect.Position + 2f * scale, popupRect.Size);
                RectanglePanel.CreateSpritesFromRect(shadowRect, _sprites, shadowColor, 0.16f);
                RectanglePanel.CreateSpritesFromRect(popupRect, _sprites, panelColor, 0.16f);

                for (int i = 0; i < children.Count; i++)
                {
                    var childNode = children[i];
                    var child = childNode.Entry;
                    var rect = new RectangleF(popupRect.X, popupRect.Y + itemHeight * i, popupRect.Width, itemHeight);
                    DrawItem(owner, rect, child, popupScale, panelColor, textColor, cursorPosition, surface, false);
                    ShowNode(childNode, rect, child != null && child.HasChildren ? CursorType.Hand : child != null ? child.Cursor : CursorType.Default);
                }
            }
        }

        void DrawItem(
            InteractiveSurfaceScript owner,
            RectangleF rect,
            GlobalMenuEntry entry,
            float textScale,
            Color panelColor,
            Color textColor,
            Vector2 cursorPosition,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            bool root)
        {
            bool hover = rect.Contains(cursorPosition);
            var fillColor = hover ? panelColor.DeriveAscentColor() : panelColor;
            RectanglePanel.CreateSpritesFromRect(rect, _sprites, fillColor, 0.5f);

            string text = GetText(entry);
            float iconSpace = !root && entry != null && !string.IsNullOrEmpty(entry.Icon) ? rect.Height : 0f;
            float arrowSpace = !root && entry != null && entry.HasChildren ? 16f * textScale : 0f;

            if (iconSpace > 0f)
            {
                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = entry.Icon,
                    Position = new Vector2(rect.X + rect.Height * 0.5f, rect.Center.Y),
                    Size = new Vector2(rect.Height * 0.62f),
                    Color = textColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            _sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.X + 8f * textScale + iconSpace, rect.Center.Y - FormatingHelper.GetSizeInPixel(text, "White", textScale, surface).Y * 0.5f),
                Color = hover ? panelColor.MulValue(0.85f) : textColor,
                FontId = "White",
                Alignment = TextAlignment.LEFT,
                RotationOrScale = textScale
            });

            if (!root && entry != null && entry.HasChildren)
            {
                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = ">",
                    Position = new Vector2(rect.Right - arrowSpace, rect.Center.Y - FormatingHelper.GetSizeInPixel(">", "White", textScale, surface).Y * 0.5f),
                    Color = hover ? panelColor.MulValue(0.85f) : textColor,
                    FontId = "White",
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = textScale
                });
            }
        }

        void ShowNode(Node node, RectangleF rect, CursorType cursor)
        {
            if (node == null)
                return;

            node.Rect = rect;

            if (node.InteractiveEntry == null)
            {
                node.InteractiveEntry = new InteractiveRectangleEntry(rect, cursor, node, OnEntryClick);
            }
            else
            {
                node.InteractiveEntry.SetRect(rect);
                node.InteractiveEntry.SetCursor(cursor);
            }

            node.InteractiveEntry.SetVisible(true);
            _interactiveEntries.Add(node.InteractiveEntry);
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
        }

        static float CalculatePopupWidth(
            InteractiveSurfaceScript owner,
            List<Node> nodes,
            float textScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale)
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

            return Math.Min(width, owner.ViewBox.Width * 0.65f);
        }

        static string GetText(GlobalMenuEntry entry)
        {
            return entry != null && !string.IsNullOrEmpty(entry.MenuItem) ? entry.MenuItem : string.Empty;
        }
    }
}