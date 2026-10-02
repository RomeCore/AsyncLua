using System;
using System.Threading;
using System.Threading.Tasks;
using AsyncLua.Compiling;
using AsyncLua.Interpreting;
using AsyncLua.Values;

namespace AsyncLua
{
	/// <summary>
	/// Provides the execution context for a Lua function call. Contains references
	/// to the owning <see cref="LuaState"/> and the current global environment table.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A <see cref="LuaCallingContext"/> is passed to every Lua function invocation,
	/// giving C# callbacks access to the Lua runtime. It is designed to be lightweight
	/// and safe to capture in asynchronous continuations.
	/// </para>
	/// </remarks>
	public class LuaCallingContext
	{
		/// <summary>
		/// Gets the <see cref="LuaState"/> that owns this context.
		/// </summary>
		public LuaState State { get; }

		/// <summary>
		/// Gets the interpreter settings for this execution context.
		/// </summary>
		public InterpreterSettings Settings { get; }

		/// <summary>
		/// Gets the global environment table for the current call.
		/// This is typically <see cref="LuaState.Globals"/> but may be overridden
		/// per-closure (_ENV).
		/// </summary>
		public LuaTable Globals { get; set; }

		/// <summary>
		/// Gets or sets a callback function that will be invoked when <c>print(...)</c> is called.
		/// </summary>
		public Action<string>? Print { get; set; }

		/// <summary>
		/// Gets or sets a callback function that will be invoked when <c>warn(...)</c> is called.
		/// </summary>
		public Action<string>? Warn { get; set; }

		/// <summary>
		/// Gets or sets a cancellation token that can be used to cancel execution.
		/// </summary>
		public CancellationToken CancellationToken { get; set; }

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaCallingContext"/> class.
		/// </summary>
		/// <param name="state">The owning Lua state.</param>
		/// <param name="globals">
		/// The global environment table for this context. If <see langword="null"/>,
		/// <see cref="LuaState.Globals"/> is used.
		/// </param>
		/// <param name="settings">
		/// Interpreter settings to use. If <see langword="null"/>, a default instance is created.
		/// </param>
		internal LuaCallingContext(
			LuaState state,
			LuaTable? globals = null,
			InterpreterSettings? settings = null)
		{
			State = state ?? throw new ArgumentNullException(nameof(state));
			Settings = settings ?? new InterpreterSettings();
			Globals = globals ?? state.Globals;
		}

		/// <summary>
		/// Parses and compiles the specified Lua code into a <see cref="CompiledLuaCode"/> bound to
		/// this context, so that it can be executed later within this context's environment, settings
		/// and print/warn sinks.
		/// </summary>
		/// <param name="code">The Lua source code to compile.</param>
		/// <param name="sourceName">Optional source name for debugging (e.g., a file name).</param>
		/// <returns>The compiled code, bound to this context.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="code"/> is <see langword="null"/>.</exception>
		public CompiledLuaCode Compile(string code, string? sourceName = null)
		{
			if (code is null)
				throw new ArgumentNullException(nameof(code));

			var block = State.Parser.Parse(code);
			var prototype = AsyncLuaCompiler.Compile(block, State.CompilerSettings, sourceName: sourceName);
			return new CompiledLuaCode(this, prototype);
		}

		/// <summary>
		/// Parses, compiles and executes the specified Lua code within this context.
		/// </summary>
		/// <param name="code">The Lua source code to execute.</param>
		/// <param name="sourceName">Optional source name for debugging (e.g., a file name).</param>
		/// <returns>A <see cref="LuaTuple"/> containing all return values of the executed chunk.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="code"/> is <see langword="null"/>.</exception>
		public LuaTuple Execute(string code, string? sourceName = null)
		{
			return Compile(code, sourceName).Execute();
		}

		/// <summary>
		/// Parses, compiles and executes the specified Lua code within this context asynchronously.
		/// Required for code that uses <c>async</c>/<c>await</c>.
		/// </summary>
		/// <param name="code">The Lua source code to execute.</param>
		/// <param name="sourceName">Optional source name for debugging (e.g., a file name).</param>
		/// <returns>A task that resolves to the chunk's return values.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="code"/> is <see langword="null"/>.</exception>
		public Task<LuaTuple> ExecuteAsync(string code, string? sourceName = null)
		{
			return Compile(code, sourceName).ExecuteAsync();
		}

		/// <summary>
		/// Invokes a Lua function within this context.
		/// </summary>
		/// <param name="function">The function to invoke.</param>
		/// <param name="args">The arguments to pass to the function.</param>
		/// <returns>The function's return values.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="function"/> is <see langword="null"/>.</exception>
		public LuaTuple Call(LuaFunction function, params LuaValue[] args)
		{
			if (function is null)
				throw new ArgumentNullException(nameof(function));

			return function.Invoke(this, args ?? []);
		}

		/// <summary>
		/// Invokes a Lua function within this context asynchronously.
		/// </summary>
		/// <param name="function">The function to invoke.</param>
		/// <param name="args">The arguments to pass to the function.</param>
		/// <returns>A task that resolves to the function's return values.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="function"/> is <see langword="null"/>.</exception>
		public Task<LuaTuple> CallAsync(LuaFunction function, params LuaValue[] args)
		{
			if (function is null)
				throw new ArgumentNullException(nameof(function));

			return function.InvokeAsync(this, args ?? []);
		}
	}
}
