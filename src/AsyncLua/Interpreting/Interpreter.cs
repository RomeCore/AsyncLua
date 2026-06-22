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
    public class Interpreter
    {
        /// <summary>
        /// Default maximum call stack depth.
        /// </summary>
        public const int DefaultMaxStackSize = 1024;

        /// <summary>
        /// Executes the specified function prototype and returns its first result.
        /// </summary>
        /// <param name="function">The function prototype to execute.</param>
        /// <param name="globals">The global environment table.</param>
        /// <param name="maxStackSize">Maximum call stack depth.</param>
        /// <returns>The first return value, or <c>nil</c> if no value is returned.</returns>
        public LuaValue Call(FunctionPrototype function, LuaTable globals, int maxStackSize = DefaultMaxStackSize)
        {
            return CallInternal(function, globals, maxStackSize, async: false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Executes the specified function prototype asynchronously and returns its first result.
        /// Required for functions that use <see cref="OpCode.AWAIT"/>.
        /// </summary>
        /// <param name="function">The function prototype to execute.</param>
        /// <param name="globals">The global environment table.</param>
        /// <param name="maxStackSize">Maximum call stack depth.</param>
        /// <returns>The first return value, or <c>nil</c> if no value is returned.</returns>
        public Task<LuaValue> CallAsync(FunctionPrototype function, LuaTable globals, int maxStackSize = DefaultMaxStackSize)
        {
            return CallInternal(function, globals, maxStackSize, async: true);
        }

        private async Task<LuaValue> CallInternal(FunctionPrototype function, LuaTable globals, int maxStackSize, bool async)
        {
            if (maxStackSize <= 0)
                throw new ArgumentException("Max stack size must be greater than zero.", nameof(maxStackSize));

            var callStack = new CallStackFrame[maxStackSize];
            int sp = 0;
            int pc = 0;
            var lockedObjects = new Stack<object>();

            callStack[0] = new CallStackFrame(function, returnPC: -1);
            var frame = callStack[0];
            var registers = frame.Registers;
            var constants = frame.Function.Constants;
            var instructions = frame.Function.Instructions;
            var callingContext = new LuaCallingContext(new LuaState(), globals);

            // Initialise all registers to nil.
            for (int i = 0; i < registers.Length; i++)
                registers[i] = LuaNil.Instance;

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
								registers[inst.A] = globals.Get(constants[inst.B]);
								pc++;
								break;
							}

                        case OpCode.SETGLOBAL:
							{
								globals.Set(constants[inst.B], registers[inst.A]);
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
                                // Store results in R[A]..R[A + results.Length - 1]
                                for (int i = 0; i < results.Length; i++)
                                    registers[inst.A + i] = results[i];

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

                                if (func is LuaNativeFunction nativeFunc)
                                {
                                    // Push a new call frame for bytecode execution.
                                    sp++;
                                    if (sp >= maxStackSize)
                                        throw new LuaRuntimeException("Call stack overflow.");

                                    var newFrame = new CallStackFrame(
                                        nativeFunc.Prototype,
                                        returnPC: pc,
                                        resultBase: inst.A,
                                        resultCount: inst.C)
                                    {
                                        Closure = nativeFunc
                                    };
                                    callStack[sp] = newFrame;

                                    // Copy arguments to new frame registers.
                                    var newRegs = newFrame.Registers;
                                    int copyCount = Math.Min(argCount, newRegs.Length);
                                    for (int i = 0; i < copyCount; i++)
                                        newRegs[i] = args[i];
                                    // Pad rest with nil.
                                    for (int i = copyCount; i < newRegs.Length; i++)
                                        newRegs[i] = LuaNil.Instance;

                                    // Switch to new frame.
                                    frame = newFrame;
                                    registers = newRegs;
                                    constants = nativeFunc.Prototype.Constants;
                                    instructions = nativeFunc.Prototype.Instructions;
                                    pc = 0;
                                }
                                else
                                {
                                    // C# callback function — invoke directly.
                                    LuaTuple results;
                                    if (async)
                                        results = await func.InvokeAsync(callingContext, args);
                                    else
                                        results = func.Invoke(callingContext, args);

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

								    if (sp > 0)
                                {
                                    // Close all open upvalues in the current frame.
                                    if (frame.OpenUpvalues != null)
                                    {
                                        foreach (var uv in frame.OpenUpvalues)
                                            uv?.Close();
                                    }

                                    // Return to caller frame.
                                    var currentFrame = callStack[sp];
                                    var callerFrame = callStack[sp - 1];

                                    // Copy results to caller's registers (using callee's stored result info).
                                    int destBase = currentFrame.ResultBase;
                                    int wantResults = currentFrame.ResultCount;
                                    for (int i = 0; i < resultCount && i < wantResults; i++)
                                        callerFrame.Registers[destBase + i] = registers[inst.A + i];
                                    // Pad with nil.
                                    for (int i = resultCount; i < wantResults; i++)
                                        callerFrame.Registers[destBase + i] = LuaNil.Instance;

                                    // Restore caller state.
                                    sp--;
                                    frame = callerFrame;
                                    registers = callerFrame.Registers;
                                    constants = callerFrame.Function.Constants;
                                    instructions = callerFrame.Function.Instructions;
                                    pc = currentFrame.ReturnPC;
                                }
                                else
                                {
                                    // Top-level return.
                                    if (resultCount == 0)
                                        return LuaNil.Instance;
                                    return registers[inst.A];
                                }
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

        private enum ArithOpKind { Add, Sub, Mul, Div, IDiv }

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
                _ => throw new LuaRuntimeException($"Unknown arithmetic operation: {kind}.")
            };

            return new LuaNumber(result);
        }

        // ── Comparisons ────────────────────────────────────────────────

        private enum CompareOpKind { Eq, Lt, Le, Gt, Ge }

        private static LuaValue CompareOp(LuaValue lhs, LuaValue rhs, CompareOpKind kind, Instruction inst)
        {
            bool result = kind switch
            {
                CompareOpKind.Eq => CompareEqual(lhs, rhs),
                CompareOpKind.Lt => CompareLessThan(lhs, rhs),
                CompareOpKind.Le => CompareLessOrEqual(lhs, rhs),
				CompareOpKind.Gt => CompareGreaterThan(lhs, rhs),
				CompareOpKind.Ge => CompareGreaterOrEqual(lhs, rhs),
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
    }
}
