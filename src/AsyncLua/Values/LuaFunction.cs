using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AsyncLua.Values
{
    /// <summary>
    /// Represents a callable Lua function. This is the abstract base class for both
    /// C# callbacks (<see cref="LuaCallbackFunction"/>) and compiled Lua closures.
    /// </summary>
    public abstract class LuaFunction : LuaValue
    {
        /// <inheritdoc />
        public override LuaType Type => LuaType.Function;

        /// <inheritdoc />
        public override string TypeName => "function";

		/// <summary>
		/// Determines whether this function is asynchronous.
		/// </summary>
		public abstract bool IsAsync { get; }

        /// <summary>
        /// Invokes the function asynchronously with the specified arguments.
        /// </summary>
        /// <param name="context">The calling context, providing access to the Lua runtime.</param>
        /// <param name="args">The arguments passed to the function. Never <see langword="null"/>.</param>
        /// <returns>A task that resolves to the function's return values as a <see cref="LuaTuple"/>.</returns>
        public abstract Task<LuaTuple> InvokeAsync(LuaCallingContext context, LuaValue[] args);

        /// <summary>
        /// Invokes the function synchronously. Blocks until the result is available.
        /// For async-native implementations, this may block a thread.
        /// </summary>
        /// <param name="context">The calling context, providing access to the Lua runtime.</param>
        /// <param name="args">The arguments passed to the function. Never <see langword="null"/>.</param>
        /// <returns>The function's return values as a <see cref="LuaTuple"/>.</returns>
        public LuaTuple Invoke(LuaCallingContext context, LuaValue[] args)
        {
            return InvokeAsync(context, args).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public override bool ToBoolean() => true;

        /// <inheritdoc />
        public override string ToString() => $"function: {RuntimeHelpers.GetHashCode(this):X}";

        /// <inheritdoc />
        public override bool Equals(LuaValue other) => ReferenceEquals(this, other);

        /// <inheritdoc />
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}
