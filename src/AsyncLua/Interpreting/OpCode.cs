using System.Collections.Generic;
using System.Text;

namespace AsyncLua.Interpreting
{
	/// <summary>
	/// Enumerates all bytecode instructions for the AsyncLua register-based virtual machine.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each instruction has four fields: <c>A</c> (<see cref="byte"/>), <c>B</c> (<see cref="ushort"/>),
	/// <c>C</c> (<see cref="ushort"/>), and <c>Flags</c> (<see cref="OpFlags"/>).
	/// </para>
	/// <para>
	/// <b>Flag conventions:</b>
	/// <list type="bullet">
	///   <item><description><see cref="OpFlags.KB"/> — operand B refers to a constant pool index (<c>K[B]</c>) rather than a register (<c>R[B]</c>).</description></item>
	///   <item><description><see cref="OpFlags.KC"/> — operand C refers to a constant pool index (<c>K[C]</c>) rather than a register (<c>R[C]</c>).</description></item>
	///   <item><description><see cref="OpFlags.SignedBX"/> — operand B is treated as a signed 16-bit offset (<c>sBx</c>) for jump instructions.</description></item>
	/// </list>
	/// </para>
	/// <para>
	/// <b>Operand notation:</b> <c>R/K[B]</c> means <c>R[B]</c> if <see cref="OpFlags.KB"/> is not set,
	/// or <c>K[B]</c> (constant pool) if it is. Analogous for <c>R/K[C]</c>.
	/// </para>
	/// </remarks>
	public enum OpCode : byte
	{
		/// <summary>
		/// No operation. Advances <c>pc</c> by 1. Useful as a placeholder or padding.
		/// </summary>
		NOP,

		/// <summary>
		/// Copy a value into a register. <c>R[A] = R/K[B]</c>.
		/// Set <see cref="OpFlags.KB"/> to read from the constant pool instead of a register.
		/// </summary>
		MOVE,

		/// <summary>
		/// Unconditional jump. <c>pc += sBx</c>.
		/// Requires <see cref="OpFlags.SignedBX"/>; <c>B</c> is a signed 16-bit offset.
		/// </summary>
		JMP,

		/// <summary>
		/// Conditional jump. If <c>R[A]</c> is truthy (<see cref="Values.LuaValue.ToBoolean"/>),
		/// <c>pc += sBx</c>; otherwise <c>pc++</c> (fall through).
		/// Requires <see cref="OpFlags.SignedBX"/>.
		/// </summary>
		JMPIF,

		/// <summary>
		/// Return from the current function.
		/// <c>B</c> = number of return values. Results are read from <c>R[A]..R[A+B-1]</c>.
		/// If <c>sp &gt; 0</c>, results are copied to the caller's registers at
		/// <c>caller.R[ResultBase]..caller.R[ResultBase+ResultCount-1]</c> (padded with <see langword="nil"/>),
		/// then the caller frame is restored. If <c>sp == 0</c> (top-level), the first result is returned
		/// to the host (<c>nil</c> if <c>B == 0</c>).
		/// </summary>
		RETURN,

		/// <summary>
		/// Arithmetic addition. <c>R[A] = R/K[B] + R/K[C]</c>.
		/// Both operands must be convertible to numbers; otherwise a runtime error is thrown.
		/// </summary>
		ADD,

		/// <summary>
		/// Arithmetic subtraction. <c>R[A] = R/K[B] - R/K[C]</c>.
		/// Both operands must be convertible to numbers.
		/// </summary>
		SUB,

		/// <summary>
		/// Arithmetic multiplication. <c>R[A] = R/K[B] * R/K[C]</c>.
		/// Both operands must be convertible to numbers.
		/// </summary>
		MUL,

		/// <summary>
		/// Arithmetic division. <c>R[A] = R/K[B] / R/K[C]</c>.
		/// Both operands must be convertible to numbers.
		/// </summary>
		DIV,

		/// <summary>
		/// Integer division (floor division). <c>R[A] = R/K[B] // R/K[C]</c>.
		/// Equivalent to <c>Math.Floor(a / b)</c>. Both operands must be convertible to numbers.
		/// </summary>
		IDIV,

