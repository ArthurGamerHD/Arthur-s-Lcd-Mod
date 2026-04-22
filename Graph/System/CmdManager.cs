// using CmdManager from ZachHembree:
// https://raw.githubusercontent.com/ZachHembree/RichHudFramework.Master/refs/heads/master/RichHudMaster/Data/Scripts/RichHudFramework/Shared/UI/CmdManager.cs
// Slight modified to remove dependency on Rich Hud Framework

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Graph.Helpers;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;

namespace Graph
{
	
	public interface IIndexedCollection<out T>
	{
		/// <summary>
		/// Retrieves the element at the specified index.
		/// </summary>
		T this[int index] { get; }

		/// <summary>
		/// Number of elements in the collection.
		/// </summary>
		int Count { get; }
	}
	
	/// <summary>
	/// Represents a collection of <see cref="IChatCommand"/>s that share a common prefix (e.g., "/modname").
	/// </summary>
	public interface ICommandGroup : IIndexedCollection<IChatCommand>
	{

		/// <summary>
		/// The chat prefix used to trigger commands in this group (e.g., "/modName").
		/// </summary>
		List<string> Aliases { get; }
		
		/// <summary>
		/// Retrieves the command with the specified name from the group.
		/// </summary>
		IChatCommand this[string name] { get; }

		/// <summary>
		/// The chat prefix used to trigger commands in this group (e.g., "/modName").
		/// </summary>
		string Prefix { get; }

		/// <summary>
		/// Attempts to register a new <see cref="IChatCommand"/> to this group. 
		/// Command names must be unique within the group.
		/// </summary>
		/// <param name="name">The name of the command (case-insensitive).</param>
		/// <param name="callback">The action to execute when the command is invoked.</param>
		/// <param name="argsRequired">The minimum number of arguments required for the command.</param>
		/// <returns>True if the command was successfully added; otherwise, false.</returns>
		bool TryAdd(string name, Action<string[]> callback = null, int argsRequired = 0);

		/// <summary>
		/// Registers a batch of new <see cref="IChatCommand"/>s defined in the provided initializer.
		/// </summary>
		/// <param name="newCommands">A collection of command definitions to add.</param>
		void AddCommands(CmdGroupInitializer newCommands);

		/// <summary>
		/// Add Extra prefixes aliases
		/// </summary>
		/// <param name="alias"></param>
		void AddAlias(string alias);
	}

	/// <summary>
	/// Represents a single chat command registered within a <see cref="ICommandGroup"/>.
	/// </summary>
	public interface IChatCommand
	{
		/// <summary>
		/// Event raised when the command is successfully invoked by a user. 
		/// Passes the array of arguments parsed from the chat message.
		/// </summary>
		event Action<string[]> CommandInvoked;

		/// <summary>
		/// The name/keyword of the command (e.g., "help" in "/prefix help").
		/// </summary>
		string CmdName { get; }

		/// <summary>
		/// The minimum number of arguments required to execute this command.
		/// </summary>
		int ArgsRequired { get; }
	}

	/// <summary>
	/// A helper container used to define a list of commands and their callbacks 
	/// before registering them to a <see cref="ICommandGroup"/>.
	/// </summary>
	public class CmdGroupInitializer : IReadOnlyList<MyTuple<string, Action<string[]>, int>>
	{
		public MyTuple<string, Action<string[]>, int> this[int index] => _data[index];
		public int Count => _data.Count;

		private readonly List<MyTuple<string, Action<string[]>, int>> _data;

		public CmdGroupInitializer(int capacity = 0)
		{
			_data = new List<MyTuple<string, Action<string[]>, int>>(capacity);
		}

		/// <summary>
		/// Adds a command definition to the initializer.
		/// </summary>
		/// <param name="cmdName">The name of the command.</param>
		/// <param name="callback">The delegate to invoke when the command is run.</param>
		/// <param name="argsRequrired">The minimum arguments required.</param>
		public void Add(string cmdName, Action<string[]> callback = null, int argsRequrired = 0)
		{
			_data.Add(new MyTuple<string, Action<string[]>, int>(cmdName, callback, argsRequrired));
		}

