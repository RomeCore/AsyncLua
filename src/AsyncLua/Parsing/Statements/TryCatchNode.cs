using System;
using System.Collections.Generic;
using System.Text;

namespace AsyncLua.Parsing.Statements
{
	public class TryCatchNode : StatementNode
	{
		/// <summary>
		/// The body of the try block. This cannot be null.
		/// </summary>
		public BlockNode TryBody { get; set; } = null!;

		/// <summary>
		/// The body of the catch block.
		/// </summary>
		public BlockNode CatchBody { get; set; } = null!;

		/// <summary>
		/// The variable name to use for the exception message. This can be empty if no message is needed.
		/// </summary>
		public string? ExceptionMessageVariable { get; set; }
	}
}