		/// <summary>
		/// Equality test. <c>R[A] = (R/K[B] == R/K[C]) ? true : false</c>.
		/// Values of different types are never equal (standard Lua semantics).
		/// </summary>
		EQ,

		/// <summary>
		/// Less-than test. <c>R[A] = (R/K[B] &lt; R/K[C]) ? true : false</c>.
		/// Both operands must be numbers or both must be strings; otherwise a runtime error is thrown.
		/// </summary>
		LT,

		/// <summary>
		/// Less-than-or-equal test. <c>R[A] = (R/K[B] &lt;= R/K[C]) ? true : false</c>.
		/// Both operands must be numbers or both must be strings.
		/// </summary>
		LE,

		/// <summary>
		/// Greater-than test. <c>R[A] = (R/K[B] &gt; R/K[C]) ? true : false</c>.
		/// Both operands must be numbers or both must be strings.
		/// </summary>
		GT,

		/// <summary>
		/// Greater-than-or-equal test. <c>R[A] = (R/K[B] &gt;= R/K[C]) ? true : false</c>.
		/// Both operands must be numbers or both must be strings.
		/// </summary>
		GE,

		/// <summary>
		/// Create a new empty table. <c>R[A] = {}</c>.
		/// </summary>
		NEWTABLE,

		/// <summary>
		/// Read a value from a table by key. <c>R[A] = R[B][R/K[C]]</c>.
		/// <c>B</c> must reference a table; the key is <c>R[C]</c> or <c>K[C]</c> depending on <see cref="OpFlags.KC"/>.
		/// </summary>
		GETTABLE,

		/// <summary>
		/// Write a value to a table by key. <c>R[A][R/K[B]] = R/K[C]</c>.
		/// <c>A</c> must reference a table; key and value are resolved via <see cref="OpFlags.KB"/>/<see cref="OpFlags.KC"/>.
		/// </summary>
		SETTABLE,

		/// <summary>
		/// Read a value from the global environment table. <c>R[A] = _G[R/K[B]]</c>.
		/// The key is <c>R[B]</c> or <c>K[B]</c> depending on <see cref="OpFlags.KB"/>.
		/// </summary>
		GETGLOBAL,

		/// <summary>
		/// Write a value to the global environment table. <c>_G[R/K[B]] = R[A]</c>.
		/// The key is <c>R[B]</c> or <c>K[B]</c> depending on <see cref="OpFlags.KB"/>.
		/// </summary>
		SETGLOBAL,

		/// <summary>
		/// Acquire an exclusive lock on the object in <c>R[A]</c> via <see cref="System.Threading.Monitor.Enter(object)"/>.
		/// The lock is tracked on an internal stack and automatically released on frame exit (even on exception).
		/// </summary>
		LOCK,

		/// <summary>
		/// Release the lock previously acquired by <see cref="LOCK"/> on the object in <c>R[A]</c>
		/// via <see cref="System.Threading.Monitor.Exit(object)"/>.
		/// </summary>
		UNLOCK,

		/// <summary>
		/// Await completion of a <see cref="Values.LuaTask"/> stored in <c>R[A]</c>.
		/// Suspends bytecode execution (via C# <see langword="await"/>) until the task completes.
		/// <para><c>C</c> = number of results expected by the caller (same semantics as <see cref="CALL"/>).</para>
		/// <para>
		/// Results are stored in <c>R[A]..R[A+C-1]</c> (padded with <see langword="nil"/>).
		/// If <c>C == 0</c>, all task results are stored (up to available registers).
		/// </para>
		/// Only valid when the interpreter is running in async mode (<see cref="Interpreter.CallAsync"/>).
		/// </summary>
		AWAIT,

