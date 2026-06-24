using System;
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
		/// Gets the global environment table for the current call.
		/// This is typically <see cref="LuaState.Globals"/> but may be overridden
		/// per-closure (_ENV).
		/// </summary>
		public LuaTable Globals { get; }

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaCallingContext"/> class.
		/// </summary>
		/// <param name="state">The owning Lua state.</param>
		/// <param name="globals">
		/// The global environment table for this context. If <see langword="null"/>,
		/// <see cref="LuaState.Globals"/> is used.
		/// </param>
		internal LuaCallingContext(LuaState state, LuaTable? globals = null)
		{
			State = state ?? throw new ArgumentNullException(nameof(state));
			Globals = globals ?? state.Globals;
		}
	}
}
