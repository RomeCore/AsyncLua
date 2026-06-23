using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AsyncLua.Interpreting;

namespace AsyncLua.Values
{
    /// <summary>
    /// A Lua function compiled from Lua source code. Wraps a <see cref="FunctionPrototype"/>
    /// and captured upvalues, and is executed by the <see cref="Interpreter"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the runtime representation of a function defined in Lua source code
    /// (e.g., <c>function add(a, b) return a + b end</c>). The <see cref="FunctionPrototype"/>
    /// contains the compiled bytecode, constants, and inner prototypes.
    /// </para>
    /// <para>
    /// Upvalues (<see cref="Upvalue"/>) capture variables from enclosing scopes,
    /// enabling closures. A newly-compiled top-level function has no upvalues.
    /// </para>
    /// </remarks>
    public sealed class LuaNativeFunction : LuaFunction
    {
        /// <summary>
        /// Gets the compiled function prototype containing bytecode and metadata.
        /// </summary>
        public FunctionPrototype Prototype { get; }

		/// <inheritdoc />
		public override bool IsAsync => Prototype.IsAsync;

        /// <summary>
        /// Gets the upvalues captured from enclosing scopes.
        /// An empty array indicates a top-level function or a function with no captured variables.
        /// </summary>
        public Upvalue[] Upvalues { get; }

        /// <summary>
        /// Gets or sets the environment table (_ENV) for this function.
        /// If <see langword="null"/>, the calling context's globals are used.
        /// </summary>
        public LuaTable? Environment { get; set; }

        /// <summary>
        /// Initialises a new instance of the <see cref="LuaNativeFunction"/> class.
        /// </summary>
        /// <param name="prototype">The compiled function prototype.</param>
        /// <param name="upvalues">
        /// The captured upvalues. If <see langword="null"/>, an empty array is used.
        /// </param>
        /// <param name="environment">
        /// The environment table. If <see langword="null"/>, the calling context's globals are used.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="prototype"/> is <see langword="null"/>.
        /// </exception>
        public LuaNativeFunction(
            FunctionPrototype prototype,
            Upvalue[]? upvalues = null,
            LuaTable? environment = null)
        {
            Prototype = prototype ?? throw new ArgumentNullException(nameof(prototype));
            Upvalues = upvalues ?? Array.Empty<Upvalue>();
            Environment = environment;
        }

        /// <inheritdoc />
        public override Task<LuaTuple> InvokeAsync(LuaCallingContext context, LuaValue[] args)
        {
            var effectiveContext = Environment is not null
                ? new LuaCallingContext(context.State, Environment)
                : context;

            return Interpreter.ExecuteAsync(Prototype, effectiveContext, args, closure: this);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var source = Prototype.SourceName;
            return source is not null
                ? $"function: {source}"
                : $"function: {RuntimeHelpers.GetHashCode(this):X}";
        }
    }
}