		/// <summary>
		/// Call a function. <c>R[A]</c> must be a <see cref="Values.LuaFunction"/>.
		/// <para><c>B</c> = number of arguments. Arguments are read from <c>R[A+1]..R[A+B]</c>.</para>
		/// <para><c>C</c> = number of results expected by the caller.</para>
		/// <para>
		/// For <see cref="Values.LuaNativeFunction"/> (Lua bytecode): a new call frame is pushed, arguments
		/// are copied into the callee's registers <c>R[0]..R[B-1]</c> (padded with <see langword="nil"/>),
		/// and execution switches to the callee. If the callee is vararg, extra arguments beyond
		/// <c>ParameterCount</c> are stored in <c>frame.VarArgs</c>.
		/// </para>
		/// <para>
		/// For <see cref="Values.LuaCallbackFunction"/> (C# delegate): the delegate is invoked directly.
		/// Results are stored immediately in <c>R[A]..R[A+C-1]</c> (padded with <see langword="nil"/>).
		/// </para>
		/// </summary>
		CALL,

		/// <summary>
		/// Create a closure from an inner function prototype.
		/// <c>R[A] = closure(InnerPrototypes[B])</c>.
		/// Upvalues referenced by the inner prototype are captured from the current frame
		/// (for locals) or from the enclosing closure (for non-local upvalues).
		/// </summary>
		CLOSURE,

		/// <summary>
		/// Read an upvalue into a register. <c>R[A] = U[B]</c>.
		/// Requires an active closure in the current frame.
		/// </summary>
		GETUPVAL,

		/// <summary>
		/// Write a register value into an upvalue. <c>U[A] = R[B]</c>.
		/// Requires an active closure in the current frame.
		/// </summary>
		SETUPVAL,

		/// <summary>
		/// Close all open upvalues starting from register <c>R[A]</c> upwards.
		/// Closed upvalues detach from the stack and preserve their value on the heap.
		/// </summary>
		CLOSE,

		/// <summary>
		/// Exponentiation. <c>R[A] = R/K[B] ^ R/K[C]</c>.
		/// Uses <see cref="System.Math.Pow(double, double)"/>. Both operands must be numbers.
		/// </summary>
		POW,

		/// <summary>
		/// Modulus (floor-modulo semantics per Lua 5.3+). <c>R[A] = R/K[B] % R/K[C]</c>.
		/// Computed as <c>a - Math.Floor(a / b) * b</c>. Division by zero throws a runtime error.
		/// Both operands must be numbers.
		/// </summary>
		MOD,

		/// <summary>
		/// String concatenation. <c>R[A] = R/K[B] .. R/K[C]</c>.
		/// Both operands must be strings or numbers (numbers are coerced to their string representation).
		/// Other types throw a runtime error.
		/// </summary>
		CONCAT,

		/// <summary>
		/// Unary minus. <c>R[A] = -(R/K[B])</c>.
		/// The operand must be convertible to a number.
		/// </summary>
		UNM,

		/// <summary>
		/// Logical negation. <c>R[A] = not (R/K[B])</c>.
		/// In Lua, only <see langword="false"/> and <see langword="nil"/> are falsy; all other values are truthy.
		/// </summary>
		NOT,

		/// <summary>
		/// Length operator. <c>R[A] = #(R/K[B])</c>.
		/// For strings, returns the byte length (UTF-16 code units in this implementation).
		/// For tables, returns the array boundary (largest consecutive integer key starting from 1).
		/// Other types throw a runtime error.
		/// </summary>
		LEN,

		/// <summary>
		/// Inequality test. <c>R[A] = (R/K[B] ~= R/K[C]) ? true : false</c>.
		/// Logical negation of <see cref="EQ"/>.
		/// </summary>
		NE,

		/// <summary>
		/// Bitwise AND. <c>R[A] = R/K[B] &amp; R/K[C]</c> (Lua 5.3+).
		/// Both operands are converted to 64-bit signed integers; non-integer values throw a runtime error.
		/// </summary>
		BAND,

		/// <summary>
		/// Bitwise OR. <c>R[A] = R/K[B] | R/K[C]</c> (Lua 5.3+).
		/// Both operands are converted to 64-bit signed integers.
		/// </summary>
		BOR,

		/// <summary>
		/// Bitwise XOR. <c>R[A] = R/K[B] ~ R/K[C]</c> (Lua 5.3+).
		/// Both operands are converted to 64-bit signed integers.
		/// </summary>
		BXOR,

