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
		/// <returns>
		/// A <see cref="LuaTuple"/> containing all return values.
		/// Use <see cref="LuaTuple.First"/> to get the first value in single-return contexts.
		/// </returns>
		public static LuaTuple Call(FunctionPrototype function, LuaCallingContext context)
		{
			return CallInternal(function, context, async: false).GetAwaiter().GetResult();
		}

		/// <summary>
		/// Executes the specified function prototype asynchronously and returns all results as a <see cref="LuaTuple"/>.
		/// Required for functions that use <see cref="OpCode.AWAIT"/>.
		/// </summary>
		/// <param name="function">The function prototype to execute.</param>
		/// <param name="context">The global environment table.</param>
		/// <returns>
		/// A task that resolves to a <see cref="LuaTuple"/> containing all return values.
		/// </returns>
		public static Task<LuaTuple> CallAsync(FunctionPrototype function, LuaCallingContext context)
		{
			return CallInternal(function, context, async: true);
		}

		/// <summary>
		/// Executes a function prototype with pre-filled arguments and an optional closure.
		/// Used by <see cref="LuaNativeFunction.InvokeAsync"/> and the CALL handler for async bytecode functions.
		/// </summary>
		internal static Task<LuaTuple> ExecuteAsync(
			FunctionPrototype function,
			LuaCallingContext context,
			LuaValue[] args,
			LuaNativeFunction? closure = null)
		{
			return CallInternal(function, context, async: true, initialArgs: args, initialClosure: closure);
		}

		private static async Task<LuaTuple> CallInternal(
			FunctionPrototype function,
			LuaCallingContext context,
			bool async,
			LuaValue[]? initialArgs = null,
			LuaNativeFunction? initialClosure = null)
		{
			int maxStackSize = context.Settings.MaxStackSize;
			if (maxStackSize <= 0)
				throw new ArgumentException("Max stack size must be greater than zero.", nameof(maxStackSize));

			var metatableMode = context.Settings.MetatableMode;
			var callStack = new Stack<CallStackFrame>();
			int pc = 0;
			var lockedObjects = new Stack<object>();
			var tryHandlers = new Stack<TryHandlerInfo>();

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
						context.CancellationToken.ThrowIfCancellationRequested();

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
									var key = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var table = registers[inst.B];

									// Walk the __index chain to resolve the key.
									var current = table;
									while (true)
									{
										// Try direct table access first.
										if (current is LuaTable curTbl)
										{
											var result = curTbl.Get(key);
											if (result.Type != LuaType.Nil)
											{
												registers[inst.A] = result;
												pc++;
												break;
											}
										}

										// Key not found — try __index metamethod.
										var index = GetMetamethod(context.State, current, LuaMetatableEvent.Index, MetatableMode.Aggressive);
										if (index.Type == LuaType.Nil)
										{
											// No __index — return nil (or error if not a table or userdata).
											if (current is LuaTable or LuaUserData)
												registers[inst.A] = LuaNil.Instance;
											else
												throw RuntimeError($"Cannot index a value from '{current.TypeName}' (must be a table or have __index metamethod).", frame.Function, pc);
											pc++;
											break;
										}

										if (index is LuaFunction func)
										{
											LuaTuple mmResult;
											if (async)
												mmResult = await func.InvokeAsync(context, new[] { current, key });
											else
												mmResult = func.Invoke(context, new[] { current, key });
											registers[inst.A] = mmResult.Count > 0 ? mmResult[0] : LuaNil.Instance;
											pc++;
											break;
										}

										if (index is LuaTable indexTable)
										{
											// Chain to the index table and continue the loop.
											current = indexTable;
											continue;
										}

										// __index is some other non-nil, non-function, non-table value — return nil.
										registers[inst.A] = LuaNil.Instance;
										pc++;
										break;
									}

									break;
								}

							case OpCode.SETTABLE:
								{
									var key = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var value = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
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
											// Not a table — try __newindex in Aggressive mode, otherwise error.
											var newIndex = GetMetamethod(context.State, table, LuaMetatableEvent.NewIndex, MetatableMode.Aggressive);
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
										var newIndex = GetMetamethod(context.State, table, LuaMetatableEvent.NewIndex, MetatableMode.Aggressive);
										if (newIndex.Type != LuaType.Nil && newIndex is LuaFunction func)
										{
											if (async)
												await func.InvokeAsync(context, new[] { table, key, value });
											else
												func.Invoke(context, new[] { table, key, value });
										}
										else
										{
											throw RuntimeError($"Cannot set a value into '{table.TypeName}' (must be a table or have __newindex metamethod).", frame.Function, pc);
										}
									}

									pc++;
									break;
								}

							case OpCode.SETLIST:
								{
									var table = registers[inst.A];
									if (table is not LuaTable tbl)
										throw RuntimeError("SETLIST: operand A must be a table.", frame.Function, pc);

									// B is a constant pool index (KB flag must be set) pointing to the start index.
									if (!inst.Flags.HasFlag(OpFlags.KB))
										throw RuntimeError("SETLIST: KB flag must be set; B must be a constant index.", frame.Function, pc);
									var startIndexValue = constants[inst.B];
									if (!startIndexValue.TryToNumber(out var startIndex))
										throw RuntimeError("SETLIST: constant at K[B] must be a number.", frame.Function, pc);

									int valueBase = inst.C;
									int count = frame.RegisterTop - valueBase;
									if (count < 0)
										count = 0;

									for (int i = 0; i < count; i++)
									{
										tbl.Set(new LuaNumber(startIndex + i), registers[valueBase + i]);
									}

									pc++;
									break;
								}

							case OpCode.GETGLOBAL:
								{
									var key = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var targetGlobals = frame.Globals ?? context.Globals;
									if (key is LuaString keyStr && keyStr.Value == "_ENV")
									{
										registers[inst.A] = targetGlobals;
									}
									else
									{
										registers[inst.A] = targetGlobals.Get(key);
									}
									pc++;
									break;
								}

							case OpCode.SETGLOBAL:
								{
									var key = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var targetGlobals = frame.Globals ?? context.Globals;
									if (key is LuaString keyStr && keyStr.Value == "_ENV")
									{
										var newEnv = registers[inst.A];
										if (newEnv is LuaTable envTable)
										{
											if (frame.Globals is not null)
												frame.Globals = envTable;
											else
												context.Globals = envTable;
										}
									}
									else
									{
										targetGlobals.Set(key, registers[inst.A]);
									}
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
										throw RuntimeError("CLOSURE: inner prototype index out of range.", frame.Function, pc);
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
												?? throw RuntimeError("CLOSURE: non-local upvalue requires an enclosing closure.", frame.Function, pc);
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
										?? throw RuntimeError("GETUPVAL: no closure in current frame.", frame.Function, pc);
									if (inst.B >= closure.Upvalues.Length)
										throw RuntimeError("GETUPVAL: invalid upvalue index.", frame.Function, pc);
									registers[inst.A] = closure.Upvalues[inst.B].Value;
									pc++;
									break;
								}

							case OpCode.SETUPVAL:
								{
									var closure = frame.Closure
										?? throw RuntimeError("SETUPVAL: no closure in current frame.", frame.Function, pc);
									if (inst.A >= closure.Upvalues.Length)
										throw RuntimeError("SETUPVAL: invalid upvalue index.", frame.Function, pc);
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
										throw RuntimeError("Awaiting is only supported in asyncronous contexts.", frame.Function, pc);

									LuaTuple results;
									var awaitable = registers[inst.A];

									var awaitMm = GetMetamethod(context.State, awaitable, LuaMetatableEvent.Await, metatableMode);
									if (awaitMm is LuaFunction awaitFunction)
									{
										try
										{
											results = await awaitFunction.InvokeAsync(context, awaitable);
										}
										catch (LuaRuntimeException)
										{
											throw; // Already a LuaRuntimeException — let it propagate to TRY handlers.
										}
										catch (Exception ex)
										{
											// Wrap non-Lua exceptions (e.g. from faulted tasks) so Lua try/catch can handle them.
											throw RuntimeError(ex.Message, frame.Function, pc, ex);
										}

									}
									else
									{
										var task = awaitable as LuaTask
											?? throw RuntimeError("Cannot await a non-LuaTask or a value that does not have an __await metamethod.", frame.Function, pc);

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
											throw RuntimeError(ex.Message, frame.Function, pc, ex);
										}
									}

									// C = 0 means "accept all results" (Lua multiple-return convention).
									int wantResults = inst.C == 0 ? results.Count : inst.C;
									int storeCount = Math.Min(results.Count, wantResults);
									// Bound writes to available register space.
									int maxWrite = registers.Length - inst.A;
									if (storeCount > maxWrite)
										storeCount = maxWrite;
									if (wantResults > maxWrite)
										wantResults = maxWrite;
									for (int i = 0; i < storeCount; i++)
										registers[inst.A + i] = results[i];
									// Pad with nil if fewer results than expected.
									for (int i = storeCount; i < wantResults; i++)
										registers[inst.A + i] = LuaNil.Instance;

									// If zero results and C=0 ("accept all"), write nil into R[A]
									// so that subsequent code / RETURN B=0 sees a clean value.
									if (storeCount == 0 && results.Count == 0 && inst.C == 0)
									{
										registers[inst.A] = LuaNil.Instance;
										storeCount = 1;
									}

									// Track highest written register for RETURN B=0.
									int top = inst.A + Math.Max(storeCount, wantResults);
									if (top > frame.RegisterTop)
										frame.RegisterTop = top;

									pc++;
									break;
								}

							case OpCode.MOVE:
								{
									registers[inst.A] = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									pc++;
									break;
								}

							case OpCode.ADD:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Add, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Add, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.SUB:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Sub, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Sub, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.MUL:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Mul, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Mul, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.DIV:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Div, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Div, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.IDIV:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.IDiv, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.IDiv, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.EQ:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = await TryComparisonMetamethodAsync(lhs, rhs, LuaMetatableEvent.Eq, metatableMode, context);
									registers[inst.A] = mmResult.HasValue
										? LuaBoolean.FromBoolean(mmResult.Value)
										: CompareOp(lhs, rhs, CompareOpKind.Eq, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.LT:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
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
											: CompareOp(lhs, rhs, CompareOpKind.Lt, inst, frame, pc);
									}
									pc++;
									break;
								}

							case OpCode.LE:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
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
											: CompareOp(lhs, rhs, CompareOpKind.Le, inst, frame, pc);
									}
									pc++;
									break;
								}

							case OpCode.GT:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
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
											: CompareOp(lhs, rhs, CompareOpKind.Gt, inst, frame, pc);
									}
									pc++;
									break;
								}

							case OpCode.GE:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
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
											: CompareOp(lhs, rhs, CompareOpKind.Ge, inst, frame, pc);
									}
									pc++;
									break;
								}

							case OpCode.POW:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Pow, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Pow, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.MOD:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Mod, metatableMode, context);
									registers[inst.A] = mmResult ?? ArithOp(lhs, rhs, ArithOpKind.Mod, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.CONCAT:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var result = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.Concat, metatableMode, context);
									if (result == null)
									{
										// In Lua, only strings and numbers can be concatenated;
										// numbers are converted to strings automatically.
										if (lhs.Type != LuaType.String && lhs.Type != LuaType.Number)
											throw RuntimeError($"Cannot concatenate '{lhs.TypeName}' with '{rhs.TypeName}'.", frame.Function, pc);
										if (rhs.Type != LuaType.String && rhs.Type != LuaType.Number)
											throw RuntimeError($"Cannot concatenate '{lhs.TypeName}' with '{rhs.TypeName}'.", frame.Function, pc);

										lhs.TryToString(out var sa);
										rhs.TryToString(out var sb);
										result = new LuaString(sa + sb);
									}
									registers[inst.A] = result;
									pc++;
									break;
								}

							case OpCode.UNM:
								{
									var operand = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var result = await TryUnaryMetamethodAsync(operand, LuaMetatableEvent.Unm, metatableMode, context);
									if (result == null)
									{
										if (!operand.TryToNumber(out var value))
											throw RuntimeError($"Cannot perform arithmetic (unary minus) on a '{operand.TypeName}' value (must have __unm metamethod or be a number).", frame.Function, pc);
										result = new LuaNumber(-value);
									}
									registers[inst.A] = result;
									pc++;
									break;
								}

							case OpCode.NOT:
								{
									registers[inst.A] = LuaBoolean.FromBoolean(
										!GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc).ToBoolean());
									pc++;
									break;
								}

							case OpCode.LEN:
								{
									var operand = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var result = await TryUnaryMetamethodAsync(operand, LuaMetatableEvent.Len, metatableMode, context);
									if (result == null)
									{
										switch (operand.Type)
										{
											case LuaType.String:
												operand.TryToString(out var s);
												result = new LuaNumber(s!.Length);
												break;
											case LuaType.Table:
												var table = (LuaTable)operand;
												result = new LuaNumber(table.Length);
												break;
											default:
												throw RuntimeError($"Cannot get length of a '{operand.TypeName}' value (must have __len metamethod or be a string or table).", frame.Function, pc);
										}
									}
									registers[inst.A] = result;
									pc++;
									break;
								}

							case OpCode.NE:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									// NE: try __eq metamethod, then negate.
									var mmResult = await TryComparisonMetamethodAsync(lhs, rhs, LuaMetatableEvent.Eq, metatableMode, context);
									if (mmResult.HasValue)
										registers[inst.A] = LuaBoolean.FromBoolean(!mmResult.Value);
									else
										registers[inst.A] = CompareOp(lhs, rhs, CompareOpKind.Ne, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.BAND:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.BAnd, metatableMode, context);
									registers[inst.A] = mmResult ?? BitwiseOp(lhs, rhs, BitwiseOpKind.And, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.BOR:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.BOr, metatableMode, context);
									registers[inst.A] = mmResult ?? BitwiseOp(lhs, rhs, BitwiseOpKind.Or, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.BXOR:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.BXor, metatableMode, context);
									registers[inst.A] = mmResult ?? BitwiseOp(lhs, rhs, BitwiseOpKind.Xor, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.SHL:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.ShL, metatableMode, context);
									registers[inst.A] = mmResult ?? BitwiseOp(lhs, rhs, BitwiseOpKind.Shl, inst, frame, pc);
									pc++;
									break;
								}

							case OpCode.SHR:
								{
									var lhs = GetRK(registers, constants, inst.B, inst.Flags.HasFlag(OpFlags.KB), frame, pc);
									var rhs = GetRK(registers, constants, inst.C, inst.Flags.HasFlag(OpFlags.KC), frame, pc);
									var mmResult = TryBinaryMetamethod(lhs, rhs, LuaMetatableEvent.ShR, metatableMode, context);
									registers[inst.A] = mmResult ?? BitwiseOp(lhs, rhs, BitwiseOpKind.Shr, inst, frame, pc);
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
										CallStackDepth = callStack.Count,
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
									throw RuntimeError(exValue.ToString(), frame.Function, pc);
								}

							case OpCode.CALL:
								{
									var func = registers[inst.A];
									pc++;

									int wantResults = inst.C;

									// Collect arguments.
									LuaValue[] args;
									bool hasVarArgCall = inst.Flags.HasFlag(OpFlags.VarArgCall);
									if (hasVarArgCall)
									{
										// Last argument is vararg: read fixed args from registers,
										// then append varargs from the current frame.
										var frameVarArgs = frame.VarArgs ?? Array.Empty<LuaValue>();
										// inst.B includes 1 for the vararg placeholder.
										int fixedCount = inst.B - 1;
										args = new LuaValue[fixedCount + frameVarArgs.Length];
										for (int i = 0; i < fixedCount; i++)
											args[i] = registers[inst.A + 1 + i];
										for (int i = 0; i < frameVarArgs.Length; i++)
											args[fixedCount + i] = frameVarArgs[i];
									}
									else
									{
										args = new LuaValue[inst.B];
										for (int i = 0; i < inst.B; i++)
											args[i] = registers[inst.A + 1 + i];
									}
									int argCount = args.Length;

									// ── Direct function call (bytecode or callback) ──
									if (func is LuaFunction luaFunc)
									{
										// ── Async function: launch and return a LuaTask immediately ──
										if (luaFunc.IsAsync)
										{
											try
											{
												var csharpTask = luaFunc.InvokeAsync(context, args);
												registers[inst.A] = LuaTask.FromTask(csharpTask);
												// Clear the next register to prevent stale values (e.g. LuaThread
												// from a previous CALL argument) from leaking into subsequent
												// multi-return after AWAIT with fewer results.
												if (inst.A + 1 < registers.Length)
													registers[inst.A + 1] = LuaNil.Instance;
												frame.RegisterTop = Math.Max(frame.RegisterTop, inst.A + 1);
											}
											catch (LuaRuntimeException ex) when (!ex.HasPosition)
											{
												throw RuntimeError(ex.OriginalMessage, function, pc - 1, ex.InnerException);
											}
											// pc already advanced; execution continues without blocking.
											break;
										}

										// ── Synchronous bytecode function ──
										if (luaFunc is LuaNativeFunction nativeFunc)
										{
											// Push a new call frame for bytecode execution.
											if (callStack.Count >= maxStackSize)
												throw RuntimeError("Call stack overflow.", frame.Function, pc - 1);

											callStack.Push(frame);
											var newFrame = new CallStackFrame(
												nativeFunc.Prototype,
												returnPC: pc,
												resultBase: inst.A,
												resultCount: inst.C)
											{
												Closure = nativeFunc,
												Globals = nativeFunc.Environment
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
											try
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
												// Bound writes to available register space.
												int callMaxWrite = registers.Length - inst.A;
												if (storeCount > callMaxWrite)
													storeCount = callMaxWrite;
												if (effectiveWant > callMaxWrite)
													effectiveWant = callMaxWrite;
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
											catch (LuaRuntimeException ex) when (!ex.HasPosition)
											{
												throw RuntimeError(ex.OriginalMessage, function, pc - 1, ex.InnerException);
											}
										}
										break;
									}

									// ── Not a function — try __call metamethod ──
									var mode = context.Settings.MetatableMode;
									var call = GetMetamethod(context.State, func, LuaMetatableEvent.Call, mode);
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
										// Bound writes to available register space.
										int mmMaxWrite = registers.Length - inst.A;
										if (storeCount > mmMaxWrite)
											storeCount = mmMaxWrite;
										if (effectiveWant > mmMaxWrite)
											effectiveWant = mmMaxWrite;
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
										throw RuntimeError("Call target must be a function or have __call metamethod.", frame.Function, pc - 1);
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
										throw RuntimeError("Operands in for loop must be numbers.", frame.Function, pc);

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
										throw RuntimeError("Operands in for loop must be numbers.", frame.Function, pc);

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
										?? throw RuntimeError("Operand in for-in loop must be a function.", frame.Function, pc);

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

									// Bound to available register space.
									int maxWrite = registers.Length - inst.A;
									if (want > maxWrite)
										want = maxWrite;

									for (int i = 0; i < want; i++)
									{
										if (i < varArgs.Length)
											registers[inst.A + i] = varArgs[i];
										else
											registers[inst.A + i] = LuaNil.Instance;
									}

									// Track highest written register for RETURN B=0.
									int top = inst.A + want;
									if (top > frame.RegisterTop)
										frame.RegisterTop = top;

									pc++;
									break;
								}

							default:
								throw RuntimeError($"Unknown opcode: {inst.Code}.", frame.Function, pc);
						}
					}
					catch (LuaRuntimeException ex) when (tryHandlers.Count > 0)
					{
						// Find the nearest unused try handler (top of stack = innermost).
						// We must preserve the original order of handlers.
						TryHandlerInfo? found = null;
						var preserved = new List<TryHandlerInfo>();
						while (tryHandlers.Count > 0)
						{
							var h = tryHandlers.Pop();
							if (!h.Used && found is null)
							{
								h.Used = true;
								found = h;
							}
							preserved.Add(h);
						}
						// Push handlers back in reverse order (to restore original stack order).
						for (int i = preserved.Count - 1; i >= 0; i--)
							tryHandlers.Push(preserved[i]);

						if (found.HasValue)
						{
							var handler = found.Value;

							// Unwind the call stack to the frame that owns this handler.
							// The current frame (callee) is in 'frame'; its caller is on top of callStack.
							// We need to pop callStack until its Count equals handler.CallStackDepth,
							// and restore the last popped frame as the current frame.
							CallStackFrame? restoredFrame = null;
							while (callStack.Count > handler.CallStackDepth)
							{
								restoredFrame = callStack.Pop();
								// Close upvalues in frames we skip over.
								if (restoredFrame.Value.OpenUpvalues != null)
								{
									foreach (var uv in restoredFrame.Value.OpenUpvalues)
										uv?.Close();
								}
							}

							// If restoredFrame is null, the handler belongs to the current frame
							// (which is already active). No unwinding needed.
							if (restoredFrame.HasValue)
							{
								frame = restoredFrame.Value;
								registers = frame.Registers;
								constants = frame.Function.Constants;
								instructions = frame.Function.Instructions;
							}

							// Remove handlers that belonged to unwound frames (they are no longer valid).
							while (tryHandlers.Count > 0 && tryHandlers.Peek().CallStackDepth > callStack.Count)
								tryHandlers.Pop();

							if (handler.CatchVarReg.HasValue)
								registers[handler.CatchVarReg.Value] = new LuaString(ex.OriginalMessage);
							pc = handler.CatchPC;
						}
						else
						{
							// rethrow — all handlers already used
							if (!ex.HasPosition)
								throw RuntimeError(ex.OriginalMessage, function, pc, ex.InnerException);
							throw;
						}
					}
					catch (LuaRuntimeException ex) when (!ex.HasPosition)
					{
						throw RuntimeError(ex.OriginalMessage, function, pc, ex.InnerException);
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

		// ── Error helpers ───────────────────────────────────────────────

		/// <summary>
		/// Creates a <see cref="LuaRuntimeException"/> with source position information
		/// from the current instruction pointer, if available.
		/// </summary>
		/// <param name="message">The error message.</param>
		/// <param name="function">The function prototype being executed.</param>
		/// <param name="pc">The current program counter (instruction index).</param>
		/// <returns>A new <see cref="LuaRuntimeException"/> with position information if available.</returns>
		private static LuaRuntimeException RuntimeError(string message, FunctionPrototype function, int pc)
		{
			var positions = function.Positions;
			if (positions != null && pc >= 0 && pc < positions.Length)
			{
				var pos = positions[pc];
				if (pos.IsValid)
					return new LuaRuntimeException(message, pos);
			}
			return new LuaRuntimeException(message);
		}

		/// <summary>
		/// Creates a <see cref="LuaRuntimeException"/> with source position information
		/// from the current instruction pointer and an inner exception, if available.
		/// </summary>
		private static LuaRuntimeException RuntimeError(string message, FunctionPrototype function, int pc, Exception? inner)
		{
			var positions = function.Positions;
			if (positions != null && pc >= 0 && pc < positions.Length)
			{
				var pos = positions[pc];
				if (pos.IsValid)
					return new LuaRuntimeException(message, pos, inner);
			}
			return new LuaRuntimeException(message, inner);
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
		private static LuaValue GetMetamethod(LuaState state, LuaValue value, LuaMetatableEvent evt, MetatableMode mode)
		{
			// In Aggressive mode, any type with a metatable can yield a metamethod.
			if (mode == MetatableMode.Aggressive)
			{
				// 1. Check the individual metatable (per-object).
				var mt = value.Metatable;
				if (mt != null && mt.HasEvent(evt))
					return mt.Get(evt);

				// 2. Fall back to the type-level metatable (shared for the Lua type).
				if (state.TypeMetatables.TryGetValue(value.Type, out var typeMt)
					&& typeMt.HasEvent(evt))
					return typeMt.Get(evt);

				return LuaNil.Instance;
			}

			// Default (Relaxed) mode: only tables (and userdata) are checked.
			if (value.Type == LuaType.Table || value.Type == LuaType.UserData)
			{
				// 1. Individual metatable first.
				var mt = value.Metatable;
				if (mt != null && mt.HasEvent(evt))
					return mt.Get(evt);

				// 2. Type metatable as fallback.
				if (state.TypeMetatables.TryGetValue(value.Type, out var typeMt)
					&& typeMt.HasEvent(evt))
					return typeMt.Get(evt);
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
			var mmLhs = GetMetamethod(context.State, lhs, evt, mode);
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
			var mmRhs = GetMetamethod(context.State, rhs, evt, mode);
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
			var mm = GetMetamethod(context.State, operand, evt, mode);
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
				var mmLhs = GetMetamethod(context.State, lhs, evt, mode);
				var mmRhs = GetMetamethod(context.State, rhs, evt, mode);
				if (mmLhs.Type != LuaType.Nil && ReferenceEquals(mmLhs, mmRhs))
				{
					if (mmLhs is LuaFunction func)
						return InvokeSafely(func);
				}
				return null;
			}

			// Aggressive mode: try left first, then right.
			var mmLeft = GetMetamethod(context.State, lhs, evt, MetatableMode.Aggressive);
			if (mmLeft.Type != LuaType.Nil && mmLeft is LuaFunction funcLeft)
				return InvokeSafely(funcLeft);

			var mmRight = GetMetamethod(context.State, rhs, evt, MetatableMode.Aggressive);
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
		private static LuaValue GetRK(in LuaValue[] registers, in LuaValue[] constants, in ushort value,
			in bool isConstant, in CallStackFrame frame, in int pc)
		{
			if (isConstant)
			{
				if (value >= constants.Length)
					throw RuntimeError($"GetRK: constant index {value} out of range (constants length={constants.Length})", frame.Function, pc);
				return constants[value];
			}
			else
			{
				if (value >= registers.Length)
					throw RuntimeError($"GetRK: register index {value} out of range (constants length={constants.Length})", frame.Function, pc);
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

		private static LuaValue ArithOp(LuaValue lhs, LuaValue rhs, ArithOpKind kind, Instruction inst, CallStackFrame frame, int pc)
		{
			if (!lhs.TryToNumber(out var a) || !rhs.TryToNumber(out var b))
			{
				throw RuntimeError($"Cannot perform arithmetic on a non-number value.", frame.Function, pc);
			}

			double result = kind switch
			{
				ArithOpKind.Add => a + b,
				ArithOpKind.Sub => a - b,
				ArithOpKind.Mul => a * b,
				ArithOpKind.Div => a / b,
				ArithOpKind.IDiv => Math.Floor(a / b),
				ArithOpKind.Pow => Math.Pow(a, b),
				ArithOpKind.Mod => b != 0 ? a - Math.Floor(a / b) * b : throw RuntimeError("Cannot perform modulo with zero divisor.", frame.Function, pc - 1),
				_ => throw RuntimeError($"Unknown arithmetic operation: {kind}.", frame.Function, pc)
			};
			
			return new LuaNumber(result);
		}

		// ── Comparisons ────────────────────────────────────────────────

		private enum CompareOpKind { Eq, Lt, Le, Gt, Ge, Ne }

		private static LuaValue CompareOp(in LuaValue lhs, in LuaValue rhs, in CompareOpKind kind,
			in Instruction inst, in CallStackFrame frame, in int pc)
		{
			switch (kind)
			{
				case CompareOpKind.Eq:
					// Lua equality: values of different types are never equal (except numbers and strings
					// that convert, but in standard Lua, "1" == 1 is false).
					if (lhs.Type != rhs.Type || !lhs.Equals(rhs))
						return LuaBoolean.False;
					return LuaBoolean.True;

				case CompareOpKind.Ne:

					if (lhs.Type == rhs.Type && lhs.Equals(rhs))
						return LuaBoolean.False;
					return LuaBoolean.True;

				case CompareOpKind.Lt:

					if (lhs.Type == LuaType.Number && rhs.Type == LuaType.Number)
					{
						lhs.TryToNumber(out var a);
						rhs.TryToNumber(out var b);
						return LuaBoolean.FromBoolean(a < b);
					}

					if (lhs.Type == LuaType.String && rhs.Type == LuaType.String)
					{
						lhs.TryToString(out var sa);
						rhs.TryToString(out var sb);
						return LuaBoolean.FromBoolean(string.CompareOrdinal(sa, sb) < 0);
					}

					throw RuntimeError($"Cannot compare '{lhs.TypeName}' with '{rhs.TypeName}' using '<'.", frame.Function, pc);

				case CompareOpKind.Le:

					if (lhs.Type == LuaType.Number && rhs.Type == LuaType.Number)
					{
						lhs.TryToNumber(out var a);
						rhs.TryToNumber(out var b);
						return LuaBoolean.FromBoolean(a <= b);
					}

					if (lhs.Type == LuaType.String && rhs.Type == LuaType.String)
					{
						lhs.TryToString(out var sa);
						rhs.TryToString(out var sb);
						return LuaBoolean.FromBoolean(string.CompareOrdinal(sa, sb) <= 0);
					}

					throw RuntimeError($"Cannot compare '{lhs.TypeName}' with '{rhs.TypeName}' using '<='.", frame.Function, pc);

				case CompareOpKind.Gt:

					if (lhs.Type == LuaType.Number && rhs.Type == LuaType.Number)
					{
						lhs.TryToNumber(out var a);
						rhs.TryToNumber(out var b);
						return LuaBoolean.FromBoolean(a > b);
					}

					if (lhs.Type == LuaType.String && rhs.Type == LuaType.String)
					{
						lhs.TryToString(out var sa);
						rhs.TryToString(out var sb);
						return LuaBoolean.FromBoolean(string.CompareOrdinal(sa, sb) > 0);
					}

					throw RuntimeError($"Cannot compare '{lhs.TypeName}' with '{rhs.TypeName}' using '>'.", frame.Function, pc);

				case CompareOpKind.Ge:

					if (lhs.Type == LuaType.Number && rhs.Type == LuaType.Number)
					{
						lhs.TryToNumber(out var a);
						rhs.TryToNumber(out var b);
						return LuaBoolean.FromBoolean(a >= b);
					}

					if (lhs.Type == LuaType.String && rhs.Type == LuaType.String)
					{
						lhs.TryToString(out var sa);
						rhs.TryToString(out var sb);
						return LuaBoolean.FromBoolean(string.CompareOrdinal(sa, sb) >= 0);
					}

					throw RuntimeError($"Cannot compare '{lhs.TypeName}' with '{rhs.TypeName}' using '>='.", frame.Function, pc);

				default:

					throw RuntimeError($"Unknown comparison operation: {kind}.", frame.Function, pc);
			}
		}

		// ── Bitwise operations (Lua 5.3+) ───────────────────────────────

		private enum BitwiseOpKind { And, Or, Xor, Shl, Shr }

		/// <summary>
		/// Performs a bitwise operation on two Lua values.
		/// Both operands must be convertible to integers (Lua 5.3+ semantics).
		/// </summary>
		private static LuaValue BitwiseOp(in LuaValue lhs, in LuaValue rhs, in BitwiseOpKind kind,
			in Instruction inst, in CallStackFrame frame, in int pc)
		{
			if (!TryToInteger(lhs, out var a) || !TryToInteger(rhs, out var b))
				throw RuntimeError($"Cannot perform bitwise operation on non-integer values.", frame.Function, pc);

			long result = kind switch
			{
				BitwiseOpKind.And => a & b,
				BitwiseOpKind.Or => a | b,
				BitwiseOpKind.Xor => a ^ b,
				BitwiseOpKind.Shl => a << (int)b,
				BitwiseOpKind.Shr => a >> (int)b,
				_ => throw RuntimeError($"Unknown bitwise operation: {kind}.", frame.Function, pc)
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
			/// The call stack depth at the moment this handler was registered.
			/// Used to unwind the call stack when this handler catches an exception.
			/// </summary>
			public int CallStackDepth;

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
