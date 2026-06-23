namespace AsyncLua
{
	/// <summary>
	/// Enumerates the fundamental Lua types.
	/// </summary>
	public enum LuaType
	{
		/// <summary>Represents the <c>nil</c> type.</summary>
		Nil,

		/// <summary>Represents the <c>boolean</c> type.</summary>
		Boolean,

		/// <summary>Represents the <c>number</c> type (IEEE 754 double-precision floating point).</summary>
		Number,

		/// <summary>Represents the <c>string</c> type.</summary>
		String,

		/// <summary>Represents the <c>table</c> type.</summary>
		Table,

		/// <summary>Represents the <c>tuple</c> type.</summary>
		Tuple,

		/// <summary>Represents the <c>function</c> type (both C# callbacks and Lua closures).</summary>
		Function,

		/// <summary>Represents the <c>userdata</c> type.</summary>
		UserData,

		/// <summary>Represents the <c>thread</c> (coroutine) type.</summary>
		Thread,

		/// <summary>Represents the <c>task</c> (AsyncLua extension) type.</summary>
		Task,
	}
}
