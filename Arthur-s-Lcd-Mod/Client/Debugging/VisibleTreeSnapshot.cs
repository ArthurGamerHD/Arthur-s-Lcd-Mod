using System;
using System.Collections.Generic;
using System.Text;
using Acrux.Core;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Client.Debugging
{
    internal static class VisibleTreeSnapshot
    {
        const int MaxDepth = 48;
        static readonly Color Header = Color.White;
        static readonly Color Root = new Color(120, 200, 255);
        static readonly Color Child = new Color(210, 230, 255);
        static readonly Color Model = new Color(170, 235, 170);
        static readonly Color Disabled = new Color(145, 145, 145);
        static readonly Color Warning = new Color(235, 210, 60);

        public static List<Line> Build(SurfaceScriptBase target)
        {
            var lines = new List<Line>(160)
            {
                new Line("Point at an LcdMod surface, then press F11 to inspect its retained tree.", Header)
            };
            if (target == null)
            {
                lines.Add(new Line("No recently targeted LcdMod surface.", Warning));
                return lines;
            }

            lines.Add(new Line("Target: " + GetTargetName(target), Header));
            lines.Add(new Line("Script: " + target.GetType().Name + "  Hash: " + target.GetHashCode(), Header));

            var host = target.RetainedAppGuiHost;
            if (host == null || host.Root == null)
            {
                lines.Add(new Line("This surface has no retained ADK visual tree.", Warning));
                return lines;
            }

            VisualElement root = host.Root;
            lines.Add(new Line(
                "Layout: " + FormatRect(root.Bounds) +
                "  Focus: " + (host.VisualTree.FocusedElement == null
                    ? "none"
                    : host.VisualTree.FocusedElement.GetType().Name),
                Header));
            lines.Add(new Line(string.Empty, Header));
            Append(lines, root, "0", 0, new HashSet<VisualElement>());
            return lines;
        }

        static void Append(
            List<Line> lines,
            VisualElement element,
            string path,
            int depth,
            HashSet<VisualElement> visited)
        {
            if (element == null)
                return;

            string prefix = new string(' ', Math.Min(depth, MaxDepth) * 2);
            Color color = !element.Enabled
                ? Disabled
                : depth == 0
                    ? Root
                    : element.DataContext != null
                        ? Model
                        : Child;
            lines.Add(new Line(prefix + path + " " + Describe(element), color));

            if (!visited.Add(element))
            {
                lines.Add(new Line(prefix + "  (cycle)", Warning));
                return;
            }

            IReadOnlyList<VisualElement> children = element.VisualChildren;
            for (int i = 0; i < children.Count; i++)
                Append(lines, children[i], path + "." + i, depth + 1, visited);
        }

        static string Describe(VisualElement element)
        {
            var text = new StringBuilder();
            text.Append(element.GetType().Name);
            if (!string.IsNullOrWhiteSpace(element.Name))
                text.Append(" name=").Append(Clamp(element.Name, 32));
            if (!string.IsNullOrWhiteSpace(element.StyleClass) && element.StyleClass != "default")
                text.Append(" class=[").Append(Clamp(element.StyleClass, 48)).Append(']');
            if (element.DataContext != null)
                text.Append(" data=").Append(Clamp(element.DataContext.GetType().Name, 32));
            if (!element.Visible)
                text.Append(" hidden");
            if (!element.Enabled)
                text.Append(" disabled");
            if (element.IsPointerOver)
                text.Append(" pointerover");
            if (element.IsFocused)
                text.Append(" focused");
            if (element.IsFocusWithin)
                text.Append(" focuswithin");
            text.Append(" visual=").Append(element.VisualChildren.Count)
                .Append(" logical=").Append(element.LogicalChildren.Count)
                .Append(" bounds=").Append(FormatRect(element.Bounds));
            return text.ToString();
        }

        static string GetTargetName(SurfaceScriptBase target)
        {
            if (target == null || target.Block == null)
                return string.Empty;
            var terminal = target.Block as IMyTerminalBlock;
            return terminal != null
                ? terminal.CustomName ?? string.Empty
                : target.Block.DisplayNameText ?? string.Empty;
        }

        static string FormatRect(RectangleF value)
        {
            return "(" + Round(value.X) + "," + Round(value.Y) + " " +
                   Round(value.Width) + "x" + Round(value.Height) + ")";
        }

        static string Round(float value) { return ((int)Math.Round(value)).ToString(); }

        static string Clamp(string value, int width)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= width)
                return value ?? string.Empty;
            return value.Substring(0, width);
        }

        internal struct Line
        {
            public readonly string Text;
            public readonly Color Color;

            public Line(string text, Color color)
            {
                Text = text;
                Color = color;
            }
        }
    }
}
