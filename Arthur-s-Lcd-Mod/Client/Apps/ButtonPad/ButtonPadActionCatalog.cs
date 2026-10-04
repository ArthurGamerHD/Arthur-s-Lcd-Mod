using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal;
using LcdMod.Client.Terminal.Models;
using LcdMod.Client.Terminal.Models.Actions;
using LcdMod.Client.Terminal.Models.Property;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;
using IMyBlockGroup = Sandbox.ModAPI.Ingame.IMyBlockGroup;
using IMyIngameTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace LcdMod.Client.Apps.ButtonPad
{
    /// <summary>Targets, compatible actions and action parameters available to a button pad on one grid.</summary>
    internal sealed class ButtonPadActionCatalog
    {
        const string MISSING_ICON = "MissingIcon";

        readonly GridLogic _grid;
        readonly List<IMyBlockGroup> _groups = new List<IMyBlockGroup>();
        readonly List<IMyIngameTerminalBlock> _groupBlocks = new List<IMyIngameTerminalBlock>();

        public ButtonPadActionCatalog(GridLogic grid)
        {
            _grid = grid;
        }

        public List<PickActionTargetResult> Targets(PickActionTargetKind kind)
        {
            var items = new List<PickActionTargetResult>();
            if (_grid == null)
                return items;
            switch (kind)
            {
                case PickActionTargetKind.Block:
                    foreach (var block in _grid.Blocks.TerminalBlocks)
                        if (block != null)
                            items.Add(new PickActionTargetResult
                            {
                                Kind = kind,
                                Id = block.EntityId.ToString(FormatingHelper.Culture),
                                DisplayName = BlockName(block),
                                SpriteName = BlockSprite(block),
                                TypeName = block.GetType().FullName
                            });
                    break;
                case PickActionTargetKind.Group:
                    foreach (var group in Groups())
                        if (group != null && !string.IsNullOrWhiteSpace(group.Name))
                            items.Add(new PickActionTargetResult
                            {
                                Kind = kind, Id = group.Name, DisplayName = group.Name, SpriteName = "SquareSimple", TypeName = "Group"
                            });
                    break;
                case PickActionTargetKind.BlockType:
                    foreach (var type in ActionHelper.Types)
                        if (type != null)
                            items.Add(new PickActionTargetResult
                            {
                                Kind = kind,
                                Id = type.FullName ?? type.Name,
                                DisplayName = TypeName(type),
                                SpriteName = TypeSprite(type),
                                TypeName = type.FullName ?? type.Name
                            });
                    break;
                case PickActionTargetKind.BlockSubtype:
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var block in _grid.Blocks.TerminalBlocks)
                    {
                        var subtype = block != null ? block.BlockDefinition.SubtypeName : null;
                        if (string.IsNullOrWhiteSpace(subtype) || !seen.Add(subtype))
                            continue;
                        items.Add(new PickActionTargetResult
                        {
                            Kind = kind, Id = subtype, DisplayName = subtype, SpriteName = SubtypeSprite(subtype), TypeName = subtype
                        });
                    }
                    break;
            }
            items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
            return items;
        }

        public List<ButtonPanelActionSettings> Actions(ButtonPanelTargetSettings target)
        {
            var actions = new List<ButtonPanelActionSettings>();
            if (target == null)
                return actions;
            foreach (var entry in ActionHelper.CustomActions)
            {
                var action = entry.Value;
                if (action == null || string.IsNullOrEmpty(action.BaseId) || RepresentativeBlock(target, action) == null)
                    continue;
                actions.Add(new ButtonPanelActionSettings
                {
                    BaseId = action.BaseId,
                    DisplayName = string.IsNullOrWhiteSpace(action.Name) ? action.BaseId : action.Name,
                    ActionTypeName = action.GetType().FullName,
                    SpriteName = ActionSprite(action)
                });
            }
            actions.Sort((a, b) => string.Compare(a.DisplayName ?? a.BaseId, b.DisplayName ?? b.BaseId,
                StringComparison.CurrentCultureIgnoreCase));
            return actions;
        }

        /// <summary>The parameter an action needs, read from a block it applies to; null when it needs none.</summary>
        public ActionParameterInfo Parameter(ButtonPanelTargetSettings target, ButtonPanelActionSettings action)
        {
            ICustomAction custom;
            if (action == null || string.IsNullOrEmpty(action.BaseId) ||
                !ActionHelper.CustomActions.TryGetValue(action.BaseId, out custom) || custom == null)
                return null;
            var block = RepresentativeBlock(target, custom);
            if (custom is IncreaseDecreaseAction)
                return new ActionParameterInfo { TypeName = ActionParameterInfo.INCREASE_DECREASE };
            if (custom is OnOffAction || custom is PropertyCustomAction<bool>)
                return new ActionParameterInfo { TypeName = ActionParameterInfo.BOOLEAN };

            var text = custom as PropertyCustomAction<string>;
            if (text != null)
                return new ActionParameterInfo { TypeName = ActionParameterInfo.STRING, Text = Value(text, block, Default(text, block, string.Empty)) };
            var builder = custom as PropertyCustomAction<StringBuilder>;
            if (builder != null)
                return new ActionParameterInfo
                {
                    TypeName = ActionParameterInfo.STRING_BUILDER,
                    Text = Value(builder, block, Default(builder, block, new StringBuilder())).ToString()
                };
            var integer = custom as PropertyCustomAction<long>;
            if (integer != null)
                return new ActionParameterInfo
                {
                    TypeName = ActionParameterInfo.INT64,
                    Minimum = Minimum(integer, block, long.MinValue),
                    Maximum = Maximum(integer, block, long.MaxValue),
                    Number = Value(integer, block, Default(integer, block, 0L))
                };
            var single = custom as PropertyCustomAction<float>;
            if (single != null)
                return new ActionParameterInfo
                {
                    TypeName = ActionParameterInfo.SINGLE,
                    Minimum = Minimum(single, block, float.MinValue),
                    Maximum = Maximum(single, block, float.MaxValue),
                    Number = Value(single, block, Default(single, block, 0f))
                };
            var color = custom as PropertyCustomAction<Color>;
            if (color != null)
                return new ActionParameterInfo { TypeName = ActionParameterInfo.COLOR, ColorValue = Value(color, block, Default(color, block, Color.White)) };
            return null;
        }

        IMyIngameTerminalBlock RepresentativeBlock(ButtonPanelTargetSettings target, ICustomAction action)
        {
            if (target == null || action == null || _grid == null)
                return null;
            switch ((PickActionTargetKind)target.Kind)
            {
                case PickActionTargetKind.Block:
                    long id;
                    if (!long.TryParse(target.Id, out id))
                        return null;
                    foreach (var block in _grid.Blocks.TerminalBlocks)
                        if (block != null && block.EntityId == id)
                            return Compatible(action, block) ? block : null;
                    return null;
                case PickActionTargetKind.Group:
                    foreach (var group in Groups())
                    {
                        if (group == null || !string.Equals(group.Name, target.Id, StringComparison.OrdinalIgnoreCase))
                            continue;
                        _groupBlocks.Clear();
                        group.GetBlocks(_groupBlocks);
                        foreach (var block in _groupBlocks)
                            if (Compatible(action, block))
                                return block;
                    }
                    return null;
                case PickActionTargetKind.BlockType:
                    var type = RegisteredType(target.TypeName ?? target.Id);
                    if (type == null || !TypeCompatible(action, type))
                        return null;
                    foreach (var block in _grid.Blocks.TerminalBlocks)
                        if (block != null && TypeMatches(type, block.GetType()) && Compatible(action, block))
                            return block;
                    return null;
                case PickActionTargetKind.BlockSubtype:
                    foreach (var block in _grid.Blocks.TerminalBlocks)
                    {
                        var cube = block as MyCubeBlock;
                        if (cube != null && cube.BlockDefinition != null &&
                            string.Equals(cube.BlockDefinition.Id.SubtypeName, target.Id, StringComparison.OrdinalIgnoreCase) &&
                            Compatible(action, block))
                            return block;
                    }
                    return null;
                default:
                    return null;
            }
        }

        List<IMyBlockGroup> Groups()
        {
            _groups.Clear();
            if (_grid == null || _grid.Grid == null)
                return _groups;
            var terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(_grid.Grid);
            if (terminal != null)
                terminal.GetBlockGroups(_groups);
            return _groups;
        }

        static bool Compatible(ICustomAction action, IMyIngameTerminalBlock block)
        {
            return block != null && TypeCompatible(action, block.GetType()) && action.Enabled(block);
        }

        static bool TypeCompatible(ICustomAction action, Type target)
        {
            if (action.Types == null || target == null)
                return false;
            foreach (var type in action.Types)
                if (type != null && TypeMatches(type, target))
                    return true;
            return false;
        }

        static bool TypeMatches(Type expected, Type actual)
        {
            return string.Equals(expected.FullName, actual.FullName, StringComparison.Ordinal) ||
                   MyAPIGateway.Reflection.IsAssignableFrom(expected, actual) ||
                   MyAPIGateway.Reflection.IsAssignableFrom(actual, expected);
        }

        static Type RegisteredType(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            foreach (var type in ActionHelper.Types)
                if (type != null && (string.Equals(type.FullName, name, StringComparison.Ordinal) ||
                                     string.Equals(type.Name, name, StringComparison.Ordinal)))
                    return type;
            return null;
        }

        static string ActionSprite(ICustomAction action)
        {
            if (action is PropertyCustomAction<bool> || action is OnOffAction)
                return "BooleanInput";
            if (action is PropertyCustomAction<string> || action is PropertyCustomAction<StringBuilder>)
                return "StringInput";
            if (action is PropertyCustomAction<Color>)
                return "ColorInput";
            if (action is PropertyCustomAction<byte> || action is PropertyCustomAction<sbyte> ||
                action is PropertyCustomAction<short> || action is PropertyCustomAction<ushort> ||
                action is PropertyCustomAction<int> || action is PropertyCustomAction<uint> ||
                action is PropertyCustomAction<long> || action is PropertyCustomAction<ulong> ||
                action is PropertyCustomAction<float> || action is PropertyCustomAction<double> ||
                action is PropertyCustomAction<decimal>)
                return "NumberInput";
            return MISSING_ICON;
        }

        static string BlockName(IMyTerminalBlock block)
        {
            if (!string.IsNullOrWhiteSpace(block.CustomName))
                return block.CustomName;
            if (!string.IsNullOrWhiteSpace(block.DisplayNameText))
                return block.DisplayNameText;
            return block.EntityId.ToString(FormatingHelper.Culture);
        }

        static string BlockSprite(IMyTerminalBlock block)
        {
            try
            {
                var cube = block as MyCubeBlock;
                if (cube != null && cube.BlockDefinition != null)
                    return TextureHelper.GetOrAddTextureForBlock(cube.BlockDefinition);
            }
            catch (Exception)
            {
                return MISSING_ICON;
            }
            return MISSING_ICON;
        }

        string TypeSprite(Type type)
        {
            foreach (var block in _grid.Blocks.TerminalBlocks)
                if (block != null && MyAPIGateway.Reflection.IsAssignableFrom(type, block.GetType()))
                    return BlockSprite(block);
            return MISSING_ICON;
        }

        static string SubtypeSprite(string subtype)
        {
            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var block = definition as MyCubeBlockDefinition;
                if (block != null && string.Equals(block.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    return TextureHelper.GetOrAddTextureForBlock(block);
            }
            string texture;
            return TextureHelper.TryGetOrAddTextureForBlockName(subtype, out texture) ? texture : "Danger";
        }

        static string TypeName(Type type)
        {
            var name = type.Name;
            if (name.StartsWith("My", StringComparison.Ordinal) && name.Length > 2)
                name = name.Substring(2);
            if (name.StartsWith("IMy", StringComparison.Ordinal) && name.Length > 3)
                name = name.Substring(3);
            return name;
        }

        static T Minimum<T>(PropertyCustomAction<T> action, IMyIngameTerminalBlock block, T fallback)
        {
            return Read(() => action.Property.GetMinimum(block), action, block, fallback);
        }

        static T Maximum<T>(PropertyCustomAction<T> action, IMyIngameTerminalBlock block, T fallback)
        {
            return Read(() => action.Property.GetMaximum(block), action, block, fallback);
        }

        static T Value<T>(PropertyCustomAction<T> action, IMyIngameTerminalBlock block, T fallback)
        {
            return Read(() => action.Property.GetValue(block), action, block, fallback);
        }

        static T Default<T>(PropertyCustomAction<T> action, IMyIngameTerminalBlock block, T fallback)
        {
            return Read(() => action.Property.GetDefaultValue(block), action, block, fallback);
        }

        static T Read<T>(Func<T> read, PropertyCustomAction<T> action, IMyIngameTerminalBlock block, T fallback)
        {
            if (action == null || action.Property == null || block == null)
                return fallback;
            try
            {
                return read();
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
