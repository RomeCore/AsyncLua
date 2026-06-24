using System;
using System.Collections.Generic;
using System.Text;

namespace AsyncLua.Parsing.Statements
{
	public class TryCatchFinallyNode : StatementNode
	{
		/// <summary>
		/// The body of the try block. This cannot be null.
		/// </summary>
		public BlockNode TryBody { get; set; } = null!;

		/// <summary>
		/// The body of the catch block. This can be null if there is no catch block.
		/// But it must not be null if the finally block is null.
		/// </summary>
		public BlockNode? CatchBody { get; set; }

		/// <summary>
		/// The body of the finally block. This can be null if there is no finally block.
		/// But it must not be null if the catch block is null.
		/// </summary>
		public BlockNode? FinallyBody { get; set; }
	}
}
