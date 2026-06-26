using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using AsyncLua.Values;

namespace AsyncLua.Interpreting
{
	/// <summary>
	/// Represents a compiled Lua code that is ready to execute.
	/// </summary>
	public class CompiledLuaCode
	{
		/// <summary>
		/// The calling context in which the compiled code will execute.
		/// </summary>
		public LuaCallingContext Context { get; set; }

		/// <summary>
		/// The function prototype that defines the compiled code.
		/// </summary>
		public FunctionPrototype Prototype { get; }

		/// <summary>
		/// The global table that will be used during execution.
		/// Shortcut for <c>Context.Globals</c>.
		/// </summary>
		public LuaTable Globals
		{
			get => Context.Globals;
			set => Context.Globals = value;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="CompiledLuaCode"/> class.
		/// </summary>
		/// <param name="context">The calling context in which the compiled code will execute.</param>
		/// <param name="prototype">The function prototype that defines the compiled code.</param>
		/// <exception cref="ArgumentNullException">Thrown when the context or prototype is null.</exception>
		public CompiledLuaCode(LuaCallingContext context, FunctionPrototype prototype)
		{
			Context = context ?? throw new ArgumentNullException(nameof(context));
			Prototype = prototype ?? throw new ArgumentNullException(nameof(prototype));
		}

		/// <summary>
		/// Executes the compiled code and returns the result as a Lua tuple.
		/// </summary>
		/// <returns>The result of executing the compiled code as a Lua tuple.</returns>
		public LuaTuple Execute()
		{
			return AsyncLuaInterpreter.Call(Prototype, Context);
		}

		/// <summary>
		/// Executes the compiled code asynchronously and returns the result as a Lua tuple.
		/// </summary>
		/// <returns>The result of executing the compiled code asynchronously as a Lua tuple.</returns>
		public Task<LuaTuple> ExecuteAsync()
		{
			return AsyncLuaInterpreter.CallAsync(Prototype, Context);
		}
	}
}
