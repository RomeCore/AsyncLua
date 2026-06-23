using System;

namespace AsyncLua
{
	/// <summary>
	/// Represents a runtime exception that occurred in the AsyncLua environment.
	/// </summary>
	public class LuaRuntimeException : Exception
	{
		public LuaRuntimeException(string message) : base(message)
		{
		}

		public LuaRuntimeException(string message, Exception? inner) : base(message, inner)
		{
		}
	}
}
