using System;
using System.Collections.Generic;
using System.Xml.Linq;
using AsyncLua.Interpreting;
using AsyncLua.Parsing;
using AsyncLua.Parsing.Expressions;
using AsyncLua.Parsing.Statements;
using AsyncLua.Values;

namespace AsyncLua.Compiling
{
	/// <summary>
	/// Compiles a Lua AST (<see cref="BlockNode"/>) into a <see cref="FunctionPrototype"/>
	/// ready for execution by the <see cref="AsyncLuaInterpreter"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A new <see cref="AsyncLuaCompiler"/> instance is created for each function (including the top-level
	/// chunk). Inner functions are compiled recursively by creating child <see cref="AsyncLuaCompiler"/>
	/// instances linked via <see cref="_parent"/>.
	/// </para>
	/// <para>
	/// The compiler uses a linear register allocator: local variables and temporary expression
	/// results are assigned monotonically increasing register slots. No register reuse is
	/// performed (SPARC-style allocation).
	/// </para>
	/// </remarks>
	public class AsyncLuaCompiler
	{
		private readonly CompilerSettings _settings;

		// ── Output ──────────────────────────────────────────────────────

		private readonly List<Instruction> _instructions = new();
		private readonly List<CodePositionalInfo> _positions = new();
		private readonly Stack<CodePositionalInfo> _positionStack = new();
		private readonly List<LuaValue> _constants = new();
		private readonly Dictionary<LuaValue, int> _constantMap = new();
		private readonly List<FunctionPrototype> _innerPrototypes = new();
		private readonly List<UpvalueDescription> _upvalueDescriptions = new();

		// ── Register management ─────────────────────────────────────────

		/// <summary>Next available register slot.</summary>
		private int _nextRegister;

		/// <summary>Maps local variable names to their register slot.</summary>
		private readonly Dictionary<string, int> _locals = new();

		// ── Control flow ────────────────────────────────────────────────

		private readonly List<JumpFixup> _fixups = new();
		private readonly Dictionary<string, int> _labels = new();
		private readonly Stack<LoopContext> _loopStack = new();

		// ── Function metadata ───────────────────────────────────────────

		private readonly AsyncLuaCompiler? _parent;
		private readonly bool _isAsync;
		private readonly int _parameterCount;
		private readonly bool _isVararg;
		private readonly string? _sourceName;

		// ── Public API ──────────────────────────────────────────────────

		/// <summary>
		/// Compiles the given AST block into a <see cref="FunctionPrototype"/>.
		/// </summary>
		/// <param name="block">The root AST block (usually the result of <see cref="Parsing.AsyncLuaParser.Parse"/>).</param>
		/// <param name="sourceName">Optional source name for debugging (e.g., file name).</param>
		/// <returns>A compiled function prototype ready for execution.</returns>
		public static FunctionPrototype Compile(BlockNode block, CompilerSettings? settings = null, string? sourceName = null)
		{
			settings ??= new CompilerSettings();
			var compiler = new AsyncLuaCompiler(settings, parent: null, isAsync: false, parameterCount: 0,
				isVararg: false, sourceName: sourceName);
			compiler.CompileBlock(block);
			compiler.EmitReturn(); // implicit return at end of chunk
			compiler.PatchForwardJumps();
			return compiler.BuildPrototype();
		}

		// ── Constructor ─────────────────────────────────────────────────

		private AsyncLuaCompiler(
			CompilerSettings settings,
			AsyncLuaCompiler? parent,
			bool isAsync,
			int parameterCount,
			bool isVararg,
			string? sourceName)
		{
			_settings = settings;
			_parent = parent;
			_isAsync = isAsync;
			_parameterCount = parameterCount;
			_isVararg = isVararg;
			_sourceName = sourceName;
		}

		// ═══════════════════════════════════════════════════════════════
		// STATEMENT COMPILATION
		// ═══════════════════════════════════════════════════════════════

		private void CompileBlock(BlockNode block)
		{
			// Save locals to provide a new scope for the block.
			var savedLocals = new Dictionary<string, int>(_locals);
			foreach (var stmt in block.Statements)
				CompileStatement(stmt);
			// Restore locals: remove newly introduced names, restore overwritten old values.
			var keysToRemove = new List<string>();
			foreach (var k in _locals.Keys)
				if (!savedLocals.ContainsKey(k))
					keysToRemove.Add(k);
			foreach (var key in keysToRemove)
				_locals.Remove(key);
			foreach (var kv in savedLocals)
				_locals[kv.Key] = kv.Value;
		}

		private void CompileStatement(StatementNode stmt)
		{
			PushPosition(stmt.Position);
			try
			{
				switch (stmt)
				{
					case AssignmentNode assign: CompileAssignment(assign); break;
					case AugassignmentNode augassign: CompileAugassignment(augassign); break;
					case CallStatementNode callStmt: CompileCallStatement(callStmt); break;
					case ReturnNode ret: CompileReturn(ret); break;
					case IfNode ifNode: CompileIf(ifNode); break;
					case WhileNode whileNode: CompileWhile(whileNode); break;
					case RepeatNode repeatNode: CompileRepeat(repeatNode); break;
					case ForNumericNode forNum: CompileForNumeric(forNum); break;
					case ForInNode forIn: CompileForIn(forIn); break;
					case DoNode doNode: CompileDo(doNode); break;
					case BreakNode breakNode: CompileBreak(breakNode); break;
					case ContinueNode continueNode: CompileContinue(continueNode); break;
					case GotoNode gotoNode: CompileGoto(gotoNode); break;
					case LabelNode labelNode: CompileLabel(labelNode); break;
					case FunctionDeclStatementNode funcDecl: CompileFunctionDeclaration(funcDecl); break;
					case LockNode lockNode: CompileLock(lockNode); break;
					case TryCatchNode tryCatch: CompileTryCatch(tryCatch); break;
					case ThrowNode throwNode: CompileThrow(throwNode); break;
					case AwaitStatementNode awaitStmt: CompileAwaitStatement(awaitStmt); break;
					default:
						throw new LuaCompilerException($"Unknown statement type: {stmt.GetType().Name}", stmt.Position);
				}
			}
			finally
			{
				PopPosition();
			}
		}

		private VariableScope ResolveScope(VariableScope? scope)
		{
			if (scope.HasValue)
				return scope.Value;
			return _settings.IsLocalByDefault ? VariableScope.Local : VariableScope.Global;
		}

		// ── Assignment ──────────────────────────────────────────────────

