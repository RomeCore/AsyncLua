using System;
using System.Threading.Tasks;
using AsyncLua.Compiling;
using AsyncLua.Parsing;
using AsyncLua.Values;
using RCParsing;

namespace AsyncLua
{
	/// <summary>
	/// Represents the main Lua runtime state, holding the global environment table
	/// and serving as the root for all execution within a single Lua universe.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each <see cref="LuaState"/> is an isolated Lua universe with its own global
	/// table and registered functions. Multiple states can coexist and are
	/// independently thread-safe (no shared mutable state between states).
	/// </para>
	/// </remarks>
	public class LuaState
	{
		private readonly AsyncLuaParser _parser;
		private readonly CompilerSettings _compilerSettings;

		/// <summary>
		/// Gets the global environment table (_G) for this Lua state.
		/// </summary>
		public LuaTable Globals { get; }

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaState"/> class
		/// with an empty global table.
		/// </summary>
		public LuaState()
		{
			_parser = new AsyncLuaParser();
			_compilerSettings = new CompilerSettings();

			Globals = new LuaTable();
			// Standard Lua: _G references the global table itself.
			Globals.Set(new LuaString("_G"), Globals);
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaState"/> class
		/// with an empty global table and provided settings.
		/// </summary>
		public LuaState(AsyncLuaParser? parser, CompilerSettings? compilerSettings)
		{
			_parser = parser ?? new AsyncLuaParser();
			_compilerSettings = compilerSettings ?? new CompilerSettings();

			Globals = new LuaTable();
			// Standard Lua: _G references the global table itself.
			Globals.Set(new LuaString("_G"), Globals);
		}

		/// <summary>
		/// Registers a Lua function in the global environment under the specified name.
		/// </summary>
		/// <param name="name">The global variable name (e.g., "print", "http_get").</param>
		/// <param name="function">The function to register.</param>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="name"/> or <paramref name="function"/> is <see langword="null"/>.
		/// </exception>
		public void Register(string name, LuaFunction function)
		{
			if (name is null)
				throw new ArgumentNullException(nameof(name));
			if (function is null)
				throw new ArgumentNullException(nameof(function));

			Globals.Set(new LuaString(name), function);
		}

		/// <summary>
		/// Retrieves a value from the global environment.
		/// </summary>
		/// <param name="name">The global variable name.</param>
		/// <returns>The stored value, or <c>nil</c> if not found.</returns>
		public LuaValue GetGlobal(string name)
		{
			return Globals.Get(new LuaString(name));
		}

		/// <summary>
		/// Creates a new <see cref="LuaCallingContext"/> bound to this state.
		/// </summary>
		/// <param name="settings">
		/// Optional interpreter settings to use. If <see langword="null"/>, defaults are used.
		/// </param>
		/// <returns>A new calling context.</returns>
		public LuaCallingContext CreateContext(Interpreting.InterpreterSettings? settings = null)
		{
			return new LuaCallingContext(this, settings: settings);
		}

		/// <summary>
		/// Parses, compiles and executes the specified Lua code synchronously.
		/// </summary>
		/// <param name="code">The Lua source code to execute.</param>
		/// <param name="sourceName">Optional source name for debugging (e.g., file name).</param>
		/// <returns>A <see cref="LuaTuple"/> containing all return values from the chunk.</returns>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="code"/> is <see langword="null"/>.
		/// </exception>
		public LuaTuple Execute(string code, string? sourceName = null)
		{
			if (code is null)
				throw new ArgumentNullException(nameof(code));

			var block = _parser.Parse(code);
			var prototype = Compiling.AsyncLuaCompiler.Compile(block, sourceName: sourceName);
			return Interpreting.AsyncLuaInterpreter.Call(prototype, CreateContext());
		}

		/// <summary>
		/// Parses, compiles and executes the specified Lua code asynchronously.
		/// Required for code that uses <c>async</c>/<c>await</c>.
		/// </summary>
		/// <param name="code">The Lua source code to execute.</param>
		/// <param name="sourceName">Optional source name for debugging (e.g., file name).</param>
		/// <returns>
		/// A task that resolves to a <see cref="LuaTuple"/> containing all return values from the chunk.
		/// </returns>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="code"/> is <see langword="null"/>.
		/// </exception>
		/// <summary>
		/// Parses and compiles the specified Lua code and returns the disassembled bytecode
		/// as a human-readable string. Does not execute the code.
		/// </summary>
		/// <param name="code">The Lua source code to compile.</param>
		/// <param name="sourceName">Optional source name for debugging (e.g., file name).</param>
		/// <returns>A multi-line string showing the compiled bytecode.</returns>
		public string DumpBytecode(string code, string? sourceName = null)
		{
			if (code is null)
				throw new ArgumentNullException(nameof(code));

			var block = _parser.Parse(code);
			var prototype = Compiling.AsyncLuaCompiler.Compile(block, sourceName: sourceName);
			return prototype.Disassemble();
		}
		public async Task<LuaTuple> ExecuteAsync(string code, string? sourceName = null)
		{
			if (code is null)
				throw new ArgumentNullException(nameof(code));

			var block = _parser.Parse(code);
			var prototype = Compiling.AsyncLuaCompiler.Compile(block, sourceName: sourceName);
			return await Interpreting.AsyncLuaInterpreter.CallAsync(prototype, CreateContext());
		}
	}
}
