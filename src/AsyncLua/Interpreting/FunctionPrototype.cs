using System;
using AsyncLua.Parsing;
using AsyncLua.Values;

namespace AsyncLua.Interpreting
{
	/// <summary>
	/// Represents a function prototype in Lua. This includes the bytecode and any associated data.
	/// </summary>
	public class FunctionPrototype
	{
		/// <summary>
		/// The list of instructions that make up the function.
		/// </summary>
		public Instruction[] Instructions { get; }

		/// <summary>
		/// The maximum number of registers used by this function.
		/// </summary>
		public int MaxRegSize { get; }

		/// <summary>
		/// Gets whether this function is an asynchronous function or not.
		/// </summary>
		public bool IsAsync { get; }

		/// <summary>
		/// The list of constants used by this function.
		/// </summary>
		public LuaValue[] Constants { get; }

		/// <summary>
		/// The list of inner prototypes for this function. Used for creating closures.
		/// </summary>
		public FunctionPrototype[] InnerPrototypes { get; }

		/// <summary>
		/// Gets the number of fixed parameters this function expects.
		/// </summary>
		public byte ParameterCount { get; }

		/// <summary>
		/// Gets whether this function accepts varargs (<c>...</c>).
		/// </summary>
		public bool IsVararg { get; }

		/// <summary>
		/// Gets descriptions of upvalues that must be captured from enclosing scopes
		/// when creating a closure from this prototype.
		/// </summary>
		public UpvalueDescription[] UpvalueDescriptions { get; }

		/// <summary>
		/// Gets the source name for error messages and debugging (e.g., file name or "chunk").
		/// </summary>
		public string? SourceName { get; }