		private void CompileAssignment(AssignmentNode node)
		{
			int valueCount = node.Values.Length;
			int targetCount = node.Targets.Length;

			// Pre-allocate registers for all targets and register local variables
			// BEFORE compiling values, so forward references (closures capturing
			// the variable being assigned) see the local.
			int baseReg = _nextRegister;
			for (int i = 0; i < targetCount; i++)
			{
				int reg = AllocateRegister(); // = baseReg + i
				var target = node.Targets[i];
				if (target is IdentifierNode ident)
				{
					var scope = ResolveScope(node.Scope);
					if (scope == VariableScope.Local)
					{
						_locals[ident.Name] = reg;
					}
				}
			}

			// Compile all r-values. Expression compilation may use AllocateRegister()
			// for temporary registers, which can displace subsequent value slots.
			// We record the actual destination register for each value and then
			// copy results to the expected baseReg+i positions if needed.
			int[] actualValueRegs = new int[valueCount];
			for (int i = 0; i < valueCount; i++)
			{
				int destReg = AllocateRegister();
				actualValueRegs[i] = destReg;
				CompileExpression(node.Values[i], destReg);

				// If there are more targets than values, and the value is a function call,
				// vararg, or await expression, expand it to fill the targets
				// (standard Lua multi-assignment).
				if (valueCount == 1 && targetCount > 1)
				{
					var lastVal = node.Values[i];
					if (lastVal is FunctionCallNode || lastVal is VarArgumentNode)
					{
						// Patch the CALL to expect targetCount results (exact fit).
						PatchLastCallToExact((ushort)targetCount);
					}
					// For AwaitExpressionNode, the AWAIT already uses C=0 (expand all),
					// so no patching is needed.
				}
			}

			// If the first multi-return value needs headroom (C=0 on CALL),
			// pre-allocate extra slots to avoid overwriting destination registers.
			bool isMultiReturnValue = valueCount == 1 && targetCount > 1 &&
				(node.Values[0] is FunctionCallNode || node.Values[0] is VarArgumentNode ||
				 node.Values[0] is AwaitExpressionNode);
			if (isMultiReturnValue)
			{
				while (_nextRegister < actualValueRegs[0] + targetCount + 8)
					AllocateRegister();
			}

			// Move value results to the expected baseReg+i positions for the write phase.
			// For multi-return calls (1 value → N targets), the CALL returns N results
			// into consecutive registers starting at actualValueRegs[0].
			bool isMultiReturnCall = isMultiReturnValue;
			int effectiveValueCount = isMultiReturnCall ? targetCount : valueCount;
			int copyCount = Math.Min(effectiveValueCount, targetCount);
			int[] valueRegs = new int[targetCount];

			// For targets beyond the expanded results, nil-fill is handled below.
			for (int i = 0; i < copyCount; i++)
			{
				int expectedReg = baseReg + i;
				// For multi-return calls, results land in consecutive registers
				// starting from actualValueRegs[0]. For regular expressions,
				// each value is in its own actualValueRegs[i].
				int actualReg = isMultiReturnCall
					? actualValueRegs[0] + i
					: actualValueRegs[i];
				if (actualReg != expectedReg)
				{
					valueRegs[i] = expectedReg;
					Emit(OpCode.MOVE, expectedReg, actualReg);
				}
				else
				{
					valueRegs[i] = expectedReg;
				}
			}
			// Ensure _nextRegister covers all target slots (in case valueCount < targetCount).
			if (_nextRegister < baseReg + targetCount)
				_nextRegister = baseReg + targetCount;

			// Emit nil-padding for targets with no corresponding value.
			// Skip if the single value is a function call / vararg / await (it was patched to expand).
			bool skipNilPadding = valueCount == 1 && targetCount > 1 &&
				(node.Values[0] is FunctionCallNode || node.Values[0] is VarArgumentNode ||
				 node.Values[0] is AwaitExpressionNode);
			if (!skipNilPadding)
			{
				for (int i = valueCount; i < targetCount; i++)
				{
					Emit(OpCode.MOVE, baseReg + i, GetConstantIndex(LuaNil.Instance),
						flags: OpFlags.KB);
				}
			}

			// Assign to targets.
			for (int i = 0; i < targetCount; i++)
			{
				int srcReg = i < valueCount ? valueRegs[i] : (baseReg + i);
				var target = node.Targets[i];
				if (target is IdentifierNode ident)
				{
					if (ResolveScope(node.Scope) != VariableScope.Local)
					{
						// global x = ... or reassignment: find existing local first.
						if (_locals.TryGetValue(ident.Name, out int localReg))
						{
							Emit(OpCode.MOVE, localReg, srcReg);
						}
						else
						{
							int? upvalueIndex = ResolveUpvalue(ident.Name);
							if (upvalueIndex.HasValue)
							{
								// Assign to an upvalue captured from an outer scope.
								Emit(OpCode.SETUPVAL, upvalueIndex.Value, srcReg);
							}
							else
							{
								EmitSETGLOBAL(ident.Name, srcReg);
							}
						}
					}
				}
				else if (target is IndexNode index)
				{
					CompileSetIndex(index, srcReg);
				}
				else
				{
					throw new LuaCompilerException(
						$"Assignment target must be an identifier or index, got {target.GetType().Name}.",
						target.Position);
				}
			}

			// Note: _nextRegister is monotonic — temporary registers used during
			// expression compilation are not freed. MaxRegSize covers all registers
			// ever allocated for this function.
		}

		// ── Augmented assignment ─────────────────────────────────────────

		private void CompileAugassignment(AugassignmentNode node)
		{
			OpCode op = BinaryOpToOpCode(node.Operator, node.Position);

			if (node.Left is IdentifierNode ident)
			{
				// ── Simple variable: x op= expr ─────────────────────
				//
				//   varReg = current value of x   (register or GETGLOBAL/GETUPVAL)
				//   rightReg = expr
				//   R[varReg] = R[varReg] op R[rightReg]
				//   write varReg back to x

				if (_locals.TryGetValue(ident.Name, out int localReg))
				{
					// Local variable — value is already in the register.
					int rightReg = AllocateRegister();
					CompileExpression(node.Right, rightReg);
					Emit(op, localReg, localReg, rightReg);
				}
				else
				{
					int? upvalueIndex = ResolveUpvalue(ident.Name);
					if (upvalueIndex.HasValue)
					{
						// Upvalue — read, modify, write back.
						int valueReg = AllocateRegister();
						Emit(OpCode.GETUPVAL, valueReg, upvalueIndex.Value);
						int rightReg = AllocateRegister();
						CompileExpression(node.Right, rightReg);
						Emit(op, valueReg, valueReg, rightReg);
						Emit(OpCode.SETUPVAL, upvalueIndex.Value, valueReg);
					}
					else
					{
						// Global variable.
						int keyIndex = GetConstantIndex(new LuaString(ident.Name));
						int valueReg = AllocateRegister();
						Emit(OpCode.GETGLOBAL, valueReg, keyIndex, flags: OpFlags.KB);
						int rightReg = AllocateRegister();
						CompileExpression(node.Right, rightReg);
						Emit(op, valueReg, valueReg, rightReg);
						EmitSETGLOBAL(ident.Name, valueReg);
					}
				}
			}
			else if (node.Left is IndexNode index)
			{
				// ── Table element: t[i] op= expr ────────────────────
				//
				//   tableReg = t
				//   indexReg = i
				//   valueReg = t[i]
				//   rightReg = expr
				//   R[valueReg] = R[valueReg] op R[rightReg]
				//   t[i] = valueReg

				int tableReg = AllocateRegister();
				CompileExpression(index.Target, tableReg);
				int indexReg = AllocateRegister();
				CompileExpression(index.Index, indexReg);
				int valueReg = AllocateRegister();
				Emit(OpCode.GETTABLE, valueReg, tableReg, indexReg);
				int rightReg = AllocateRegister();
				CompileExpression(node.Right, rightReg);
				Emit(op, valueReg, valueReg, rightReg);
				Emit(OpCode.SETTABLE, tableReg, indexReg, valueReg);
			}
			else
			{
				throw new LuaCompilerException(
					$"Augmented assignment target must be an identifier or index, got {node.Left.GetType().Name}.",
					node.Left.Position);
			}
		}

		// ── Call statement ──────────────────────────────────────────────

		private void CompileCallStatement(CallStatementNode node)
		{
			int reg = AllocateRegister();
			CompileExpression(node.Call, reg);
			// Result is discarded, but the CALL instruction still reads argument
			// registers that were allocated by CompileFunctionCall. We must not
			// shrink _nextRegister below the registers needed by the emitted CALL.
			// The safest approach is to leave _nextRegister as-is; dead registers
			// above the call result slot are harmless.
		}

