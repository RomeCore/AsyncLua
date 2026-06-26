using System;

namespace AsyncLua.Values
{
	/// <summary>
	/// Marks a member as hidden from Lua when exposed through <see cref="UserDataMetatableGenerator"/>.
	/// The member will not appear in <c>__index</c> lookups and cannot be called or accessed from Lua.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Event)]
	public sealed class LuaHiddenAttribute : Attribute
	{
	}

	/// <summary>
	/// Marks a member as visible to Lua even if it would otherwise be filtered out
	/// (e.g., non-public members). By default, only public members are exposed.
	/// Use this attribute to expose private, protected, or internal members.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This attribute is checked in addition to the public-member filter. If a member
	/// is already public, an explicit <see cref="LuaVisibleAttribute"/> is not required
	/// but does no harm.
	/// </para>
	/// </remarks>
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Event)]
	public sealed class LuaVisibleAttribute : Attribute
	{
	}
}
