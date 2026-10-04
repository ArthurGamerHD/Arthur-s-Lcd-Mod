using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using Adk.Gui.Controls;
using Adk.Gui.Layout;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(21)]
    // ReSharper disable once PartialTypeWithSinglePart
    internal sealed partial class SessionDebugApp : App
    {
        const string DEBUG_FONT = "Monospace";
        const float LINE_SCALE = 0.62f;
        const float INNER_PADDING = 8f;
        const float SCROLLBAR_WIDTH = 8f;

        static readonly Color IdleColor = new Color(230, 90, 90);

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<DebugLine> _lines = new List<DebugLine>(64);
        readonly Dictionary<string, DebugLine> _linesByKey = new Dictionary<string, DebugLine>();
        readonly HashSet<string> _activeLineKeys = new HashSet<string>();
        readonly List<string> _staleLineKeys = new List<string>();
        readonly ItemsControl _lineItems;
        readonly ScrollViewer _scrollViewer;

        readonly SessionDebugSurfaceScript _host;
        
        public SessionDebugApp(SessionDebugSurfaceScript sessionDebugSurfaceScript) : base(sessionDebugSurfaceScript)
        {
            _host = sessionDebugSurfaceScript;
            _lineItems = new ItemsControl
            {
                ItemTemplate = new DataTemplate<DebugLine>(CreateLineControl),
                ItemKeySelector = item => ((DebugLine)item).Key,
                ItemsSource = _lines
            };
            _scrollViewer = new ScrollViewer
            {
                Child = _lineItems,
                Padding = new Vector4(INNER_PADDING),
                VerticalScrollEnabled = true,
                HorizontalScrollEnabled = false,
                ScrollerWidthPixels = SCROLLBAR_WIDTH
            };
            SetContent(new SessionDebug.SessionDebugView
            {
                DataContext = new SessionDebug.SessionDebugViewModel(this),
                Content = _scrollViewer
            });
            SetContentLayout(true);
        }
        float Scale => GeneralComponent.GetScale();

        float FontScale => _host != null && _host.Surface != null ? Math.Max(0.1f, _host.Surface.FontSize) : 1f;

        float TextScale => LINE_SCALE * Scale * FontScale;

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();
            return _sprites;
        }

        public override void Update()
        {
            var snapshot = LcdModSessionComponent.DebugSnapshot;
            BuildDebugLines(snapshot, _host);
            _lineItems.RefreshItems();
            var lineHeight = TextWrappingHelper.GetLineHeight(
                _host.Surface,
                DEBUG_FONT,
                TextScale,
                2f * Math.Max(0.5f, Scale * FontScale));

            _scrollViewer.WheelStep = lineHeight;
            for (int i = 0; i < _lineItems.ItemCount; i++)
            {
                var text = _lineItems.GetContainerAt(i) as TextBlock;
                if (text == null)
                    continue;
                text.FontSize = TextScale;
                text.MinHeight = lineHeight;
            }

            ClearDirtyAfterRender();
        }

        TextBlock CreateLineControl(DebugLine line)
        {
            TextBlock text = null;
            text = new TextBlock
            {
                FontFamily = DEBUG_FONT,
                FontSize = TextScale,
                TextAlignment = Adk.Gui.Core.HorizontalAlignment.Left,
                VerticalTextAlignment = Adk.Gui.Core.VerticalAlignment.Top
            };
            text.TextProvider = delegate
            {
                DebugLine current = text.DataContext as DebugLine;
                return current == null ? string.Empty : current.Text;
            };
            text.ColorProvider = delegate
            {
                DebugLine current = text.DataContext as DebugLine;
                return current == null ? Color.White : current.Color;
            };
            return text;
        }

        void BuildDebugLines(SessionDebugSnapshot snapshot, SessionDebugSurfaceScript owner)
        {
            _lines.Clear();
            _activeLineKeys.Clear();
            AddLine("title", "LcdMod Session Debug - " + owner.GetHashCode(), Color.White);
            AddLine("screen",
                "Screen Info: " + owner.Surface.Name + " - " + owner.Surface.TextureSize + " - " + owner.ViewBox,
                Color.White);
            AddLine("tick", "Tick: " + Fixed4(snapshot.UpdateTick), Color.White);
            AddLine("tracked-grids", "Tracked Grids: " + Fixed4(snapshot.TrackedGrids), Color.White);
            AddLine("grid-logic", "GridLogic Entries: " + Fixed4(snapshot.TrackedGridLogic), Color.White);
            AddLine("refresh", "Refresh In Progress: " + Fixed4(snapshot.RefreshInProgress), Color.White);
            AddLine("iterations", "Last Iterations Sum: " + Fixed4(snapshot.TotalLastRefreshIterations), Color.White);
            AddLine("processed", "Last Processed Sum: " + Fixed4(snapshot.TotalLastRefreshProcessed), Color.White);
            AddLine("batch", "Avg Next Batch: " + Fixed4(snapshot.AverageNextBatchSize), Color.White);
            AddLine("grid-gap", string.Empty, Color.White);
            AddLine("grid-heading", "Per Grid:", Color.White);
            AddLine("grid-columns", "Name blk itm", Color.White);

            var components = LcdModSessionComponent.Components;
            if (components == null || components.Count == 0)
            {
                AddLine("grid-none", " (none)", Color.White);
            }
            else
            {
                foreach (var pair in components
                             .Where(p => p.Value != null && p.Value.Grid != null)
                             .OrderByDescending(p => p.Value.TrackedBlockCount)
                             .ThenBy(p => p.Key))
                {
                    var logic = pair.Value;
                    if (logic == null)
                        continue;

                    var gridName = ClampToWidth(
                        logic.Grid != null ? logic.Grid.CustomName ?? string.Empty : string.Empty,
                        22);
                    AddLine(
                        "grid:" + pair.Key,
                        gridName + " " + Fixed4(logic.TrackedBlockCount) + " " +
                        Fixed4(logic.TrackedItemCount),
                        IdleColor);
                }
            }

            AddLine("module-gap", string.Empty, Color.White);
            AddLine("module-heading", "Modules:", Color.White);
            AddLine("module-columns", "Name cnt act", Color.White);

            var moduleLines = snapshot.ModuleLines;
            if (moduleLines == null || moduleLines.Length == 0)
            {
                AddLine("module-none", " (none)", Color.White);
                RemoveStaleLines();
                return;
            }

            for (int i = 0; i < moduleLines.Length; i++)
            {
                string line = moduleLines[i];
                string moduleName;
                int count;
                int active;
                ParseModuleLine(line, out moduleName, out count, out active);
                string key = "module:" + moduleName;
                moduleName = ClampToWidth(moduleName, 22);
                AddLine(key,
                    moduleName + " " + Fixed4(count) + " " + Fixed4(active),
                    Color.White);
            }
            RemoveStaleLines();
        }

        void AddLine(string key, string text, Color color)
        {
            DebugLine line;
            if (!_linesByKey.TryGetValue(key, out line))
            {
                line = new DebugLine(key);
                _linesByKey.Add(key, line);
            }
            line.Text = text;
            line.Color = color;
            _lines.Add(line);
            _activeLineKeys.Add(key);
        }

        void RemoveStaleLines()
        {
            _staleLineKeys.Clear();
            foreach (string key in _linesByKey.Keys)
                if (!_activeLineKeys.Contains(key))
                    _staleLineKeys.Add(key);
            for (int i = 0; i < _staleLineKeys.Count; i++)
                _linesByKey.Remove(_staleLineKeys[i]);
        }

        static string ClampToWidth(string value, int width)
        {
            if (string.IsNullOrEmpty(value))
                return new string(' ', width);

            if (value.Length > width)
                return value.Substring(0, width);

            return value.PadRight(width);
        }

        static string Fixed4(int value)
        {
            return ClampToWidth(value.ToString(), 4);
        }

        static void ParseModuleLine(string line, out string moduleName, out int count, out int active)
        {
            moduleName = string.Empty;
            count = 0;
            active = 0;

            if (string.IsNullOrEmpty(line))
                return;

            var parts = line.Split(':');
            if (parts.Length == 0)
                return;

            moduleName = parts[0].Trim();

            if (parts.Length > 1)
            {
                int parsedCount;
                if (int.TryParse(parts[1].Trim(), out parsedCount))
                    count = parsedCount;
            }

            if (parts.Length > 2)
            {
                int parsedActive;
                if (int.TryParse(parts[2].Trim(), out parsedActive))
                    active = parsedActive;
            }
        }

        sealed class DebugLine
        {
            public readonly string Key;
            public string Text;
            public Color Color;

            public DebugLine(string key)
            {
                Key = key;
            }
        }

        public override void Close()
        {
            base.Close();
        }

        // hack: not needed on this app
    }
}
