namespace AsyncLua.Libraries
{
	/// <summary>
	/// Provides extension methods for <see cref="LuaState"/> to load <see cref="LuaLibrary"/> instances.
	/// </summary>
	public static class LuaStateLibraryExtensions
	{
		/// <summary>
		/// Loads the specified library into this Lua state.
		/// </summary>
		/// <param name="state">The Lua state.</param>
		/// <param name="library">The library to import.</param>
		/// <returns>This Lua state instance, for fluent chaining.</returns>
		public static LuaState LoadLibrary(this LuaState state, LuaLibrary library)
		{
			library.Import(state);
			return state;
		}

		/// <summary>
		/// Loads multiple libraries into this Lua state.
		/// </summary>
		/// <param name="state">The Lua state.</param>
		/// <param name="libraries">The libraries to import.</param>
		/// <returns>This Lua state instance, for fluent chaining.</returns>
		public static LuaState LoadLibraries(this LuaState state, params LuaLibrary[] libraries)
		{
			foreach (var library in libraries)
				library.Import(state);
			return state;
		}
	}
}
