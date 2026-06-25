using AsyncLua.Values;

namespace AsyncLua.Libraries
{
	/// <summary>
	/// Base class for Lua libraries that can be imported into a <see cref="LuaState"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A library is a collection of related functions (and optionally constants) that
	/// can be batch-imported into a Lua state. Libraries fall into two categories:
	/// </para>
	/// <list type="bullet">
	///   <item><description>
	///     <b>Global libraries</b> (<see cref="Namespace"/> is <see langword="null"/>) —
	///     functions are registered directly on the global environment (<c>_G</c>).
	///     Suitable for <c>print</c>, <c>type</c>, <c>error</c>, etc.
	///   </description></item>
	///   <item><description>
	///     <b>Table libraries</b> (<see cref="Namespace"/> is a non-null string) —
	///     functions are registered as fields of a table stored under the namespace
	///     name in <c>_G</c>. Suitable for <c>math</c>, <c>string</c>, <c>table</c>, etc.
	///   </description></item>
	/// </list>
	/// </remarks>
	public abstract class LuaLibrary
	{
		/// <summary>
		/// Gets the library namespace name (e.g., <c>"math"</c>, <c>"string"</c>).
		/// Returns <see langword="null"/> for libraries that register directly
		/// in the global scope.
		/// </summary>
		public abstract string? Namespace { get; }

		/// <summary>
		/// Imports all functions and constants defined by this library into the
		/// specified <see cref="LuaState"/>.
		/// </summary>
		/// <param name="state">The Lua state to import into.</param>
		public abstract void Import(LuaState state);
	}
}
