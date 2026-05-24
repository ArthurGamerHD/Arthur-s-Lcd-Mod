using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.SurfaceScripts;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps
{
    internal sealed class VisibleTreeDebugApp : IApp
    {
        const string DebugFont = "Monospace";
        const float LineScale = 0.52f;
        const int MaxDepth = 32;
        static readonly Color HeaderColor = Color.White;
        static readonly Color RootColor = new Color(120, 200, 255);
        static readonly Color ChildColor = new Color(210, 230, 255);
        static readonly Color ModelColor = new Color(170, 235, 170);
        static readonly Color WarningColor = new Color(235, 210, 60);
        static readonly Color ErrorColor = new Color(230, 90, 90);

        readonly List<MySprite> _sprites = new List<MySprite>();

        public List<MySprite> GetSprites(
            VisibleTreeDebugSurfaceScript owner,
            SurfaceScriptBase target,
            string status)
        {
            var viewBox = GetViewBox(owner);
            var lines = BuildDebugLines(owner, target, status);
            float textScale = GetTextScale(owner);
            float lineHeight = owner.Surface.MeasureStringInPixels(new StringBuilder("A"), DebugFont, textScale).Y + 2f;
            var start = viewBox.Position + new Vector2(8f, 8f);
            int maxLines = Math.Max(1, (int)Math.Floor((viewBox.Height - 16f) / lineHeight));

            _sprites.Clear();
            for (int i = 0; i < lines.Count && i < maxLines; i++)
            {
                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = lines[i].Text,
                    Position = start + new Vector2(0f, i * lineHeight),
                    Color = lines[i].Color,
                    FontId = DebugFont,
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = textScale
                });
            }

            if (lines.Count > maxLines)
            {
                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = "-- More " + (lines.Count - maxLines) + " --",
                    Position = start + new Vector2(0f, (maxLines - 1) * lineHeight),
                    Color = WarningColor,
                    FontId = DebugFont,
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = textScale
                });
            }

            return _sprites;
        }

        static float GetTextScale(VisibleTreeDebugSurfaceScript owner)
        {
            float fontScale = owner.Surface != null && owner.Surface.FontSize > 0f
                ? owner.Surface.FontSize
                : 1f;
            return Math.Max(0.05f, LineScale * owner.Scale * fontScale);
        }

        static RectangleF GetViewBox(VisibleTreeDebugSurfaceScript owner)
        {
            var sizeOffset = (owner.Surface.TextureSize - owner.Surface.SurfaceSize) / 2f;
            var padding = (owner.Surface.TextPadding / 100f) * owner.Surface.SurfaceSize;
            sizeOffset += padding / 2f;
            return new RectangleF(
                sizeOffset.X,
                sizeOffset.Y,
                owner.Surface.SurfaceSize.X - padding.X,
                owner.Surface.SurfaceSize.Y - padding.Y);
        }

        static List<DebugLine> BuildDebugLines(
            VisibleTreeDebugSurfaceScript owner,
            SurfaceScriptBase target,
            string status)
        {
            var lines = new List<DebugLine>(128);
            lines.Add(new DebugLine("LcdMod Visible Tree Debug - " + owner.GetHashCode(), HeaderColor));

            if (target == null)
            {
                lines.Add(new DebugLine(status ?? "No target", ErrorColor));
                return lines;
            }

            var interactive = target as InteractiveSurfaceScript;
            lines.Add(new DebugLine("Target: " + GetTargetName(target), HeaderColor));
            lines.Add(new DebugLine("Script: " + target.GetType().Name + "  Hash: " + target.GetHashCode(), HeaderColor));

            if (interactive == null)
            {
                lines.Add(new DebugLine("Target is not interactive", WarningColor));
                return lines;
            }

            var entries = interactive.InteractiveEntries;
            int rootCount = entries != null ? entries.Count : 0;
            lines.Add(new DebugLine("Cursor: " + FormatVector(interactive.CursorPosition) +
                                    "  Root Controls: " + Fixed4(rootCount), HeaderColor));
            lines.Add(new DebugLine(string.Empty, HeaderColor));

            if (entries == null || entries.Count == 0)
            {
                lines.Add(new DebugLine("  (empty)", WarningColor));
                return lines;
            }

            int index = 0;
            var visited = new HashSet<ControlBase>();
            foreach (var entry in entries)
            {
                AppendControl(lines, entry, index.ToString(), 0, visited);
                index++;
            }

            return lines;
        }

        static void AppendControl(
            List<DebugLine> lines,
            ControlBase control,
            string path,
            int depth,
            HashSet<ControlBase> visited)
        {
            if (control == null || !control.Visible)
                return;

            var color = depth == 0
                ? RootColor
                : control.Model != null
                    ? ModelColor
                    : ChildColor;

            string prefix = new string(' ', Math.Min(depth, MaxDepth) * 2);
            lines.Add(new DebugLine(prefix + path + " " + BuildControlText(control), color));

            if (!visited.Add(control))
            {
                lines.Add(new DebugLine(prefix + "  (cycle)", WarningColor));
                return;
            }

            var children = control.Children;
            if (children == null || children.Count == 0)
                return;

            for (int i = 0; i < children.Count; i++)
                AppendControl(lines, children[i], path + "." + i, depth + 1, visited);
        }

        static string BuildControlText(ControlBase control)
        {
            var sb = new StringBuilder();
            sb.Append(control.GetType().Name);

            var model = control.Model;
            if (model != null)
                sb.Append(" model=").Append(model.GetType().Name);
            else if (control.DataContext != null)
                sb.Append(" data=").Append(ClampToWidth(control.DataContext.GetType().Name, 24));

            sb.Append(" flags=").Append(GetFlags(control));
            if (control.Cursor != CursorType.Default)
                sb.Append(" cursor=").Append(control.Cursor);

            var scrollPanel = control as ScrollPanel;
            if (scrollPanel != null)
            {
                int maxStartRow = Math.Max(0, scrollPanel.TotalRows - scrollPanel.MaxVisibleRows);
                sb.Append(" scroll=").Append(scrollPanel.StartRow).Append("/").Append(maxStartRow)
                    .Append(" px=").Append(Round(scrollPanel.ScrollOffsetPixels));
                if (scrollPanel.IsAnimating)
                    sb.Append(" vel=").Append(Round(scrollPanel.ScrollVelocityPixelsPerFrame));
                if (scrollPanel.AutoScrollSecondsPerStep > 0f)
                    sb.Append(" auto=").Append(scrollPanel.AutoScrollSecondsPerStep.ToString("0.##"));
            }

            sb.Append(" children=").Append(control.Children != null ? control.Children.Count : 0);
            sb.Append(" bounds=").Append(FormatRect(control.Bounds));

            return sb.ToString();
        }

        static string GetTargetName(SurfaceScriptBase target)
        {
            if (target == null || target.Block == null)
                return string.Empty;

            var terminalBlock = target.Block as IMyTerminalBlock;
            return terminalBlock != null ? terminalBlock.CustomName ?? string.Empty : target.Block.DisplayNameText ?? string.Empty;
        }

        static string GetFlags(ControlBase control)
        {
            var sb = new StringBuilder(6);
            if (control.CanPrimaryClick)
                sb.Append('P');
            if (control.CanSecondaryClick)
                sb.Append('R');
            if (control.CanScroll)
                sb.Append('S');
            if (control.CanHover)
                sb.Append('H');
            if (control.Tooltip != null)
                sb.Append('T');
            if (sb.Length == 0)
                sb.Append('-');
            return sb.ToString();
        }

        static string FormatRect(RectangleF rect)
        {
            return "(" +
                   Round(rect.X) + "," +
                   Round(rect.Y) + " " +
                   Round(rect.Width) + "x" +
                   Round(rect.Height) + ")";
        }

        static string FormatVector(Vector2 value)
        {
            if (float.IsNaN(value.X) || float.IsNaN(value.Y))
                return "(nan)";

            return "(" + Round(value.X) + "," + Round(value.Y) + ")";
        }

        static string Round(float value)
        {
            return ((int)Math.Round(value)).ToString();
        }

        static string ClampToWidth(string value, int width)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (value.Length > width)
                return value.Substring(0, width);
            return value;
        }

        static string Fixed4(int value)
        {
            var text = value.ToString();
            if (text.Length > 4)
                return text.Substring(0, 4);
            return text.PadRight(4);
        }

        struct DebugLine
        {
            public readonly string Text;
            public readonly Color Color;

            public DebugLine(string text, Color color)
            {
                Text = text;
                Color = color;
            }
        }

        public void Update()
        {
        }

        public void LayoutChanged()
        {
        }

        public List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }
    }
}
