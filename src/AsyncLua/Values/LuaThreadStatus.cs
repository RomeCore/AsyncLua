namespace AsyncLua.Values
{
	/// <summary>
	/// Represents the status of a <see cref="LuaThread"/> (coroutine).
	/// </summary>
	public enum LuaThreadStatus
	{
		/// <summary>The coroutine is suspended (has yielded or hasn't started yet).</summary>
		Suspended,

		/// <summary>The coroutine is currently running.</summary>
		Running,

		/// <summary>The coroutine has finished executing (returned).</summary>
		Dead,
	}
}