		/// <summary>
		/// Bitwise left shift. <c>R[A] = R/K[B] &lt;&lt; R/K[C]</c> (Lua 5.3+).
		/// Both operands are converted to 64-bit signed integers. The shift amount is implicitly
		/// masked to the lower 6 bits (standard C# behaviour for <see cref="long"/>).
		/// </summary>
		SHL,

		/// <summary>
		/// Bitwise right shift. <c>R[A] = R/K[B] &gt;&gt; R/K[C]</c> (Lua 5.3+).
		/// Both operands are converted to 64-bit signed integers. The shift amount is implicitly
		/// masked to the lower 6 bits.
		/// </summary>
		SHR,

		/// <summary>
		/// Prepare a numeric for-loop for iteration.
		/// <c>R[A] -= R[A+2]; pc += sBx</c>.
		/// <para>
		/// The loop registers must be set up as:
		/// <c>R[A] = start</c>, <c>R[A+1] = limit</c>, <c>R[A+2] = step</c>.
		/// </para>
		/// <para>
		/// This instruction subtracts the step from the start value so that the subsequent
		/// <see cref="FORLOOP"/> can add it back on the first iteration.
		/// Then it jumps to the corresponding <see cref="FORLOOP"/> instruction (skipping the loop body).
		/// Requires <see cref="OpFlags.SignedBX"/>.
		/// </para>
		/// </summary>
		FORPREP,

		/// <summary>
		/// Iterate a numeric for-loop.
		/// <c>R[A] += R[A+2]</c>; then:
		/// <para>
		/// If <c>step &gt; 0</c> and <c>R[A] &lt;= R[A+1]</c>, <c>pc += sBx</c> (jump to body).
		/// If <c>step &lt;= 0</c> and <c>R[A] &gt;= R[A+1]</c>, <c>pc += sBx</c>.
		/// Otherwise <c>pc++</c> (exit the loop).
		/// </para>
		/// Requires <see cref="OpFlags.SignedBX"/>. The jump target should be the loop body
		/// (the instruction after <see cref="FORPREP"/>), not <see cref="FORPREP"/> itself.
		/// </summary>
		FORLOOP,

		/// <summary>
		/// Call the iterator function for a generic for-in loop.
		/// <para>
		/// Register layout: <c>R[A] = iterator function f</c>, <c>R[A+1] = state s</c>,
		/// <c>R[A+2] = initial variable var</c>.
		/// </para>
		/// <para>
		/// The old values of <c>R[A]..R[A+2]</c> are backed up to <c>R[A+3]..R[A+5]</c>,
		/// then <c>f(s, var)</c> is called. The <c>C</c> results (or all results if <c>C == 0</c>)
		/// are stored at <c>R[A+3]..R[A+2+C]</c>, overwriting the backup.
		/// The original <c>R[A]..R[A+2]</c> are preserved for the next iteration.
		/// </para>
		/// </summary>
		TFORCALL,

		/// <summary>
		/// Test the iterator result for a generic for-in loop and branch accordingly.
		/// <para>
		/// <c>A</c> should point to the first result slot (typically <c>base + 2</c> where
		/// <c>base</c> is the register used for <see cref="TFORCALL"/>).
		/// </para>
		/// <para>
		/// If <c>R[A+1] != nil</c>: <c>R[A] = R[A+1]</c> (update the variable for the next call),
		/// then <c>pc += sBx</c> (jump to the loop body).
		/// If <c>R[A+1] == nil</c>: <c>pc++</c> (exit the loop).
		/// </para>
		/// Requires <see cref="OpFlags.SignedBX"/>.
		/// </summary>
		TFORLOOP,

		/// <summary>
		/// Copy vararg values from the current frame's <c>VarArgs</c> array into registers.
		/// <para>
		/// If <c>B &gt; 0</c>: <c>R[A]..R[A+B-1] = VarArgs[0..B-1]</c>.
		/// Missing varargs are padded with <see langword="nil"/>.
		/// </para>
		/// <para>
		/// If <c>B == 0</c>: all vararg values are copied (the number is determined by
		/// <c>VarArgs.Length</c>).
		/// </para>
		/// The <c>VarArgs</c> array is populated during <see cref="CALL"/> when the called
		/// function is vararg and more arguments are passed than fixed parameters.
		/// </summary>
		VARARG,
	}
}
