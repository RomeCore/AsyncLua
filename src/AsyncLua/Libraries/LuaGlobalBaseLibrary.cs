namespace AsyncLua.Libraries
{
	/// <summary>
	/// Base class for Lua libraries that register functions directly into the global
	/// scope (<c>_G</c>), as opposed to under a named table.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Libraries derived from <see cref="LuaGlobalBaseLibrary"/> have a <see cref="LuaLibrary.Namespace"/>
	/// of <see langword="null"/> and register their functions individually via
	/// <see cref="LuaState.Register"/>.
	/// </para>
	/// <para>
	/// Examples of global functions: <c>print</c>, <c>type</c>, <c>tostring</c>,
	/// <c>tonumber</c>, <c>error</c>, <c>assert</c>, <c>pcall</c>, <c>ipairs</c>, <c>pairs</c>.
	/// </para>
	/// </remarks>
	public abstract class LuaGlobalBaseLibrary : LuaLibrary
	{
		/// <summary>
		/// Gets <see langword="null"/> — global libraries do not have a namespace prefix.
		/// </summary>
		public sealed override string? Namespace => null;
	}
}