		// ── Return ──────────────────────────────────────────────────────

		private void CompileReturn(ReturnNode node)
		{
			int baseReg = AllocateRegister();
			int valueCount = node.Values.Length;

			// Pre-allocate registers for all return values.
			while (_nextRegister < baseReg + valueCount)
				AllocateRegister();

			// Check if the last value may expand:
			// - AwaitExpressionNode: AWAIT with C=0
			// - FunctionCallNode / VarArgumentNode: CALL with C=0
			bool lastExpands = valueCount > 0 &&
				(node.Values[valueCount - 1] is AwaitExpressionNode ||
				 node.Values[valueCount - 1] is FunctionCallNode ||
				 node.Values[valueCount - 1] is VarArgumentNode);

			for (int i = 0; i < valueCount; i++)
			{
				int reg = baseReg + i;
				int instrBefore = _instructions.Count;
				CompileExpression(node.Values[i], reg);

				// Non-last await values: patch to C=1 (no expansion).
				if (i < valueCount - 1 && node.Values[i] is AwaitExpressionNode)
					PatchLastAwaitToSingle(instrBefore);
				// Non-last call/vararg values: patch to C=1 (no expansion).
				if (i < valueCount - 1 &&
					(node.Values[i] is FunctionCallNode || node.Values[i] is VarArgumentNode))
					PatchLastCallToSingle(instrBefore);
			}

			// For the last value: if it's a call/vararg, patch its CALL to C=0 (expand).
			if (valueCount > 0)
			{
				var lastValue = node.Values[valueCount - 1];
				if (lastValue is FunctionCallNode || lastValue is VarArgumentNode)
					PatchLastCallToExpand();
			}

			// Reserve headroom for expansion so AWAIT/CALL don't overflow the register array.
			if (lastExpands)
			{
				while (_nextRegister < baseReg + valueCount + 16)
					AllocateRegister();
			}

			// B=0: variable count (last value may expand, tracked by RegisterTop at runtime).
			// B>0: exact count.
			ushort returnCount = lastExpands ? (ushort)0 : (ushort)valueCount;
			Emit(OpCode.RETURN, (byte)baseReg, returnCount);
		}

		/// <summary>
		/// Finds the last AWAIT instruction emitted since <paramref name="startIndex"/>
		/// and changes its C operand to 1 (non-expanding).
		/// </summary>
		private void PatchLastAwaitToSingle(int startIndex)
		{
			for (int j = _instructions.Count - 1; j >= startIndex; j--)
			{
				var inst = _instructions[j];
				if (inst.Code == OpCode.AWAIT)
				{
					_instructions[j] = new Instruction(inst.Code, inst.A, inst.B, 1, inst.Flags);
					return;
				}
			}
		}

		/// <summary>
		/// Finds the last CALL instruction emitted and changes its C operand to 1 (non-expanding).
		/// </summary>
		private void PatchLastCallToSingle(int startIndex)
		{
			for (int j = _instructions.Count - 1; j >= startIndex; j--)
			{
				var inst = _instructions[j];
				if (inst.Code == OpCode.CALL)
				{
					_instructions[j] = new Instruction(inst.Code, inst.A, inst.B, 1, inst.Flags);
					return;
				}
			}
		}

		/// <summary>
		/// Finds the last CALL instruction emitted and changes its C operand to 0 (expand results).
		/// </summary>
		private void PatchLastCallToExpand()
		{
			for (int j = _instructions.Count - 1; j >= 0; j--)
			{
				var inst = _instructions[j];
				if (inst.Code == OpCode.CALL)
				{
					_instructions[j] = new Instruction(inst.Code, inst.A, inst.B, 0, inst.Flags);
					return;
				}
			}
		}
		/// <summary>
		/// Finds the last CALL instruction emitted and changes its C operand to the specified exact count.
		/// </summary>
		private void PatchLastCallToExact(ushort resultCount)
		{
			for (int j = _instructions.Count - 1; j >= 0; j--)
			{
				var inst = _instructions[j];
				if (inst.Code == OpCode.CALL)
				{
					_instructions[j] = new Instruction(inst.Code, inst.A, inst.B, resultCount, inst.Flags);
					return;
				}
			}
		}

		/// <summary>Emits an implicit RETURN at the end of a function.</summary>
		private void EmitReturn()
		{
			// Use the current register top as base, so RETURN B=0 returns 0 values.
			// If we used A=0 (the old behaviour), RETURN B=0 would return all live
			// registers from R[0] to RegisterTop, which is wrong for implicit returns.
			Emit(OpCode.RETURN, (byte)_nextRegister, 0);
		}

		// ── If ──────────────────────────────────────────────────────────

		private void CompileIf(IfNode node)
		{
			// if cond then body [elseif ...]* [else] end
			// Structure:
			//   JMPIF cond → body1
			//   JMP → next_check (or end_label)
			// body1_label: body1
			//   JMP → end_label
			// next_check_label: ...

			int endLabel = AllocateLabel();
			var jumpsToEnd = new List<int>();

			void CompileBranch(ExpressionNode condition, BlockNode body)
			{
				int condReg = AllocateRegister();
				CompileExpression(condition, condReg);

				int bodyLabel = AllocateLabel();
				int skipLabel = AllocateLabel();

				EmitJMPIF_Label(condReg, bodyLabel);  // if true → body
				EmitJMP_Label(skipLabel);              // else → skip

				MarkLabel(bodyLabel);
				CompileBlock(body);
				EmitJMP_Label(endLabel);               // jump to end after body
				jumpsToEnd.Add(_instructions.Count - 1);

				MarkLabel(skipLabel);
			}

			// Main if branch.
			CompileBranch(node.Condition, node.Body);

			// Elseif branches.
			foreach (var clause in node.ElseIfClauses)
				CompileBranch(clause.Condition, clause.Body);

			// Else branch.
			if (node.ElseBlock is not null)
				CompileBlock(node.ElseBlock);

			MarkLabel(endLabel);
		}

		// ── While ───────────────────────────────────────────────────────

		private void CompileWhile(WhileNode node)
		{
			int loopStartIndex = _instructions.Count;
			int loopEndLabel = AllocateLabel();

			int condReg = AllocateRegister();
			CompileExpression(node.Condition, condReg);
			// JMPIF: if truthy → skip JMP and go to body (body = current + 2).
			EmitJMPIF_To(condReg, _instructions.Count + 2);
			EmitJMP_Label(loopEndLabel);

			int bodyStart = _instructions.Count;
			int continueLabel = AllocateLabel();
			MarkLabel(continueLabel); // 'continue' jumps here (start of body)
			_loopStack.Push(new LoopContext(loopEndLabel, continueLabel));
			CompileBlock(node.Body);
			_loopStack.Pop();

			EmitJMP_To(loopStartIndex);
			MarkLabel(loopEndLabel);
		}

		// ── Repeat ... until ────────────────────────────────────────────

		private void CompileRepeat(RepeatNode node)
		{
			int loopStartIndex = _instructions.Count;
			int loopEndLabel = AllocateLabel();
			int continueLabel = AllocateLabel();
			MarkLabel(continueLabel); // 'continue' jumps here (start of body)

			_loopStack.Push(new LoopContext(loopEndLabel, continueLabel));
			CompileBlock(node.Body);
			_loopStack.Pop();

			int condReg = AllocateRegister();
			CompileExpression(node.Condition, condReg);
			EmitJMPIF_Label(condReg, loopEndLabel);   // if true → exit
			EmitJMP_To(loopStartIndex);               // else → repeat

			MarkLabel(loopEndLabel);
		}

