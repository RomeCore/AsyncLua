using AsyncLua.Compiling;

namespace AsyncLua.Interpreting
{
	/// <summary>
	/// Configuration options for the AsyncLua interpreter.
	/// </summary>
	public class InterpreterSettings
	{
		/// <summary>
		/// Gets or sets the metatable resolution mode.
		/// Defaults to <see cref="MetatableMode.Default"/> (standard Lua semantics).
		/// </summary>
		public MetatableMode MetatableMode { get; set; } = MetatableMode.Default;

		/// <summary>
		/// Maximum call stack depth. Defaults to <see cref="AsyncLuaInterpreter.DefaultMaxStackSize"/>.
		/// </summary>
		public int MaxStackSize { get; set; } = AsyncLuaInterpreter.DefaultMaxStackSize;

		public InterpreterSettings Clone()
		{
			return new InterpreterSettings
			{
				MetatableMode = this.MetatableMode,
				MaxStackSize = this.MaxStackSize,
			};
		}
	}
}
