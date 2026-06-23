using System;
using AsyncLua.Values;

namespace AsyncLua.Interpreting
{
	/// <summary>
	/// Represents a captured variable from an enclosing scope in a Lua closure.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An upvalue can be in one of two states:
	/// </para>
	/// <list type="bullet">
	/// <item>
	///   <term>Open</term>
	///   <description>
	///     The upvalue points directly into the register array of an active call frame.
	///     Reading/writing the upvalue directly accesses the register.
	///   </description>
	/// </item>
	/// <item>
	///   <term>Closed</term>
	///   <description>
	///     The enclosing frame has returned, and the value has been copied into this
	///     upvalue's internal storage. The upvalue now owns the value.
	///   </description>
	/// </item>
	/// </list>
	/// </remarks>
	public sealed class Upvalue
	{
		// Open state: points to a register in an active frame.
		internal LuaValue[]? _stack;
		internal int _index;

		// Closed state: owns the value after the enclosing frame is destroyed.
		internal LuaValue _cachedValue;

		/// <summary>
		/// Gets whether this upvalue is open (still pointing into an active call frame).
		/// </summary>
		public bool IsOpen => _stack != null;

		/// <summary>
		/// Gets or sets the current value of the captured variable.
		/// </summary>
		public LuaValue Value
		{
			get => IsOpen ? _stack![_index] : _cachedValue;
			set
			{
				if (IsOpen)
					_stack![_index] = value;
				else
					_cachedValue = value;
			}
		}

		/// <summary>
		/// Creates an open upvalue pointing to a register in the specified stack.
		/// </summary>
		/// <param name="stack">The register array of the enclosing frame.</param>
		/// <param name="index">The index in the register array.</param>
		internal Upvalue(LuaValue[] stack, int index)
		{
			_stack = stack ?? throw new ArgumentNullException(nameof(stack));
			_index = index;
			_cachedValue = LuaNil.Instance;
		}

		/// <summary>
		/// Closes the upvalue by copying the current value from the register
		/// into internal storage and releasing the reference to the stack.
		/// Called when the enclosing frame is destroyed.
		/// </summary>
		internal void Close()
		{
			if (IsOpen)
			{
				_cachedValue = _stack![_index];
				_stack = null;
			}
		}
	}
}
