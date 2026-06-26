using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using AsyncLua.Compiling;
using AsyncLua.Interpreting;
using AsyncLua.Libraries;
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
		private static readonly AsyncLuaParser _defaultParser = new();

		private readonly AsyncLuaParser _parser;
		private readonly CompilerSettings _compilerSettings;
		private readonly InterpreterSettings _interpreterSettings;

		/// <summary>
		/// Gets the global environment table (_G) for this Lua state.
		/// </summary>
		public LuaTable Globals { get; }

		/// <summary>
		/// Gets a dictionary mapping Lua types to their corresponding default metatables.
		/// </summary>
		public ConcurrentDictionary<LuaType, LuaMetatable> TypeMetatables { get; } = [];

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaState"/> class
		/// with an empty global table.
		/// </summary>
		public LuaState()
		{
			_parser = _defaultParser;
			_compilerSettings = new CompilerSettings();
			_interpreterSettings = new InterpreterSettings();

			Globals = new LuaTable();
			// Standard Lua: _G references the global table itself.
			Globals.Set(new LuaString("_G"), Globals);
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaState"/> class
		/// with an empty global table and provided settings.
		/// </summary>
		public LuaState(AsyncLuaParser? parser, CompilerSettings? compilerSettings, InterpreterSettings? interpreterSettings)
		{
			_parser = parser ?? _defaultParser;
			_compilerSettings = compilerSettings ?? new CompilerSettings();
			_interpreterSettings = interpreterSettings ?? new InterpreterSettings();

			Globals = new LuaTable();
			// Standard Lua: _G references the global table itself.
			Globals.Set(new LuaString("_G"), Globals);
		}

		/// <summary>
		/// Loads the built-in default libraries into this Lua state:
		/// <list type="bullet">
		///   <item><description><c>BasicLibrary</c> — global functions (print, type, tostring, tonumber, error, assert, ipairs, pairs, next, select)</description></item>
		///   <item><description><c>MathLibrary</c> — math functions and constants</description></item>
		///   <item><description><c>StringLibrary</c> — string manipulation functions</description></item>
		///   <item><description><c>TableLibrary</c> — table manipulation functions</description></item>
		///   <item><description><c>CoroutineLibrary</c> — coroutine functions (coroutine.create, coroutine.resume, coroutine.yield)</description></item>
		/// </list>
		/// </summary>
		/// <returns>This Lua state instance, for fluent chaining.</returns>
		public LuaState LoadDefaultLibraries()
		{
			new BasicLibrary().Import(this);
			new MathLibrary().Import(this);
			new StringLibrary().Import(this);
			new TableLibrary().Import(this);
			new CoroutineLibrary().Import(this);

			return this;
		}

		/// <summary>
		/// Sets a Lua object in the global environment under the specified name.
		/// </summary>
		/// <param name="name">The global variable name optionally separated by dots to access nested tables or modules (e.g., "print", "web.http_get", "math.pi").</param>
		/// <param name="value">The value to set.</param>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="name"/> or <paramref name="value"/> is <see langword="null"/>.
		/// </exception>
		public void SetGlobal(string name, LuaValue value)
		{
			if (name is null)
				throw new ArgumentNullException(nameof(name));
			if (value is null)
				throw new ArgumentNullException(nameof(value));

			var table = Globals;

			int lastDotIndex = name.LastIndexOf('.');
			if (lastDotIndex != -1)
				table = table.ResolveNamespace(name.Substring(0, lastDotIndex));

			table.Set(new LuaString(name), value);
		}

		/// <summary>
		/// Retrieves a value from the global environment.
		/// </summary>
		/// <param name="name">The global variable name optionally separated by dots to access nested tables or modules (e.g., "web.http_get", "math.pi").</param>
		/// <returns>The stored value, or <c>nil</c> if not found.</returns>
		public LuaValue GetGlobal(string name)
		{
			var table = Globals;

			int lastDotIndex = name.LastIndexOf('.');
			if (lastDotIndex != -1)
				table = table.ResolveNamespace(name.Substring(0, lastDotIndex));

			return table.Get(new LuaString(name));
		}

		/// <summary>
		/// Creates a new <see cref="LuaCallingContext"/> bound to this state.
		/// </summary>
		/// <param name="environment">
		/// Optional table to use as the local environment. If <see langword="null"/>, uses the global table defined in this state.
		/// </param>
		/// <param name="settings">
		/// Optional interpreter settings to use. If <see langword="null"/>, defaults are used.
		/// </param>
		/// <returns>A new calling context.</returns>
		public LuaCallingContext CreateContext(LuaTable? environment = null, InterpreterSettings? settings = null)
		{
			return new LuaCallingContext(this, globals: environment ?? Globals, settings: settings ?? _interpreterSettings);
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
			var prototype = AsyncLuaCompiler.Compile(block, _compilerSettings, sourceName: sourceName);
			return AsyncLuaInterpreter.Call(prototype, CreateContext());
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
		public async Task<LuaTuple> ExecuteAsync(string code, string? sourceName = null)
		{
			if (code is null)
				throw new ArgumentNullException(nameof(code));

			var block = _parser.Parse(code);
			var prototype = AsyncLuaCompiler.Compile(block, _compilerSettings, sourceName: sourceName);
			return await AsyncLuaInterpreter.CallAsync(prototype, CreateContext());
		}

		/// <summary>
		/// Compiles the specified Lua code into a <see cref="CompiledLuaCode"/> object to be executed later.
		/// </summary>
		/// <param name="code">The Lua source code to compile.</param>
		/// <param name="sourceName">Optional source name for debugging (e.g., file name).</param>
		/// <returns>The compiled Lua code.</returns>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="code"/> is <see langword="null"/>.
		/// </exception>
		public CompiledLuaCode Compile(string code, string? sourceName = null)
		{
			if (code is null)
				throw new ArgumentNullException(nameof(code));

			var block = _parser.Parse(code);
			var prototype = AsyncLuaCompiler.Compile(block, _compilerSettings, sourceName: sourceName);
			return new CompiledLuaCode(CreateContext(), prototype);
		}

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
			var prototype = Compiling.AsyncLuaCompiler.Compile(block, _compilerSettings, sourceName: sourceName);
			return prototype.Disassemble();
		}
	}
}
