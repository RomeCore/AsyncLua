using System;
using System.Runtime.CompilerServices;

namespace AsyncLua.Values
{
	/// <summary>
	/// Represents a Lua <c>userdata</c> type — a wrapper around an arbitrary .NET object.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="LuaUserData"/> holds a reference to any .NET object and exposes it to the
	/// Lua runtime. Behaviour such as indexing, method calls, and operators is provided
	/// exclusively through the value's <see cref="LuaValue.Metatable"/> (e.g. <c>__index</c>,
	/// <c>__call</c>, <c>__add</c>). Without a metatable, a userdata value is opaque and
	/// supports only identity comparison and truth testing.
	/// </para>
	/// <para>
	/// Unlike <see cref="LuaTable"/>, userdata values do <b>not</b> support direct key-value
	/// storage. All member access must go through metamethods. This design keeps the type
	/// lightweight and forces explicit metatable setup for any meaningful operation.
	/// </para>
	/// <para>
	/// Equality is based on <see cref="object.ReferenceEquals(object, object)"/>, matching
	/// standard Lua semantics for userdata.
	/// </para>
	/// </remarks>
	public sealed class LuaUserData : LuaValue
	{
		/// <summary>
		/// Gets the underlying .NET object wrapped by this userdata.
		/// </summary>
		public object Target { get; }

		/// <summary>
		/// Gets a human-readable name for the wrapped type, used in error messages
		/// and diagnostic output (e.g., <c>"FileStream"</c>, <c>"HttpClient"</c>).
		/// </summary>
		public string UserDataTypeName { get; }

		/// <summary>
		/// Initialises a new <see cref="LuaUserData"/> that wraps the specified .NET object.
		/// </summary>
		/// <param name="target">The .NET object to wrap. Must not be <see langword="null"/>.</param>
		/// <param name="userDataTypeName">
		/// An optional display name for the wrapped type. If <see langword="null"/>,
		/// the <see cref="Type.Name"/> of <paramref name="target"/> is used.
		/// </param>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="target"/> is <see langword="null"/>.
		/// </exception>
		public LuaUserData(object target, string? userDataTypeName = null)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			UserDataTypeName = userDataTypeName ?? target.GetType().Name;
		}

		/// <inheritdoc />
		public override LuaType Type => LuaType.UserData;

		/// <inheritdoc />
		public override string TypeName => "userdata";

		/// <inheritdoc />
		public override string ToString() => $"userdata: {UserDataTypeName}";

		/// <summary>
		/// Compares this userdata to another <see cref="LuaValue"/> by reference identity.
		/// </summary>
		/// <param name="other">The other value to compare with.</param>
		/// <returns>
		/// <see langword="true"/> if <paramref name="other"/> is a <see cref="LuaUserData"/>
		/// wrapping the exact same .NET object instance; otherwise, <see langword="false"/>.
		/// </returns>
		public override bool Equals(LuaValue other)
		{
			return other is LuaUserData ud && ReferenceEquals(Target, ud.Target);
		}

		/// <inheritdoc />
		public override int GetHashCode() => RuntimeHelpers.GetHashCode(Target);

		/// <inheritdoc />
		/// <returns>Always <see langword="true"/> — userdata is truthy in Lua.</returns>
		public override bool ToBoolean() => true;
	}
}
