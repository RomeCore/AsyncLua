namespace AsyncLua.Interpreting
{
	public readonly struct Instruction
	{
		/// <summary>
		/// The opcode of the instruction.
		/// </summary>
		public readonly OpCode Code;

		/// <summary>
		/// The A value of the instruction.
		/// </summary>
		public readonly byte A;

		/// <summary>
		/// The B value of the instruction.
		/// </summary>
		public readonly ushort B;

		/// <summary>
		/// The C value of the instruction.
		/// </summary>
		public readonly ushort C;

		/// <summary>
		/// The flags associated with this instruction.
		/// </summary>
		public readonly OpFlags Flags;

		/// <summary>
		/// Constructs an instruction with the specified opcode and operands.
		/// </summary>
		/// <param name="code">The opcode of the instruction.</param>
		/// <param name="a">The first operand of the instruction.</param>
		/// <param name="b">The second operand of the instruction.</param>
		/// <param name="c">The third operand of the instruction.</param>
		/// <param name="flags">The flags associated with the instruction.</param>
		public Instruction(OpCode code, byte a, ushort b, ushort c, OpFlags flags)
		{
			Code = code;
			A = a;
			B = b;
			C = c;
			Flags = flags;
		}
	}
}
