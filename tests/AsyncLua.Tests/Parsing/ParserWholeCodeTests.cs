using AsyncLua.Parsing;
using AsyncLua.Parsing.Expressions;
using AsyncLua.Parsing.Statements;

namespace AsyncLua.Tests.Parsing;

public class ParserWholeCodeTests
{
	private static readonly AsyncLuaParser Parser = new();

	private static BlockNode ParseBlock(string source)
	{
		var result = Parser.Parser.ParseRule("block", source);
		return result.GetValue<BlockNode>();
	}

	// ── Empty / trivial ──────────────────────────────────────────────

	[Fact]
	public void Parse_EmptyBlock_ReturnsEmptyBlock()
	{
		var block = ParseBlock("");
		Assert.NotNull(block);
		Assert.Empty(block.Statements);
	}

	[Fact]
	public void Parse_BlockWithOnlyComments_ReturnsEmptyBlock()
	{
		var block = ParseBlock("-- this is a comment\n--[[ another ]]");
		Assert.Empty(block.Statements);
	}

	[Fact]
	public void Parse_BlockWithOnlyWhitespace_ReturnsEmptyBlock()
	{
		var block = ParseBlock("   \n  \t  \n  ");
		Assert.Empty(block.Statements);
	}

	// ── Sequence of independent statements ───────────────────────────

	[Fact]
	public void Parse_SequenceOfAssignments_ReturnsMultipleStatements()
	{
		var block = ParseBlock(
			"x = 1\n" +
			"y = 2\n" +
			"z = 3\n");

		Assert.Equal(3, block.Statements.Length);

		for (int i = 0; i < 3; i++)
		{
			var assign = Assert.IsType<AssignmentNode>(block.Statements[i]);
			Assert.Null(assign.Scope);
			Assert.Single(assign.Targets);
			Assert.Single(assign.Values);
		}
	}

	[Fact]
	public void Parse_MixedAssignmentsAndExpressions_ReturnsCorrectOrder()
	{
		var block = ParseBlock(
			"a = 10\n" +
			"b = a + 5\n" +
			"c = b * 2\n" +
			"d = c / 3\n");

		Assert.Equal(4, block.Statements.Length);
		Assert.Equal("a", ((IdentifierNode)((AssignmentNode)block.Statements[0]).Targets[0]).Name);
		Assert.Equal("b", ((IdentifierNode)((AssignmentNode)block.Statements[1]).Targets[0]).Name);
		Assert.Equal("c", ((IdentifierNode)((AssignmentNode)block.Statements[2]).Targets[0]).Name);
		Assert.Equal("d", ((IdentifierNode)((AssignmentNode)block.Statements[3]).Targets[0]).Name);
	}

	// ── Functions ────────────────────────────────────────────────────

	[Fact]
	public void Parse_SimpleFunctionDefinition_CorrectStructure()
	{
		var block = ParseBlock(
			"function greet(name)\n" +
			"    return \"Hello, \" .. name\n" +
			"end\n");

		Assert.Single(block.Statements);
		var funcDecl = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.Equal("greet", funcDecl.Name);
		Assert.Null(funcDecl.TargetObject);
		Assert.Null(funcDecl.Scope);
		Assert.Single(funcDecl.Parameters);
		Assert.Equal("name", funcDecl.Parameters[0].Name);
		Assert.Single(funcDecl.Body.Statements);
		var ret = Assert.IsType<ReturnNode>(funcDecl.Body.Statements[0]);
		Assert.Single(ret.Values);
		Assert.IsType<BinaryOperatorNode>(ret.Values[0]);
	}

