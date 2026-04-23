using System;
using System.Collections.Generic;
using Generated;
using Graph.Apps.Abstract;
using Graph.Helpers;
using Graph.System;
using Graph.System.TerminalControls.Color;
using Graph.System.TerminalControls.Filter;
using Graph.System.TerminalControls.Filter.Buttons;
using Graph.System.TerminalControls.Filter.Listbox;
using Graph.System.TerminalControls.Generic;
using Graph.System.TerminalControls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace Graph.Apps.Refinery
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class RefineryQueueSurfaceScript : ItemsSurfaceScriptBase,
        IUsesTerminalControlGroup<BaseTerminalControlGroup>,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<CheckboxHideEmpty>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>,
        IUsesTerminalControl<ComboboxSorting>
    {
        public const string ID = "RefineryQueue";
        public const string TITLE = "DisplayName_BlockGroup_InputOutputGroup";

        protected override string DefaultTitle => TITLE;

        public override Dictionary<MyItemType, double> ItemSource =>
            GridLogic != null && Config != null
                ? GridLogic.GetRefineryItems(0, Config, Block as IMyTerminalBlock)
                : new Dictionary<MyItemType, double>();


        enum EntryKind
        {
            Header,
            Item,
            Empty
        }

        struct VirtualEntry
        {
            public EntryKind Kind;
            public string Label;
            public KeyValuePair<MyItemType, double> Item;
        }


        public RefineryQueueSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        public override void DrawItems()
        {
            using (var frame = Surface.DrawFrame())
            {
                List<MySprite> sprites = new List<MySprite>();

                AddBackground(sprites);
                DrawTitle(sprites);
                DrawFooter(sprites);

                if (GridLogic == null)
                {
                    sprites.Add(MakeText(
                        (IMyTextSurface)Surface,
                        LocHelper.Empty,
                        ViewBox.Center,
                        Scale,
                        TextAlignment.CENTER));
                    frame.AddRange(sprites);
                    return;
                }

                if (Config != null && Config.DisplayMode == DisplayMode.Grid)
                    DrawGridMode(sprites);
                else
                    DrawListMode(sprites);

                frame.AddRange(sprites);
            }
        }


        void DrawListMode(List<MySprite> sprites)
        {
            List<VirtualEntry> virtualList = BuildVirtualList();

            if (virtualList.Count == 0)
            {
                sprites.Add(MakeText(
                    (IMyTextSurface)Surface,
                    LocHelper.Empty,
                    ViewBox.Center,
                    Scale,
                    TextAlignment.CENTER));
                return;
            }

            float rowHeight = LINE_HEIGHT * Scale;
            float availableH = ViewBox.Height - (CaretY - ViewBox.Y) - FooterHeight;
            int maxRows = Math.Max(1, (int)Math.Floor(availableH / rowHeight));
            int totalEntries = virtualList.Count;
            bool shouldScroll = totalEntries > maxRows;
            int startIndex = 0;

            float margin = ViewBox.Size.X * Margin;
            float xLeft = ViewBox.X + margin;
            float xRight = ViewBox.X + ViewBox.Width - margin;

            if (shouldScroll)
            {
                int totalSteps = Math.Max(1, totalEntries - maxRows);
                int step = GetScrollStep(SCROLL_DELAY / 6);
                startIndex = step % (totalSteps + 1);

                float viewportHeight = maxRows * rowHeight - (SCROLLER_WIDTH * 2 * Scale);
                float scrollBarHeight = (float)maxRows / totalEntries * viewportHeight;

                float totalScrollable = totalEntries - maxRows;
                float scrollFraction = totalScrollable > 0f
                    ? startIndex / totalScrollable
                    : 0f;

                float scrollBarTravel = viewportHeight - scrollBarHeight;
                float scrollBarY = scrollFraction * scrollBarTravel;
                float scrollBarCenter = scrollBarY + scrollBarHeight / 2f;

                float initialY = CaretY + SCROLLER_WIDTH * Scale;
                DrawScrollBar(sprites, Scale, initialY, viewportHeight, scrollBarCenter, scrollBarHeight);

                xRight -= SCROLLER_WIDTH * Scale;
            }

            for (int i = startIndex; i < startIndex + Math.Min(maxRows, totalEntries - startIndex); i++)
            {
                if (virtualList[i].Kind == EntryKind.Item)
                {
                    PreviousType = virtualList[i].Item.Key.TypeId;
                    break;
                }
            }

            int showCount = Math.Min(maxRows, totalEntries - startIndex);
            for (int i = startIndex; i < startIndex + showCount; i++)
            {
                VirtualEntry entry = virtualList[i];

                switch (entry.Kind)
                {
                    case EntryKind.Header:
                        DrawSectionHeader(sprites, entry.Label, xLeft, xRight);
                        break;

                    case EntryKind.Item:
                        DrawRow(sprites, entry.Item, shouldScroll);
                        break;

                    case EntryKind.Empty:
                        DrawEmptyRow(sprites, xLeft, xRight);
                        break;
                }
            }
        }

        struct GridViewRow
        {
            public EntryKind Kind;
            public string Label;
            public List<KeyValuePair<MyItemType, double>> Items;
            public float Height;
        }

        void DrawGridMode(List<MySprite> sprites)
        {
            float gridCellH = 3 * LINE_HEIGHT * Scale;
            float headerH = LINE_HEIGHT * Scale;
            float availableH = ViewBox.Height - (CaretY - ViewBox.Y) - FooterHeight;
            float margin = ViewBox.Size.X * Margin;
            float xLeft = ViewBox.X + margin;
            float xRight = ViewBox.X + ViewBox.Width - margin;
            int maxCols = GetMaxGridCols();

            List<GridViewRow> rows = BuildGridRows(maxCols, gridCellH, headerH);

            if (rows.Count == 0)
            {
                sprites.Add(MakeText(
                    (IMyTextSurface)Surface,
                    LocHelper.Empty,
                    ViewBox.Center,
                    Scale,
                    TextAlignment.CENTER));
                return;
            }

            float testH = 0f;
            int maxVisible = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (testH + rows[i].Height > availableH) break;
                testH += rows[i].Height;
                maxVisible++;
            }

            bool shouldScroll = maxVisible < rows.Count;
            int startIndex = 0;

            if (shouldScroll)
            {
                xRight -= SCROLLER_WIDTH * Scale;

                int totalSteps = Math.Max(1, rows.Count - maxVisible);
                int step = GetScrollStep(SCROLL_DELAY / 6);
                startIndex = step % (totalSteps + 1);

                float viewportH = availableH - (SCROLLER_WIDTH * 2 * Scale);
                float scrollBarHeight = (float)maxVisible / rows.Count * viewportH;
                float scrollFraction = (float)startIndex / totalSteps;
                float scrollBarTravel = viewportH - scrollBarHeight;
                float scrollBarCenter = scrollFraction * scrollBarTravel + scrollBarHeight / 2f;
                float initialY = CaretY + SCROLLER_WIDTH * Scale;
                DrawScrollBar(sprites, Scale, initialY, viewportH, scrollBarCenter, scrollBarHeight);
            }

            float remainingH = availableH;
            for (int i = startIndex; i < rows.Count; i++)
            {
                GridViewRow row = rows[i];
                if (remainingH < row.Height) break;
                remainingH -= row.Height;

                switch (row.Kind)
                {
                    case EntryKind.Header:
                        DrawSectionHeader(sprites, row.Label, xLeft, xRight);
                        break;

                    case EntryKind.Empty:
                        DrawEmptyRow(sprites, xLeft, xRight);
                        break;

                    case EntryKind.Item:
                        float colWidth = (xRight - xLeft) / maxCols;
                        for (int c = 0; c < row.Items.Count; c++)
                        {
                            float cx = xLeft + c * colWidth;
                            float cx2 = (c == maxCols - 1) ? xRight : cx + colWidth;
                            DrawGridCell(sprites, row.Items[c], cx, cx2, c == row.Items.Count - 1);
                        }

                        break;
                }
            }
        }

        List<GridViewRow> BuildGridRows(int maxCols, float gridCellH, float headerH)
        {
            var rows = new List<GridViewRow>();

            var referenceBlock = Block as IMyTerminalBlock;
            List<KeyValuePair<MyItemType, double>> ores =
                SortSection(GridLogic.GetRefineryItems(0, Config, referenceBlock));
            List<KeyValuePair<MyItemType, double>> ingots =
                SortSection(GridLogic.GetRefineryItems(1, Config, referenceBlock));

            string oreLabel = MyTexts.GetString("BlockPropertyProperties_CurrentInput");
            string ingotLabel = MyTexts.GetString("BlockPropertyProperties_CurrentOutput");

            rows.Add(new GridViewRow { Kind = EntryKind.Header, Label = oreLabel, Height = headerH });
            if (ores.Count == 0)
            {
                rows.Add(new GridViewRow { Kind = EntryKind.Empty, Height = headerH });
            }
            else
            {
                for (int i = 0; i < ores.Count; i += maxCols)
                {
                    var batch = new List<KeyValuePair<MyItemType, double>>();
                    for (int j = i; j < ores.Count && j < i + maxCols; j++)
                        batch.Add(ores[j]);
                    rows.Add(new GridViewRow { Kind = EntryKind.Item, Items = batch, Height = gridCellH });
                }
            }

            rows.Add(new GridViewRow { Kind = EntryKind.Header, Label = ingotLabel, Height = headerH });
            if (ingots.Count == 0)
            {
                rows.Add(new GridViewRow { Kind = EntryKind.Empty, Height = headerH });
            }
            else
            {
                for (int i = 0; i < ingots.Count; i += maxCols)
                {
                    var batch = new List<KeyValuePair<MyItemType, double>>();
                    for (int j = i; j < ingots.Count && j < i + maxCols; j++)
                        batch.Add(ingots[j]);
                    rows.Add(new GridViewRow { Kind = EntryKind.Item, Items = batch, Height = gridCellH });
                }
            }

            return rows;
        }

        int GetMaxGridCols()
        {
            float max = ViewBox.Width - ViewBox.X;
            float perCol = MINIMUM_COL_WIDTH * Scale;
            return Math.Max(1, (int)Math.Round(max / perCol - .5, MidpointRounding.AwayFromZero));
        }


        List<VirtualEntry> BuildVirtualList()
        {
            List<VirtualEntry> list = new List<VirtualEntry>();

            var referenceBlock = Block as IMyTerminalBlock;
            Dictionary<MyItemType, double> ores = GridLogic.GetRefineryItems(0, Config, referenceBlock);
            Dictionary<MyItemType, double> ingots = GridLogic.GetRefineryItems(1, Config, referenceBlock);

            string oreLabel = MyTexts.GetString("BlockPropertyProperties_CurrentInput");
            string ingotLabel = MyTexts.GetString("BlockPropertyProperties_CurrentOutput");

            list.Add(new VirtualEntry { Kind = EntryKind.Header, Label = oreLabel });
            if (ores.Count == 0)
            {
                list.Add(new VirtualEntry { Kind = EntryKind.Empty });
            }
            else
            {
                List<KeyValuePair<MyItemType, double>> sorted = SortSection(ores);
                for (int i = 0; i < sorted.Count; i++)
                    list.Add(new VirtualEntry { Kind = EntryKind.Item, Item = sorted[i] });
            }

            list.Add(new VirtualEntry { Kind = EntryKind.Header, Label = ingotLabel });
            if (ingots.Count == 0)
            {
                list.Add(new VirtualEntry { Kind = EntryKind.Empty });
            }
            else
            {
                List<KeyValuePair<MyItemType, double>> sorted = SortSection(ingots);
                for (int i = 0; i < sorted.Count; i++)
                    list.Add(new VirtualEntry { Kind = EntryKind.Item, Item = sorted[i] });
            }

            return list;
        }

        List<KeyValuePair<MyItemType, double>> SortSection(Dictionary<MyItemType, double> source)
        {
            List<KeyValuePair<MyItemType, double>> list = new List<KeyValuePair<MyItemType, double>>(source.Count);
            foreach (KeyValuePair<MyItemType, double> kv in source)
                list.Add(kv);

            if (Config != null && Config.SortMethod == SortMethod.Type)
                list.Sort((a, b) => ItemTypeComparer.Instance.Compare(a.Key, b.Key));
            else
                list.Sort((a, b) => b.Value.CompareTo(a.Value));

            return list;
        }

        void DrawSectionHeader(List<MySprite> frame, string label, float xLeft, float xRight)
        {
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2((xLeft + xRight) / 2f, CaretY),
                Size = new Vector2(xRight - xLeft, 2f),
                Color = Config.HeaderColor,
                Alignment = TextAlignment.CENTER
            });

            AddHeaderSprite(frame, new MySprite
            {
                Type = SpriteType.TEXT,
                Data = label,
                Position = new Vector2(xLeft, CaretY),
                RotationOrScale = Scale * 1.0f * FontScale,
                Color = Config.HeaderColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            CaretY += LINE_HEIGHT * Scale;
        }

        void DrawEmptyRow(List<MySprite> frame, float xLeft, float xRight)
        {
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = LocHelper.Empty,
                Position = new Vector2((xLeft + xRight) / 2f, CaretY),
                RotationOrScale = Scale * FontScale,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });

            CaretY += LINE_HEIGHT * Scale;
        }
    }
}