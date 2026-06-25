using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AsyncLua.Values;

namespace AsyncLua.Interpreting
{
	/// <summary>
	/// Executes compiled Lua function prototypes using a register-based VM.
	/// </summary>
	public static class AsyncLuaInterpreter
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

			var metatableMode = context.Settings.MetatableMode;
			var callStack = new Stack<CallStackFrame>();
			int pc = 0;
			var lockedObjects = new Stack<object>();
			var tryHandlers = new Stack<TryHandlerInfo>();
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
				frame.RegisterTop = copyCount;

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
					try
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
									var key = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var table = registers[inst.B];
									var mode = context.Settings.MetatableMode;

									// Try direct table access first.
									if (table is LuaTable tbl)
									{
										var result = tbl.Get(key);
										if (result.Type != LuaType.Nil)
										{
											registers[inst.A] = result;
											pc++;
											break;
										}
									}

									// Key not found (or not a table) — try __index metamethod.
									var index = GetMetamethod(table, LuaMetatableEvent.Index, mode);
									if (index.Type != LuaType.Nil)
									{
										if (index is LuaFunction func)
										{
											LuaTuple mmResult;
											if (async)
												mmResult = await func.InvokeAsync(context, new[] { table, key });
											else
												mmResult = func.Invoke(context, new[] { table, key });
											registers[inst.A] = mmResult.Count > 0 ? mmResult[0] : LuaNil.Instance;
											pc++;
											break;
										}

										if (index is LuaTable indexTable)
										{
											registers[inst.A] = indexTable.Get(key);
											pc++;
											break;
										}
									}

									// Fallback: if it's a table, return nil; otherwise it's an error.
									if (table is LuaTable)
										registers[inst.A] = LuaNil.Instance;
									else
										throw new LuaRuntimeException("GETTABLE: operand B must be a table.");

