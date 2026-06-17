using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    /// <summary>
    ///     Three-step dialog opened from a cargo entry: pick an action (Send / Receive / Balance),
    ///     pick the target containers, pick the item types, then run it (server-authoritative).
    /// </summary>
    internal sealed class ContainerActionDialog : Dialog
    {
        private const int MAX_VISIBLE_ROWS = 20;
        private readonly List<IMyTerminalBlock> _candidates = new List<IMyTerminalBlock>();
        private readonly List<DisplayRow> _displayRows = new List<DisplayRow>();
        private readonly Action<List<string>, List<string>> _onSaveFilter;
        private readonly Action<string> _onStatusMessage;

        private readonly List<RectangleControl> _pool = new List<RectangleControl>();
        private readonly HashSet<string> _selectedCategories = new HashSet<string>();

        private readonly HashSet<long> _selectedTargets = new HashSet<long>();
        private readonly HashSet<string> _selectedTypeKeys = new HashSet<string>();
        private readonly Action<Dialog> _showDialog;

        private readonly IMyTerminalBlock _source;
        private readonly List<TypeRow> _types = new List<TypeRow>();
        private int _maxScroll;
        private TransferMode _mode;
        private int _poolIndex;
        private int _scroll;
        private RectangleControl _scrollCatcher;
        private bool _scrollRenderQueued;

        private int _step;
        private bool _typesBuilt;

        public ContainerActionDialog(IApp parentApp, IMyTerminalBlock source,
            List<IMyTerminalBlock> candidates, Action<Dialog> showDialog,
            IEnumerable<string> savedFilter, IEnumerable<string> savedCategories,
            Action<List<string>, List<string>> onSaveFilter,
            Action<string> onStatusMessage = null)
            : base(parentApp)
        {
            _source = source;
            _showDialog = showDialog;
            _onSaveFilter = onSaveFilter;
            _onStatusMessage = onStatusMessage;

            if (savedFilter != null)
                foreach (var key in savedFilter)
                    if (!string.IsNullOrEmpty(key))
                        _selectedTypeKeys.Add(key);

            if (savedCategories != null)
                foreach (var cat in savedCategories)
                    if (!string.IsNullOrEmpty(cat))
                        _selectedCategories.Add(cat);

            if (candidates != null)
                for (var i = 0; i < candidates.Count; i++)
                {
                    var block = candidates[i];
                    if (block != null && block != source && block.HasInventory)
                        _candidates.Add(block);
                }
        }

        protected override void OnDismiss()
        {
            if (_onSaveFilter != null)
                _onSaveFilter(new List<string>(_selectedTypeKeys), new List<string>(_selectedCategories));
        }

        protected override void BuildDialogControls(
            InteractiveSurfaceScript owner,
            RectangleF viewBox,
            float scale,
            float fontScale,
            IMyTextSurface surface,
            Color textColor,
            Color backgroundColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            EnsureContainer(viewBox);
            ContainerControl.ClearChildren();
            _poolIndex = 0;

            var cardColor = ResolveColor(ThemeResources.SurfaceContainerHighColor);
            var cardTextColor = ResolveColor(ThemeResources.OnSurfaceColor);
            var shadowColor = ResolveColor(ThemeResources.ShadowColor);

            Sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                surface.TextureSize / 2, surface.TextureSize, new Color(0, 0, 0, 128)));

            var pad = 18f * scale;
            var titleScale = 0.72f * scale * fontScale;
            var bodyScale = 0.5f * scale * fontScale;
            var buttonScale = 0.6f * scale * fontScale;

            var cardWidth = Math.Min(viewBox.Width - 2f * pad, viewBox.Width * 0.84f);
            var cardHeight = Math.Min(viewBox.Height - 2f * pad, viewBox.Height * 0.92f);
            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth, cardHeight);
            RegisterDialogCard(cardRect);

            Border.CreateSpritesFromRect(new RectangleF(cardRect.Position + 2f, cardRect.Size), Sprites, shadowColor,
                radiusScale: scale);
            Border.CreateSpritesFromRect(cardRect, Sprites, cardColor, radiusScale: scale);

            var title = GetTitle();
            var titleSize = FormatingHelper.GetSizeInPixel(title, "White", titleScale, surface);
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = title,
                Position = new Vector2(cardRect.Center.X, cardRect.Y + pad),
                Color = cardTextColor,
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = titleScale
            });

            var bodyTop = cardRect.Y + pad + titleSize.Y + 12f * scale;
            var footerHeight = Math.Max(28f * scale, FormatingHelper.LineHeight(buttonScale, surface) + 14f * scale);
            var footerTop = cardRect.Bottom - pad - footerHeight;
            var bodyRect = new RectangleF(cardRect.X + pad, bodyTop, cardRect.Width - 2f * pad,
                footerTop - bodyTop - 8f * scale);

            if (_step == 0)
                RenderActionStep(bodyRect, scale, buttonScale, cardTextColor);
            else
                RenderListStep(bodyRect, scale, bodyScale, cardTextColor, surface);

            RenderFooter(cardRect, footerTop, footerHeight, pad, scale, buttonScale, surface);
        }

        private string GetTitle()
        {
            var name = SafeName(_source);
            switch (_step)
            {
                case 1:
                    return name + " - " + LocHelper.GetLoc("LcdMod_Transfer_SelectTargets");
                case 2:
                    return name + " - " + LocHelper.GetLoc("LcdMod_Transfer_SelectItems");
                default:
                    return name;
            }
        }

        private void RenderActionStep(RectangleF body, float scale, float buttonScale, Color textColor)
        {
            var gap = 10f * scale;
            var h = Math.Min(46f * scale, (body.Height - 2f * gap) / 3f);
            var w = Math.Min(body.Width, 320f * scale);
            var x = body.Center.X - w * 0.5f;
            var y = body.Y + Math.Max(0f, (body.Height - (h * 3f + gap * 2f)) * 0.5f);

            DrawButton(new RectangleF(x, y, w, h), LocHelper.GetLoc("LcdMod_Transfer_Send"), buttonScale, true, true,
                delegate
                {
                    _mode = TransferMode.Send;
                    GoToStep(1);
                });
            y += h + gap;
            DrawButton(new RectangleF(x, y, w, h), LocHelper.GetLoc("LcdMod_Transfer_Receive"), buttonScale, true, true,
                delegate
                {
                    _mode = TransferMode.Receive;
                    GoToStep(1);
                });
            y += h + gap;
            DrawButton(new RectangleF(x, y, w, h), LocHelper.GetLoc("LcdMod_Transfer_Balance"), buttonScale, true, true,
                delegate
                {
                    _mode = TransferMode.Balance;
                    GoToStep(1);
                });
        }

        private void RenderListStep(RectangleF body, float scale, float rowScale, Color textColor,
            IMyTextSurface surface)
        {
            var rowHeight = FormatingHelper.LineHeight(rowScale, surface) + 6f * scale;
            var rowGap = 2f * scale;
            var total = _step == 1 ? _candidates.Count : _displayRows.Count;
            var maxVisible = Math.Max(1,
                Math.Min(MAX_VISIBLE_ROWS, (int)((body.Height + rowGap) / (rowHeight + rowGap))));
            var scrollable = total > maxVisible;

            var maxScroll = Math.Max(0, total - maxVisible);
            if (_scroll > maxScroll) _scroll = maxScroll;
            if (_scroll < 0) _scroll = 0;
            _maxScroll = maxScroll;

            if (_scrollCatcher == null)
                _scrollCatcher = new RectangleControl(body, CursorType.Default);
            else
                _scrollCatcher.SetRect(body);
            _scrollCatcher.SetOnScroll(OnListScroll);
            _scrollCatcher.SetVisible(true);
            ContainerControl.AddChild(_scrollCatcher);

            var listWidth = scrollable ? body.Width - 30f * scale : body.Width;
            var y = body.Y;
            var shown = Math.Min(maxVisible, total - _scroll);
            for (var r = 0; r < shown; r++)
            {
                var index = _scroll + r;
                var rect = new RectangleF(body.X, y, listWidth, rowHeight);
                if (_step == 1)
                {
                    var block = _candidates[index];
                    var id = block.EntityId;
                    var selected = _selectedTargets.Contains(id);
                    DrawRow(rect, SafeName(block), rowScale, selected,
                        delegate { Toggle(_selectedTargets, id); });
                }
                else
                {
                    var row = _displayRows[index];
                    if (row.IsGroup)
                    {
                        var groupKey = row.GroupKey;
                        var selected = _selectedCategories.Contains(groupKey);
                        DrawRow(rect, row.Label, rowScale, selected,
                            delegate { Toggle(_selectedCategories, groupKey); });
                    }
                    else
                    {
                        var key = row.Key;
                        var selected = _selectedTypeKeys.Contains(key);
                        DrawRow(rect, row.Label, rowScale, selected,
                            delegate { Toggle(_selectedTypeKeys, key); });
                    }
                }

                y += rowHeight + rowGap;
            }

            if (scrollable)
            {
                var sx = body.Right - 26f * scale;
                var sw = 26f * scale;
                var sh = Math.Min(rowHeight, 30f * scale);
                DrawButton(new RectangleF(sx, body.Y, sw, sh), "^", rowScale, false, _scroll > 0,
                    delegate
                    {
                        _scroll = Math.Max(0, _scroll - 1);
                        _showDialog?.Invoke(this);
                    });
                DrawButton(new RectangleF(sx, body.Bottom - sh, sw, sh), "v", rowScale, false, _scroll < maxScroll,
                    delegate
                    {
                        _scroll = Math.Min(maxScroll, _scroll + 1);
                        _showDialog?.Invoke(this);
                    });
            }
        }

        private void RenderFooter(RectangleF cardRect, float footerTop, float footerHeight, float pad, float scale,
            float buttonScale, IMyTextSurface surface)
        {
            if (_step == 0)
                return;

            var gap = 8f * scale;
            var btnW = Math.Min(130f * scale, (cardRect.Width - 2f * pad - 2f * gap) / 3f);
            var x = cardRect.X + pad;
            var y = footerTop;

            DrawButton(new RectangleF(x, y, btnW, footerHeight), LocHelper.GetLoc("LcdMod_Transfer_Back"), buttonScale,
                false, true,
                delegate { GoToStep(_step - 1); });

            DrawButton(new RectangleF(x + btnW + gap, y, btnW, footerHeight), LocHelper.GetLoc("LcdMod_Transfer_All"),
                buttonScale, false, true,
                delegate { ToggleAll(); });

            var primaryRect = new RectangleF(cardRect.Right - pad - btnW, y, btnW, footerHeight);
            if (_step == 1)
            {
                var ok = _selectedTargets.Count > 0;
                DrawButton(primaryRect, LocHelper.GetLoc("LcdMod_Transfer_Next"), buttonScale, true, ok,
                    delegate
                    {
                        if (_selectedTargets.Count > 0) GoToItems();
                    });
            }
            else
            {
                var ok = _selectedTypeKeys.Count > 0 || _selectedCategories.Count > 0;
                DrawButton(primaryRect, LocHelper.GetLoc("LcdMod_Cargo_Sorter"), buttonScale, true, ok,
                    delegate
                    {
                        if (_selectedTypeKeys.Count > 0 || _selectedCategories.Count > 0) Apply();
                    });
            }
        }

        private bool OnListScroll(object dataContext, object sender, int delta)
        {
            if (_step == 0)
                return false;

            var newScroll = _scroll + (delta > 0 ? -1 : 1);
            if (newScroll < 0) newScroll = 0;
            if (newScroll > _maxScroll) newScroll = _maxScroll;

            if (newScroll != _scroll)
            {
                _scroll = newScroll;
                if (!_scrollRenderQueued)
                {
                    _scrollRenderQueued = true;
                    LcdModClientComponent.RunNextFrame.Add(delegate
                    {
                        _scrollRenderQueued = false;
                        _showDialog?.Invoke(this);
                    });
                }
            }

            return true;
        }

        private void GoToStep(int step)
        {
            if (step < 0)
            {
                Dismiss();
                return;
            }

            _step = step;
            _scroll = 0;
            _showDialog?.Invoke(this);
        }

        private void GoToItems()
        {
            GatherGameTypes();
            _step = 2;
            _scroll = 0;
            _showDialog?.Invoke(this);
        }

        private void ToggleAll()
        {
            if (_step == 1)
            {
                if (_selectedTargets.Count >= _candidates.Count)
                    _selectedTargets.Clear();
                else
                    for (var i = 0; i < _candidates.Count; i++)
                        _selectedTargets.Add(_candidates[i].EntityId);
            }
            else
            {
                if (_selectedTypeKeys.Count >= _types.Count)
                    _selectedTypeKeys.Clear();
                else
                    for (var i = 0; i < _types.Count; i++)
                        _selectedTypeKeys.Add(_types[i].Key);
            }

            _showDialog?.Invoke(this);
        }

        private void Toggle(HashSet<long> set, long value)
        {
            if (!set.Remove(value))
                set.Add(value);
            _showDialog?.Invoke(this);
        }

        private void Toggle(HashSet<string> set, string value)
        {
            if (!set.Remove(value))
                set.Add(value);
            _showDialog?.Invoke(this);
        }

        private void GatherGameTypes()
        {
            if (_typesBuilt)
                return;

            _types.Clear();
            _displayRows.Clear();
            var seenKeys = new HashSet<string>();
            var seenNames = new HashSet<string>();
            foreach (var definitionBase in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var def = definitionBase as MyPhysicalItemDefinition;
                if (def == null || !def.Public || !PassesWhiteList(def))
                    continue;

                var key = def.Id.TypeId + "/" + def.Id.SubtypeName;
                if (!seenKeys.Add(key))
                    continue;

                var name = !string.IsNullOrEmpty(def.DisplayNameText) ? def.DisplayNameText : def.Id.SubtypeName;
                name = name ?? string.Empty;
                int order;
                var category = CategoryFor(def.Id.TypeId.ToString(), out order);

                if (!seenNames.Add(category + "" + name.ToLowerInvariant()))
                    continue;

                _types.Add(new TypeRow { Key = key, Name = name, Category = category, Order = order });
            }

            _types.Sort(delegate(TypeRow a, TypeRow b)
            {
                if (a.Order != b.Order)
                    return a.Order.CompareTo(b.Order);
                var byCategory = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
                if (byCategory != 0)
                    return byCategory;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            var groups = ItemCategoryHelper.Groups;
            for (var i = 0; i < groups.Length; i++)
                _displayRows.Add(new DisplayRow
                {
                    IsGroup = true,
                    Label = ItemCategoryHelper.GetGroupDisplayName(groups[i]),
                    GroupKey = groups[i],
                    Key = null
                });

            for (var i = 0; i < _types.Count; i++)
                _displayRows.Add(new DisplayRow { IsGroup = false, Label = _types[i].Name, Key = _types[i].Key });

            _typesBuilt = true;
        }

        private static bool PassesWhiteList(MyPhysicalItemDefinition def)
        {
            var id = def.Id.ToString();
            return !id.Contains("_TreeObject/")
                   && !id.Contains("GunObject/GoodAIReward")
                   && !id.Contains("GunObject/CubePlacerItem");
        }

        private static string CategoryFor(string typeId, out int order)
        {
            var t = typeId ?? string.Empty;
            const string prefix = "MyObjectBuilder_";
            if (t.StartsWith(prefix, StringComparison.Ordinal))
                t = t.Substring(prefix.Length);

            var groups = ItemCategoryHelper.Groups;
            for (var i = 0; i < groups.Length; i++)
                if (t.StartsWith(groups[i], StringComparison.OrdinalIgnoreCase))
                {
                    order = i;
                    return ItemCategoryHelper.GetGroupDisplayName(groups[i]);
                }

            order = groups.Length + 1;
            return string.IsNullOrEmpty(t) ? "Other" : t;
        }

        private void ExpandCategoriesToKeys(List<IMyTerminalBlock> targets, HashSet<string> keys)
        {
            AddCategoryItemKeys(_source, keys);
            for (var i = 0; i < targets.Count; i++)
                AddCategoryItemKeys(targets[i], keys);
        }

        private void AddCategoryItemKeys(IMyTerminalBlock block, HashSet<string> keys)
        {
            if (block == null || !block.HasInventory)
                return;

            var inv = block.GetInventory(0);
            if (inv == null)
                return;

            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            for (var k = 0; k < items.Count; k++)
            {
                var typeId = items[k].Type.TypeId;
                foreach (var cat in _selectedCategories)
                    if (typeId.EndsWith(cat, StringComparison.OrdinalIgnoreCase))
                    {
                        keys.Add(InventoryDistributorCommon.KeyOf(items[k].Type));
                        break;
                    }
            }
        }

        private void Apply()
        {
            string status = null;
            try
            {
                var targets = new List<IMyTerminalBlock>(_selectedTargets.Count);
                for (var i = 0; i < _candidates.Count; i++)
                    if (_selectedTargets.Contains(_candidates[i].EntityId))
                        targets.Add(_candidates[i]);

                var keys = new HashSet<string>(_selectedTypeKeys);
                if (_selectedCategories.Count > 0)
                    ExpandCategoriesToKeys(targets, keys);

                if (keys.Count == 0)
                {
                    Dismiss();
                    return;
                }

                if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer)
                {
                    var moved = InventoryDistributorCommon.Execute(_source, targets, keys, _mode);
                    status = string.Format(LocHelper.GetLoc("LcdMod_CargoActions_ActionDone"), GetModeLabel(), moved);
                }
                else
                {
                    var targetIds = new long[targets.Count];
                    for (var i = 0; i < targets.Count; i++)
                        targetIds[i] = targets[i].EntityId;

                    var keyArr = new string[keys.Count];
                    keys.CopyTo(keyArr);

                    LcdModSessionComponent.NetworkManager.TransmitToServer(
                        new PacketTransferItems(_source.EntityId, targetIds, keyArr, (int)_mode), false);
                    status = LocHelper.GetLoc("LcdMod_Cargo_SortRequested");
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(ContainerActionDialog));
            }

            // Dismiss first, then notify: NotifyStatus triggers a synchronous render, and with the
            // dialog still open the status banner would be drawn underneath it (visible only on the
            // next external render).
            Dismiss();
            NotifyStatus(status);
        }

        private void NotifyStatus(string message)
        {
            if (_onStatusMessage != null && !string.IsNullOrEmpty(message))
                _onStatusMessage(message);
        }

        private string GetModeLabel()
        {
            switch (_mode)
            {
                case TransferMode.Send:
                    return LocHelper.GetLoc("LcdMod_Transfer_Send");
                case TransferMode.Receive:
                    return LocHelper.GetLoc("LcdMod_Transfer_Receive");
                default:
                    return LocHelper.GetLoc("LcdMod_Transfer_Balance");
            }
        }

        private void DrawButton(RectangleF rect, string text, float textScale, bool primary, bool enabled,
            Action onClick)
        {
            var control = Rent(rect, enabled ? OnClickAction(onClick) : null);
            control.SetCursor(enabled ? CursorType.Hand : CursorType.Default);
            ContainerControl.AddChild(control);

            var panel = enabled
                ? primary
                    ? ResolveColor(ThemeResources.AccentContainerColor)
                    : ResolveColor(ThemeResources.SurfaceContainerColor)
                : ResolveColor(ThemeResources.SurfaceContainerLowColor);
            var txt = primary
                ? ResolveColor(ThemeResources.OnAccentContainerColor)
                : ResolveColor(ThemeResources.OnSurfaceColor);
            var label = text;
            var btnScale = textScale;
            var surface = control.TextSurface;

            control.CustomRender = delegate(ControlTemplate ctrl, List<MySprite> sprites)
            {
                var r = ctrl.Bounds;
                var hover = enabled && ctrl.IsMouseOver;
                Border.CreateSpritesFromRect(r, sprites, hover ? panel.MulValue(1.18f) : panel, radiusScale: ctrl.LayoutScale);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = label,
                    Position = new Vector2(r.Center.X,
                        r.Center.Y - FormatingHelper.GetSizeInPixel(label, "White", btnScale, surface).Y * 0.5f),
                    Color = txt,
                    FontId = "White",
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = btnScale
                });
            };

            control.Render(Sprites);
        }

        private void DrawRow(RectangleF rect, string text, float textScale, bool selected, Action onClick)
        {
            var control = Rent(rect, OnClickAction(onClick));
            control.SetCursor(CursorType.Hand);
            ContainerControl.AddChild(control);

            var panel = selected
                ? ResolveColor(ThemeResources.AccentContainerColor)
                : ResolveColor(ThemeResources.SurfaceContainerColor);
            var txt = selected
                ? ResolveColor(ThemeResources.OnAccentContainerColor)
                : ResolveColor(ThemeResources.OnSurfaceColor);
            var label = text;
            var rowScale = textScale;
            var surface = control.TextSurface;
            var isSelected = selected;

            control.CustomRender = delegate(ControlTemplate ctrl, List<MySprite> sprites)
            {
                var r = ctrl.Bounds;
                var hover = ctrl.IsMouseOver;
                Border.CreateSpritesFromRect(r, sprites, hover && !isSelected ? panel.MulValue(1.18f) : panel,
                    radiusScale: ctrl.LayoutScale);

                var pad = 10f * ctrl.LayoutScale;
                if (isSelected)
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Checkmark",
                        Position = new Vector2(r.Right - pad - 8f * ctrl.LayoutScale, r.Center.Y),
                        Size = new Vector2(16f * ctrl.LayoutScale),
                        Color = txt,
                        Alignment = TextAlignment.CENTER
                    });

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = label,
                    Position = new Vector2(r.X + pad,
                        r.Center.Y - FormatingHelper.GetSizeInPixel(label, "White", rowScale, surface).Y * 0.5f),
                    Color = txt,
                    FontId = "White",
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = rowScale
                });
            };

            control.Render(Sprites);
        }

        private RectangleControl Rent(RectangleF rect, Action<object, object> onClick)
        {
            RectangleControl control;
            if (_poolIndex < _pool.Count)
            {
                control = _pool[_poolIndex];
                control.SetRect(rect);
                control.SetOnClick(onClick);
            }
            else
            {
                control = new RectangleControl(rect, CursorType.Hand, null, onClick);
                _pool.Add(control);
            }

            _poolIndex++;
            control.SetVisible(true);
            return control;
        }

        private static Action<object, object> OnClickAction(Action onClick)
        {
            if (onClick == null)
                return null;
            return delegate { onClick(); };
        }

        private static string SafeName(IMyTerminalBlock block)
        {
            if (block == null)
                return "?";
            try
            {
                var name = block.CustomName;
                if (string.IsNullOrEmpty(name)) name = block.DisplayNameText;
                if (string.IsNullOrEmpty(name)) name = block.BlockDefinition.SubtypeName;
                return string.IsNullOrEmpty(name) ? "?" : name;
            }
            catch
            {
                return "?";
            }
        }

        private static string GetItemName(MyItemType type)
        {
            try
            {
                MyDefinitionId id;
                if (MyDefinitionId.TryParse(type.TypeId + "/" + type.SubtypeId, out id))
                {
                    var def = MyDefinitionManager.Static.GetPhysicalItemDefinition(id);
                    if (def != null && !string.IsNullOrEmpty(def.DisplayNameText))
                        return def.DisplayNameText;
                }
            }
            catch (Exception)
            {
            }

            return type.SubtypeId ?? string.Empty;
        }

        private struct TypeRow
        {
            public string Key;
            public string Name;
            public string Category;
            public int Order;
        }

        private struct DisplayRow
        {
            public bool IsGroup;
            public string Label;
            public string Key;
            public string GroupKey;
        }
    }
}