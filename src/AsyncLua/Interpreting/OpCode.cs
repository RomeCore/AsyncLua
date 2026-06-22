using System.Collections.Generic;
using System.Text;

namespace AsyncLua.Interpreting
{
	public enum OpCode : byte
	{
		/// <summary>
		/// Move a value from one register (or constant) to another. R[A] = R/C[B]
		/// </summary>
		MOVE,

		/// <summary>
		/// Unconditional jump. pc += signed(B)
		/// </summary>
		JMP,

		/// <summary>
		/// Conditional jump. If R[A] is truthy, pc += signed(B); otherwise fall through.
		/// </summary>
		JMPIF,

		/// <summary>
		/// Return from the current function.
		/// </summary>
		RETURN,

		/// <summary>
		/// Add two values together. R[A] = R/C[B] + R/C[C]
		/// </summary>
		ADD,

		/// <summary>
		/// Subtract one value from another. R[A] = R/C[B] - R/C[C]
		/// </summary>
		SUB,

		/// <summary>
		/// Multiply two values together. R[A] = R/C[B] * R/C[C]
		/// </summary>
		MUL,

		/// <summary>
		/// Divide one value by another. R[A] = R/C[B] / R/C[C]
		/// </summary>
		DIV,

		/// <summary>
		/// Integer divide one value by another. R[A] = R/C[B] // R/C[C] (integer division)
		/// </summary>
		IDIV,

		/// <summary>
		/// Test for equality. R[A] = (R/K[B] == R/K[C]) ? true : false
		/// </summary>
		EQ,

		/// <summary>
		/// Test for less-than. R[A] = (R/K[B] &lt; R/K[C]) ? true : false
		/// </summary>
		LT,

		/// <summary>
		/// Test for less-than-or-equal. R[A] = (R/K[B] &lt;= R/K[C]) ? true : false
		/// </summary>
		LE,

		/// <summary>
		/// Create a new empty table. R[A] = {}
		/// </summary>
		NEWTABLE,

		/// <summary>
		/// Read a value from a table. R[A] = R[B][R/K[C]]
		/// </summary>
		GETTABLE,

		/// <summary>
		/// Write a value to a table. R[A][R/K[B]] = R/K[C]
		/// </summary>
		SETTABLE,

		/// <summary>
		/// Read a value from the global environment. R[A] = _G[K[B]]
		/// </summary>
		GETGLOBAL,

		/// <summary>
		/// Write a value to the global environment. _G[K[B]] = R[A]
		/// </summary>
		SETGLOBAL,

		/// <summary>
		/// Acquire an exclusive lock on the object in R[A] using <see cref="System.Threading.Monitor"/>.
		/// </summary>
		LOCK,

		/// <summary>
		/// Release the lock previously acquired by <see cref="LOCK"/> on the object in R[A].
		/// </summary>
		UNLOCK,

		/// <summary>
		/// Await a <see cref="AsyncLua.Values.LuaTask"/> stored in R[A].
		/// Suspends execution until the task completes, then stores results in R[A]..R[A+N-1].
		/// </summary>
		AWAIT,

		/// <summary>
		/// Call a function. R[A] = function, R[A+1]..R[A+B-1] = args (B-1 args),
		/// R[A]..R[A+C-2] = results (C-1 results expected).
		/// </summary>
		CALL,

		/// <summary>
		/// Create a closure from an inner prototype. R[A] = closure(K[B]), capturing upvalues.
		/// </summary>
		CLOSURE,

		/// <summary>
		/// Read an upvalue into a register. R[A] = U[B]
		/// </summary>
		GETUPVAL,

		/// <summary>
		/// Write a register value into an upvalue. U[A] = R[B]
		/// </summary>
		SETUPVAL,

		/// <summary>
		/// Close upvalues of local variables starting at R[A].
		/// </summary>
		CLOSE,
	}
}