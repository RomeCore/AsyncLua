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

		/// <summary>
		/// The coroutine is active but not running; it is resumed by and resuming
		/// another coroutine (i.e., A resumed B, so A is "normal" while B runs).
		/// </summary>
		Normal,

		/// <summary>The coroutine has finished executing (returned).</summary>
		Dead,
	}
}
