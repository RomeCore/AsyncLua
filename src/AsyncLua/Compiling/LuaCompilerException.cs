using System;
using AsyncLua.Parsing;

namespace AsyncLua.Compiling
{
	/// <summary>
	/// The exception that is thrown when a compilation error occurs.
	/// </summary>
	public class LuaCompilerException : Exception
	{
		/// <summary>
		/// Gets the original exception message without positional info.
		/// </summary>
		public string OriginalMessage { get; }

		/// <summary>
		/// Gets the source position where the exception occurred, if available.
		/// </summary>
		public CodePositionalInfo Position { get; }

		/// <summary>
		/// Gets whether source position information is available.
		/// </summary>
		public bool HasPosition => Position.IsValid;

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaCompilerException"/> class.
		/// </summary>
		/// <param name="message">The error message.</param>
		public LuaCompilerException(string message) : base(message)
		{
			OriginalMessage = message;
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaCompilerException"/> class.
		/// </summary>
		/// <param name="message">The error message.</param>
		public LuaCompilerException(string message, CodePositionalInfo position) : base(FormatMessageWithPosition(message, position))
		{
			OriginalMessage = message;
			Position = position;
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaCompilerException"/> class
		/// with an inner exception.
		/// </summary>
		/// <param name="message">The error message.</param>
		/// <param name="inner">The inner exception.</param>
		public LuaCompilerException(string message, Exception? inner) : base(message, inner)
		{
			OriginalMessage = message;
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaCompilerException"/> class
		/// with source position information and an inner exception.
		/// </summary>
		/// <param name="message">The error message.</param>
		/// <param name="position">The source position where the error occurred.</param>
		/// <param name="inner">The inner exception.</param>
		public LuaCompilerException(string message, CodePositionalInfo position, Exception? inner) : base(FormatMessageWithPosition(message, position), inner)
		{
			OriginalMessage = message;
			Position = position;
		}

		private static string FormatMessageWithPosition(string message, CodePositionalInfo position)
		{
			return $"{message}\n{position}";
		}
	}
}