	[Fact]
	public void Parse_FunctionWithMultipleParamsAndLocals_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"function distance(x1, y1, x2, y2)\n" +
			"    local dx = x2 - x1\n" +
			"    local dy = y2 - y1\n" +
			"    return math.sqrt(dx * dx + dy * dy)\n" +
			"end\n");

		Assert.Single(block.Statements);
		var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.Equal("distance", func.Name);
		Assert.Equal(4, func.Parameters.Length);
		Assert.Equal("x1", func.Parameters[0].Name);
		Assert.Equal("y1", func.Parameters[1].Name);
		Assert.Equal("x2", func.Parameters[2].Name);
		Assert.Equal("y2", func.Parameters[3].Name);

		Assert.Equal(3, func.Body.Statements.Length);
		Assert.IsType<AssignmentNode>(func.Body.Statements[0]); // local dx
		Assert.IsType<AssignmentNode>(func.Body.Statements[1]); // local dy
		Assert.IsType<ReturnNode>(func.Body.Statements[2]);
	}

	[Fact]
	public void Parse_LocalFunctionDefinition_ReturnsLocalScope()
	{
		var block = ParseBlock(
			"local function fib(n)\n" +
			"    if n <= 1 then return n end\n" +
			"    return fib(n - 1) + fib(n - 2)\n" +
			"end\n");

		Assert.Single(block.Statements);
		var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.Equal(VariableScope.Local, func.Scope);
		Assert.Equal("fib", func.Name);
	}

	[Fact]
	public void Parse_NestedFunctions_ReturnsCorrectHierarchy()
	{
		var block = ParseBlock(
			"function outer()\n" +
			"    local function inner()\n" +
			"        return 42\n" +
			"    end\n" +
			"    return inner()\n" +
			"end\n");

		Assert.Single(block.Statements);
		var outer = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.Equal("outer", outer.Name);

		Assert.Equal(2, outer.Body.Statements.Length);
		var inner = Assert.IsType<FunctionDeclStatementNode>(outer.Body.Statements[0]);
		Assert.Equal(VariableScope.Local, inner.Scope);
		Assert.Equal("inner", inner.Name);
		Assert.Single(inner.Body.Statements);
		Assert.IsType<ReturnNode>(inner.Body.Statements[0]);
	}

	[Fact]
	public void Parse_FunctionWithVarArg_ReturnsHasVarArgTrue()
	{
		var block = ParseBlock(
			"function sum(...)\n" +
			"    local total = 0\n" +
			"    for _, v in ipairs({...}) do\n" +
			"        total = total + v\n" +
			"    end\n" +
			"    return total\n" +
			"end\n");

		Assert.Single(block.Statements);
		var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.True(func.HasVarArg);
		Assert.Empty(func.Parameters); // vararg doesn't create a named parameter
	}

	// ── If / else / elseif chains ────────────────────────────────────

	[Fact]
	public void Parse_ChainedIfElseifElse_CorrectStructure()
	{
		var block = ParseBlock(
			"if x < 0 then\n" +
			"    sign = -1\n" +
			"elseif x == 0 then\n" +
			"    sign = 0\n" +
			"else\n" +
			"    sign = 1\n" +
			"end\n");

		Assert.Single(block.Statements);
		var ifNode = Assert.IsType<IfNode>(block.Statements[0]);

		Assert.IsType<BinaryOperatorNode>(ifNode.Condition);
		Assert.Single(ifNode.Body.Statements);

		Assert.Single(ifNode.ElseIfClauses);
		Assert.NotNull(ifNode.ElseIfClauses[0].Condition);
		Assert.Single(ifNode.ElseIfClauses[0].Body.Statements);

		Assert.NotNull(ifNode.ElseBlock);
		Assert.Single(ifNode.ElseBlock.Statements);
	}

	// ── Loops ────────────────────────────────────────────────────────

	[Fact]
	public void Parse_WhileLoop_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"i = 0\n" +
			"while i < 10 do\n" +
			"    i = i + 1\n" +
			"end\n");

		Assert.Equal(2, block.Statements.Length);

		var whileNode = Assert.IsType<WhileNode>(block.Statements[1]);
		Assert.IsType<BinaryOperatorNode>(whileNode.Condition);
		Assert.Single(whileNode.Body.Statements);
		Assert.IsType<AssignmentNode>(whileNode.Body.Statements[0]);
	}

	[Fact]
	public void Parse_WhileLoopWithBreak_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"i = 0\n" +
			"while i < 10 do\n" +
			"    if i == 5 then break end\n" +
			"    i = i + 1\n" +
			"end\n" +
			"return i\n");

		Assert.Equal(3, block.Statements.Length);

		var whileNode = Assert.IsType<WhileNode>(block.Statements[1]);
		Assert.Equal(2, whileNode.Body.Statements.Length);
		Assert.IsType<IfNode>(whileNode.Body.Statements[0]);
		Assert.IsType<AssignmentNode>(whileNode.Body.Statements[1]);
	}

	[Fact]
	public void Parse_RepeatUntil_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"x = 1\n" +
			"repeat\n" +
			"    x = x * 2\n" +
			"until x > 1000\n");

		Assert.Equal(2, block.Statements.Length);
		var repeat = Assert.IsType<RepeatNode>(block.Statements[1]);
		Assert.Single(repeat.Body.Statements);
		Assert.IsType<AssignmentNode>(repeat.Body.Statements[0]);
		Assert.IsType<BinaryOperatorNode>(repeat.Condition);
	}

	[Fact]
	public void Parse_NumericForLoop_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"local sum = 0\n" +
			"for i = 1, 100 do\n" +
			"    sum = sum + i\n" +
			"end\n" +
			"return sum\n");

		Assert.Equal(3, block.Statements.Length);
		var forNum = Assert.IsType<ForNumericNode>(block.Statements[1]);
		Assert.Equal("i", forNum.Variable);
		Assert.NotNull(forNum.Start);
		Assert.NotNull(forNum.Limit);
		Assert.Null(forNum.Step);
		Assert.Single(forNum.Body.Statements);
	}

	[Fact]
	public void Parse_NumericForLoopWithStep_ReturnsStepSet()
	{
		var block = ParseBlock(
			"for i = 0, 100, 2 do\n" +
			"    print(i)\n" +
			"end\n");

		Assert.Single(block.Statements);
		var forNum = Assert.IsType<ForNumericNode>(block.Statements[0]);
		Assert.Equal("i", forNum.Variable);
		Assert.NotNull(forNum.Step);
	}

	[Fact]
	public void Parse_GenericForLoop_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"local t = {a = 1, b = 2, c = 3}\n" +
			"for k, v in pairs(t) do\n" +
			"    print(k, v)\n" +
			"end\n");

		Assert.Equal(2, block.Statements.Length);
		var forIn = Assert.IsType<ForInNode>(block.Statements[1]);
		Assert.Equal(2, forIn.Variables.Length);
		Assert.Equal("k", forIn.Variables[0]);
		Assert.Equal("v", forIn.Variables[1]);
		Assert.Single(forIn.Expressions);
		Assert.Single(forIn.Body.Statements);
	}

	// ── Tables ───────────────────────────────────────────────────────

	[Fact]
	public void Parse_TableConstructor_ReturnsTableConstructionNode()
	{
		var block = ParseBlock(
			"t = {1, 2, 3, 4, 5}\n");

		Assert.Single(block.Statements);
		var assign = Assert.IsType<AssignmentNode>(block.Statements[0]);
		var tableNode = Assert.IsType<TableConstructionNode>(assign.Values[0]);
		Assert.Equal(5, tableNode.Pairs.Length);
	}

	[Fact]
	public void Parse_TableConstructorWithKeys_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"t = {x = 10, y = 20, z = 30}\n");

		Assert.Single(block.Statements);
		var assign = Assert.IsType<AssignmentNode>(block.Statements[0]);
		var tableNode = Assert.IsType<TableConstructionNode>(assign.Values[0]);
		Assert.Equal(3, tableNode.Pairs.Length);
	}

	[Fact]
	public void Parse_TableConstructorMixed_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"t = {\"apple\", \"banana\", key = \"value\", [1 + 1] = \"computed\"}\n");

		Assert.Single(block.Statements);
		var assign = Assert.IsType<AssignmentNode>(block.Statements[0]);
		var tableNode = Assert.IsType<TableConstructionNode>(assign.Values[0]);
		Assert.Equal(4, tableNode.Pairs.Length);
	}

	// ── Multiple return values ───────────────────────────────────────

	[Fact]
	public void Parse_MultipleReturnValues_ReturnsReturnNode()
	{
		var block = ParseBlock(
			"function minmax(a, b)\n" +
			"    if a < b then return a, b\n" +
			"    else return b, a end\n" +
			"end\n");

		Assert.Single(block.Statements);
		var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		var ifNode = Assert.IsType<IfNode>(func.Body.Statements[0]);

		var thenReturn = Assert.IsType<ReturnNode>(ifNode.Body.Statements[0]);
		Assert.Equal(2, thenReturn.Values.Length);

		var elseReturn = Assert.IsType<ReturnNode>(ifNode.ElseBlock!.Statements[0]);
		Assert.Equal(2, elseReturn.Values.Length);
	}

	// ── Async / await ────────────────────────────────────────────────

	[Fact]
	public void Parse_AsyncFunction_DetectsAsyncModifier()
	{
		var block = ParseBlock(
			"async function fetchData(url)\n" +
			"    local response = await http.get(url)\n" +
			"    return response.body\n" +
			"end\n");

		Assert.Single(block.Statements);
		var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.Equal("fetchData", func.Name);
		Assert.Equal(2, func.Body.Statements.Length);
	}

	[Fact]
	public void Parse_AwaitExpression_InsideAsyncFunction()
	{
		var block = ParseBlock(
			"async function run()\n" +
			"    local result = await someTask\n" +
			"    print(result)\n" +
			"end\n");

		Assert.Single(block.Statements);
		var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		var assign = Assert.IsType<AssignmentNode>(func.Body.Statements[0]);
		Assert.IsType<AwaitExpressionNode>(assign.Values[0]);
	}

	[Fact]
	public void Parse_AwaitStatement_ReturnsAwaitStatementNode()
	{
		var block = ParseBlock(
			"async function run()\n" +
			"    await someTask\n" +
			"end\n");

		Assert.Single(block.Statements);
		var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		var awaitStmt = Assert.IsType<AwaitStatementNode>(func.Body.Statements[0]);
		Assert.NotNull(awaitStmt.AwaitExpression);
	}

	// ── Method declarations ──────────────────────────────────────────

	[Fact]
	public void Parse_MethodDeclaration_ReturnsTargetObject()
	{
		var block = ParseBlock(
			"function obj:greet(name)\n" +
			"    self.name = name\n" +
			"    return \"Hello, \" .. self.name\n" +
			"end\n");

		Assert.Single(block.Statements);
		var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.Equal("greet", func.Name);
		Assert.NotNull(func.TargetObject);
		Assert.IsType<IdentifierNode>(func.TargetObject);
		Assert.Equal("obj", ((IdentifierNode)func.TargetObject).Name);
		Assert.Equal("greet", func.MethodName);
	}

	// ── Lock statement ───────────────────────────────────────────────

	[Fact]
	public void Parse_LockStatement_ReturnsLockNode()
	{
		var block = ParseBlock(
			"local mutex = {}\n" +
			"lock mutex do\n" +
			"    critical = newValue\n" +
			"end\n");

		Assert.Equal(2, block.Statements.Length);
		var lockNode = Assert.IsType<LockNode>(block.Statements[1]);
		Assert.IsType<IdentifierNode>(lockNode.Target);
		Assert.Single(lockNode.Body.Statements);
		Assert.IsType<AssignmentNode>(lockNode.Body.Statements[0]);
	}

	// ── Do block ─────────────────────────────────────────────────────

	[Fact]
	public void Parse_DoBlock_ReturnsDoNode()
	{
		var block = ParseBlock(
			"do\n" +
			"    local x = 1\n" +
			"    x = x + 1\n" +
			"end\n");

		Assert.Single(block.Statements);
		var doNode = Assert.IsType<DoNode>(block.Statements[0]);
		Assert.Equal(2, doNode.Body.Statements.Length);
	}

	// ── Realistic programs ───────────────────────────────────────────

	[Fact]
	public void Parse_FizzBuzzProgram_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"function fizzbuzz(n)\n" +
			"    for i = 1, n do\n" +
			"        if i % 15 == 0 then\n" +
			"            print(\"FizzBuzz\")\n" +
			"        elseif i % 3 == 0 then\n" +
			"            print(\"Fizz\")\n" +
			"        elseif i % 5 == 0 then\n" +
			"            print(\"Buzz\")\n" +
			"        else\n" +
			"            print(i)\n" +
			"        end\n" +
			"    end\n" +
			"end\n" +
			"fizzbuzz(100)\n");

		Assert.Equal(2, block.Statements.Length);

		var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.Equal("fizzbuzz", func.Name);
		Assert.Single(func.Parameters);
		Assert.Equal("n", func.Parameters[0].Name);

		var forLoop = Assert.IsType<ForNumericNode>(func.Body.Statements[0]);
		Assert.Equal("i", forLoop.Variable);
		Assert.Single(forLoop.Body.Statements);

		var ifNode = Assert.IsType<IfNode>(forLoop.Body.Statements[0]);
		Assert.Equal(2, ifNode.ElseIfClauses.Length);
		Assert.NotNull(ifNode.ElseBlock);

		var callStmt = Assert.IsType<CallStatementNode>(block.Statements[1]);
		Assert.NotNull(callStmt.Call);
	}

	[Fact]
	public void Parse_FactorialRecursive_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"function fact(n)\n" +
			"    if n <= 1 then\n" +
			"        return 1\n" +
			"    end\n" +
			"    return n * fact(n - 1)\n" +
			"end\n");

		Assert.Single(block.Statements);
		var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.Equal("fact", func.Name);
		Assert.Equal(2, func.Body.Statements.Length);

		var ifNode = Assert.IsType<IfNode>(func.Body.Statements[0]);
		Assert.Single(ifNode.Body.Statements);
		Assert.IsType<ReturnNode>(ifNode.Body.Statements[0]);
		Assert.Null(ifNode.ElseBlock);

		var ret = Assert.IsType<ReturnNode>(func.Body.Statements[1]);
		var binOp = Assert.IsType<BinaryOperatorNode>(ret.Values[0]);
		Assert.Equal(BinaryOperatorType.Multiply, binOp.Operator);
	}

	[Fact]
	public void Parse_QuickSortProgram_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"function quicksort(arr, low, high)\n" +
			"    if low < high then\n" +
			"        local p = partition(arr, low, high)\n" +
			"        quicksort(arr, low, p - 1)\n" +
			"        quicksort(arr, p + 1, high)\n" +
			"    end\n" +
			"end\n" +
			"\n" +
			"function partition(arr, low, high)\n" +
			"    local pivot = arr[high]\n" +
			"    local i = low - 1\n" +
			"    for j = low, high - 1 do\n" +
			"        if arr[j] <= pivot then\n" +
			"            i = i + 1\n" +
			"            arr[i], arr[j] = arr[j], arr[i]\n" +
			"        end\n" +
			"    end\n" +
			"    arr[i + 1], arr[high] = arr[high], arr[i + 1]\n" +
			"    return i + 1\n" +
			"end\n");

		Assert.Equal(2, block.Statements.Length);

		var quicksortFn = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.Equal("quicksort", quicksortFn.Name);
		Assert.Equal(3, quicksortFn.Parameters.Length);
		Assert.Single(quicksortFn.Body.Statements);
		Assert.IsType<IfNode>(quicksortFn.Body.Statements[0]);

		var partitionFn = Assert.IsType<FunctionDeclStatementNode>(block.Statements[1]);
		Assert.Equal("partition", partitionFn.Name);
		Assert.Equal(3, partitionFn.Parameters.Length);

		// Partition body: local pivot, local i, for loop, swap, return
		Assert.Equal(5, partitionFn.Body.Statements.Length);
		Assert.IsType<AssignmentNode>(partitionFn.Body.Statements[0]); // local pivot
		Assert.IsType<AssignmentNode>(partitionFn.Body.Statements[1]); // local i
		Assert.IsType<ForNumericNode>(partitionFn.Body.Statements[2]); // for j
		Assert.IsType<AssignmentNode>(partitionFn.Body.Statements[3]); // swap (multiple assignment)
		Assert.IsType<ReturnNode>(partitionFn.Body.Statements[4]); // return
	}

	// ── Complex expressions ──────────────────────────────────────────

	[Fact]
	public void Parse_ComplexArithmeticExpression_ReturnsBinaryTree()
	{
		var block = ParseBlock(
			"result = (a + b) * (c - d) / (e % f) ^ g\n");

		Assert.Single(block.Statements);
		var assign = Assert.IsType<AssignmentNode>(block.Statements[0]);
		Assert.Single(assign.Values);
		Assert.IsType<BinaryOperatorNode>(assign.Values[0]);
	}

	[Fact]
	public void Parse_LogicalExpressions_ReturnsBinaryOperatorNodes()
	{
		var block = ParseBlock(
			"flag = a and b or not c\n" +
			"test = (x > 5) and (y < 10) or (z == 0)\n");

		Assert.Equal(2, block.Statements.Length);
		Assert.IsType<BinaryOperatorNode>(((AssignmentNode)block.Statements[0]).Values[0]);
		Assert.IsType<BinaryOperatorNode>(((AssignmentNode)block.Statements[1]).Values[0]);
	}

	[Fact]
	public void Parse_StringConcatenation_ReturnsBinaryOperatorNode()
	{
		var block = ParseBlock(
			"full = first .. \" \" .. last\n");

		Assert.Single(block.Statements);
		var assign = Assert.IsType<AssignmentNode>(block.Statements[0]);
		Assert.IsType<BinaryOperatorNode>(assign.Values[0]);
	}

	// ── Error handling / edge cases ──────────────────────────────────

	[Fact]
	public void Parse_IncompleteBlock_ThrowsException()
	{
		Assert.ThrowsAny<Exception>(() => Parser.Parse("function foo("));
	}

	[Fact]
	public void Parse_MissingEnd_ThrowsException()
	{
		Assert.ThrowsAny<Exception>(() => Parser.Parse("if true then\n  x = 1\n"));
	}

	[Fact]
	public void Parse_UnmatchedBracket_ThrowsException()
	{
		Assert.ThrowsAny<Exception>(() => Parser.Parse("x = (1 + 2\n"));
	}

	[Fact]
	public void Parse_DoubleSemicolons_SeparatesStatements()
	{
		var block = ParseBlock("x = 1;; y = 2;;; z = 3\n");
		Assert.Equal(3, block.Statements.Length);
	}

	// ── Concurrent / async patterns ──────────────────────────────────

	[Fact]
	public void Parse_AsyncFunctionWithMultipleAwaits_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"async function processItems(items)\n" +
			"    local results = {}\n" +
			"    for i = 1, #items do\n" +
			"        local data = await fetch(items[i])\n" +
			"        local processed = await process(data)\n" +
			"        results[i] = processed\n" +
			"    end\n" +
			"    return results\n" +
			"end\n");

		Assert.Single(block.Statements);
		var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.Equal("processItems", func.Name);
		Assert.Single(func.Parameters);

		var forLoop = Assert.IsType<ForNumericNode>(func.Body.Statements[1]);
		Assert.Equal(3, forLoop.Body.Statements.Length);
	}

	// ── Mixed feature demonstration ──────────────────────────────────

	[Fact]
	public void Parse_RealisticDataProcessingScript_ReturnsValidAST()
	{
		var script =
			"-- Data processing pipeline\n" +
			"local function loadData(filename)\n" +
			"    local file = io.open(filename, \"r\")\n" +
			"    if not file then\n" +
			"        error(\"Cannot open file: \" .. filename)\n" +
			"    end\n" +
			"    local content = file:read(\"*all\")\n" +
			"    file:close()\n" +
			"    return content\n" +
			"end\n" +
			"\n" +
			"local function parseLines(text)\n" +
			"    local lines = {}\n" +
			"    for line in text:gmatch(\"[^\\n]+\") do\n" +
			"        table.insert(lines, line)\n" +
			"    end\n" +
			"    return lines\n" +
			"end\n" +
			"\n" +
			"local function processLine(line, lookup)\n" +
			"    local parts = {}\n" +
			"    local idx = 1\n" +
			"    for part in line:gmatch(\"[^,]+\") do\n" +
			"        local mapped = lookup[part] or part\n" +
			"        parts[idx] = mapped\n" +
			"        idx = idx + 1\n" +
			"    end\n" +
			"    return table.concat(parts, \",\")\n" +
			"end\n" +
			"\n" +
			"local data = loadData(\"input.csv\")\n" +
			"local lines = parseLines(data)\n" +
			"local lookup = {old = \"new\", foo = \"bar\"}\n" +
			"local results = {}\n" +
			"for i = 1, #lines do\n" +
			"    results[i] = processLine(lines[i], lookup)\n" +
			"end\n" +
			"return results\n";

		var block = ParseBlock(script);

		// Total top-level statements:
		// 3 local function declarations + 4 local assignments + 1 for loop + 1 return
		Assert.Equal(9, block.Statements.Length);

		// First three are local functions
		for (int i = 0; i < 3; i++)
		{
			var func = Assert.IsType<FunctionDeclStatementNode>(block.Statements[i]);
			Assert.Equal(VariableScope.Local, func.Scope);
		}

		// Functions have bodies
		var loadDataFn = (FunctionDeclStatementNode)block.Statements[0];
		Assert.Equal("loadData", loadDataFn.Name);
		Assert.True(loadDataFn.Body.Statements.Length >= 4);

		var parseLinesFn = (FunctionDeclStatementNode)block.Statements[1];
		Assert.Equal("parseLines", parseLinesFn.Name);
		Assert.IsType<ForInNode>(parseLinesFn.Body.Statements[1]);

		var processLineFn = (FunctionDeclStatementNode)block.Statements[2];
		Assert.Equal("processLine", processLineFn.Name);
		Assert.IsType<ForInNode>(processLineFn.Body.Statements[2]);

		// Followed by local assignments
		Assert.IsType<AssignmentNode>(block.Statements[3]); // local data
		Assert.IsType<AssignmentNode>(block.Statements[4]); // local lines
		Assert.IsType<AssignmentNode>(block.Statements[5]); // local lookup
		Assert.IsType<AssignmentNode>(block.Statements[6]); // local results

		// For loop
		Assert.IsType<ForNumericNode>(block.Statements[7]);

		// Final return
		Assert.IsType<ReturnNode>(block.Statements[8]);
	}

	// ── Large generated program ──────────────────────────────────────

	[Fact]
	public void Parse_LargeGeneratedProgram_DoesNotThrow()
	{
		var lines = new System.Collections.Generic.List<string>();
		lines.Add("-- Large generated program stress test");

		// Generate 50 local variables
		for (int i = 0; i < 50; i++)
		{
			lines.Add($"local var{i} = {i * 2 + 1}");
		}

		// Generate 30 simple functions
		for (int i = 0; i < 30; i++)
		{
			lines.Add($"function func{i}(x) return x + {i} end");
		}

		// Nested if-else chain
		lines.Add("function classify(n)");
		lines.Add("    if n < 0 then return \"negative\"");
		for (int i = 0; i < 10; i++)
		{
			int lower = i * 10;
			int upper = lower + 9;
			lines.Add($"    elseif n >= {lower} and n <= {upper} then return \"range_{i}\"");
		}
		lines.Add("    else return \"large\" end");
		lines.Add("end");

		// Table with many entries
		lines.Add("local big_table = {");
		for (int i = 0; i < 100; i++)
		{
			lines.Add($"    [\"key_{i}\"] = {i},");
		}
		lines.Add("}");

		// For loop over table
		lines.Add("local sum = 0");
		lines.Add("for k, v in pairs(big_table) do");
		lines.Add("    sum = sum + v");
		lines.Add("end");

		var source = string.Join("\n", lines);

		var block = ParseBlock(source);

		// Top-level: 50 locals + 30 funcs + 1 classify + 1 big_table + 1 sum + 1 for = 84
		int expectedStatements =
			50   // local vars
			+ 30 // function declarations
			+ 1  // classify function
			+ 1  // local big_table
			+ 2; // local sum + for loop

		Assert.Equal(expectedStatements, block.Statements.Length);
	}

	[Fact]
	public void Parse_DeeplyNestedBlocks_ReturnsValidAST()
	{
		var source =
			"function level1()\n" +
			"    function level2()\n" +
			"        function level3()\n" +
			"            function level4()\n" +
			"                function level5()\n" +
			"                    return 42\n" +
			"                end\n" +
			"            end\n" +
			"        end\n" +
			"    end\n" +
			"end\n";

		var block = ParseBlock(source);
		Assert.Single(block.Statements);

		var l1 = Assert.IsType<FunctionDeclStatementNode>(block.Statements[0]);
		Assert.Equal("level1", l1.Name);
		Assert.Single(l1.Body.Statements);

		var l2 = Assert.IsType<FunctionDeclStatementNode>(l1.Body.Statements[0]);
		Assert.Equal("level2", l2.Name);
		Assert.Single(l2.Body.Statements);

		var l3 = Assert.IsType<FunctionDeclStatementNode>(l2.Body.Statements[0]);
		Assert.Equal("level3", l3.Name);
		Assert.Single(l3.Body.Statements);

		var l4 = Assert.IsType<FunctionDeclStatementNode>(l3.Body.Statements[0]);
		Assert.Equal("level4", l4.Name);
		Assert.Single(l4.Body.Statements);

		var l5 = Assert.IsType<FunctionDeclStatementNode>(l4.Body.Statements[0]);
		Assert.Equal("level5", l5.Name);
		Assert.Single(l5.Body.Statements);

		Assert.IsType<ReturnNode>(l5.Body.Statements[0]);
	}

	// ── Edge cases ───────────────────────────────────────────────────

	[Fact]
	public void Parse_UnicodeIdentifiers_ReturnsCorrectNames()
	{
		var block = ParseBlock(
			"привет = 42\n" +
			"π = 3.14159\n" +
			"Δx = 10\n");

		Assert.Equal(3, block.Statements.Length);
		Assert.Equal("привет", ((IdentifierNode)((AssignmentNode)block.Statements[0]).Targets[0]).Name);
		Assert.Equal("π", ((IdentifierNode)((AssignmentNode)block.Statements[1]).Targets[0]).Name);
		Assert.Equal("Δx", ((IdentifierNode)((AssignmentNode)block.Statements[2]).Targets[0]).Name);
	}

	[Fact]
	public void Parse_MultipleAssignmentsSwap_ReturnsCorrectStructure()
	{
		var block = ParseBlock(
			"a, b = 1, 2\n" +
			"a, b = b, a\n");  // idiomatic swap

		Assert.Equal(2, block.Statements.Length);

		var swap = Assert.IsType<AssignmentNode>(block.Statements[1]);
		Assert.Equal(2, swap.Targets.Length);
		Assert.Equal(2, swap.Values.Length);
		Assert.IsType<IdentifierNode>(swap.Values[0]);
		Assert.IsType<IdentifierNode>(swap.Values[1]);
	}

	[Fact]
	public void Parse_GotoStatement_ReturnsGotoNode()
	{
		var block = ParseBlock(
			"goto exit\n" +
			"::exit::\n" +
			"return 0\n");

		Assert.Equal(3, block.Statements.Length);
		Assert.IsType<GotoNode>(block.Statements[0]);
		Assert.IsType<LabelNode>(block.Statements[1]);
		Assert.IsType<ReturnNode>(block.Statements[2]);
	}

	[Fact]
	public void Parse_BreakStatement_ReturnsBreakNode()
	{
		var block = ParseBlock(
			"for i = 1, 10 do\n" +
			"    if i == 5 then break end\n" +
			"end\n");

		Assert.Single(block.Statements);
		var forNode = Assert.IsType<ForNumericNode>(block.Statements[0]);
		var ifNode = Assert.IsType<IfNode>(forNode.Body.Statements[0]);
		Assert.IsType<BreakNode>(ifNode.Body.Statements[0]);
	}

	[Fact]
	public void Parse_ExpressionAsStatement_CreatesCallStatementNode()
	{
		var block = ParseBlock(
			"print(\"hello world\")\n" +
			"io.write(\"test\")\n");

		Assert.Equal(2, block.Statements.Length);
		Assert.IsType<CallStatementNode>(block.Statements[0]);
		Assert.IsType<CallStatementNode>(block.Statements[1]);
	}

	[Fact]
	public void Parse_MethodCallStatement_ReturnsCallStatementWithMethod()
	{
		var block = ParseBlock(
			"obj:method(arg1, arg2)\n");

		Assert.Single(block.Statements);
		var callStmt = Assert.IsType<CallStatementNode>(block.Statements[0]);
		Assert.Equal("method", callStmt.Call.Method);
		Assert.Equal(2, callStmt.Call.Arguments.Length);
	}

	[Fact]
	public void Parse_ChainedFunctionCalls_ReturnsNestedFunctionCallNodes()
	{
		var block = ParseBlock(
			"result = foo(bar(baz(x)))\n");

		Assert.Single(block.Statements);
		var assign = Assert.IsType<AssignmentNode>(block.Statements[0]);
		var outerCall = Assert.IsType<FunctionCallNode>(assign.Values[0]);
		Assert.Equal("foo", ((IdentifierNode)outerCall.Target).Name);
		Assert.Single(outerCall.Arguments);
		var middleCall = Assert.IsType<FunctionCallNode>(outerCall.Arguments[0]);
		Assert.Equal("bar", ((IdentifierNode)middleCall.Target).Name);
		Assert.Single(middleCall.Arguments);
		var innerCall = Assert.IsType<FunctionCallNode>(middleCall.Arguments[0]);
		Assert.Equal("baz", ((IdentifierNode)innerCall.Target).Name);
		Assert.Single(innerCall.Arguments);
		Assert.IsType<IdentifierNode>(innerCall.Arguments[0]);
	}
}
