using System;

namespace AsyncLua.Interpreting
{
	[Flags]
	public enum OpFlags : ushort
	{
		/// <summary>
		/// No flags are set.
		/// </summary>
		None = 0,

		/// <summary>
		/// The value in <see cref="Instruction.B"/> is constant.
		/// </summary>
		KB = 1 << 0,

		/// <summary>
		/// The value in <see cref="Instruction.C"/> is constant.
		/// </summary>
		KC = 1 << 1,

		/// <summary>
		/// The B operand is a signed offset (used with JMP/JMPIF).
		/// Interpret <see cref="Instruction.B"/> as <see cref="short"/>.
		/// </summary>
		SignedBX = 1 << 2,

		/// <summary>
		/// The CALL instruction should append the current frame's varargs
		/// (<see cref="CallStackFrame.VarArgs"/>) after the fixed arguments
		/// read from registers. Used when the last argument expression is <c>...</c>.
		/// </summary>
		VarArgCall = 1 << 3,

		/// <summary>
		/// The last argument of a CALL expands to however many values it produced: the
		/// arguments run from <c>R[A+1]</c> up to the frame's current register top instead
		/// of stopping after <see cref="Instruction.B"/> fixed slots. Used when the last
		/// argument expression is a call or an await, mirroring Lua's "last argument
		/// expands" rule.
		/// </summary>
		ExpandArgs = 1 << 4,
	}
}