		// ── For numeric ─────────────────────────────────────────────────

		private void CompileForNumeric(ForNumericNode node)
		{
			// Register layout: R[base] = var, R[base+1] = limit, R[base+2] = step
			int baseReg = AllocateRegister();
			int varReg = baseReg;
			int limitReg = baseReg + 1;
			int stepReg = baseReg + 2;
			_nextRegister = Math.Max(_nextRegister, baseReg + 3);

			// Compile start, limit, step.
			CompileExpression(node.Start, varReg);
			CompileExpression(node.Limit, limitReg);
			if (node.Step is not null)
				CompileExpression(node.Step, stepReg);
			else
				Emit(OpCode.MOVE, stepReg, GetConstantIndex(new LuaNumber(1)), flags: OpFlags.KB);

			// Map loop variable.
			_locals[node.Variable] = varReg;

			// FORPREP jumps to FORLOOP (skip body on first iteration).
			int forprepIndex = _instructions.Count;
			EmitFORPREP(varReg);

			int bodyStart = _instructions.Count;
			int loopEndLabel = AllocateLabel();
			int continueLabel = AllocateLabel();

			_loopStack.Push(new LoopContext(loopEndLabel, continueLabel));
			CompileBlock(node.Body);
			_loopStack.Pop();

			// Emit FORLOOP (jumps back to bodyStart) and patch FORPREP to jump here.
			// Place continueLabel right before FORLOOP so that 'continue' jumps here
			// and causes the loop variable to increment and the condition to be checked.
			MarkLabel(continueLabel);
			EmitFORLOOP(varReg, bodyStart);
			PatchFORPREP(forprepIndex, _instructions.Count - 1); // patch to FORLOOP

			MarkLabel(loopEndLabel);
		}

		// ── For in ──────────────────────────────────────────────────────

		private void CompileForIn(ForInNode node)
		{
			// Register layout:
			//   R[base] = iterator, R[base+1] = state, R[base+2] = var
			//   R[base+3..base+5] = backup area
			int baseReg = AllocateRegister();
			_nextRegister = Math.Max(_nextRegister, baseReg + 6);

			int exprCount = node.Expressions.Length;
			int slotsToFill = Math.Min(exprCount, 3);
			int lastIdx = slotsToFill - 1; // -1 if slotsToFill == 0

			// Compile all but the last expression normally (each with C=1).
			for (int i = 0; i < slotsToFill - 1; i++)
				CompileExpression(node.Expressions[i], baseReg + i);

			// Compile the last expression. If it's a call/vararg, allow it to expand (C=0)
			// so multiple return values populate the remaining slots (standard Lua for-in semantics).
			if (slotsToFill > 0)
			{
				int instrBefore = _instructions.Count;
				CompileExpression(node.Expressions[lastIdx], baseReg + lastIdx);

				// If there are more slots to fill and the last expression is a call/vararg,
				// patch it to expand results.
				if (slotsToFill < 3 && lastIdx == exprCount - 1)
				{
					var lastExpr = node.Expressions[lastIdx];
					if (lastExpr is FunctionCallNode || lastExpr is VarArgumentNode)
						PatchLastCallToExpand();
				}

				// Reserve headroom for expansion.
				while (_nextRegister < baseReg + 6 + 8)
					AllocateRegister();
			}

			// If the last expression was NOT a call/vararg that expands,
			// or if there are more than 3 expressions, pad remaining slots with nil.
			bool lastExpands = slotsToFill > 0 && lastIdx == exprCount - 1 &&
				(node.Expressions[lastIdx] is FunctionCallNode || node.Expressions[lastIdx] is VarArgumentNode);
			if (!lastExpands)
			{
				// Pad uninitialised slots with nil.
				for (int i = exprCount; i < 3; i++)
					Emit(OpCode.MOVE, baseReg + i, GetConstantIndex(LuaNil.Instance), flags: OpFlags.KB);
			}

			// Map loop variables.
			for (int i = 0; i < node.Variables.Length; i++)
				_locals[node.Variables[i]] = baseReg + 3 + i;

			int tforCall = _instructions.Count;
			EmitTFORCALL(baseReg, (ushort)node.Variables.Length);

			int loopEnd = AllocateLabel();
			int continueLabel = AllocateLabel();

			// Structure: TFORCALL, TFORLOOP, JMP_exit, body, continue-JMP_back, loopEnd.
			// bodyStart = tforCall + 3 (TFORCALL + TFORLOOP + JMP_exit).
			EmitTFORLOOP(baseReg, tforCall + 3);
			EmitJMP_Label(loopEnd); // nil → skip body and back-jump, go straight to exit

			int bodyStart = _instructions.Count;

			_loopStack.Push(new LoopContext(loopEnd, continueLabel));
			CompileBlock(node.Body);
			_loopStack.Pop();

			// 'continue' jumps here (to the JMP back to TFORCALL).
			// This causes the next iteration value to be fetched from the iterator,
			// rather than re-executing the body with the same loop variables.
			MarkLabel(continueLabel);
			// Jump back to TFORCALL.
			EmitJMP_To(tforCall);

			MarkLabel(loopEnd);
		}

		// ── Do ─────────────────────────────────────────────────────────

		private void CompileDo(DoNode node)
		{
			CompileBlock(node.Body);
		}

		// ── Break / Continue ────────────────────────────────────────────

		private void CompileBreak(BreakNode node)
		{
			if (_loopStack.Count == 0)
				throw new LuaCompilerException("<break> statement outside of a loop.", node.Position);

			var loop = _loopStack.Peek();
			EmitJMP_Label(loop.ExitLabel);
		}

		private void CompileContinue(ContinueNode node)
		{
			if (_loopStack.Count == 0)
				throw new LuaCompilerException("<continue> statement outside of a loop.", node.Position);

			var loop = _loopStack.Peek();
			EmitJMP_Label(loop.ContinueLabel);
		}

		// ── Goto / Label ────────────────────────────────────────────────

		private void CompileGoto(GotoNode node)
		{
			EmitJMP_Label(GetLabel(node.LabelName));
		}

		private void CompileLabel(LabelNode node)
		{
			MarkLabel(GetLabel(node.Name));
		}

		// ── Function declaration ────────────────────────────────────────

