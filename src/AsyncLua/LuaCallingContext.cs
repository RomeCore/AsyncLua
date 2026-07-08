using System;
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
	}
}
