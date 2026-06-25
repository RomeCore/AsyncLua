using AsyncLua.Values;

namespace AsyncLua.Libraries
{
	/// <summary>
	/// Base class for Lua libraries that register their functions under a named table
	/// in the global environment (e.g., <c>math.sqrt</c>, <c>string.sub</c>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Derived classes must implement <see cref="PopulateTable"/> to fill the table
	/// with functions and constants. The <see cref="Import"/> method automatically
	/// creates the table and assigns it to <c>_G[Namespace]</c>.
	/// </para>
	/// </remarks>
	public abstract class LuaTableBaseLibrary : LuaLibrary
	{
		/// <summary>
		/// Creates a new <see cref="LuaTable"/>, populates it via
		/// <see cref="PopulateTable"/>, and assigns it to <c>_G[Namespace]</c>.
		/// </summary>
		/// <param name="state">The Lua state to import into.</param>
		/// <exception cref="global::System.InvalidOperationException">
		/// Thrown if <see cref="LuaLibrary.Namespace"/> is <see langword="null"/>.
		/// Override <see cref="LuaGlobalBaseLibrary"/> instead for global-scope libraries.
		/// </exception>
		public sealed override void Import(LuaState state)
		{
			var ns = Namespace
				?? throw new global::System.InvalidOperationException(
					$"Table library '{GetType().Name}' has a null Namespace. " +
					$"Use LuaGlobalLibrary for libraries without a namespace.");

			var nsTable = state.Globals.ResolveNamespace(ns);
			PopulateTable(state, nsTable);
		}

		/// <summary>
		/// When overridden in a derived class, populates the library table with
		/// functions and constants.
		/// </summary>
		/// <param name="state">The Lua state to import into if needed.</param>
		/// <param name="table">The table to populate. It is empty when this method is called.</param>
		protected abstract void PopulateTable(LuaState state, LuaTable table);
	}
}