		private void CompileFunctionDeclaration(FunctionDeclStatementNode node)
		{
			// Method-style declarations (function obj.name() or function obj:name())
			// are compiled as assignments to a table element, not as local/global variables.
			bool isMethodStyle = node.TargetObject is not null;

			if (isMethodStyle)
			{
				CompileMethodStyleFunction(node);
				return;
			}

			// Determine whether this function should be assigned to a local variable.
			// If the name already exists as a local (e.g. forward-declared "local f"),
			// the function must be assigned to that local, not a global.
			bool isLocal = ResolveScope(node.Scope) == VariableScope.Local || _locals.ContainsKey(node.Name);

			int closureReg;
			if (isLocal)
			{
				// Reuse an existing local register if present; otherwise allocate a new one.
				if (_locals.TryGetValue(node.Name, out int existingReg))
				{
					closureReg = existingReg;
				}
				else
				{
					closureReg = AllocateRegister();
					_locals[node.Name] = closureReg;
					// Initialise the slot with nil until the CLOSURE is created.
					Emit(OpCode.MOVE, (byte)closureReg,
						GetConstantIndex(LuaNil.Instance), flags: OpFlags.KB);
				}
			}
			else
			{
				// Will allocate after compiling the body.
				closureReg = -1;
			}

			// For colon-style (obj:method), the parser doesn't add 'self' — we add it here.
			int implicitSelf = node.MethodName is not null ? 1 : 0;
			int totalParams = node.Parameters.Length + implicitSelf;

			var childCompiler = new AsyncLuaCompiler(
				_settings,
				parent: this,
				isAsync: node.IsAsync,
				parameterCount: totalParams,
				isVararg: node.HasVarArg,
				sourceName: node.Name);

			// Register parameters in the child compiler.
			if (implicitSelf > 0)
				childCompiler._locals["self"] = 0;
			for (int i = 0; i < node.Parameters.Length; i++)
				childCompiler._locals[node.Parameters[i].Name] = implicitSelf + i;
			childCompiler._nextRegister = totalParams;

			childCompiler.CompileBlock(node.Body);
			childCompiler.EmitReturn();
			childCompiler.PatchForwardJumps();

			var innerProto = childCompiler.BuildPrototype();
			int protoIndex = _innerPrototypes.Count;
			_innerPrototypes.Add(innerProto);

			if (isLocal)
			{
				// Use the pre-determined register (either reused or pre-allocated).
				EmitCLOSURE(closureReg, (ushort)protoIndex);
			}
			else
			{
				closureReg = AllocateRegister();
				EmitCLOSURE(closureReg, (ushort)protoIndex);
				EmitSETGLOBAL(node.Name, closureReg);
			}
		}

		/// <summary>
		/// Compiles a method-style function declaration such as
		/// <c>function obj.method(params) body end</c> or
		/// <c>function obj:method(params) body end</c>.
		/// </summary>
		private void CompileMethodStyleFunction(FunctionDeclStatementNode node)
		{
			// For colon-style (obj:method), add an implicit 'self' parameter.
			int implicitSelf = node.MethodName is not null ? 1 : 0;
			int totalParams = node.Parameters.Length + implicitSelf;

			var childCompiler = new AsyncLuaCompiler(
				_settings,
				parent: this,
				isAsync: node.IsAsync,
				parameterCount: totalParams,
				isVararg: node.HasVarArg,
				sourceName: node.Name);

			// Register parameters in the child compiler, with 'self' first if colon-style.
			if (implicitSelf > 0)
				childCompiler._locals["self"] = 0;
			for (int i = 0; i < node.Parameters.Length; i++)
				childCompiler._locals[node.Parameters[i].Name] = implicitSelf + i;
			childCompiler._nextRegister = totalParams;

			childCompiler.CompileBlock(node.Body);
			childCompiler.EmitReturn();
			childCompiler.PatchForwardJumps();

			var innerProto = childCompiler.BuildPrototype();
			int protoIndex = _innerPrototypes.Count;
			_innerPrototypes.Add(innerProto);

			// Allocate register for the closure.
			int closureReg = AllocateRegister();
			EmitCLOSURE(closureReg, (ushort)protoIndex);

			// Compile the assignment target: obj.method (SETTABLE).
			// This is equivalent to: obj[methodName] = closure
			int targetReg = AllocateRegister();
			CompileExpression(node.TargetObject!, targetReg);

			// Use node.MethodName if present (colon-style), otherwise node.Name (dot-style).
			string propertyName = node.MethodName ?? node.Name;
			int keyIndex = GetConstantIndex(new LuaString(propertyName));

			Emit(OpCode.SETTABLE, (byte)targetReg, (ushort)keyIndex, (ushort)closureReg,
				OpFlags.KB);
		}

		// ── Lock ────────────────────────────────────────────────────────

		private void CompileLock(LockNode node)
		{
			int lockReg = AllocateRegister();
			CompileExpression(node.Target, lockReg);

			Emit(OpCode.LOCK, (byte)lockReg);
			CompileBlock(node.Body);
			Emit(OpCode.UNLOCK, (byte)lockReg);
		}

		// ── Await statement ─────────────────────────────────────────────

		private void CompileAwaitStatement(AwaitStatementNode node)
		{
			var exprs = node.AwaitExpression.Expressions;
			int baseReg = AllocateRegister();
			int count = exprs.Length;

			// Ensure consecutive registers are allocated.
			for (int i = 1; i < count; i++)
				AllocateRegister();

			// Compile each task expression.
			for (int i = 0; i < count; i++)
				CompileExpression(exprs[i], baseReg + i);

			// Reserve headroom for expansion so AWAIT doesn't overflow the register array.
			// Last task has C=0 (accept all results), which may expand.
			while (_nextRegister < baseReg + count + 16)
				AllocateRegister();

			// Await each task. Last task results are expanded (C=0).
			for (int i = 0; i < count; i++)
			{
				ushort wantResults = (ushort)(i == count - 1 ? 0 : 1);
				Emit(OpCode.AWAIT, (byte)(baseReg + i), 0, wantResults);
			}
		}

		// ── Try-catch ────────────────────────────────────────────────────

		private void CompileTryCatch(TryCatchNode node)
		{
			// Layout:
			//   TRY catchLabel, varReg
			//   <try block>
			//   JMP endLabel
			// catchLabel:
			//   <catch block>
			// endLabel:
			//   ENDTRY

			int catchLabel = AllocateLabel();
			int endLabel = AllocateLabel();

			int varReg = node.ExceptionMessageVariable is not null
				? AllocateRegister()
				: 0xFF; // 0xFF = no register

			// TRY catchLabel, varReg
			EmitTRY_Label(varReg, catchLabel);

			// Try block
			CompileBlock(node.TryBody);

			// JMP over catch block
			EmitJMP_Label(endLabel);

			// Catch block
			MarkLabel(catchLabel);

			if (node.ExceptionMessageVariable is not null && varReg != 0xFF)
			{
				// Map the exception variable name to the register
				// (it's a local variable in the catch scope)
				_locals[node.ExceptionMessageVariable] = varReg;
			}

			CompileBlock(node.CatchBody);

			// End label + ENDTRY
			MarkLabel(endLabel);
			Emit(OpCode.ENDTRY);
		}

		private void EmitTRY_Label(int varReg, int catchLabelId)
		{
			_fixups.Add(new JumpFixup
			{
				InstructionIndex = _instructions.Count,
				TargetLabel = catchLabelId
			});
			Emit(OpCode.TRY, varReg, 0, flags: OpFlags.SignedBX);
		}

		// ── Throw ───────────────────────────────────────────────────────

		private void CompileThrow(ThrowNode node)
		{
			int reg = AllocateRegister();
			CompileExpression(node.Exception, reg);
			Emit(OpCode.THROW, (byte)reg);
		}


		// ═══════════════════════════════════════════════════════════════
		// EXPRESSION COMPILATION
		// ═══════════════════════════════════════════════════════════════

		/// <summary>
		/// Compiles an expression, placing the result into the specified register.
		/// </summary>
		private void CompileExpression(ExpressionNode expr, int destReg)
		{
			PushPosition(expr.Position);
			try
			{
				switch (expr)
				{
					case LiteralNode lit:
						Emit(OpCode.MOVE, (byte)destReg, (ushort)GetConstantIndex(lit.Literal), flags: OpFlags.KB);
						break;

					case IdentifierNode ident:
						CompileIdentifier(ident, destReg);
						break;

					case BinaryOperatorNode binOp:
						CompileBinaryOp(binOp, destReg);
						break;

					case UnaryOperatorNode unOp:
						CompileUnaryOp(unOp, destReg);
						break;

					case FunctionCallNode call:
						CompileFunctionCall(call, destReg);
						break;

					case IndexNode index:
						CompileGetIndex(index, destReg);
						break;

					case TableConstructionNode table:
						CompileTableConstructor(table, destReg);
						break;

					case FunctionDeclExpressionNode funcExpr:
						CompileFunctionExpression(funcExpr, destReg);
						break;

					case AwaitExpressionNode awaitExpr:
						CompileAwaitExpression(awaitExpr, destReg);
						break;

					case VarArgumentNode:
						Emit(OpCode.VARARG, (byte)destReg, 0);
						break;

					default:
						throw new LuaCompilerException($"Unknown expression type: {expr.GetType().Name}", expr.Position);
				}
			}
			finally
			{
				PopPosition();
			}
		}

