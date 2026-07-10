using System;

namespace AsyncLua.Values
{
	/// <summary>
	/// Represents the Lua <c>nil</c> value. This is a singleton — only one instance exists.
	/// </summary>
	/// <remarks>
	/// <c>nil</c> is falsy in Lua boolean context.
	/// </remarks>
	public sealed class LuaNil : LuaValue, IEquatable<LuaNil>
	{
		/// <summary>
		/// Gets the singleton instance of <see cref="LuaNil"/>.
		/// </summary>
		public static readonly LuaNil Instance = new LuaNil();

		public override LuaMetatable? Metatable { get => null; set => throw new LuaRuntimeException("Cannot change the metatable of nil."); }

		private LuaNil() { }

		/// <inheritdoc />
		public override LuaType Type => LuaType.Nil;

		/// <inheritdoc />
		public override string TypeName => "nil";

		/// <inheritdoc />
		public override string ToString() => "nil";

		/// <inheritdoc />
		public override bool Equals(LuaValue other)
		{
			return other is LuaNil;
		}

		/// <inheritdoc />
		public bool Equals(LuaNil other)
		{
			return other is not null;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return obj is LuaNil;
		}

		/// <inheritdoc />
		public override int GetHashCode() => 0;

		/// <inheritdoc />
		public override bool TryToString(out string value)
		{
			value = "nil";
			return true;
		}

		/// <inheritdoc />
		/// <returns><see langword="false"/> — <c>nil</c> is always falsy.</returns>
		public override bool ToBoolean() => false;
	}
}
