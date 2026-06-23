using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AsyncLua.Values;

namespace AsyncLua.Interpreting
{
    /// <summary>
    /// Executes compiled Lua function prototypes using a register-based VM.
    /// </summary>
    public static class Interpreter
    {
        /// <summary>
        /// Default maximum call stack depth.
        /// </summary>
        public const int DefaultMaxStackSize = 1024;

		/// <summary>
		/// Executes the specified function prototype and returns all results as a <see cref="LuaTuple"/>.
		/// </summary>
		/// <param name="function">The function prototype to execute.</param>
		/// <param name="context">The global environment table.</param>
		/// <param name="maxStackSize">Maximum call stack depth.</param>
		/// <returns>
		/// A <see cref="LuaTuple"/> containing all return values.
		/// Use <see cref="LuaTuple.First"/> to get the first value in single-return contexts.
		/// </returns>
		public static LuaTuple Call(FunctionPrototype function, LuaCallingContext context, int maxStackSize = DefaultMaxStackSize)
        {
            return CallInternal(function, context, maxStackSize, async: false).GetAwaiter().GetResult();
        }

		/// <summary>
		/// Executes the specified function prototype asynchronously and returns all results as a <see cref="LuaTuple"/>.
		/// Required for functions that use <see cref="OpCode.AWAIT"/>.
		/// </summary>
		/// <param name="function">The function prototype to execute.</param>
		/// <param name="context">The global environment table.</param>
		/// <param name="maxStackSize">Maximum call stack depth.</param>
		/// <returns>
		/// A task that resolves to a <see cref="LuaTuple"/> containing all return values.
		/// </returns>
		public static Task<LuaTuple> CallAsync(FunctionPrototype function, LuaCallingContext context, int maxStackSize = DefaultMaxStackSize)
        {
            return CallInternal(function, context, maxStackSize, async: true);
        }

        /// <summary>
        /// Executes a function prototype with pre-filled arguments and an optional closure.
        /// Used by <see cref="LuaNativeFunction.InvokeAsync"/> and the CALL handler for async bytecode functions.
        /// </summary>
        internal static Task<LuaTuple> ExecuteAsync(
            FunctionPrototype function,
            LuaCallingContext context,
            LuaValue[] args,
            LuaNativeFunction? closure = null,
            int maxStackSize = DefaultMaxStackSize)
        {
            return CallInternal(function, context, maxStackSize, async: true, initialArgs: args, initialClosure: closure);
        }