		// ── Identifier ──────────────────────────────────────────────────

		private void CompileIdentifier(IdentifierNode ident, int destReg)
		{
			if (_locals.TryGetValue(ident.Name, out int localReg))
			{
				Emit(OpCode.MOVE, (byte)destReg, (ushort)localReg);
			}
			else
			{
				int? upvalueIndex = ResolveUpvalue(ident.Name);
				if (upvalueIndex.HasValue)
				{
					Emit(OpCode.GETUPVAL, (byte)destReg, (ushort)upvalueIndex.Value);
				}
				else
				{
					// Global variable.
					int keyIndex = GetConstantIndex(new LuaString(ident.Name));
					Emit(OpCode.GETGLOBAL, (byte)destReg, (ushort)keyIndex, flags: OpFlags.KB);
				}
			}
		}

		// ── Binary operators ────────────────────────────────────────────

		private void CompileBinaryOp(BinaryOperatorNode node, int destReg)
		{
			// Logical operators use short-circuit evaluation.
			if (node.Operator == BinaryOperatorType.LogicalAnd ||
				node.Operator == BinaryOperatorType.LogicalOr)
			{
				CompileLogicalOp(node, destReg);
				return;
			}

			OpCode op = BinaryOpToOpCode(node.Operator, node.Position);

			int leftReg = destReg;
			int rightReg = AllocateRegister();
			CompileExpression(node.Left, leftReg);
			CompileExpression(node.Right, rightReg);

			OpFlags flags = OpFlags.None;
			Emit(op, (byte)destReg, (ushort)leftReg, (ushort)rightReg, flags);
		}

		private void CompileLogicalOp(BinaryOperatorNode node, int destReg)
		{
			// For 'and': if left is falsy → result = left; else result = right.
			// For 'or':  if left is truthy → result = left; else result = right.

			bool isAnd = node.Operator == BinaryOperatorType.LogicalAnd;

			CompileExpression(node.Left, destReg);

			int skipLabel = AllocateLabel();
			int endLabel = AllocateLabel();

			if (isAnd)
				EmitJMPIF_Not_Label(destReg, skipLabel);  // falsy → skip (use left)
			else
				EmitJMPIF_Label(destReg, skipLabel);       // truthy → skip (use left)

			// Evaluate right.
			CompileExpression(node.Right, destReg);
			EmitJMP_Label(endLabel);

			MarkLabel(skipLabel);
			// left value is already in destReg.
			MarkLabel(endLabel);
		}

		// ── Unary operators ─────────────────────────────────────────────

		private void CompileUnaryOp(UnaryOperatorNode node, int destReg)
		{
			OpCode op = node.Type switch
			{
				UnaryOperatorType.Minus => OpCode.UNM,
				UnaryOperatorType.LogicalNot => OpCode.NOT,
				UnaryOperatorType.LengthOf => OpCode.LEN,
				_ => throw new LuaCompilerException($"Unknown unary operator: {node.Type}", node.Position)
			};

			CompileExpression(node.Operand, destReg);
			Emit(op, (byte)destReg, (ushort)destReg);
		}

		// ── Function call ───────────────────────────────────────────────

		private void CompileFunctionCall(FunctionCallNode node, int destReg)
		{
			int funcReg = destReg;
			int argBase = destReg + 1;

			// If method call (obj:method), make obj the first argument (self).
			int fixedArgCount = node.Arguments.Length;
			if (node.Method is not null)
				fixedArgCount++; // +1 for self

			// Check if the last argument is a VarArgumentNode (will expand at runtime).
			bool lastIsVararg = node.Arguments.Length > 0 &&
				node.Arguments[node.Arguments.Length - 1] is VarArgumentNode;
			// If the last arg is vararg, it will be expanded from frame.VarArgs at runtime,
			// so we don't compile it into a register.
			int nonVarargExplicitCount = lastIsVararg ? node.Arguments.Length - 1 : node.Arguments.Length;

			// Calculate total register slots needed for fixed arguments:
			// function slot (1) + explicit non-vararg args + implicit self (if method call).
			int totalFixedSlots = 1 + nonVarargExplicitCount;
			if (node.Method is not null)
				totalFixedSlots++; // for implicit self

			// Ensure registers are allocated for fixed arguments + function slot.
			// We don't pre-allocate for varargs (they're read from frame at runtime).
			while (_nextRegister < destReg + totalFixedSlots)
				AllocateRegister();

			// Compile the target (function/object).
			if (node.Method is not null)
			{
				// obj:method(args) → obj.method(obj, args)
				// Compile obj into funcReg.
				CompileExpression(node.Target, funcReg);
				// Save self (the object) as the first argument BEFORE reading the method,
				// because GETTABLE will overwrite funcReg.
				Emit(OpCode.MOVE, (byte)argBase, (ushort)funcReg);
				// Now read the method into funcReg.
				int methodKeyIndex = GetConstantIndex(new LuaString(node.Method));
				Emit(OpCode.GETTABLE, (byte)funcReg, (ushort)argBase,
					(ushort)methodKeyIndex, OpFlags.KC);
			}
			else
			{
				CompileExpression(node.Target, funcReg);
			}

			// Compile fixed explicit arguments (skip self slot if method call, skip vararg).
			int argOffset = node.Method is not null ? 1 : 0;
			for (int i = 0; i < nonVarargExplicitCount; i++)
				CompileExpression(node.Arguments[i], argBase + argOffset + i);

			OpFlags callFlags = OpFlags.None;
			if (lastIsVararg)
				callFlags |= OpFlags.VarArgCall;

			Emit(OpCode.CALL, (byte)funcReg, (ushort)fixedArgCount, (ushort)1, callFlags); // want 1 result
		}

		// ── Table index (get) ───────────────────────────────────────────

		private void CompileGetIndex(IndexNode node, int destReg)
		{
			int tableReg = AllocateRegister();
			CompileExpression(node.Target, tableReg);

			// Compile the index into destReg temporarily, then overwrite.
			int indexReg = destReg;
			if (indexReg == tableReg)
				indexReg = AllocateRegister();

			CompileExpression(node.Index, indexReg);

			Emit(OpCode.GETTABLE, (byte)destReg, (ushort)tableReg, (ushort)indexReg);
		}

		/// <summary>Compiles an index node as an l-value assignment target.</summary>
		private void CompileSetIndex(IndexNode node, int valueReg)
		{
			int tableReg = AllocateRegister();
			CompileExpression(node.Target, tableReg);

			int indexReg = AllocateRegister();
			CompileExpression(node.Index, indexReg);

			Emit(OpCode.SETTABLE, (byte)tableReg, (ushort)indexReg, (ushort)valueReg);
		}

		// ── Table constructor ───────────────────────────────────────────

