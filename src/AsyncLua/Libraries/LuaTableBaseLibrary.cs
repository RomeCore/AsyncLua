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
		public sealed override void Import(LuaState state)
		{
			var nsTable = state.Globals.ResolveNamespace(Namespace);
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