		/// <summary>
		/// Gets the source position information for each instruction, for debugging.
		/// May be <see langword="null"/> if debug info is not available.
		/// Parallel array to <see cref="Instructions"/>.
		/// </summary>
		public CodePositionalInfo[]? Positions { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="FunctionPrototype"/> class.
		/// </summary>
		/// <param name="instructions">The list of instructions that make up the function. Cannot be <see langword="null"/>.</param>
		/// <param name="maxRegSize">The maximum number of registers used by this function. Must be non-negative.</param>
		/// <param name="isAsync">Whether this function is an asynchronous function.</param>
		/// <param name="constants">The list of constants used by this function. Cannot be <see langword="null"/>.</param>
		/// <param name="innerPrototypes">The list of inner prototypes for this function. Used for creating closures.</param>
		/// <param name="parameterCount">The number of fixed parameters.</param>
		/// <param name="isVararg">Whether the function accepts varargs.</param>
		/// <param name="sourceName">The source name for debugging. Defaults to <c>"chunk"</c>.</param>
		/// <param name="positions">Source position information for each instruction. May be <see langword="null"/>.</param>
		/// <param name="upvalueDescriptions">Descriptions of upvalues for closure creation.</param>
		/// <exception cref="ArgumentNullException">Thrown when the <paramref name="instructions"/>, <paramref name="constants"/>, or <paramref name="innerPrototypes"/> parameters are <see langword="null"/>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the <paramref name="maxRegSize"/> parameter is negative.</exception>
		public FunctionPrototype(
			Instruction[] instructions,
			int maxRegSize,
			bool isAsync,
			LuaValue[] constants,
			FunctionPrototype[] innerPrototypes,
			byte parameterCount = 0,
			bool isVararg = false,
			string? sourceName = null,
			CodePositionalInfo[]? positions = null,
			UpvalueDescription[]? upvalueDescriptions = null)
		{
			Instructions = instructions ?? throw new ArgumentNullException(nameof(instructions));
			MaxRegSize = maxRegSize < 0 ? throw new ArgumentOutOfRangeException(nameof(maxRegSize), "The maximum register size must be non-negative.") : maxRegSize;
			IsAsync = isAsync;
			Constants = constants ?? throw new ArgumentNullException(nameof(constants));
			InnerPrototypes = innerPrototypes ?? throw new ArgumentNullException(nameof(innerPrototypes));
			ParameterCount = parameterCount;
			IsVararg = isVararg;
			SourceName = sourceName ?? "chunk";
			Positions = positions;
			UpvalueDescriptions = upvalueDescriptions ?? Array.Empty<UpvalueDescription>();
		}

		/// <summary>
		/// Returns a human-readable disassembly of this function prototype's bytecode.
		/// </summary>
		/// <returns>A multi-line string showing instructions, constants, and inner prototypes.</returns>
		public string Disassemble()
		{
			var sb = new System.Text.StringBuilder();

			sb.AppendLine($"=== Function: {SourceName ?? "?"} ===");
			sb.AppendLine($"  IsAsync: {IsAsync}, Params: {ParameterCount}, Vararg: {IsVararg}, MaxRegs: {MaxRegSize}");
			sb.AppendLine($"  Upvalues: {UpvalueDescriptions.Length}, InnerProtos: {InnerPrototypes.Length}");
			sb.AppendLine("  Constants:");
			for (int i = 0; i < Constants.Length; i++)
				sb.AppendLine($"    [{i}] = {Constants[i]} ({Constants[i].TypeName})");
			sb.AppendLine("  Instructions:");
			for (int i = 0; i < Instructions.Length; i++)
			{
				var inst = Instructions[i];
				string flags = inst.Flags != OpFlags.None ? $" [{inst.Flags}]" : "";
				string comment = GetInstructionComment(inst);
				string pos = Positions != null && i < Positions.Length && Positions[i].IsValid
					? $"  ; {Positions[i]}"
					: "";
				sb.AppendLine($"    {i,4}: {inst.Code,-12} A={inst.A,3} B={inst.B,5} C={inst.C,5}{flags}  {comment}{pos}");
			}

			for (int i = 0; i < InnerPrototypes.Length; i++)
			{
				sb.AppendLine();
				sb.AppendLine($"  --- Inner prototype [{i}] ---");
				sb.Append(InnerPrototypes[i].Disassemble());
			}

			return sb.ToString();
		}

		private static string GetInstructionComment(Instruction inst)
		{
			return inst.Code switch
			{
				OpCode.MOVE => $"; R[{inst.A}] = R/K[{inst.B}]",
				OpCode.JMP => $"; pc += {GetSignedOffsetComment(inst)}",
				OpCode.JMPIF => $"; if R[{inst.A}] pc += {GetSignedOffsetComment(inst)}",
				OpCode.RETURN => $"; return R[{inst.A}]..R[{inst.A}+{inst.B}-1]",
				OpCode.CALL => $"; R[{inst.A}](R[{inst.A}+1]..R[{inst.A}+{inst.B}]) -> {inst.C} results",
				OpCode.GETTABLE => $"; R[{inst.A}] = R[{inst.B}][R/K[{inst.C}]]",
				OpCode.SETTABLE => $"; R[{inst.A}][R/K[{inst.B}]] = R/K[{inst.C}]",
				OpCode.GETGLOBAL => $"; R[{inst.A}] = _G[K[{inst.B}]]",
				OpCode.SETGLOBAL => $"; _G[K[{inst.B}]] = R[{inst.A}]",
				OpCode.NEWTABLE => $"; R[{inst.A}] = {{}}",
				OpCode.CLOSURE => $"; R[{inst.A}] = closure(Inner[{inst.B}])",
				OpCode.ADD => $"; R[{inst.A}] = R/K[{inst.B}] + R/K[{inst.C}]",
				OpCode.SUB => $"; R[{inst.A}] = R/K[{inst.B}] - R/K[{inst.C}]",
				OpCode.MUL => $"; R[{inst.A}] = R/K[{inst.B}] * R/K[{inst.C}]",
				OpCode.DIV => $"; R[{inst.A}] = R/K[{inst.B}] / R/K[{inst.C}]",
				OpCode.LT => $"; R[{inst.A}] = R/K[{inst.B}] < R/K[{inst.C}]",
				OpCode.LE => $"; R[{inst.A}] = R/K[{inst.B}] <= R/K[{inst.C}]",
				OpCode.EQ => $"; R[{inst.A}] = R/K[{inst.B}] == R/K[{inst.C}]",
				OpCode.LOCK => $"; lock(R[{inst.A}])",
				OpCode.UNLOCK => $"; unlock(R[{inst.A}])",
				OpCode.AWAIT => $"; await R[{inst.A}]",
				OpCode.FORPREP => $"; R[{inst.A}] -= R[{inst.A}+2]; pc += {GetSignedOffsetComment(inst)}",
				OpCode.FORLOOP => $"; R[{inst.A}] += R[{inst.A}+2]; if cond pc += {GetSignedOffsetComment(inst)}",
				OpCode.TFORCALL => $"; R[{inst.A}+3..] = R[{inst.A}](R[{inst.A}+1], R[{inst.A}+2])",
				OpCode.TFORLOOP => $"; if R[{inst.A}+1] ~= nil pc += {GetSignedOffsetComment(inst)}",
				OpCode.CONCAT => $"; R[{inst.A}] = R/K[{inst.B}] .. R/K[{inst.C}]",
				OpCode.LEN => $"; R[{inst.A}] = #R[{inst.B}]",
				OpCode.NOT => $"; R[{inst.A}] = !R[{inst.B}]",
				OpCode.UNM => $"; R[{inst.A}] = -R[{inst.B}]",
				OpCode.MOD => $"; R[{inst.A}] = R/K[{inst.B}] % R/K[{inst.C}]",
				OpCode.POW => $"; R[{inst.A}] = R/K[{inst.B}] ^ R/K[{inst.C}]",
				OpCode.GETUPVAL => $"; R[{inst.A}] = U[{inst.B}]",
				OpCode.SETUPVAL => $"; U[{inst.B}] = R[{inst.A}]",
				OpCode.THROW => $"; throw R[{inst.A}]",
				OpCode.TRY => $"; try: catch at pc+{GetSignedOffsetComment(inst)}",
				_ => ""
			};
		}

		private static short GetSignedOffset(Instruction inst)
		{
			return (short)inst.B;
		}

		private static string GetSignedOffsetComment(Instruction inst)
		{
			return $"{(short)inst.B:+0;-#}";
		}
	}
}
