using System;
using System.Collections.Generic;
using System.Text;
using AsyncLua.Values;

namespace AsyncLua.Interpreting
{
	/// <summary>
	/// Represents a frame in the call stack of an AsyncLua interpreter.
	/// </summary>
	public struct CallStackFrame
	{
		/// <summary>
		/// The registers of the frame, which hold local variables and temporary values.
		/// </summary>
		public LuaValue[] Registers;

		/// <summary>
		/// The function prototype associated with this frame that is being called now.
		/// </summary>
		public FunctionPrototype Function;

		/// <summary>
		/// The closure being executed, if this frame is executing a <see cref="LuaNativeFunction"/>.
		/// <see langword="null"/> for top-level chunks that have no closure wrapper.
		/// </summary>
		public LuaNativeFunction? Closure;

		/// <summary>
		/// The program counter to restore after this frame returns
		/// (points to the instruction after the CALL that created this frame).
		/// </summary>
		public int ReturnPC;

		/// <summary>
		/// The base register index in the caller where results should be stored.
		/// </summary>
		public int ResultBase;

		/// <summary>
		/// The number of results expected by the caller.
		/// </summary>
		public int ResultCount;

		/// <summary>
		/// Open upvalues in this frame, indexed by register slot.
		/// Used to find existing upvalues when creating nested closures.
		/// </summary>
		public Upvalue[]? OpenUpvalues;

		public CallStackFrame(FunctionPrototype function, int returnPC, int resultBase = 0, int resultCount = 0)
		{
			if (function == null) throw new ArgumentNullException(nameof(function));
			Registers = new LuaValue[function.MaxRegSize];
			Function = function;
			ReturnPC = returnPC;
			ResultBase = resultBase;
			ResultCount = resultCount;
		}
	}
}
