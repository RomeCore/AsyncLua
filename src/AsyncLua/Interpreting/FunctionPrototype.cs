using System;
using System.Collections.Generic;
using System.Text;
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
		/// Gets the line number information for each instruction, for debugging.
		/// May be <see langword="null"/> if debug info is not available.
		/// </summary>
		public int[]? LineInfo { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="FunctionPrototype"/> class.
		/// </summary>
		/// <param name="instructions">The list of instructions that make up the function. Cannot be null.</param>
		/// <param name="maxRegSize">The maximum number of registers used by this function. Must be non-negative.</param>
		/// <param name="constants">The list of constants used by this function. Cannot be null.</param>
		/// <param name="innerPrototypes">The list of inner prototypes for this function. Used for creating closures.</param>
		/// <param name="parameterCount">The number of fixed parameters.</param>
		/// <param name="isVararg">Whether the function accepts varargs.</param>
		/// <param name="sourceName">The source name for debugging. Defaults to <c>"chunk"</c>.</param>
		/// <param name="lineInfo">Line number information for debugging. May be null.</param>
		/// <exception cref="ArgumentNullException">Thrown when the <paramref name="instructions"/>, <paramref name="constants"/>, or <paramref name="innerPrototypes"/> parameters are null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the <paramref name="maxRegSize"/> parameter is negative.</exception>
		public FunctionPrototype(
			Instruction[] instructions,
			int maxRegSize,
			LuaValue[] constants,
			FunctionPrototype[] innerPrototypes,
			byte parameterCount = 0,
			bool isVararg = false,
			string? sourceName = null,
			int[]? lineInfo = null,
			UpvalueDescription[]? upvalueDescriptions = null)
		{
			Instructions = instructions ?? throw new ArgumentNullException(nameof(instructions));
			MaxRegSize = maxRegSize < 0 ? throw new ArgumentOutOfRangeException(nameof(maxRegSize), "The maximum register size must be non-negative.") : maxRegSize;
			Constants = constants ?? throw new ArgumentNullException(nameof(constants));
			InnerPrototypes = innerPrototypes ?? throw new ArgumentNullException(nameof(innerPrototypes));
			ParameterCount = parameterCount;
			IsVararg = isVararg;
			SourceName = sourceName ?? "chunk";
			LineInfo = lineInfo;
			UpvalueDescriptions = upvalueDescriptions ?? Array.Empty<UpvalueDescription>();
		}
	}
}
