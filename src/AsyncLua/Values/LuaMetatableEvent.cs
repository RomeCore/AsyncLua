namespace AsyncLua.Values
{
	/// <summary>
	/// The Lua metamethod names - the keys a metatable uses to define operator overloading
	/// and type behaviour.
	/// </summary>
	/// <remarks>
	/// Each member is a ready-made <see cref="LuaString"/> key, so a metatable can be an ordinary
	/// <see cref="LuaTable"/>: <c>metatable.Get(LuaMetatableEvent.Index)</c> reads <c>__index</c>
	/// and any other key the table carries is left untouched.
	/// </remarks>
	public static class LuaMetatableEvent
	{
		// Arithmetic

		/// <summary>The addition (<c>+</c>) metamethod: <c>__add</c>.</summary>
		public static readonly LuaString Add = new LuaString("__add");

		/// <summary>The subtraction (<c>-</c>) metamethod: <c>__sub</c>.</summary>
		public static readonly LuaString Sub = new LuaString("__sub");

		/// <summary>The multiplication (<c>*</c>) metamethod: <c>__mul</c>.</summary>
		public static readonly LuaString Mul = new LuaString("__mul");

		/// <summary>The division (<c>/</c>) metamethod: <c>__div</c>.</summary>
		public static readonly LuaString Div = new LuaString("__div");

		/// <summary>The modulo (<c>%</c>) metamethod: <c>__mod</c>.</summary>
		public static readonly LuaString Mod = new LuaString("__mod");

		/// <summary>The exponentiation (<c>^</c>) metamethod: <c>__pow</c>.</summary>
		public static readonly LuaString Pow = new LuaString("__pow");

		/// <summary>The negation (unary <c>-</c>) metamethod: <c>__unm</c>.</summary>
		public static readonly LuaString Unm = new LuaString("__unm");

		/// <summary>The floor division (<c>//</c>) metamethod: <c>__idiv</c>.</summary>
		public static readonly LuaString IDiv = new LuaString("__idiv");

		// Bitwise

		/// <summary>The bitwise AND (<c>&amp;</c>) metamethod: <c>__band</c>.</summary>
		public static readonly LuaString BAnd = new LuaString("__band");

		/// <summary>The bitwise OR (<c>|</c>) metamethod: <c>__bor</c>.</summary>
		public static readonly LuaString BOr = new LuaString("__bor");

		/// <summary>The bitwise XOR (<c>~</c>) metamethod: <c>__bxor</c>.</summary>
		public static readonly LuaString BXor = new LuaString("__bxor");

		/// <summary>The bitwise NOT (unary <c>~</c>) metamethod: <c>__bnot</c>.</summary>
		public static readonly LuaString BNot = new LuaString("__bnot");

		/// <summary>The left shift (<c>&lt;&lt;</c>) metamethod: <c>__shl</c>.</summary>
		public static readonly LuaString ShL = new LuaString("__shl");

		/// <summary>The right shift (<c>&gt;&gt;</c>) metamethod: <c>__shr</c>.</summary>
		public static readonly LuaString ShR = new LuaString("__shr");

		// Relational / Misc

		/// <summary>The concatenation (<c>..</c>) metamethod: <c>__concat</c>.</summary>
		public static readonly LuaString Concat = new LuaString("__concat");

		/// <summary>The length (<c>#</c>) metamethod: <c>__len</c>.</summary>
		public static readonly LuaString Len = new LuaString("__len");

		/// <summary>The equality (<c>==</c>) metamethod: <c>__eq</c>.</summary>
		public static readonly LuaString Eq = new LuaString("__eq");

		/// <summary>The less-than (<c>&lt;</c>) metamethod: <c>__lt</c>.</summary>
		public static readonly LuaString Lt = new LuaString("__lt");

		/// <summary>The less-than-or-equal (<c>&lt;=</c>) metamethod: <c>__le</c>.</summary>
		public static readonly LuaString Le = new LuaString("__le");

		// Table / Object access

		/// <summary>The indexing (<c>t[k]</c>) metamethod: <c>__index</c>.</summary>
		public static readonly LuaString Index = new LuaString("__index");

		/// <summary>The new-index (<c>t[k] = v</c>) metamethod: <c>__newindex</c>.</summary>
		public static readonly LuaString NewIndex = new LuaString("__newindex");

		// Callable

		/// <summary>The call (<c>f(...)</c>) metamethod: <c>__call</c>.</summary>
		public static readonly LuaString Call = new LuaString("__call");

		// AsyncLua extensions

		/// <summary>The <c>await</c> operator metamethod: <c>__await</c>.</summary>
		public static readonly LuaString Await = new LuaString("__await");

		// Introspection

		/// <summary>The <c>tostring()</c> metamethod: <c>__tostring</c>.</summary>
		public static new readonly LuaString ToString = new LuaString("__tostring");

		/// <summary>The <c>tonumber()</c> metamethod: <c>__tonumber</c>.</summary>
		public static readonly LuaString ToNumber = new LuaString("__tonumber");

		/// <summary>The garbage-collector finalizer metamethod: <c>__gc</c>.</summary>
		public static readonly LuaString GC = new LuaString("__gc");

		/// <summary>The weak-reference mode metamethod: <c>__mode</c>.</summary>
		public static readonly LuaString Mode = new LuaString("__mode");

		/// <summary>The metatable protection metamethod: <c>__metatable</c>.</summary>
		public static readonly LuaString MetaTable = new LuaString("__metatable");

		/// <summary>The name metamethod (for error messages and introspection): <c>__name</c>.</summary>
		public static readonly LuaString Name = new LuaString("__name");

		/// <summary>The <c>pairs()</c> iterator metamethod: <c>__pairs</c>.</summary>
		public static readonly LuaString Pairs = new LuaString("__pairs");

		/// <summary>The to-be-closed variable metamethod: <c>__close</c>.</summary>
		public static readonly LuaString Close = new LuaString("__close");
	}
}
