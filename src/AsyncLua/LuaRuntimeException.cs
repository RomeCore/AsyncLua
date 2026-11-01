using System;
using AsyncLua.Parsing;
using AsyncLua.Values;

namespace AsyncLua
{
	/// <summary>
	/// Represents a runtime exception that occurred in the AsyncLua environment.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A Lua error is a value, not a message: <c>error</c>, <c>assert</c> and <c>throw</c>
	/// raise whatever object they were given, and <c>try</c>/<c>catch</c> hands that same
	/// object back. The value travels in <see cref="ErrorValue"/>; the CLR message is only
	/// for host-side diagnostics.
	/// </para>
	/// <para>
	/// <c>error(string, level)</c> additionally stores the source position *inside* the error
	/// string, which is the only reason <c>level</c> exists. That positioned form is what
	/// <see cref="ErrorValueWithPosition"/> exposes - and therefore what <c>pcall</c> returns
	/// and what an <c>xpcall</c> handler receives - while <see cref="ErrorValue"/> stays the
	/// raw object, which is what <c>catch</c> binds.
	/// </para>
	/// </remarks>
	public class LuaRuntimeException : Exception
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
		/// Gets the Lua value the error carries, exactly as it was raised: the object passed to
		/// <c>error</c>, <c>assert</c> or <c>throw</c>, or the message string for an error raised
		/// by the runtime or a library itself.
		/// </summary>
		public LuaValue ErrorValue { get; }

		/// <summary>
		/// Gets the error value in the form Lua hands back to the code that handles the error:
		/// a string gains the source position, any other value is returned untouched. This is
		/// what <c>pcall</c> returns and what an <c>xpcall</c> message handler receives.
		/// </summary>
		public LuaValue ErrorValueWithPosition => Position.IsValid && ErrorValue is LuaString s
			? new LuaString($"{s.Value}\n{Position}")
			: ErrorValue;

		/// <summary>
		/// Gets whether the interpreter should attach source position information to this
		/// error when it reaches a call boundary. Lua turns position reporting off for
		/// <c>error(value, 0)</c> and for any non-string error value, and so does this flag.
		/// </summary>
		public bool AddPosition { get; }

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaRuntimeException"/> class.
		/// </summary>
		/// <param name="message">The error message.</param>
		public LuaRuntimeException(string message) : base(message)
		{
			OriginalMessage = message;
			ErrorValue = new LuaString(message);
			AddPosition = true;
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaRuntimeException"/> class
		/// with source position information.
		/// </summary>
		/// <param name="message">The error message.</param>
		/// <param name="position">The source position where the error occurred.</param>
		public LuaRuntimeException(string message, CodePositionalInfo position) : base(FormatMessageWithPosition(message, position))
		{
			OriginalMessage = message;
			Position = position;
			ErrorValue = new LuaString(message);
			AddPosition = true;
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaRuntimeException"/> class
		/// with an inner exception.
		/// </summary>
		/// <param name="message">The error message.</param>
		/// <param name="inner">The inner exception.</param>
		public LuaRuntimeException(string message, Exception? inner) : base(message, inner)
		{
			OriginalMessage = message;
			ErrorValue = new LuaString(message);
			AddPosition = true;
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaRuntimeException"/> class
		/// with source position information and an inner exception.
		/// </summary>
		/// <param name="message">The error message.</param>
		/// <param name="position">The source position where the error occurred.</param>
		/// <param name="inner">The inner exception.</param>
		public LuaRuntimeException(string message, CodePositionalInfo position, Exception? inner) : base(FormatMessageWithPosition(message, position), inner)
		{
			OriginalMessage = message;
			Position = position;
			ErrorValue = new LuaString(message);
			AddPosition = true;
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaRuntimeException"/> class carrying
		/// an arbitrary Lua error value, as raised by <c>error</c>, <c>assert</c> or <c>throw</c>.
		/// </summary>
		/// <param name="errorValue">The Lua object raised as the error.</param>
		/// <param name="addPosition">Whether the interpreter may append the source position.</param>
		public LuaRuntimeException(LuaValue errorValue, bool addPosition) : base(TextOf(errorValue))
		{
			OriginalMessage = TextOf(errorValue);
			ErrorValue = errorValue;
			AddPosition = addPosition;
		}

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaRuntimeException"/> class carrying a
		/// Lua error value that was raised at a known source position. The value itself is kept
		/// raw - the position is applied only when the error is rendered for Lua or for the CLR.
		/// </summary>
		/// <param name="errorValue">The Lua object raised as the error.</param>
		/// <param name="position">The source position where the error occurred.</param>
		/// <param name="inner">The inner exception, if the error came from a host call.</param>
		internal LuaRuntimeException(LuaValue errorValue, CodePositionalInfo position, Exception? inner = null)
			: base(FormatMessageWithPosition(TextOf(errorValue), position), inner)
		{
			OriginalMessage = TextOf(errorValue);
			ErrorValue = errorValue;
			Position = position;
			AddPosition = false;
		}

		/// <summary>
		/// Returns the text of an error value: a string is used verbatim, anything else is
		/// rendered the way <c>tostring</c> would render it.
		/// </summary>
		private static string TextOf(LuaValue value) => value is LuaString s ? s.Value : value.ToString();

		/// <summary>
		/// Appends the source position to the message, skipping the append when the position
		/// is unknown.
		/// </summary>
		private static string FormatMessageWithPosition(string message, CodePositionalInfo position)
		{
			return position.IsValid ? $"{message}\n{position}" : message;
		}
	}
}