        private static async Task<LuaTuple> CallInternal(
            FunctionPrototype function,
            LuaCallingContext context,
            int maxStackSize,
            bool async,
            LuaValue[]? initialArgs = null,
            LuaNativeFunction? initialClosure = null)
        {
            if (maxStackSize <= 0)
                throw new ArgumentException("Max stack size must be greater than zero.", nameof(maxStackSize));

            var callStack = new Stack<CallStackFrame>();
            int pc = 0;
            var lockedObjects = new Stack<object>();
            var globals = context.Globals;

            var frame = new CallStackFrame(function, returnPC: -1)
            {
                Closure = initialClosure
            };
			var registers = frame.Registers;
            var constants = frame.Function.Constants;
            var instructions = frame.Function.Instructions;

            // Fill registers: initial args first, then nil for the rest.
            if (initialArgs != null)
            {
                int copyCount = Math.Min(initialArgs.Length, registers.Length);
                for (int i = 0; i < copyCount; i++)
                    registers[i] = initialArgs[i];
                for (int i = copyCount; i < registers.Length; i++)
                    registers[i] = LuaNil.Instance;

                // Handle varargs for the initial call.
                if (function.IsVararg)
                {
                    byte fixedCount = function.ParameterCount;
                    int extraCount = initialArgs.Length - fixedCount;
                    if (extraCount > 0)
                    {
                        var varArgs = new LuaValue[extraCount];
                        for (int i = 0; i < extraCount; i++)
                            varArgs[i] = initialArgs[fixedCount + i];
                        frame.VarArgs = varArgs;
                    }
                    else
                    {
                        frame.VarArgs = Array.Empty<LuaValue>();
                    }
                }
            }
            else
            {
                for (int i = 0; i < registers.Length; i++)
                    registers[i] = LuaNil.Instance;
            }

            try
            {
                while (true)
                {
                    var inst = instructions[pc];

                    switch (inst.Code)
                    {
                        case OpCode.JMP:
							{
								pc += GetSignedOffset(inst);
								break;
							}

                        case OpCode.JMPIF:
							{
								if (registers[inst.A].ToBoolean())
									pc += GetSignedOffset(inst);
								else
									pc++;
								break;
							}

                        case OpCode.NEWTABLE:
							{
								registers[inst.A] = new LuaTable();
								pc++;
								break;
							}

                        case OpCode.GETTABLE:
                            {
                                var table = registers[inst.B] as LuaTable
                                    ?? throw new LuaRuntimeException("GETTABLE: operand B must be a table.");
                                var key = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
                                registers[inst.A] = table.Get(key);
								pc++;
								break;
                            }

                        case OpCode.SETTABLE:
                            {
                                var table = registers[inst.A] as LuaTable
                                    ?? throw new LuaRuntimeException("SETTABLE: operand A must be a table.");
                                var key = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
                                var value = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
                                table.Set(key, value);
                                pc++;
                                break;
                            }

                        case OpCode.GETGLOBAL:
                            {
                                var key = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
                                registers[inst.A] = globals.Get(key);
                                pc++;
                                break;
                            }

                        case OpCode.SETGLOBAL:
                            {
                                var key = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
                                globals.Set(key, registers[inst.A]);
                                pc++;
                                break;
                            }

                        case OpCode.LOCK:
                            {
                                var lockTarget = registers[inst.A];
                                Monitor.Enter(lockTarget);
                                lockedObjects.Push(lockTarget);
							    pc++;
							    break;
                            }

                        case OpCode.UNLOCK:
                            {
                                var lockTarget = registers[inst.A];
                                Monitor.Exit(lockTarget);
                                // Remove from tracking stack (search from top).
                                if (lockedObjects.Count > 0 && ReferenceEquals(lockedObjects.Peek(), lockTarget))
                                    lockedObjects.Pop();
							    pc++;
							    break;
                            }

                            case OpCode.CLOSURE:
                            {
                                var innerProtos = frame.Function.InnerPrototypes;
                                if (inst.B >= innerProtos.Length)
                                    throw new LuaRuntimeException("CLOSURE: inner prototype index out of range.");
                                var proto = innerProtos[inst.B];

                                var upvalueDescs = proto.UpvalueDescriptions;
                                var upvalues = new Upvalue[upvalueDescs.Length];

                                for (int i = 0; i < upvalueDescs.Length; i++)
                                {
                                    var desc = upvalueDescs[i];
                                    if (desc.IsLocal)
                                    {
                                        // Capture from the current frame.
                                        var existing = frame.OpenUpvalues?[desc.RegisterIndex];
                                        if (existing != null)
                                        {
                                            upvalues[i] = existing;
                                        }
                                        else
                                        {
                                            var upval = new Upvalue(registers, desc.RegisterIndex);
                                            if (frame.OpenUpvalues == null)
                                                frame.OpenUpvalues = new Upvalue[registers.Length];
                                            frame.OpenUpvalues[desc.RegisterIndex] = upval;
                                            upvalues[i] = upval;
                                        }
                                    }
                                    else
                                    {
                                        // Capture from an outer scope: reuse from the current closure's upvalues.
                                        var closure = frame.Closure
                                            ?? throw new LuaRuntimeException("CLOSURE: non-local upvalue requires an enclosing closure.");
                                        upvalues[i] = closure.Upvalues[desc.RegisterIndex];
                                    }
                                }

                                registers[inst.A] = new LuaNativeFunction(proto, upvalues);
								pc++;
								break;
                            }

                        case OpCode.GETUPVAL:
                            {
                                var closure = frame.Closure
                                    ?? throw new LuaRuntimeException("GETUPVAL: no closure in current frame.");
                                if (inst.B >= closure.Upvalues.Length)
                                    throw new LuaRuntimeException("GETUPVAL: invalid upvalue index.");
                                registers[inst.A] = closure.Upvalues[inst.B].Value;
								pc++;
								break;
                            }

                        case OpCode.SETUPVAL:
                            {
                                var closure = frame.Closure
                                    ?? throw new LuaRuntimeException("SETUPVAL: no closure in current frame.");
                                if (inst.A >= closure.Upvalues.Length)
                                    throw new LuaRuntimeException("SETUPVAL: invalid upvalue index.");
                                closure.Upvalues[inst.A].Value = registers[inst.B];
								pc++;
								break;
                            }

                        case OpCode.CLOSE:
                            {
                                int startReg = inst.A;
                                if (frame.OpenUpvalues != null)
                                {
                                    for (int i = startReg; i < frame.OpenUpvalues.Length; i++)
                                    {
                                        var uv = frame.OpenUpvalues[i];
                                        if (uv != null)
                                        {
                                            uv.Close();
                                            frame.OpenUpvalues[i] = null;
                                        }
                                    }
								}
								pc++;
								break;
                            }

                        case OpCode.AWAIT:
                            {
                                if (!async)
                                    throw new LuaRuntimeException("AWAIT is only supported in CallAsync, not Call.");

                                var task = registers[inst.A] as LuaTask
                                    ?? throw new LuaRuntimeException("AWAIT: operand A must be a LuaTask.");

                                var results = await task;
                                // C = 0 means "accept all results" (Lua multiple-return convention).
                                int wantResults = inst.C == 0 ? results.Count : inst.C;

                                int storeCount = Math.Min(results.Count, wantResults);
                                for (int i = 0; i < storeCount; i++)
                                    registers[inst.A + i] = results[i];
                                // Pad with nil if fewer results than expected.
                                for (int i = storeCount; i < wantResults; i++)
                                    registers[inst.A + i] = LuaNil.Instance;

								pc++;
								break;
                            }

                        case OpCode.MOVE:
							{
								registers[inst.A] = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
								pc++;
								break;
							}

                        case OpCode.ADD:
							{
								registers[inst.A] = ArithOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									ArithOpKind.Add, inst);
								pc++;
								break;
							}

                        case OpCode.SUB:
							{
								registers[inst.A] = ArithOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									ArithOpKind.Sub, inst);
								pc++;
								break;
							}

                        case OpCode.MUL:
							{
								registers[inst.A] = ArithOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									ArithOpKind.Mul, inst);
								pc++;
								break;
							}

                        case OpCode.DIV:
							{
								registers[inst.A] = ArithOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									ArithOpKind.Div, inst);
								pc++;
								break;
							}

