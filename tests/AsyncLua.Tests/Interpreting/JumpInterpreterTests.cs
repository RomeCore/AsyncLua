using AsyncLua.Interpreting;
using AsyncLua.Values;

namespace AsyncLua.Tests.Interpreting;

public class JumpInterpreterTests
{
	private static FunctionPrototype MakeProto(Instruction[] instructions, int maxRegSize = 1, LuaValue[]? constants = null)
	{
		return new FunctionPrototype(
			instructions,
			maxRegSize,
			false,
			constants ?? Array.Empty<LuaValue>(),
			Array.Empty<FunctionPrototype>());
	}

	private static LuaCallingContext Context() => new LuaState().CreateContext();

	// ── JMP (unconditional) ───────────────────────────────────────────

	[Fact]
	public void Jmp_SkipsInstructions_ReturnsLaterValue()
	{
		// Jump over the first return, land on MOVE + RETURN that give 42.
		// [0] JMP +2     → skip [1], land on [2]
		// [1] RETURN 99  ← skipped
		// [2] MOVE R0, K[0] → R[0] = 42
		// [3] RETURN R0
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.JMP, a: 0, b: 2, c: 0, flags: OpFlags.SignedBX),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None), // skipped
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),     // R[0] = 42
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42), new LuaNumber(99) });

		var result = Interpreter.Call(proto, Context());
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Jmp_ZeroOffset_InfiniteLoop_NotUsedHere()
	{
		// JMP +1 falls through to MOVE, then RETURN.
		// [0] JMP +1     → fall through to [1]
		// [1] MOVE R0, K[0]  → R[0] = 42
		// [2] RETURN R0
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.JMP, a: 0, b: 1, c: 0, flags: OpFlags.SignedBX),
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42) });

		var result = Interpreter.Call(proto, Context());
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── JMPIF (conditional) ───────────────────────────────────────────

	[Fact]
	public void JmpIf_Truthy_Jumps()
	{
		// if K[0] (42, truthy) → jump over first return
		// [0] MOVE R1, K[0]      → R[1] = 42 (truthy)
		// [1] JMPIF R1, +3        → jump to [4] (skip [2],[3])
		// [2] RETURN K[1] (99)    ← skipped
		// [3] RETURN K[0] (42)    ← skipped
		// [4] MOVE R0, K[0]       → R[0] = 42
		// [5] RETURN R0
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 1, b: 0, c: 0, flags: OpFlags.KB),      // [0] R[1] = 42
            new Instruction(OpCode.JMPIF, a: 1, b: 3, c: 0, flags: OpFlags.SignedBX), // [1] if R[1] → skip to [4]
            new Instruction(OpCode.MOVE, a: 0, b: 1, c: 0, flags: OpFlags.KB),      // [2] R[0] = 99 (skipped)
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),   // [3] (skipped)
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),      // [4] R[0] = 42
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),   // [5]
        }, maxRegSize: 2, constants: new LuaValue[] { new LuaNumber(42), new LuaNumber(99) });

		var result = Interpreter.Call(proto, Context());
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void JmpIf_Falsy_FallsThrough()
	{
		// if nil (falsy) → DON'T jump, fall through
		// [0] JMPIF R1, +3        → R[1] is nil (falsy), fall through
		// [1] MOVE R0, K[0]       → R[0] = 42
		// [2] RETURN R0
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.JMPIF, a: 1, b: 3, c: 0, flags: OpFlags.SignedBX), // R[1] is nil → falsy
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),       // R[0] = 42
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 2, constants: new LuaValue[] { new LuaNumber(42) });

		var result = Interpreter.Call(proto, Context());
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void JmpIf_False_FallsThrough()
	{
		// if false → DON'T jump
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 1, b: 0, c: 0, flags: OpFlags.KB),      // R[1] = false
            new Instruction(OpCode.JMPIF, a: 1, b: 3, c: 0, flags: OpFlags.SignedBX), // R[1] falsy → fall through
            new Instruction(OpCode.MOVE, a: 0, b: 1, c: 0, flags: OpFlags.KB),      // R[0] = 42
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 2, constants: new LuaValue[] { LuaBoolean.False, new LuaNumber(42) });

		var result = Interpreter.Call(proto, Context());
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void JmpIf_ZeroIsTruthy_Jumps()
	{
		// In Lua, 0 is truthy! So JMPIF should jump.
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 1, b: 0, c: 0, flags: OpFlags.KB),      // [0] R[1] = 0 (truthy!)
            new Instruction(OpCode.JMPIF, a: 1, b: 3, c: 0, flags: OpFlags.SignedBX), // [1] R[1] truthy → jump to [4]
            new Instruction(OpCode.MOVE, a: 0, b: 1, c: 0, flags: OpFlags.KB),      // [2] R[0] = 99 (skipped)
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),   // [3] (skipped)
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),      // [4] R[0] = 0 (truthy value)
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),   // [5]
        }, maxRegSize: 2, constants: new LuaValue[] { new LuaNumber(0), new LuaNumber(99) });

		var result = Interpreter.Call(proto, Context());
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void JmpIf_EmptyStringIsTruthy_Jumps()
	{
		// Empty string is truthy in Lua.
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 1, b: 0, c: 0, flags: OpFlags.KB),      // R[1] = ""
            new Instruction(OpCode.JMPIF, a: 1, b: 3, c: 0, flags: OpFlags.SignedBX), // truthy → jump
            new Instruction(OpCode.MOVE, a: 0, b: 1, c: 0, flags: OpFlags.KB),      // R[0] = "nope" (skipped)
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),   // skipped
            new Instruction(OpCode.MOVE, a: 0, b: 2, c: 0, flags: OpFlags.KB),      // R[0] = "yes"
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 2, constants: new LuaValue[] { new LuaString(""), new LuaString("nope"), new LuaString("yes") });

		var result = Interpreter.Call(proto, Context());
		Assert.Equal("yes", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── Combined: if-then-else pattern ────────────────────────────────

	[Fact]
	public void JmpIf_IfThenElse_ReturnsCorrectBranch()
	{
		// Simulates: if K[0] (true) then return 42 else return 99 end
		//
		// [0] MOVE R1, K[0]       → R[1] = true
		// [1] JMPIF R1, +3         → jump to then-branch [4] (skip else [2],[3])
		// [2] MOVE R0, K[2]        → else: R[0] = 99 (skipped when true)
		// [3] RETURN R0
		// [4] MOVE R0, K[1]        → then: R[0] = 42
		// [5] RETURN R0

		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 1, b: 0, c: 0, flags: OpFlags.KB),      // [0] R[1] = true
            new Instruction(OpCode.JMPIF, a: 1, b: 3, c: 0, flags: OpFlags.SignedBX), // [1] if truthy → skip to [4]
            new Instruction(OpCode.MOVE, a: 0, b: 2, c: 0, flags: OpFlags.KB),      // [2] R[0] = 99 (else)
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),   // [3]
            new Instruction(OpCode.MOVE, a: 0, b: 1, c: 0, flags: OpFlags.KB),      // [4] R[0] = 42 (then)
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),   // [5]
        }, maxRegSize: 2, constants: new LuaValue[] { LuaBoolean.True, new LuaNumber(42), new LuaNumber(99) });

		var result = Interpreter.Call(proto, Context());
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── Offset = 1 (fall through) ─────────────────────────────────────

	[Fact]
	public void Jmp_NextInstruction_FallsThrough()
	{
		// JMP +1 → next instruction, fall through.
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.JMP, a: 0, b: 1, c: 0, flags: OpFlags.SignedBX),
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB), // R[0] = 42
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42) });

		var result = Interpreter.Call(proto, Context());
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void JmpIf_NextInstruction_FallsThrough()
	{
		// JMPIF with offset 1 → next instruction, fall through.
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 1, b: 0, c: 0, flags: OpFlags.KB),      // R[1] = true
            new Instruction(OpCode.JMPIF, a: 1, b: 1, c: 0, flags: OpFlags.SignedBX), // offset 1 → next
            new Instruction(OpCode.MOVE, a: 0, b: 1, c: 0, flags: OpFlags.KB),      // R[0] = 42
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 2, constants: new LuaValue[] { LuaBoolean.True, new LuaNumber(42) });

		var result = Interpreter.Call(proto, Context());
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── Real backward loop ─────────────────────────────────────────────

	[Fact]
	public void Jmp_BackwardLoop_CountsDownCorrectly()
	{
		// Simulates: for i = 5, 1, -1 do ... end; return i
		//
		// [0] MOVE R0, K[0]    → R[0] = 5 (counter)
		// [1] EQ R1, R0, K[1]  → R[1] = (R[0] == 0)
		// [2] JMPIF R1, +3      → if R[0]==0, exit loop (jump to [5])
		// [3] SUB R0, R0, K[2] → R[0] = R[0] - 1
		// [4] JMP -3            → jump back to [1] (pc: 4 + (-3) = 1)
		// [5] RETURN R0

		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
			new Instruction(OpCode.EQ, a: 1, b: 0, c: 1, flags: OpFlags.None | OpFlags.KC),
			new Instruction(OpCode.JMPIF, a: 1, b: 3, c: 0, flags: OpFlags.SignedBX),
			new Instruction(OpCode.SUB, a: 0, b: 0, c: 2, flags: OpFlags.None | OpFlags.KC),
			new Instruction(OpCode.JMP, a: 0, b: unchecked((ushort)-3), c: 0, flags: OpFlags.SignedBX),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 2, constants: new LuaValue[]
		{
			new LuaNumber(5),
			new LuaNumber(0),
			new LuaNumber(1),
		});

		var result = Interpreter.Call(proto, Context());
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}
}