									pc++;
									break;
								}

							case OpCode.SETTABLE:
								{
									var key = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var value = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var table = registers[inst.A];
									var mode = context.Settings.MetatableMode;

									if (table is LuaTable tbl)
									{
										// In Default mode, __newindex is only invoked if the key does NOT already exist.
										// In Aggressive mode, always consult __newindex.
										bool keyExists = tbl.ContainsKey(key);

										if (keyExists && mode != MetatableMode.Aggressive)
										{
											tbl.Set(key, value);
										}
										else
										{
											var newIndex = GetMetamethod(table, LuaMetatableEvent.NewIndex, mode);
											if (newIndex.Type != LuaType.Nil && newIndex is LuaFunction func)
											{
												if (async)
													await func.InvokeAsync(context, new[] { table, key, value });
												else
													func.Invoke(context, new[] { table, key, value });
											}
											else
											{
												tbl.Set(key, value);
											}
										}
									}
									else
									{
										// Not a table — try __newindex in Aggressive mode, otherwise error.
										var newIndex = GetMetamethod(table, LuaMetatableEvent.NewIndex, mode);
										if (newIndex.Type != LuaType.Nil && newIndex is LuaFunction func)
										{
											if (async)
												await func.InvokeAsync(context, new[] { table, key, value });
											else
												func.Invoke(context, new[] { table, key, value });
										}
										else
										{
											throw new LuaRuntimeException("SETTABLE: operand A must be a table.");
										}
									}

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
									if (async)
										await LuaMonitor.EnterAsync(lockTarget);
									else
										LuaMonitor.Enter(lockTarget);
									lockedObjects.Push(lockTarget);
									pc++;
									break;
								}

							case OpCode.UNLOCK:
								{
									var lockTarget = registers[inst.A];
									LuaMonitor.Exit(lockTarget);
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

									LuaTuple results;
									try
									{
										results = await task;
									}
									catch (LuaRuntimeException)
									{
										throw; // Already a LuaRuntimeException — let it propagate to TRY handlers.
									}
									catch (Exception ex)
									{
										// Wrap non-Lua exceptions (e.g. from faulted tasks) so Lua try/catch can handle them.
										throw new LuaRuntimeException(ex.Message, ex);
									}

									// C = 0 means "accept all results" (Lua multiple-return convention).
									int wantResults = inst.C == 0 ? results.Count : inst.C;
									int storeCount = Math.Min(results.Count, wantResults);
									for (int i = 0; i < storeCount; i++)
										registers[inst.A + i] = results[i];
									// Pad with nil if fewer results than expected.
									for (int i = storeCount; i < wantResults; i++)
										registers[inst.A + i] = LuaNil.Instance;

									// Track highest written register for RETURN B=0.
									int top = inst.A + Math.Max(storeCount, wantResults);
									if (top > frame.RegisterTop)
										frame.RegisterTop = top;

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
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Add, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Add, inst);
									pc++;
									break;
								}

							case OpCode.SUB:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Sub, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Sub, inst);
									pc++;
									break;
								}

							case OpCode.MUL:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Mul, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Mul, inst);
									pc++;
									break;
								}

							case OpCode.DIV:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Div, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Div, inst);
									pc++;
									break;
								}

							case OpCode.IDIV:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.IDiv, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.IDiv, inst);
									pc++;
									break;
								}

							case OpCode.EQ:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = await TryComparisonMetamethodAsync(lhs, rhs, LuaMetatableEvent.Eq, metatableMode, context);
									registers[inst.A] = mmResult.HasValue
										? LuaBoolean.FromBoolean(mmResult.Value)
										: CompareOp(lhs, rhs, CompareOpKind.Eq, inst);
									pc++;
									break;
								}

							case OpCode.LT:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									// Try __lt first, then fall back to __le on swapped operands (Lua 5.3 semantics).
									var mmResult = await TryComparisonMetamethodAsync(lhs, rhs, LuaMetatableEvent.Lt, metatableMode, context);
									if (mmResult.HasValue)
									{
										registers[inst.A] = LuaBoolean.FromBoolean(mmResult.Value);
									}
									else
									{
										var mmLeSwapped = await TryComparisonMetamethodAsync(rhs, lhs, LuaMetatableEvent.Le, metatableMode, context);
										registers[inst.A] = mmLeSwapped.HasValue
											? LuaBoolean.FromBoolean(!mmLeSwapped.Value)
											: CompareOp(lhs, rhs, CompareOpKind.Lt, inst);
									}
									pc++;
									break;
								}

							case OpCode.LE:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									// Try __le first, then fall back to __lt on swapped operands (Lua 5.3 semantics).
									var mmResult = await TryComparisonMetamethodAsync(lhs, rhs, LuaMetatableEvent.Le, metatableMode, context);
									if (mmResult.HasValue)
									{
										registers[inst.A] = LuaBoolean.FromBoolean(mmResult.Value);
									}
									else
									{
										var mmLtSwapped = await TryComparisonMetamethodAsync(rhs, lhs, LuaMetatableEvent.Lt, metatableMode, context);
										registers[inst.A] = mmLtSwapped.HasValue
											? LuaBoolean.FromBoolean(!mmLtSwapped.Value)
											: CompareOp(lhs, rhs, CompareOpKind.Le, inst);
									}
									pc++;
									break;
								}

							case OpCode.GT:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									// GT: try __lt on swapped operands (b < a), then fallback.
									var mmResult = await TryComparisonMetamethodAsync(rhs, lhs, LuaMetatableEvent.Lt, metatableMode, context);
									if (mmResult.HasValue)
									{
										registers[inst.A] = LuaBoolean.FromBoolean(mmResult.Value);
									}
									else
									{
										var mmLe = await TryComparisonMetamethodAsync(lhs, rhs, LuaMetatableEvent.Le, metatableMode, context);
										registers[inst.A] = mmLe.HasValue
											? LuaBoolean.FromBoolean(!mmLe.Value)
											: CompareOp(lhs, rhs, CompareOpKind.Gt, inst);
									}
									pc++;
									break;
								}

							case OpCode.GE:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									// GE: try __le on swapped operands (b <= a), then fallback.
									var mmResult = await TryComparisonMetamethodAsync(rhs, lhs, LuaMetatableEvent.Le, metatableMode, context);
									if (mmResult.HasValue)
									{
										registers[inst.A] = LuaBoolean.FromBoolean(mmResult.Value);
									}
									else
									{
										var mmLt = await TryComparisonMetamethodAsync(lhs, rhs, LuaMetatableEvent.Lt, metatableMode, context);
										registers[inst.A] = mmLt.HasValue
											? LuaBoolean.FromBoolean(!mmLt.Value)
											: CompareOp(lhs, rhs, CompareOpKind.Ge, inst);
									}
									pc++;
									break;
								}

							case OpCode.POW:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Pow, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Pow, inst);
									pc++;
									break;
								}

							case OpCode.MOD:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Mod, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Mod, inst);
									pc++;
									break;
								}

							case OpCode.CONCAT:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Concat, metatableMode, context);
									registers[inst.A] = mmResult ?? ConcatOp(lhs, rhs, inst);
									pc++;
									break;
								}

							case OpCode.UNM:
								{
									var operand = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var mmResult = await TryUnaryMetamethodAsync(operand, LuaMetatableEvent.Unm, metatableMode, context);
									registers[inst.A] = mmResult ?? UnmOp(operand, inst);
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
									var operand = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var mmResult = await TryUnaryMetamethodAsync(operand, LuaMetatableEvent.Len, metatableMode, context);
									registers[inst.A] = mmResult ?? LenOp(operand, inst);
									pc++;
									break;
								}

							case OpCode.NE:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									// NE: try __eq metamethod, then negate.
									var mmResult = await TryComparisonMetamethodAsync(lhs, rhs, LuaMetatableEvent.Eq, metatableMode, context);
									if (mmResult.HasValue)
										registers[inst.A] = LuaBoolean.FromBoolean(!mmResult.Value);
									else
										registers[inst.A] = CompareOp(lhs, rhs, CompareOpKind.Ne, inst);
									pc++;
									break;
								}


							case OpCode.BAND:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.BAnd, metatableMode, context);
									registers[inst.A] = mmResult ?? BitwiseOp(lhs, rhs, BitwiseOpKind.And, inst);
									pc++;
									break;
								}

							case OpCode.BOR:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.BOr, metatableMode, context);
									registers[inst.A] = mmResult ?? BitwiseOp(lhs, rhs, BitwiseOpKind.Or, inst);
									pc++;
									break;
								}

							case OpCode.BXOR:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.BXor, metatableMode, context);
									registers[inst.A] = mmResult ?? BitwiseOp(lhs, rhs, BitwiseOpKind.Xor, inst);
									pc++;
									break;
								}

							case OpCode.SHL:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.ShL, metatableMode, context);
									registers[inst.A] = mmResult ?? BitwiseOp(lhs, rhs, BitwiseOpKind.Shl, inst);
									pc++;
									break;
								}

							case OpCode.SHR:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB));
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC));
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.ShR, metatableMode, context);
									registers[inst.A] = mmResult ?? BitwiseOp(lhs, rhs, BitwiseOpKind.Shr, inst);
									pc++;
									break;
								}


							case OpCode.TRY:
								{
									int catchReg = inst.A; // 0xFF = none
									int catchPC = pc + 1 + GetSignedOffset(inst);
									tryHandlers.Push(new TryHandlerInfo
									{
										CatchPC = catchPC,
										CatchVarReg = catchReg != 0xFF ? catchReg : null,
										Used = false
									});
									pc++;
									break;
								}

							case OpCode.ENDTRY:
								{
									if (tryHandlers.Count > 0)
										tryHandlers.Pop();
									pc++;
									break;
								}

							case OpCode.THROW:
								{
									var exValue = registers[inst.A];
									throw new LuaRuntimeException(exValue.ToString());
								}

							case OpCode.CALL:
								{
									var func = registers[inst.A];
									pc++;

									int argCount = inst.B;
									int wantResults = inst.C;

									// Collect arguments.
									var args = new LuaValue[argCount];
									for (int i = 0; i < argCount; i++)
										args[i] = registers[inst.A + 1 + i];

									// ── Direct function call (bytecode or callback) ──
									if (func is LuaFunction luaFunc)
									{
										// ── Async function: launch and return a LuaTask immediately ──
										if (luaFunc.IsAsync)
										{
											var csharpTask = luaFunc.InvokeAsync(context, args);
											registers[inst.A] = LuaTask.FromTask(csharpTask);
											// pc already advanced; execution continues without blocking.
											break;
										}

										// ── Synchronous bytecode function ──
										if (luaFunc is LuaNativeFunction nativeFunc)
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
											newFrame.RegisterTop = copyCount;

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
												results = await luaFunc.InvokeAsync(context, args);
											else
												results = luaFunc.Invoke(context, args);

											// Store results in R[A]..R[A + effectiveWant - 1].
											// C=0 means "accept all results" (Lua multiple-return convention).
											int effectiveWant = wantResults == 0 ? results.Count : wantResults;
											int storeCount = Math.Min(results.Count, effectiveWant);
											for (int i = 0; i < storeCount; i++)
												registers[inst.A + i] = results[i];
											// Pad with nil if fewer results than expected.
											for (int i = storeCount; i < effectiveWant; i++)
												registers[inst.A + i] = LuaNil.Instance;

											// Update RegisterTop for RETURN B=0 to know how many values are live.
											int top = inst.A + effectiveWant;
											if (top > frame.RegisterTop)
												frame.RegisterTop = top;
										}
										break;
									}

									// ── Not a function — try __call metamethod ──
									var mode = context.Settings.MetatableMode;
									var call = GetMetamethod(func, LuaMetatableEvent.Call, mode);
									if (call.Type != LuaType.Nil && call is LuaFunction callFunc)
									{
										// Build arguments: metamethod receives (func, ...originalArgs).
										var callArgs = new LuaValue[1 + argCount];
										callArgs[0] = func;
										for (int i = 0; i < argCount; i++)
											callArgs[1 + i] = args[i];

										LuaTuple results;
										if (async)
											results = await callFunc.InvokeAsync(context, callArgs);
										else
											results = callFunc.Invoke(context, callArgs);

										// C=0 means "accept all results" (Lua multiple-return convention).
										int effectiveWant = wantResults == 0 ? results.Count : wantResults;
										int storeCount = Math.Min(results.Count, effectiveWant);
										for (int i = 0; i < storeCount; i++)
											registers[inst.A + i] = results[i];
										for (int i = storeCount; i < effectiveWant; i++)
											registers[inst.A + i] = LuaNil.Instance;

										// Update RegisterTop for RETURN B=0 to know how many values are live.
										int top = inst.A + effectiveWant;
										if (top > frame.RegisterTop)
											frame.RegisterTop = top;
									}
									else
									{
										throw new LuaRuntimeException("CALL: operand A must be a function or have __call metamethod.");
									}

									break;
								}


							case OpCode.RETURN:
								{
									// B = 0 means "variable number of results" (Lua convention).
									// Return all values from R[A] to RegisterTop (tracked by AWAIT).
									int resultCount = inst.B == 0
										? Math.Max(0, frame.RegisterTop - inst.A)
										: inst.B;
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
										// If the caller wants multiple results (wantResults=0), accept all.
										int effectiveWant = wantResults == 0 ? resultCount : wantResults;
										for (int i = 0; i < resultCount && i < effectiveWant; i++)
											callerFrame.Registers[destBase + i] = registers[inst.A + i];
										// Pad with nil.
										for (int i = resultCount; i < effectiveWant; i++)
											callerFrame.Registers[destBase + i] = LuaNil.Instance;

										// Update RegisterTop for the caller's RETURN B=0 to see these results.
										int top = destBase + effectiveWant;
										if (top > callerFrame.RegisterTop)
											callerFrame.RegisterTop = top;

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
									// Call R[A] (the iterator function) with arguments R[A+1] (state), R[A+2] (var).
									// Results are stored at R[A+3].. (for loop variables).
									// R[A+2] is updated from the first result (new var).
									// R[A+1] (state) is NOT updated — it remains as set by the initialiser
									// (e.g. pairs/ipairs), following standard Lua semantics.
									var tforFunc = registers[inst.A] as LuaFunction
										?? throw new LuaRuntimeException("TFORCALL: operand A must be a function.");

									var tforArgs = new LuaValue[] { registers[inst.A + 1], registers[inst.A + 2] };

									LuaTuple results;
									if (async)
										results = await tforFunc.InvokeAsync(context, tforArgs);
									else
										results = tforFunc.Invoke(context, tforArgs);

									// Store results at R[A+3].. (for loop variables mapped by the compiler).
									int baseResult = inst.A + 3;
									int wantResults = inst.C;
									if (wantResults == 0)
										wantResults = results.Count;

									for (int i = 0; i < wantResults && i < results.Count; i++)
										registers[baseResult + i] = results[i];
									for (int i = results.Count; i < wantResults; i++)
										registers[baseResult + i] = LuaNil.Instance;

									// Update var (R[A+2]) from the first result. Nil if exhausted.
									registers[inst.A + 2] = results.Count > 0 ? results[0] : LuaNil.Instance;

									pc++;
									break;
								}

							case OpCode.TFORLOOP:
								{
									// If R[A+2] (current value) != nil, jump to body;
									// otherwise fall through to the exit jump (emitted by the compiler).
									// R[A] (the iterator function) is preserved across iterations.
									if (registers[inst.A + 2].Type != LuaType.Nil)
									{
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
					catch (LuaRuntimeException ex) when (tryHandlers.Count > 0)
					{
						// Find the nearest unused try handler.
						TryHandlerInfo? found = null;
						var popped = new Stack<TryHandlerInfo>();
						while (tryHandlers.Count > 0)
						{
							var h = tryHandlers.Pop();
							if (!h.Used && found is null)
							{
								h.Used = true;
								found = h;
							}
							popped.Push(h);
						}
						// Push all handlers back; the used one stays on top.
						while (popped.Count > 0)
							tryHandlers.Push(popped.Pop());

						if (found.HasValue)
						{
							if (found.Value.CatchVarReg.HasValue)
								registers[found.Value.CatchVarReg.Value] = new LuaString(ex.Message);
							pc = found.Value.CatchPC;
						}
						else
						{
							throw; // rethrow — all handlers already used
						}
					}
				}
			}
			finally
			{
				// Release any remaining locks (in reverse order).
				// Only release locks owned by the current thread; after an await
				// we may have resumed on a different thread that doesn't own the lock.
				while (lockedObjects.Count > 0)
				{
					var obj = lockedObjects.Pop();
					LuaMonitor.Exit(obj);
				}
			}
		}

		// ── Metamethod helpers ─────────────────────────────────────────

		/// <summary>
		/// Looks up a metamethod handler for the specified event on the given value,
		/// respecting the current <see cref="MetatableMode"/>.
		/// </summary>
		/// <param name="value">The value whose metatable to inspect.</param>
		/// <param name="evt">The metamethod event to look up.</param>
		/// <param name="mode">The current metatable resolution mode.</param>
		/// <returns>
		/// The metamethod handler, or <see cref="LuaNil.Instance"/> if no suitable handler exists.
		/// </returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static LuaValue GetMetamethod(LuaValue value, LuaMetatableEvent evt, MetatableMode mode)
		{
			// In Aggressive mode, any type with a metatable can yield a metamethod.
			if (mode == MetatableMode.Aggressive)
			{
				var mt = value.Metatable;
				if (mt != null && mt.HasEvent(evt))
					return mt.Get(evt);
				return LuaNil.Instance;
			}

			// Default (Relaxed) mode: only tables (and userdata) are checked.
			if (value.Type == LuaType.Table)
			{
				var mt = value.Metatable;
				if (mt != null && mt.HasEvent(evt))
					return mt.Get(evt);
			}

			return LuaNil.Instance;
		}

		/// <summary>
		/// Attempts to invoke a binary metamethod synchronously.
		/// </summary>
		private static LuaValue? TryBinaryMetamethod(
			LuaValue lhs, LuaValue rhs, LuaMetatableEvent evt, MetatableMode mode,
			LuaCallingContext context)
		{
			// Try left operand first.
			var mmLhs = GetMetamethod(lhs, evt, mode);
			if (mmLhs.Type != LuaType.Nil)
			{
				if (mmLhs is LuaFunction func)
				{
					// Temporarily strip metatables to prevent infinite recursion
					// when the metamethod itself uses the same operator on the operands.
					var savedLhs = lhs.Metatable;
					var savedRhs = rhs.Metatable;
					try
					{
						lhs.Metatable = null;
						rhs.Metatable = null;
						var result = func.Invoke(context, new[] { lhs, rhs });
						return result.Count > 0 ? result[0] : LuaNil.Instance;
					}
					finally
					{
						lhs.Metatable = savedLhs;
						rhs.Metatable = savedRhs;
					}
				}
			}

			// Then try right operand.
			var mmRhs = GetMetamethod(rhs, evt, mode);
			if (mmRhs.Type != LuaType.Nil)
			{
				if (mmRhs is LuaFunction func)
				{
					var savedLhs = lhs.Metatable;
					var savedRhs = rhs.Metatable;
					try
					{
						lhs.Metatable = null;
						rhs.Metatable = null;
						var result = func.Invoke(context, new[] { lhs, rhs });
						return result.Count > 0 ? result[0] : LuaNil.Instance;
					}
					finally
					{
						lhs.Metatable = savedLhs;
						rhs.Metatable = savedRhs;
					}
				}
			}

			return null;
		}

		/// <summary>
		/// Attempts to invoke a binary metamethod (e.g., <c>__add</c>, <c>__sub</c>) asynchronously.
		/// </summary>
		private static Task<LuaValue?> TryBinaryMetamethodAsync(
			LuaValue lhs, LuaValue rhs, LuaMetatableEvent evt, MetatableMode mode,
			LuaCallingContext context)
		{
			// Fast path: just call the sync version and wrap in Task.
			return Task.FromResult(TryBinaryMetamethod(lhs, rhs, evt, mode, context));
		}

		/// <summary>
		/// Attempts to invoke a unary metamethod synchronously.
		/// </summary>
		private static LuaValue? TryUnaryMetamethod(
			LuaValue operand, LuaMetatableEvent evt, MetatableMode mode,
			LuaCallingContext context)
		{
			var mm = GetMetamethod(operand, evt, mode);
			if (mm.Type != LuaType.Nil && mm is LuaFunction func)
			{
				// Temporarily strip metatable to prevent infinite recursion
				// when the metamethod itself uses the same operator on the operand.
				var saved = operand.Metatable;
				try
				{
					operand.Metatable = null;
					var result = func.Invoke(context, new[] { operand });
					return result.Count > 0 ? result[0] : LuaNil.Instance;
				}
				finally
				{
					operand.Metatable = saved;
				}
			}
			return null;
		}

		/// <summary>
		/// Attempts to invoke a unary metamethod asynchronously.
		/// </summary>
		private static Task<LuaValue?> TryUnaryMetamethodAsync(
			LuaValue operand, LuaMetatableEvent evt, MetatableMode mode,
			LuaCallingContext context)
		{
			return Task.FromResult(TryUnaryMetamethod(operand, evt, mode, context));
		}

		/// <summary>
		/// Attempts to invoke a comparison metamethod synchronously.
		/// </summary>
		private static bool? TryComparisonMetamethod(
			LuaValue lhs, LuaValue rhs, LuaMetatableEvent evt, MetatableMode mode,
			LuaCallingContext context)
		{
			// Helper to safely invoke a comparison metamethod, stripping metatables
			// to prevent infinite recursion.
			bool? InvokeSafely(LuaFunction func)
			{
				var savedLhs = lhs.Metatable;
				var savedRhs = rhs.Metatable;
				try
				{
					lhs.Metatable = null;
					rhs.Metatable = null;
					var result = func.Invoke(context, new[] { lhs, rhs });
					return result.Count > 0 && result[0].ToBoolean();
				}
				finally
				{
					lhs.Metatable = savedLhs;
					rhs.Metatable = savedRhs;
				}
			}

			// In Default mode, only invoke if both operands share the same metamethod.
			if (mode == MetatableMode.Default)
			{
				var mmLhs = GetMetamethod(lhs, evt, mode);
				var mmRhs = GetMetamethod(rhs, evt, mode);
				if (mmLhs.Type != LuaType.Nil && ReferenceEquals(mmLhs, mmRhs))
				{
					if (mmLhs is LuaFunction func)
						return InvokeSafely(func);
				}
				return null;
			}

			// Aggressive mode: try left first, then right.
			var mmLeft = GetMetamethod(lhs, evt, MetatableMode.Aggressive);
			if (mmLeft.Type != LuaType.Nil && mmLeft is LuaFunction funcLeft)
				return InvokeSafely(funcLeft);

			var mmRight = GetMetamethod(rhs, evt, MetatableMode.Aggressive);
			if (mmRight.Type != LuaType.Nil && mmRight is LuaFunction funcRight)
				return InvokeSafely(funcRight);

			return null;
		}

		/// <summary>
		/// Attempts to invoke a comparison metamethod asynchronously.
		/// </summary>
		private static Task<bool?> TryComparisonMetamethodAsync(
			LuaValue lhs, LuaValue rhs, LuaMetatableEvent evt, MetatableMode mode,
			LuaCallingContext context)
		{
			return Task.FromResult(TryComparisonMetamethod(lhs, rhs, evt, mode, context));
		}


		// ── Operand resolution ────────────────────────────────────────

		/// <summary>
		/// Resolves an operand: if <paramref name="isConstant"/> is <see langword="true"/>,
		/// reads from the constant pool; otherwise reads from the register file.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static LuaValue GetRK(LuaValue[] registers, LuaValue[] constants, ushort value, bool isConstant)
		{
			if (isConstant)
			{
				if (value >= constants.Length)
					throw new LuaRuntimeException($"GetRK: constant index {value} out of range (constants length={constants.Length})");
				return constants[value];
			}
			else
			{
				if (value >= registers.Length)
					throw new LuaRuntimeException($"GetRK: register index {value} out of range (registers length={registers.Length})");
				return registers[value];
			}
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

			throw new LuaRuntimeException($"Attempt to compare '{lhs.TypeName}' with '{rhs.TypeName}' using '>='.");
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

			throw new LuaRuntimeException($"Attempt to compare '{lhs.TypeName}' with '{rhs.TypeName}' using '>'.");
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
		/// Stores information about an active try-catch handler in the VM.
		/// </summary>
		private struct TryHandlerInfo
		{
			/// <summary>
			/// The program counter to jump to when the catch block is entered.
			/// </summary>
			public int CatchPC;

			/// <summary>
			/// The register index to store the exception message, or <see langword="null"/> if none.
			/// </summary>
			public int? CatchVarReg;

			/// <summary>
			/// Indicates whether this handler has already been used to catch an exception.
			/// Prevents re-entering the same catch block.
			/// </summary>
			public bool Used;
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
