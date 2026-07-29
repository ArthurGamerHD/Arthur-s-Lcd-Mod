using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Terminal.Models;
using LcdMod.Client.Terminal.Models.Actions;
using LcdMod.Client.Terminal.Models.Property;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Utils;
using VRageMath;
using IMyFunctionalBlock = Sandbox.ModAPI.IMyFunctionalBlock;

namespace LcdMod.Client.Terminal
{

    public static class ActionHelper
    {
#if EXPERIMENTAL
        public static readonly SortedDictionary<string, ITerminalAction> TerminalActions =
            new SortedDictionary<string, ITerminalAction>();

        public static readonly SortedDictionary<string, ITerminalProperty> TerminalProperties =
            new SortedDictionary<string, ITerminalProperty>();

        public static readonly SortedDictionary<string, ICustomAction> CustomActions =
            new SortedDictionary<string, ICustomAction>();

        static readonly Dictionary<string, ITerminalAction> Increase = new Dictionary<string, ITerminalAction>();
        static readonly Dictionary<string, ITerminalAction> Decrease = new Dictionary<string, ITerminalAction>();
        static readonly Dictionary<string, ITerminalAction> On = new Dictionary<string, ITerminalAction>();
        static readonly Dictionary<string, ITerminalAction> Off = new Dictionary<string, ITerminalAction>();

        public static HashSet<Type> Types { get; } = new HashSet<Type>();

        public static void RegisterNewBlock(IMyFunctionalBlock myFunctionalBlock)
        {
            Increase.Clear();
            Decrease.Clear();
            On.Clear();
            Off.Clear();

            var type = myFunctionalBlock.GetType();

            if (myFunctionalBlock is IMyTextPanel)
            {
                
            }
            
            List<ITerminalAction> terminalActions = new List<ITerminalAction>();
            List<ITerminalProperty> terminalProperties = new List<ITerminalProperty>();
            myFunctionalBlock.GetActions(terminalActions);
            myFunctionalBlock.GetProperties(terminalProperties);

            for (var index = 0; index < terminalActions.Count;)
            {
                var action = terminalActions[index];
                if (action.Id.StartsWith("Increase")) Increase[action.Id.Substring(8)] = action;
                else if (action.Id.StartsWith("Decrease")) Decrease[action.Id.Substring(8)] = action;
                else if (action.Id.EndsWith("_On")) On[action.Id.Substring(0, action.Id.Length - 3)] = action;
                else if (action.Id.EndsWith("_Off")) Off[action.Id.Substring(0, action.Id.Length - 4)] = action;
                else
                {
                    index++;
                    continue;
                }

                terminalActions.RemoveAt(index);
            }

            
            foreach (var action in terminalActions)
            {
                ICustomAction customAction;
                if (!CustomActions.TryGetValue(action.Id, out customAction))
                {
                    ITerminalAction offValue;
                    ITerminalAction onValue;
                    if (On.TryGetValue(action.Id, out onValue) && Off.TryGetValue(action.Id, out offValue))
                    {
                        CustomActions[action.Id] = customAction = new OnOffAction()
                        {
                            On = onValue,
                            Off = offValue,
                            Action = action,
                            BaseId = action.Id,
                            Name = action.Name?.ToString()
                        };
                    }
                    else
                    {
                        CustomActions[action.Id] = customAction = new CustomAction()
                        {
                            Action = action,
                            BaseId = action.Id,
                            Name = action.Name?.ToString()
                        };
                    }
                }
                
                customAction?.Types.Add(type);
            }
            
            foreach (var action in Increase)
            {
                ICustomAction customAction;
                if (!CustomActions.TryGetValue(action.Key, out customAction))
                {
                    ITerminalAction value;
                    if (Decrease.TryGetValue(action.Key, out value))
                    {
                        CustomActions[action.Key] = customAction = new IncreaseDecreaseAction()
                        {
                            Increase = action.Value,
                            Decrease = value,
                            BaseId = action.Key,
                            Name = action.Value.Name?.ToString()
                        };
                    }
                }

                customAction?.Types.Add(type);
            }


            foreach (var property in terminalProperties)
            {
                ICustomAction customAction;
                if (!CustomActions.TryGetValue(property.Id, out customAction))
                {
                    try
                    {
                        switch (property.TypeName)
                        {
                            case "Boolean":
                            {
                                var asBool = property.As<bool>();
                                customAction = new BooleanProperty()
                                {
                                    Property = asBool,
                                    BaseId = property.Id
                                };
                                break;
                            }
                            case "String":
                            {
                                var asString = property.As<string>();
                                customAction = new StringProperty()
                                {
                                    Property = asString,
                                    BaseId = property.Id
                                };
                                break;
                            }
                            case "Int64":
                            {
                                var asInt64 = property.As<long>();
                                customAction = new Int64Property()
                                {
                                    Property = asInt64,
                                    BaseId = property.Id
                                };
                                break;
                            }
                            case "Single":
                            {
                                var asFloat = property.As<float>();
                                customAction = new FloatProperty()
                                {
                                    Property = asFloat,
                                    BaseId = property.Id
                                };
                                break;
                            }
                            case "Color":
                            {
                                var asColor = property.As<Color>();
                                customAction = new ColorProperty()
                                {
                                    Property = asColor,
                                    BaseId = property.Id
                                };
                                break;
                            }
                            case "StringBuilder":
                            {
                                var asStringbuilder = property.As<StringBuilder>();
                                customAction = new StringBuilderProperty()
                                {
                                    Property = asStringbuilder,
                                    BaseId = property.Id
                                };
                                break;
                            }
                            default:
                            {
                                LogHelper.Log(MyLogSeverity.Warning, "Not implemented: " + property.TypeName + $" for property {property.Id}");
                                continue;
                            }
                        }

                        customAction.Name = property.Id;
                        CustomActions[property.Id] = customAction;
                    }
                    catch (Exception e)
                    {
                        LogHelper.Log(MyLogSeverity.Error, $"error on property: {property.Id}" + e);
                        throw;
                    }
                }

                customAction?.Types.Add(type);
            }

            foreach (var property in terminalProperties)
                TerminalProperties[property.Id] = property;

            foreach (var action in terminalActions)
                TerminalActions[action.Id] = action;

            Types.Add(type);

        }
#endif
    }
}