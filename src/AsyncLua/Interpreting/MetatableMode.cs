namespace AsyncLua.Interpreting
{
	/// <summary>
	/// Defines how aggressively metamethods are resolved during instruction execution.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Default (Relaxed)</b> mimics standard Lua behaviour: metamethods are only consulted
	/// for tables (and userdata) in most operations, and <c>__eq</c>/<c>__lt</c>/<c>__le</c>
	/// require both operands to share the same metamethod.
	/// </para>
	/// <para>
	/// <b>Aggressive</b> always consults the metatable for any value type if a metamethod
	/// is present, enabling operator overloading on primitives and per-type semantics.
	/// </para>
	/// </remarks>
	public enum MetatableMode
	{
		/// <summary>
		/// Standard Lua semantics: metamethods are only invoked for tables (and userdata),
		/// equality/comparison metamethods require both operands to have the same metamethod.
		/// </summary>
		Default,

		/// <summary>
		/// Aggressive resolution: metamethods are always consulted when present,
		/// regardless of the value's type. Allows operator overloading on any type.
		/// </summary>
		Aggressive,
	}
}