		private void CompileTableConstructor(TableConstructionNode node, int destReg)
		{
			Emit(OpCode.NEWTABLE, (byte)destReg);

			int arrayIndex = 1;
			for (int idx = 0; idx < node.Pairs.Length; idx++)
			{
				var pair = node.Pairs[idx];
				bool isLast = idx == node.Pairs.Length - 1;

				// If the last pair has no key and is a function call or vararg,
				// expand all return values into consecutive array slots.
				bool canExpand = isLast && pair.Key is null &&
					(pair.Value is FunctionCallNode || pair.Value is VarArgumentNode);

				int valueReg = AllocateRegister();
				int instrBefore = _instructions.Count;
				CompileExpression(pair.Value, valueReg);

				if (canExpand)
				{
					// Patch the CALL (or VARARG) to expand all results (C=0).
					if (pair.Value is FunctionCallNode)
						PatchLastCallToExpand();
					// VARARG with B=0 already expands by default.

					// Reserve headroom for expanded results.
					while (_nextRegister < valueReg + 16)
						AllocateRegister();

					int startIndexConst = GetConstantIndex(new LuaNumber(arrayIndex));
					Emit(OpCode.SETLIST, (byte)destReg, (ushort)startIndexConst,
						(ushort)valueReg, OpFlags.KB);

					// arrayIndex is not known at compile time for the expanded values;
					// it's tracked by the runtime SETLIST.
				}
				else if (pair.Key is null)
				{
					// Array element: t[arrayIndex] = value.
					int keyReg = AllocateRegister();
					Emit(OpCode.MOVE, (byte)keyReg,
						(ushort)GetConstantIndex(new LuaNumber(arrayIndex)), flags: OpFlags.KB);
					Emit(OpCode.SETTABLE, (byte)destReg, (ushort)keyReg, (ushort)valueReg);
					arrayIndex++;
				}
				else
				{
					// Named element: t[key] = value.
					int keyReg = AllocateRegister();
					CompileExpression(pair.Key, keyReg);
					Emit(OpCode.SETTABLE, (byte)destReg, (ushort)keyReg, (ushort)valueReg);
				}
			}
		}

		// ── Function expression ─────────────────────────────────────────

		private void CompileFunctionExpression(FunctionDeclExpressionNode node, int destReg)
		{
			var childCompiler = new AsyncLuaCompiler(
				_settings,
				parent: this,
				isAsync: node.IsAsync,
				parameterCount: node.Parameters.Length,
				isVararg: node.HasVarArg,
				sourceName: null);

			for (int i = 0; i < node.Parameters.Length; i++)
				childCompiler._locals[node.Parameters[i].Name] = i;
			childCompiler._nextRegister = node.Parameters.Length;

			childCompiler.CompileBlock(node.Body);
			childCompiler.EmitReturn();
			childCompiler.PatchForwardJumps();

			var innerProto = childCompiler.BuildPrototype();
			int protoIndex = _innerPrototypes.Count;
			_innerPrototypes.Add(innerProto);

			EmitCLOSURE(destReg, (ushort)protoIndex);
		}

		// ── Await expression ────────────────────────────────────────────

		private void CompileAwaitExpression(AwaitExpressionNode node, int destReg)
		{
			int count = node.Expressions.Length;

			// Compile each task expression into consecutive registers.
			for (int i = 0; i < count; i++)
			{
				int reg = destReg + i;
				CompileExpression(node.Expressions[i], reg);
			}

			// Await each task. The last task's results are expanded (C=0),
			// others contribute only their first result (C=1).
			for (int i = 0; i < count; i++)
			{
				int reg = destReg + i;
				ushort wantResults = (ushort)(i == count - 1 ? 0 : 1);
				Emit(OpCode.AWAIT, (byte)reg, 0, wantResults);
			}
		}

		// ═══════════════════════════════════════════════════════════════
		// REGISTER MANAGEMENT
		// ═══════════════════════════════════════════════════════════════

		/// <summary>Allocates and returns the next available register slot.</summary>
		private int AllocateRegister()
		{
			return _nextRegister++;
		}

		// ═══════════════════════════════════════════════════════════════
		// CONSTANTS
		// ═══════════════════════════════════════════════════════════════

		/// <summary>
		/// Gets the index of a constant in the constant pool, adding it if not already present.
		/// </summary>
		private int GetConstantIndex(LuaValue value)
		{
			if (_constantMap.TryGetValue(value, out int index))
				return index;

			index = _constants.Count;
			_constants.Add(value);
			_constantMap[value] = index;
			return index;
		}

		// ═══════════════════════════════════════════════════════════════
		// JUMP / LABEL BACKPATCHING
		// ═══════════════════════════════════════════════════════════════

		private struct JumpFixup
		{
			public int InstructionIndex;
			public int TargetLabel;
		}

		private struct LoopContext
		{
			public int ExitLabel;
			public int ContinueLabel; // for future 'continue' support

			public LoopContext(int exitLabel, int continueLabel)
			{
				ExitLabel = exitLabel;
				ContinueLabel = continueLabel;
			}
		}

		private int _nextLabelId;

		/// <summary>Allocates a unique label ID for forward jumps.</summary>
		private int AllocateLabel()
		{
			return _nextLabelId++;
		}

		/// <summary>Gets or creates a label ID for a named label (goto/label).</summary>
		private int GetLabel(string name)
		{
			if (_labels.TryGetValue(name, out int id))
				return id;
			id = AllocateLabel();
			_labels[name] = id;
			return id;
		}

		/// <summary>Marks the current position as the target of a label.</summary>
		private void MarkLabel(int labelId)
		{
			_labels[labelId.ToString()] = _instructions.Count;
		}

		/// <summary>Emits an unconditional JMP to a forward label (fixup recorded).</summary>
		private void EmitJMP_Label(int labelId)
		{
			_fixups.Add(new JumpFixup
			{
				InstructionIndex = _instructions.Count,
				TargetLabel = labelId
			});
			Emit(OpCode.JMP, 0, 0, flags: OpFlags.SignedBX);
		}

		/// <summary>Emits an unconditional JMP to a known instruction index (no fixup).</summary>
		private void EmitJMP_To(int targetIndex)
		{
			int offset = targetIndex - _instructions.Count;
			Emit(OpCode.JMP, 0, (ushort)offset, flags: OpFlags.SignedBX);
		}

		/// <summary>Emits JMPIF to a forward label: if R[a] is truthy, jump to labelId.</summary>
		private void EmitJMPIF_Label(int condReg, int labelId)
		{
			_fixups.Add(new JumpFixup
			{
				InstructionIndex = _instructions.Count,
				TargetLabel = labelId
			});
			Emit(OpCode.JMPIF, (byte)condReg, 0, flags: OpFlags.SignedBX);
		}

		/// <summary>Emits JMPIF to a known instruction index: if R[a] is truthy, jump.</summary>
		private void EmitJMPIF_To(int condReg, int targetIndex)
		{
			int offset = targetIndex - _instructions.Count;
			Emit(OpCode.JMPIF, (byte)condReg, (ushort)offset, flags: OpFlags.SignedBX);
		}

		/// <summary>Emits JMPIF with inverted condition to a forward label (jump if falsy).</summary>
		private void EmitJMPIF_Not_Label(int condReg, int labelId)
		{
			int tempReg = AllocateRegister();
			Emit(OpCode.NOT, (byte)tempReg, (ushort)condReg);
			EmitJMPIF_Label(tempReg, labelId);
		}

		/// <summary>Emits JMPIF with inverted condition to a known index (jump if falsy).</summary>
		private void EmitJMPIF_Not_To(int condReg, int targetIndex)
		{
			int tempReg = AllocateRegister();
			Emit(OpCode.NOT, (byte)tempReg, (ushort)condReg);
			EmitJMPIF_To(tempReg, targetIndex);
		}

