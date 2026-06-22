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
	}
}