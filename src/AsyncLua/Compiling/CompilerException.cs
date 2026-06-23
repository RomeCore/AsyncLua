using System;

namespace AsyncLua.Compiling
{
	/// <summary>
	/// The exception that is thrown when a compilation error occurs.
	/// </summary>
	public class CompilerException : Exception
	{
		/// <summary>
		/// Initialises a new instance of the <see cref="CompilerException"/> class.
		/// </summary>
		/// <param name="message">The error message.</param>
		public CompilerException(string message) : base(message) { }
	}
}