		/// <summary>Emits FORPREP: placeholder offset, patched later via <see cref="PatchFORPREP"/>.</summary>
		private void EmitFORPREP(int baseReg)
		{
			Emit(OpCode.FORPREP, (byte)baseReg, 0, flags: OpFlags.SignedBX);
		}

		/// <summary>Emits FORLOOP: jumps back to body start.</summary>
		private void EmitFORLOOP(int baseReg, int bodyStart)
		{
			int offset = bodyStart - _instructions.Count;
			Emit(OpCode.FORLOOP, (byte)baseReg, (ushort)offset, flags: OpFlags.SignedBX);
		}

		/// <summary>Emits TFORCALL.</summary>
		private void EmitTFORCALL(int baseReg, ushort varCount)
		{
			Emit(OpCode.TFORCALL, (byte)baseReg, 0, varCount);
		}

		/// <summary>Emits TFORLOOP: jumps to body start.</summary>
		private void EmitTFORLOOP(int baseReg, int bodyStart)
		{
			int offset = bodyStart - _instructions.Count;
			Emit(OpCode.TFORLOOP, (byte)baseReg, (ushort)offset, flags: OpFlags.SignedBX);
		}

		/// <summary>Patches FORPREP offset to jump to the body start.</summary>
		private void PatchFORPREP(int forprepIndex, int bodyStart)
		{
			var inst = _instructions[forprepIndex];
			int offset = bodyStart - forprepIndex;
			_instructions[forprepIndex] = new Instruction(
				inst.Code, inst.A, (ushort)offset, inst.C, inst.Flags);
		}

		/// <summary>Resolves all forward jumps by patching their offsets.</summary>
		private void PatchForwardJumps()
		{
			foreach (var fixup in _fixups)
			{
				string labelKey = fixup.TargetLabel.ToString();
				if (!_labels.TryGetValue(labelKey, out int targetPosition))
				{
					throw new LuaCompilerException(
						$"Unresolved jump target: label {fixup.TargetLabel}.");
				}

				var inst = _instructions[fixup.InstructionIndex];
				int offset = inst.Code == OpCode.TRY
					? targetPosition - (fixup.InstructionIndex + 1)   // TRY: pc = TRY_pc + 1 + sBx
					: targetPosition - fixup.InstructionIndex;        // JMP: pc += sBx
				_instructions[fixup.InstructionIndex] = new Instruction(
					inst.Code, inst.A, (ushort)offset, inst.C, inst.Flags);
			}
			_fixups.Clear();
		}

		// ═══════════════════════════════════════════════════════════════
		// UPVALUE RESOLUTION
		// ═══════════════════════════════════════════════════════════════

		/// <summary>
		/// Attempts to resolve a variable name to an upvalue index.
		/// Searches the chain of parent compilers.
		/// </summary>
		/// <returns>The upvalue index in this function, or <see langword="null"/> if not found.</returns>
		private int? ResolveUpvalue(string name)
		{
			if (_parent is null)
				return null;

			// Check if the parent has it as a local.
			if (_parent._locals.TryGetValue(name, out int parentReg))
			{
				// Add upvalue description: isLocal=true, registerIndex=parentReg.
				var desc = new UpvalueDescription((byte)parentReg, isLocal: true);
				int index = _upvalueDescriptions.Count;
				_upvalueDescriptions.Add(desc);
				return index;
			}

			// Check if the parent has it as an upvalue (recursive).
			int? parentUpvalueIndex = _parent.ResolveUpvalue(name);
			if (parentUpvalueIndex.HasValue)
			{
				// This is a non-local upvalue: reuse the parent's upvalue.
				var desc = new UpvalueDescription((byte)parentUpvalueIndex.Value, isLocal: false);
				int index = _upvalueDescriptions.Count;
				_upvalueDescriptions.Add(desc);
				return index;
			}

			return null;
		}

		// ═══════════════════════════════════════════════════════════════
		// EMIT HELPERS
		// ═══════════════════════════════════════════════════════════════

		/// <summary>
		/// Sets the source position to be associated with subsequently emitted instructions.
		/// </summary>
		private void PushPosition(CodePositionalInfo pos)
		{
			if (pos.IsValid)
				_positionStack.Push(pos);
			else if (_positionStack.Count > 0)
				_positionStack.Push(_positionStack.Peek());
			else
				_positionStack.Push(new CodePositionalInfo { StartIndex = -1 });
		}

		private void PopPosition()
		{
			_positionStack.Pop();
		}

		private void Emit(OpCode code, int a = 0, int b = 0, int c = 0, OpFlags flags = OpFlags.None)
		{
			_instructions.Add(new Instruction(code, (byte)a, (ushort)b, (ushort)c, flags));
			if (_positionStack.Count > 0)
				_positions.Add(_positionStack.Peek());
			else
				_positions.Add(new CodePositionalInfo { StartIndex = -1 });
		}

		private void EmitSETGLOBAL(string name, int srcReg)
		{
			int keyIndex = GetConstantIndex(new LuaString(name));
			Emit(OpCode.SETGLOBAL, (byte)srcReg, (ushort)keyIndex, flags: OpFlags.KB);
		}

		private void EmitCLOSURE(int destReg, ushort protoIndex)
		{
			Emit(OpCode.CLOSURE, (byte)destReg, protoIndex);
		}

		// ═══════════════════════════════════════════════════════════════
		// BUILD PROTOTYPE
		// ═══════════════════════════════════════════════════════════════

		private FunctionPrototype BuildPrototype()
		{
			return new FunctionPrototype(
				instructions: _instructions.ToArray(),
				maxRegSize: _nextRegister,
				isAsync: _isAsync,
				constants: _constants.ToArray(),
				innerPrototypes: _innerPrototypes.ToArray(),
				parameterCount: (byte)_parameterCount,
				isVararg: _isVararg,
				sourceName: _sourceName,
				positions: _positions.Count > 0 ? _positions.ToArray() : null,
				upvalueDescriptions: _upvalueDescriptions.ToArray()
			);
		}

		// ═══════════════════════════════════════════════════════════════
		// OPCODE MAPPING
		// ═══════════════════════════════════════════════════════════════

		private static OpCode BinaryOpToOpCode(BinaryOperatorType op, CodePositionalInfo pos) => op switch
		{
			BinaryOperatorType.Add => OpCode.ADD,
			BinaryOperatorType.Substract => OpCode.SUB,
			BinaryOperatorType.Multiply => OpCode.MUL,
			BinaryOperatorType.Divide => OpCode.DIV,
			BinaryOperatorType.IntegerDivide => OpCode.IDIV,
			BinaryOperatorType.Modulus => OpCode.MOD,
			BinaryOperatorType.Exponentiate => OpCode.POW,
			BinaryOperatorType.Concatenate => OpCode.CONCAT,
			BinaryOperatorType.BitAnd => OpCode.BAND,
			BinaryOperatorType.BitOr => OpCode.BOR,
			BinaryOperatorType.BitXor => OpCode.BXOR,
			BinaryOperatorType.BitShiftLeft => OpCode.SHL,
			BinaryOperatorType.BitShiftRight => OpCode.SHR,
			BinaryOperatorType.Equals => OpCode.EQ,
			BinaryOperatorType.NotEquals => OpCode.NE,
			BinaryOperatorType.LessThan => OpCode.LT,
			BinaryOperatorType.LessThanEqual => OpCode.LE,
			BinaryOperatorType.GreaterThan => OpCode.GT,
			BinaryOperatorType.GreaterThanEqual => OpCode.GE,
			_ => throw new LuaCompilerException($"Unknown binary operator: {op}", pos)
		};
	}
}
