using System;
using AsyncLua.Values;

namespace AsyncLua
{
    /// <summary>
    /// Represents the main Lua runtime state, holding the global environment table
    /// and serving as the root for all execution within a single Lua universe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each <see cref="LuaState"/> is an isolated Lua universe with its own global
    /// table and registered functions. Multiple states can coexist and are
    /// independently thread-safe (no shared mutable state between states).
    /// </para>
    /// </remarks>
    public class LuaState
    {
        /// <summary>
        /// Gets the global environment table (_G) for this Lua state.
        /// </summary>
        public LuaTable Globals { get; }

        /// <summary>
        /// Initialises a new instance of the <see cref="LuaState"/> class
        /// with an empty global table.
        /// </summary>
        public LuaState()
        {
            Globals = new LuaTable();
        }

        /// <summary>
        /// Registers a Lua function in the global environment under the specified name.
        /// </summary>
        /// <param name="name">The global variable name (e.g., "print", "http_get").</param>
        /// <param name="function">The function to register.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="name"/> or <paramref name="function"/> is <see langword="null"/>.
        /// </exception>
        public void Register(string name, LuaFunction function)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));
            if (function is null)
                throw new ArgumentNullException(nameof(function));

            Globals.Set(new LuaString(name), function);
        }

        /// <summary>
        /// Retrieves a value from the global environment.
        /// </summary>
        /// <param name="name">The global variable name.</param>
        /// <returns>The stored value, or <c>nil</c> if not found.</returns>
        public LuaValue GetGlobal(string name)
        {
            return Globals.Get(new LuaString(name));
        }

        /// <summary>
        /// Creates a new <see cref="LuaCallingContext"/> bound to this state.
        /// </summary>
        /// <returns>A new calling context.</returns>
        public LuaCallingContext CreateContext()
        {
            return new LuaCallingContext(this);
        }
    }
}
