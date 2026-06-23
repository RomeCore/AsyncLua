namespace AsyncLua.Values
{
	/// <summary>
	/// Represents the status of a <see cref="LuaTask"/>.
	/// </summary>
	public enum LuaTaskStatus
	{
		/// <summary>The task has been created but not yet completed.</summary>
		Pending,

		/// <summary>The task completed successfully with a result.</summary>
		Completed,

		/// <summary>The task completed with an unhandled exception.</summary>
		Faulted,

		/// <summary>The task was cancelled before completion.</summary>
		Canceled,
	}
}
