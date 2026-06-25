using LcdMod.Common.Config.Components;
#if DEBUG
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
using VRage.ModAPI;
using VRageMath;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(24, Name = "VisibleTreeDebug")]
    [ConfigComponent(Constants.APP, typeof(VisibleTreeDebugConfigComponent), PropertyName = "VisibleTreeDebugComponent")]
    [ConfigComponent(Constants.VisibleTreeReference, typeof(BlockReferenceConfigComponent), PropertyName = "VisibleTreeReferenceComponent")]
    internal sealed partial class VisibleTreeDebugApp : App
    {
        const string DEBUG_FONT = "Monospace";
        const float LINE_SCALE = 0.52f;
        const int MAX_DEPTH = 32;
        static readonly Color HeaderColor = Color.White;
        static readonly Color RootColor = new Color(120, 200, 255);
        static readonly Color ChildColor = new Color(210, 230, 255);
        static readonly Color ModelColor = new Color(170, 235, 170);
        static readonly Color DisabledColor = new Color(145, 145, 145);
        static readonly Color WarningColor = new Color(235, 210, 60);
        static readonly Color ErrorColor = new Color(230, 90, 90);

        readonly List<MySprite> _sprites = new List<MySprite>();

        public override IReadOnlyList<Control> Children { get; } = new Control[] {};

        public VisibleTreeDebugApp(VisibleTreeDebugSurfaceScript host)
            : base(host)
        {
        }

        public override void Update()
        {
        }

        public override List<MySprite> GetSprites()
        {
            var owner = Host as VisibleTreeDebugSurfaceScript;
            if (owner == null)
                return _sprites;

            SurfaceScriptBase target;
            string status;
            TryGetDebugTarget(out target, out status);
            return GetSprites(owner, target, status);
        }

        public bool TryGetDebugTarget(out SurfaceScriptBase target, out string status)
        {
            target = null;
            status = null;

            if (VisibleTreeReferenceComponent.EntityId == 0L)
            {
                status = "Screen Not Linked";
                return false;
            }

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(VisibleTreeReferenceComponent.EntityId, out entity))
            {
                status = "Reference block not found";
                return false;
            }

            var targetBlock = entity as IMyTerminalBlock;
            if (targetBlock == null || targetBlock.MarkedForClose)
            {
                status = "Invalid reference block";
                return false;
            }

            var instances = SurfaceScriptBase.Instances.GetInstances(targetBlock);
            if (instances == null)
            {
                status = "No LcdMod script instance";
                return false;
            }

            target = instances.GetInstance(VisibleTreeDebugComponent.ReferenceScreenIndex);
            if (target == null)
            {
                status = "No active script for screen " + VisibleTreeDebugComponent.ReferenceScreenIndex;
                return false;
            }

            return true;
        }

        public List<MySprite> GetSprites(
            VisibleTreeDebugSurfaceScript owner,
            SurfaceScriptBase target,
            string status)
        {
            var viewBox = GetViewBox(owner);
            var lines = BuildDebugLines(owner, target, status);
            float textScale = GetTextScale(owner);
            float lineHeight = owner.Surface.MeasureStringInPixels(new StringBuilder("A"), DEBUG_FONT, textScale).Y + 2f;
            var start = viewBox.Position + new Vector2(8f, 8f);

            _sprites.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = lines[i].Text,
                    Position = start + new Vector2(0f, i * lineHeight),
                    Color = lines[i].Color,
                    FontId = DEBUG_FONT,
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
            return Math.Max(0.05f, LINE_SCALE * owner.ConfiguredScale * fontScale);
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

            var activeEntries = new HashSet<ControlTemplate>();
            var activeEntryList = interactive.InteractiveEntries;
            if (activeEntryList != null)
            {
                foreach (var entry in activeEntryList)
                {
                    var control = entry as ControlTemplate;
                    if (control != null)
                        activeEntries.Add(control);
                }
            }

            var entries = interactive.GetInteractiveEntries(true);
            int rootCount = entries?.Count ?? 0;
            lines.Add(new DebugLine("Cursor: " + FormatVector(interactive.CursorPosition) +
                                    "  Root Controls: " + Fixed4(rootCount) +
                                    "  Include Disabled", HeaderColor));
            lines.Add(new DebugLine(string.Empty, HeaderColor));

            if (entries == null || entries.Count == 0)
            {
                lines.Add(new DebugLine("  (empty)", WarningColor));
                return lines;
            }

            int index = 0;
            var visited = new HashSet<ControlTemplate>();
            foreach (var entry in entries)
            {
                var control = entry as ControlTemplate;
                if(control == null)
                    continue;
                bool disabled = !activeEntries.Contains(control);
                AppendControl(lines, control, index.ToString(), 0, visited, disabled);
                index++;
            }

            return lines;
        }

        static void AppendControl(
            List<DebugLine> lines,
            ControlTemplate control,
            string path,
            int depth,
            HashSet<ControlTemplate> visited,
            bool disabled)
        {
            if (control == null || !control.Visible)
                return;

            Color color;
            if (disabled)
                color = DisabledColor;
            else if (depth == 0)
                color = RootColor;
            else if (control.Model != null)
                color = ModelColor;
            else
                color = ChildColor;

            string prefix = new string(' ', Math.Min(depth, MAX_DEPTH) * 2);
            lines.Add(new DebugLine(prefix + path + " " + BuildControlText(control, disabled), color));

            if (!visited.Add(control))
            {
                lines.Add(new DebugLine(prefix + "  (cycle)", WarningColor));
                return;
            }

            var children = control.Children;
            if (children == null || children.Count == 0)
                return;

            for (int i = 0; i < children.Count; i++)
                AppendControl(lines, children[i] as ControlTemplate, path + "." + i, depth + 1, visited, disabled);
        }

        static string BuildControlText(ControlTemplate control, bool disabled)
        {
            var sb = new StringBuilder();
            sb.Append(control.GetType().Name);

            var overlayKind = GetOverlayKind(control);
            if (!string.IsNullOrEmpty(overlayKind))
                sb.Append(" overlay=").Append(overlayKind);

            var model = control.Model;
            var parentApp = control.DataContext as IApp;
            if (model != null)
                sb.Append(" model=").Append(model.GetType().Name);
            else if (parentApp != null)
                sb.Append(" datacontext=").Append(ClampToWidth(parentApp.GetType().Name, 24));
            
            if (control.Class != null)
                sb.Append(" class=[").Append(ClampToWidth(control.Class, 48)).Append("]");
            
            if (disabled)
                sb.Append(" disabled");

            else if (control.DataContext != null)
                sb.Append(" data=").Append(ClampToWidth(control.DataContext.GetType().Name, 24));



            sb.Append(" flags=").Append(GetFlags(control));

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

            sb.Append(" children=").Append(control.Children?.Count ?? 0);
            sb.Append(" bounds=").Append(FormatRect(control.Bounds));

            return sb.ToString();
        }

        static string GetOverlayKind(ControlTemplate control)
        {
            if (control == null)
                return string.Empty;

            var name = control.GetType().Name;
            if (name == "TooltipContainerControl")
                return "Tooltip";
            if (name == "DialogContainerControl")
                return "Dialog";
            if (name == "GlobalMenu")
                return "GlobalMenu";
            if (name == "GlobalMenuContainerControl")
                return "GlobalMenu";
            if (name == "HiddenGlobalMenuControl")
                return "HiddenGlobalMenu";

            return string.Empty;
        }

        static string GetTargetName(SurfaceScriptBase target)
        {
            if (target == null || target.Block == null)
                return string.Empty;

            var terminalBlock = target.Block as IMyTerminalBlock;
            return terminalBlock != null ? terminalBlock.CustomName ?? string.Empty : target.Block.DisplayNameText ?? string.Empty;
        }

        static string GetFlags(ControlTemplate control)
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

    }
}
#endif