		public IEnumerator<MyTuple<string, Action<string[]>, int>> GetEnumerator() =>
			_data.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() =>
			_data.GetEnumerator();
	}

	/// <summary>
	/// Singleton Session Component responsible for managing chat command registration, 
	/// parsing incoming chat messages, and executing matching commands.
	/// </summary>
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate, 0)]
	public sealed class CmdManager : MySessionComponentBase
	{
		/// <summary>
		/// A read-only list of all currently registered command groups.
		/// </summary>
		public static IReadOnlyList<ICommandGroup> CommandGroups => _instance?._commandGroups;

		private static CmdManager _instance;
		private readonly Regex _cmdParser;
		private readonly List<CommandGroup> _commandGroups;
		private readonly Dictionary<string, Command> _commands;

		/// <summary>
		/// Initializes the <see cref="CmdManager"/> singleton. 
		/// <para>NOTE: This is called automatically by the game engine via <see cref="MySessionComponentDescriptor"/>.</para>
		/// </summary>
		/// <exception cref="Exception">Thrown if an instance of CmdManager already exists.</exception>
		/// <exclude/>
		public CmdManager()
		{
			if (_instance == null)
				_instance = this;
			else
				throw new Exception("Only one instance of CmdManager can exist at any given time.");

			_commandGroups = new List<CommandGroup>();
			_commands = new Dictionary<string, Command>();
			// Regex parses arguments separated by whitespace/comma/semicolon/pipe, handling quoted strings.
			// Unquoted tokens accept any non-separator/non-quote chars (e.g. #ff0).
			_cmdParser = new Regex(@"((\s*?[\s,;|]\s*?)(([^,\s;|\""]+)|("".+"")))+");
			//RichHudCore.LateMessageEntered += MessageHandler;
			
		}

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			base.Init(sessionComponent);
			MyAPIGateway.Utilities.MessageEnteredSender += MessageHandler;
		}

		/// <summary>
		/// Unloads the component data and unregisters the chat message handler.
		/// </summary>
		/// <exclude/>
		protected override void UnloadData()
		{
			// RichHudCore.LateMessageEntered -= MessageHandler; 
			MyAPIGateway.Utilities.MessageEnteredSender -= MessageHandler;
			_instance = null;
		}

		/// <summary>
		/// Retrieves an existing <see cref="ICommandGroup"/> by its prefix, or creates and registers 
		/// a new one if it does not exist.
		/// </summary>
		/// <param name="prefix">The command prefix (e.g., "/mycmd"). Case-insensitive.</param>
		/// <param name="groupInitializer">Optional set of commands to register immediately if creating a new group.</param>
		/// <returns>The requested <see cref="ICommandGroup"/>.</returns>
		public static ICommandGroup GetOrCreateGroup(string prefix, CmdGroupInitializer groupInitializer = null)
		{
			prefix = prefix.ToLower();
			CommandGroup group = _instance._commandGroups.Find(x => x.Prefix == prefix);

			if (group == null)
			{
				group = new CommandGroup(prefix);
				_instance._commandGroups.Add(group);
				group.AddCommands(groupInitializer);
			}

			return group;
		}

		/// <summary>
		/// Intercepts chat messages to check for registered command prefixes.
		/// If a match is found, the message is suppressed from chat and processed as a command.
		/// </summary>
		private void MessageHandler(ulong sender, string message, ref bool sendToOthers)
		{
			message = message.ToLower();
			
			CommandGroup group = _commandGroups.Find(x => message.StartsWith(x.Prefix) || x.Aliases.Contains(message.Split(' ')[0]));
			
			if (group != null)
			{
				sendToOthers = false;
				try
				{
					group.TryRunCommand(message);
				}
				catch (Exception e)
				{
					ErrorHandlerHelper.LogError(e, this);
					throw;
				}
				
				//ExceptionHandler.Run(() => group.TryRunCommand(message));
			}
		}

		/// <summary>
		/// Parses a raw command string into a list of arguments using Regex.
		/// Handles quoted strings as single arguments.
		/// </summary>
		/// <param name="cmd">The raw command string.</param>
		/// <param name="matches">The output array of parsed arguments.</param>
		/// <returns>True if the command contained valid matches; otherwise, false.</returns>
		private static bool TryParseCommand(string cmd, out string[] matches)
		{
			Match match = _instance._cmdParser.Match(cmd);
			CaptureCollection captures = match.Groups[3].Captures;
			matches = new string[captures.Count];

			for (int n = 0; n < captures.Count; n++)
			{
				matches[n] = captures[n].Value;

				// Strip quotes from arguments if present
				if (matches[n][0] == '"' && matches[n][matches[n].Length - 1] == '"')
					matches[n] = matches[n].Substring(1, matches[n].Length - 2);
			}

			return matches.Length > 0;
		}

		private class CommandGroup : ICommandGroup
		{
			public IChatCommand this[int index] => _commands[index];
			public List<string> Aliases { get; } = new List<string>();
			public IChatCommand this[string name] => _commands.Find(x => x.CmdName.ToLower() == name.ToLower());
			public int Count => _commands.Count;
			public ICommandGroup Commands => this;
			public string Prefix { get; }

			private readonly List<Command> _commands;

			public CommandGroup(string prefix)
			{
				_commands = new List<Command>();
				this.Prefix = prefix;
			}

			public bool TryRunCommand(string message)
			{
				bool cmdFound = false, success = false;
				string[] matches;

				if (TryParseCommand(message, out matches))
				{
					// First match is the command name (after the prefix)
					string cmdName = matches[0];
					Command command;

					// Locate command using composed key "prefix.cmdName"
					if (_instance._commands.TryGetValue($"{Prefix}.{cmdName}", out command))
					{
						//string[] args = matches.GetSubarray(1);
						string[] args = GetSubarray(matches, 1);
						cmdFound = true;

						if (args.Length >= command.ArgsRequired)
						{
							command.InvokeCommand(args);
							success = true;
						}
					}
				}

				if (!cmdFound)
				{
					var sb = new StringBuilder("Available commands:");

					foreach (var command in _instance._commands)
						sb.Append('"').Append(command.Key.Replace('.', ' ')).Append("\", ");

					//ExceptionHandler.SendChatMessage("Command not recognised.");
					MyAPIGateway.Utilities.ShowMessage("LuckBox", $"Command not recognised.\n{sb.ToString().TrimEnd(',', ' ')}");
				}


				return success;
			}

			public bool TryAdd(string name, Action<string[]> callback = null, int argsRequired = 0)
			{
				name = name.ToLower();
				string key = $"{Prefix}.{name}";

				if (_instance != null && !_instance._commands.ContainsKey(key))
				{
					Command command = new Command(name, argsRequired);
					_commands.Add(command);
					_instance._commands.Add(key, command);

					if (callback != null)
						command.CommandInvoked += callback;

					foreach (var extraPrefix in Aliases)
					{
						_instance._commands.Add( $"{extraPrefix}.{command.CmdName}", command);
					}
					
					return true;
				}
				else
					return false;
			}

			public void AddCommands(CmdGroupInitializer newCommands)
			{
				for (int n = 0; n < newCommands.Count; n++)
				{
					var cmd = newCommands[n];
					TryAdd(cmd.Item1, cmd.Item2, cmd.Item3);
				}
			}

			public void AddAlias(string alias)
			{
				alias = alias.ToLower();
				Aliases.Add(alias);
				foreach (var command in _commands)
				{
					string key = $"{alias}.{command.CmdName}";
					_instance._commands[key] = command;
				}
			}

			public static T[] GetSubarray<T>(T[] arr, int start)
			{
				var result = new T[arr.Length - start];
				Array.Copy(arr, start, result, 0, result.Length);
				return result;
			}
		}

		private class Command : IChatCommand
		{
			public event Action<string[]> CommandInvoked;
			public string CmdName { get; }
			public int ArgsRequired { get; }

			public Command(string cmdName, int argsRequired)
			{
				CmdName = cmdName.ToLower();
				ArgsRequired = argsRequired;
			}

			public void InvokeCommand(string[] args) =>
				CommandInvoked?.Invoke(args);
		}
	}
}