                        case OpCode.IDIV:
							{
								registers[inst.A] = ArithOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									ArithOpKind.IDiv, inst);
								pc++;
								break;
							}

                        case OpCode.EQ:
							{
								registers[inst.A] = CompareOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									CompareOpKind.Eq, inst);
								pc++;
								break;
							}

                        case OpCode.LT:
							{
								registers[inst.A] = CompareOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									CompareOpKind.Lt, inst);
								pc++;
								break;
							}

                        case OpCode.LE:
							{
								registers[inst.A] = CompareOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									CompareOpKind.Le, inst);
								pc++;
								break;
							}

						case OpCode.GT:
							{
								registers[inst.A] = CompareOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									CompareOpKind.Gt, inst);
								pc++;
								break;
							}

						case OpCode.GE:
							{
								registers[inst.A] = CompareOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									CompareOpKind.Ge, inst);
								pc++;
								break;
							}


						case OpCode.POW:
							{
								registers[inst.A] = ArithOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									ArithOpKind.Pow, inst);
								pc++;
								break;
							}

						case OpCode.MOD:
							{
								registers[inst.A] = ArithOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									ArithOpKind.Mod, inst);
								pc++;
								break;
							}

						case OpCode.CONCAT:
							{
								var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
								var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
								registers[inst.A] = ConcatOp(lhs, rhs, inst);
								pc++;
								break;
							}

						case OpCode.UNM:
							{
								registers[inst.A] = UnmOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									inst);
								pc++;
								break;
							}

						case OpCode.NOT:
							{
								registers[inst.A] = NotOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)));
								pc++;
								break;
							}

						case OpCode.LEN:
							{
								registers[inst.A] = LenOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									inst);
								pc++;
								break;
							}

						case OpCode.NE:
							{
								registers[inst.A] = CompareOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									CompareOpKind.Ne, inst);
								pc++;
								break;
							}

						case OpCode.BAND:
							{
								registers[inst.A] = BitwiseOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									BitwiseOpKind.And, inst);
								pc++;
								break;
							}

						case OpCode.BOR:
							{
								registers[inst.A] = BitwiseOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									BitwiseOpKind.Or, inst);
								pc++;
								break;
							}

						case OpCode.BXOR:
							{
								registers[inst.A] = BitwiseOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									BitwiseOpKind.Xor, inst);
								pc++;
								break;
							}

						case OpCode.SHL:
							{
								registers[inst.A] = BitwiseOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									BitwiseOpKind.Shl, inst);
								pc++;
								break;
							}

						case OpCode.SHR:
							{
								registers[inst.A] = BitwiseOp(
									GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB)),
									GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC)),
									BitwiseOpKind.Shr, inst);
								pc++;
								break;
							}

						case OpCode.CALL:
                            {
                                var func = registers[inst.A] as LuaFunction
                                    ?? throw new LuaRuntimeException("CALL: operand A must be a function.");
								pc++;

								int argCount = inst.B;
                                int wantResults = inst.C;

                                // Collect arguments.
                                var args = new LuaValue[argCount];
                                for (int i = 0; i < argCount; i++)
                                    args[i] = registers[inst.A + 1 + i];

                                // ── Async function: launch and return a LuaTask immediately ──
                                if (func.IsAsync)
                                {
                                    var csharpTask = func.InvokeAsync(context, args);
                                    registers[inst.A] = LuaTask.FromTask(csharpTask);
                                    // pc already advanced; execution continues without blocking.
                                    break;
                                }

                                // ── Synchronous bytecode function ──
                                if (func is LuaNativeFunction nativeFunc)
                                {
                                    // Push a new call frame for bytecode execution.
                                    if (callStack.Count >= maxStackSize)
                                        throw new LuaRuntimeException("Call stack overflow.");

									callStack.Push(frame);
									var newFrame = new CallStackFrame(
                                        nativeFunc.Prototype,
                                        returnPC: pc,
                                        resultBase: inst.A,
                                        resultCount: inst.C)
                                    {
                                        Closure = nativeFunc
                                    };

                                    // Copy arguments to new frame registers.
                                    var newRegs = newFrame.Registers;
                                    int copyCount = Math.Min(argCount, newRegs.Length);
                                    for (int i = 0; i < copyCount; i++)
                                        newRegs[i] = args[i];
                                    // Pad rest with nil.
                                    for (int i = copyCount; i < newRegs.Length; i++)
                                        newRegs[i] = LuaNil.Instance;

                                    // Switch to new frame.

                                    // If the function is vararg, store extra arguments in VarArgs.
                                    if (nativeFunc.Prototype.IsVararg)
                                    {
                                        byte fixedCount = nativeFunc.Prototype.ParameterCount;
                                        int extraCount = argCount - fixedCount;
                                        if (extraCount > 0)
                                        {
                                            var varArgs = new LuaValue[extraCount];
                                            for (int i = 0; i < extraCount; i++)
                                                varArgs[i] = args[fixedCount + i];
                                            newFrame.VarArgs = varArgs;
                                        }
                                        else
                                        {
                                            newFrame.VarArgs = Array.Empty<LuaValue>();
                                        }
                                    }
                                    frame = newFrame;
                                    registers = newRegs;
                                    constants = nativeFunc.Prototype.Constants;
                                    instructions = nativeFunc.Prototype.Instructions;
                                    pc = 0;
                                }
                                else
                                {
                                    // ── Synchronous C# callback function ──
                                    LuaTuple results;
                                    if (async)
                                        results = await func.InvokeAsync(context, args);
                                    else
                                        results = func.Invoke(context, args);

                                    // Store results in R[A]..R[A + wantResults - 1].
                                    int storeCount = Math.Min(results.Count, wantResults);
                                    for (int i = 0; i < storeCount; i++)
                                        registers[inst.A + i] = results[i];
                                    // Pad with nil if fewer results than expected.
                                    for (int i = storeCount; i < wantResults; i++)
                                        registers[inst.A + i] = LuaNil.Instance;
								}
								break;
                            }

                        case OpCode.RETURN:
                            {
                                int resultCount = inst.B;
								pc++;

								if (callStack.Count > 0)
                                {
                                    // Close all open upvalues in the current frame.
                                    if (frame.OpenUpvalues != null)
                                    {
                                        foreach (var uv in frame.OpenUpvalues)
                                            uv?.Close();
                                    }

                                    // Return to caller frame.
                                    var callerFrame = callStack.Pop();

                                    // Copy results to caller's registers (using callee's stored result info).
                                    int destBase = frame.ResultBase;
                                    int wantResults = frame.ResultCount;
                                    for (int i = 0; i < resultCount && i < wantResults; i++)
                                        callerFrame.Registers[destBase + i] = registers[inst.A + i];
                                    // Pad with nil.
                                    for (int i = resultCount; i < wantResults; i++)
                                        callerFrame.Registers[destBase + i] = LuaNil.Instance;

									// Restore caller state.
									pc = frame.ReturnPC;
									frame = callerFrame;
                                    registers = callerFrame.Registers;
                                    constants = callerFrame.Function.Constants;
                                    instructions = callerFrame.Function.Instructions;
                                }
                                else
                                {
                                    // Top-level return — collect all results into a LuaTuple.
                                    var results = new LuaValue[resultCount];
                                    for (int i = 0; i < resultCount; i++)
                                        results[i] = registers[inst.A + i];
                                    return new LuaTuple(results);
                                }
                                break;
                            }

						case OpCode.FORPREP:
							{
								// R[A] -= R[A+2]; pc += sBx
								var start = registers[inst.A];
								var step = registers[inst.A + 2];
								if (!start.TryToNumber(out var s) || !step.TryToNumber(out var st))
									throw new LuaRuntimeException("FORPREP: operands must be numbers.");

								registers[inst.A] = new LuaNumber(s - st);
								pc += GetSignedOffset(inst);
								break;
							}

						case OpCode.FORLOOP:
							{
								// R[A] += R[A+2]; check condition; jump if true
								var counter = registers[inst.A];
								var limit = registers[inst.A + 1];
								var step = registers[inst.A + 2];

								if (!counter.TryToNumber(out var c) || !limit.TryToNumber(out var l) || !step.TryToNumber(out var st))
									throw new LuaRuntimeException("FORLOOP: operands must be numbers.");

								c += st;
								registers[inst.A] = new LuaNumber(c);

								bool cont;
								if (st > 0)
									cont = c <= l;
								else
									cont = c >= l;

								if (cont)
									pc += GetSignedOffset(inst);
								else
									pc++;
								break;
							}

						case OpCode.TFORCALL:
							{
								// Standard Lua TFORCALL: backup R(A)..R(A+2) to R(A+3)..R(A+5),
								// then call the backed-up function with backed-up args,
								// storing results at R(A+3).. onwards.
								var backupBase = inst.A + 3;

								// Backup f, s, var.
								registers[backupBase] = registers[inst.A];
								registers[backupBase + 1] = registers[inst.A + 1];
								registers[backupBase + 2] = registers[inst.A + 2];

								var tforFunc = registers[backupBase] as LuaFunction
									?? throw new LuaRuntimeException("TFORCALL: operand A must be a function.");

								var tforArgs = new LuaValue[] { registers[backupBase + 1], registers[backupBase + 2] };

								LuaTuple results;
								if (async)
									results = await tforFunc.InvokeAsync(context, tforArgs);
								else
									results = tforFunc.Invoke(context, tforArgs);

								// Store results over the backup area.
								int wantResults = inst.C;
								if (wantResults == 0)
									wantResults = results.Count;

								for (int i = 0; i < wantResults && i < results.Count; i++)
									registers[backupBase + i] = results[i];
								for (int i = results.Count; i < wantResults; i++)
									registers[backupBase + i] = LuaNil.Instance;

								pc++;
								break;
							}

						case OpCode.TFORLOOP:
							{
								// If R[A+1] != nil, then R[A] = R[A+1] and jump; else exit.
								if (registers[inst.A + 1].Type != LuaType.Nil)
								{
									registers[inst.A] = registers[inst.A + 1];
									pc += GetSignedOffset(inst);
								}
								else
								{
									pc++;
								}
								break;
							}

						case OpCode.VARARG:
							{
								var varArgs = frame.VarArgs ?? Array.Empty<LuaValue>();
								int want = inst.B;
								if (want == 0)
									want = varArgs.Length;

								for (int i = 0; i < want; i++)
								{
									if (i < varArgs.Length)
										registers[inst.A + i] = varArgs[i];
									else
										registers[inst.A + i] = LuaNil.Instance;
								}

								pc++;
								break;
							}

                        default:
                            throw new LuaRuntimeException($"Unknown opcode: {inst.Code}.");
                    }
                }
            }
            finally
            {
                // Release any remaining locks (in reverse order).
                while (lockedObjects.Count > 0)
                {
                    Monitor.Exit(lockedObjects.Pop());
                }
            }
        }

        // ── Operand resolution ────────────────────────────────────────

        /// <summary>
        /// Resolves an operand: if <paramref name="isConstant"/> is <see langword="true"/>,
        /// reads from the constant pool; otherwise reads from the register file.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static LuaValue GetRK(LuaValue[] registers, LuaValue[] constants, ushort value, bool isConstant)
        {
            return isConstant ? constants[value] : registers[value];
        }

        /// <summary>
        /// Extracts the signed jump offset from an instruction.
        /// If <see cref="OpFlags.SignedBX"/> is set, B is treated as a signed 16-bit value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetSignedOffset(Instruction inst)
        {
            return inst.Flags.HasFlag(OpFlags.SignedBX)
                ? unchecked((short)inst.B)
                : inst.B;
        }

        // ── Arithmetic ─────────────────────────────────────────────────

        private enum ArithOpKind { Add, Sub, Mul, Div, IDiv, Pow, Mod }

        private static LuaValue ArithOp(LuaValue lhs, LuaValue rhs, ArithOpKind kind, Instruction inst)
        {
            if (!lhs.TryToNumber(out var a) || !rhs.TryToNumber(out var b))
            {
                throw new LuaRuntimeException(
                    $"Attempt to perform arithmetic on a non-number value (instruction {inst.Code}).");
            }

            double result = kind switch
            {
                ArithOpKind.Add => a + b,
                ArithOpKind.Sub => a - b,
                ArithOpKind.Mul => a * b,
                ArithOpKind.Div => a / b,
                ArithOpKind.IDiv => Math.Floor(a / b),
                ArithOpKind.Pow => Math.Pow(a, b),
                ArithOpKind.Mod => Modulo(a, b),
                _ => throw new LuaRuntimeException($"Unknown arithmetic operation: {kind}.")
            };

            return new LuaNumber(result);
        }

        // ── Comparisons ────────────────────────────────────────────────

        private enum CompareOpKind { Eq, Lt, Le, Gt, Ge, Ne }

        private static LuaValue CompareOp(LuaValue lhs, LuaValue rhs, CompareOpKind kind, Instruction inst)
        {
            bool result = kind switch
            {
                CompareOpKind.Eq => CompareEqual(lhs, rhs),
                CompareOpKind.Lt => CompareLessThan(lhs, rhs),
                CompareOpKind.Le => CompareLessOrEqual(lhs, rhs),
				CompareOpKind.Gt => CompareGreaterThan(lhs, rhs),
				CompareOpKind.Ge => CompareGreaterOrEqual(lhs, rhs),
				CompareOpKind.Ne => !CompareEqual(lhs, rhs),
				_ => throw new LuaRuntimeException($"Unknown comparison operation: {kind}.")
            };

            return LuaBoolean.FromBoolean(result);
        }

        private static bool CompareEqual(LuaValue lhs, LuaValue rhs)
        {
            // Lua equality: values of different types are never equal (except numbers and strings
            // that convert, but in standard Lua, "1" == 1 is false).
            if (lhs.Type != rhs.Type)
                return false;

            return lhs.Equals(rhs);
        }

        private static bool CompareLessThan(LuaValue lhs, LuaValue rhs)
        {
            // Lua less-than: both must be numbers or both must be strings.
            if (lhs.Type == LuaType.Number && rhs.Type == LuaType.Number)
            {
                lhs.TryToNumber(out var a);
                rhs.TryToNumber(out var b);
                return a < b;
            }

            if (lhs.Type == LuaType.String && rhs.Type == LuaType.String)
            {
                lhs.TryToString(out var sa);
                rhs.TryToString(out var sb);
                return string.CompareOrdinal(sa, sb) < 0;
            }

            throw new LuaRuntimeException($"Attempt to compare '{lhs.TypeName}' with '{rhs.TypeName}' using '<'.");
        }

        private static bool CompareGreaterOrEqual(LuaValue lhs, LuaValue rhs)
        {
            // Lua less-or-equal: same types as less-than.
            if (lhs.Type == LuaType.Number && rhs.Type == LuaType.Number)
            {
                lhs.TryToNumber(out var a);
                rhs.TryToNumber(out var b);
                return a >= b;
            }

            if (lhs.Type == LuaType.String && rhs.Type == LuaType.String)
            {
                lhs.TryToString(out var sa);
                rhs.TryToString(out var sb);
                return string.CompareOrdinal(sa, sb) >= 0;
            }

            throw new LuaRuntimeException($"Attempt to compare '{lhs.TypeName}' with '{rhs.TypeName}' using '<='.");
        }

        private static bool CompareGreaterThan(LuaValue lhs, LuaValue rhs)
        {
            // Lua less-than: both must be numbers or both must be strings.
            if (lhs.Type == LuaType.Number && rhs.Type == LuaType.Number)
            {
                lhs.TryToNumber(out var a);
                rhs.TryToNumber(out var b);
                return a > b;
            }

            if (lhs.Type == LuaType.String && rhs.Type == LuaType.String)
            {
                lhs.TryToString(out var sa);
                rhs.TryToString(out var sb);
                return string.CompareOrdinal(sa, sb) > 0;
            }

            throw new LuaRuntimeException($"Attempt to compare '{lhs.TypeName}' with '{rhs.TypeName}' using '<'.");
        }

        private static bool CompareLessOrEqual(LuaValue lhs, LuaValue rhs)
        {
            // Lua less-or-equal: same types as less-than.
            if (lhs.Type == LuaType.Number && rhs.Type == LuaType.Number)
            {
                lhs.TryToNumber(out var a);
                rhs.TryToNumber(out var b);
                return a <= b;
            }

            if (lhs.Type == LuaType.String && rhs.Type == LuaType.String)
            {
                lhs.TryToString(out var sa);
                rhs.TryToString(out var sb);
                return string.CompareOrdinal(sa, sb) <= 0;
			}

			throw new LuaRuntimeException($"Attempt to compare '{lhs.TypeName}' with '{rhs.TypeName}' using '<='.");
		}

		// ── Modulo (floor semantics per Lua 5.3+) ───────────────────────

		/// <summary>
		/// Computes floor-modulo: <c>a - floor(a / b) * b</c>.
		/// </summary>
		private static double Modulo(double a, double b)
        {
            if (b == 0.0)
                throw new LuaRuntimeException("Attempt to perform modulo by zero.");
            return a - Math.Floor(a / b) * b;
        }

        // ── String concatenation ────────────────────────────────────────

        /// <summary>
        /// Concatenates two Lua values as strings (<c>..</c> operator).
        /// Both operands are converted to their string representation.
        /// </summary>
        private static LuaString ConcatOp(LuaValue lhs, LuaValue rhs, Instruction inst)
        {
            // In Lua, only strings and numbers can be concatenated;
            // numbers are converted to strings automatically.
            if (lhs.Type != LuaType.String && lhs.Type != LuaType.Number)
                throw new LuaRuntimeException($"Attempt to concatenate a '{lhs.TypeName}' value.");
            if (rhs.Type != LuaType.String && rhs.Type != LuaType.Number)
                throw new LuaRuntimeException($"Attempt to concatenate a '{rhs.TypeName}' value.");

            lhs.TryToString(out var sa);
            rhs.TryToString(out var sb);
            return new LuaString(sa + sb);
        }

        // ── Unary operators ─────────────────────────────────────────────

        /// <summary>
        /// Unary minus (<c>-x</c>).
        /// </summary>
        private static LuaValue UnmOp(LuaValue operand, Instruction inst)
        {
            if (!operand.TryToNumber(out var value))
                throw new LuaRuntimeException($"Attempt to perform arithmetic on a '{operand.TypeName}' value.");
            return new LuaNumber(-value);
        }

        /// <summary>
        /// Logical negation (<c>not x</c>).
        /// </summary>
        private static LuaBoolean NotOp(LuaValue operand)
        {
            return LuaBoolean.FromBoolean(!operand.ToBoolean());
        }

        /// <summary>
        /// Length operator (<c>#x</c>).
        /// </summary>
        private static LuaValue LenOp(LuaValue operand, Instruction inst)
        {
            switch (operand.Type)
            {
                case LuaType.String:
                    operand.TryToString(out var s);
                    return new LuaNumber(s!.Length);
                case LuaType.Table:
                    var table = (LuaTable)operand;
                    return new LuaNumber(table.Length);
                default:
                    throw new LuaRuntimeException(
                        $"Attempt to get length of a '{operand.TypeName}' value.");
            }
        }

        // ── Bitwise operations (Lua 5.3+) ───────────────────────────────

        private enum BitwiseOpKind { And, Or, Xor, Shl, Shr }

        /// <summary>
        /// Performs a bitwise operation on two Lua values.
        /// Both operands must be convertible to integers (Lua 5.3+ semantics).
        /// </summary>
        private static LuaValue BitwiseOp(LuaValue lhs, LuaValue rhs, BitwiseOpKind kind, Instruction inst)
        {
            if (!TryToInteger(lhs, out var a) || !TryToInteger(rhs, out var b))
                throw new LuaRuntimeException(
                    $"Attempt to perform bitwise operation on non-integer values.");

            long result = kind switch
            {
                BitwiseOpKind.And => a & b,
                BitwiseOpKind.Or => a | b,
                BitwiseOpKind.Xor => a ^ b,
                BitwiseOpKind.Shl => a << (int)b,
                BitwiseOpKind.Shr => a >> (int)b,
                _ => throw new LuaRuntimeException($"Unknown bitwise operation: {kind}.")
            };

            return new LuaNumber(result);
        }

        /// <summary>
        /// Attempts to convert a <see cref="LuaValue"/> to a 64-bit signed integer
        /// following Lua 5.3+ conversion rules (must be an exact integer value).
        /// </summary>
        private static bool TryToInteger(LuaValue value, out long result)
        {
            if (value.TryToNumber(out var d))
            {
                // Must be a finite, exact integer value.
                if (!double.IsInfinity(d) && d == Math.Floor(d) && d >= long.MinValue && d <= long.MaxValue)
                {
                    result = (long)d;
                    return true;
                }
            }
            result = 0;
            return false;
        }
    }
}
