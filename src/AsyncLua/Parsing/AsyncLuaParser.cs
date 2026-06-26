using System;
using System.Collections.Generic;
using System.Linq;
using AsyncLua.Parsing.Expressions;
using AsyncLua.Parsing.Statements;
using AsyncLua.Values;
using RCParsing;
using RCParsing.TokenPatterns;

namespace AsyncLua.Parsing
{
	public class AsyncLuaParser
	{
		public Parser Parser { get; }

		public AsyncLuaParser()
		{
			Parser = CreateParser();
		}

		protected virtual Parser CreateParser()
		{
			var builder = new ParserBuilder();

			FillWithDefaultRules(builder);

			ModifyParserBuilder(builder);

			return builder.Build();
		}

		public BlockNode Parse(string input)
		{
			return Parser.Parse<BlockNode>(input);
		}

		protected virtual void ModifyParserBuilder(ParserBuilder builder)
		{
		}

		private static void FillWithDefaultRules(ParserBuilder builder)
		{
			builder.CreateRule("skip")
				.Choice(
					b => b.Spaces(),
					b => b.Newline(),
					b => b.Literal("--[[").TextUntil("]]").Literal("]]"),
					b => b.Literal("--").TextUntil('\n', '\r')
				)
				.ConfigureForSkip();

			builder.Settings.Skip(b => b.Rule("skip"), ParserSkippingStrategy.TryParseThenSkipLazy);

			builder.Settings.UseCaching();

			DeclareExpressions(builder);

			DeclareStatements(builder);

			builder.CreateMainRule("program")
				.Rule("block")
				.EOF()
				.TransformSelect(0);
		}

		private static void DeclareExpressions(ParserBuilder builder)
		{
			// ── Tokens ────────────────────────────────────────────────────

			builder.CreateToken("keywords")
				.KeywordChoice(
					"async", "await", "lock", "function",
					"return", "if", "goto", "then",
					"elseif", "else", "end", "for",
					"while", "do", "repeat", "until",
					"break", "local", "global", "and",
					"or", "not", "in", "true",
					"false", "nil", "continue", "try",
					"catch", "throw");

			builder.CreateToken("minus")
				.First(
					b => b.Literal("-"),
					b => b.NegativeLookahead(b => b.Literal("-"))
				);

			builder.CreateToken("identifier")
				.Second(
					b => b.NegativeLookahead(b => b.Token("keywords")),
					b => b.MapSpan(b => b.UnicodeIdentifier(), s => s.ToString())
				);

			builder.CreateToken("literal")
				.KeywordChoice(("true", LuaBoolean.True), ("false", LuaBoolean.False), ("nil", LuaNil.Instance));

			builder.CreateToken("number")
				.LongestChoice(
					b => b.Map<long>(b => b.IntegerNumber<long>(IntegerNumberFlags.GroupSeparators, defaultBase: 10, baseMappings: new Dictionary<char, int> {
						['x'] = 16,
						['X'] = 16,
						['b'] = 2,
						['B'] = 2
					}), d => new LuaNumber(d)),
					b => b.Map<double>(b => b.Number<double>(NumberFlags.StrictUnsignedScientific),
						d => new LuaNumber(d))
				);

			builder.CreateToken("string")
				.Map<string>(b => b.Choice(
					b => b.Between(b => b.Literal('\"'), b => b.EscapedText(new AsyncLuaStringEscapingStrategy { IsSingleQuote = false }), b => b.Literal('\"')),
					b => b.Between(b => b.Literal('\''), b => b.EscapedText(new AsyncLuaStringEscapingStrategy { IsSingleQuote = true }), b => b.Literal('\'')),
					b => b.Between(b => b.Literal("[["), b => b.TextUntil("]]"), b => b.Literal("]]"))
				), s => new LuaString(s));

			// ── Primaries ────────────────────────────────────────────────

			builder.CreateRule("literal_expr")
				.Token(b => b.Choice(
					b => b.Token("literal"),
					b => b.Token("number"),
					b => b.Token("string")
				))
				.Transform(v => new LiteralNode { Literal = v.GetIntermediateValue<LuaValue>(), Position = CodePositionalInfo.From(v) });

			builder.CreateRule("identifier_expr")
				.Token("identifier")
				.ToSequence()
				.Transform(v => new IdentifierNode { Name = v.Text, Position = CodePositionalInfo.From(v) });

			builder.CreateRule("vararg_expr")
				.Literal("...")
				.Transform(v => new VarArgumentNode { Position = CodePositionalInfo.From(v) });

			builder.CreateRule("table_constructor")
				.Literal("{")
				.ZeroOrMoreSeparated(
					b => b.Rule("table_field"),
					s => s.Literal(","),
					allowTrailingSeparator: true)
				.Literal("}")
				.Transform(v =>
				{
					var pairs = v.Children[1].SelectValues<TableConstructionPair>();
					return new TableConstructionNode { Pairs = pairs.ToArray(), Position = CodePositionalInfo.From(v) };
				});

			builder.CreateRule("table_field")
				.Choice(
					// [key] = value
					b => b
						.Literal("[")
						.Rule("expression")
						.Literal("]")
						.Literal("=")
						.Rule("expression")
						.Transform(v => new TableConstructionPair
						{
							Key = v.GetValue<ExpressionNode>(1),
							Value = v.GetValue<ExpressionNode>(4)
						}),
					// key = value (identifier as key)
					b => b
						.Token("identifier")
						.Literal("=")
						.Rule("expression")
						.Transform(v => new TableConstructionPair
						{
							Key = new LiteralNode { Literal = new LuaString(v.GetIntermediateValue<string>(0)) },
							Value = v.GetValue<ExpressionNode>(2)
						}),
					// value (array element)
					b => b
						.Rule("expression")
						.ToSequence()
						.Transform(v => new TableConstructionPair
						{
							Key = null,
							Value = v.GetValue<ExpressionNode>(0)
						})
				);

			// ── Function expression ──────────────────────────────────────

			builder.CreateRule("func_params")
				.Literal("(")
				.ZeroOrMoreSeparated(
					b => b.Choice(
						b => b.Token("identifier"),
						b => b.Literal("...")
					),
					s => s.Literal(","))
				.Literal(")")
				.Transform(v =>
				{
					var paramNames = v.Children[1].Select(v => v.GetValue<string>());
					var parameters = paramNames.Select(n => new ParameterNode { Name = n, IsVarArg = n == "..." });
					return parameters.ToArray();
				});

			builder.CreateRule("function_expr")
				.Optional(b => b.Keyword("async"))
				.Keyword("function")
				.Rule("func_params")
				.Rule("block")
				.Keyword("end")
				.Transform(v =>
				{
					var parameters = v.GetValue<ParameterNode[]>(2);

					return new FunctionDeclExpressionNode
					{
						IsAsync = v.Children[0].Length > 0,
						Parameters = parameters.Where(p => !p.IsVarArg).ToArray(),
						HasVarArg = parameters.Any(p => p.IsVarArg),
						Body = v.GetValue<BlockNode>(3),
						Position = CodePositionalInfo.From(v)
					};
				});

			// ── Await expression (AsyncLua extension) ───────────────────

			builder.CreateRule("await_expr")
				.Literal("await")
				.OneOrMoreSeparated(
					b => b.Rule("postfix_expr"),
					s => s.Literal(","))
				.Transform(v => new AwaitExpressionNode
				{
					Expressions = v.SelectArray<ExpressionNode>(1),
					Position = CodePositionalInfo.From(v)
				});

			// ── Primary ──────────────────────────────────────────────────

			builder.CreateRule("primary")
				.Choice(
					c => c.Rule("literal_expr"),
					c => c.Rule("function_expr"),
					c => c.Rule("table_constructor"),
					c => c.Rule("vararg_expr"),
					c => c.Rule("identifier_expr"),
					c => c.Literal("(").Rule("expression").Literal(")").TransformSelect(1));

			// ── Postfix (calls, indexing, member access) ────────────────

			builder.CreateRule("postfix_expr")
				.Rule("primary")
				.ZeroOrMore(b => b.Choice(
					// method call: obj:method(args)
					b => b
						.Literal(":")
						.Token("identifier")
						.Literal("(")
						.ZeroOrMoreSeparated(
							a => a.Rule("expression"),
							s => s.Literal(","))
						.Literal(")")
						.Transform(v => new FunctionCallNode
						{
							Target = null!, // filled later
							Method = v.GetIntermediateValue<string>(1),
							Arguments = v.Children[3].SelectValues<ExpressionNode>().ToArray(),
							Position = CodePositionalInfo.From(v)
						}),
					// index: obj[key]
					b => b
						.Literal("[")
						.Rule("expression")
						.Literal("]")
						.Transform(v => new IndexNode
						{
							Target = null!, // filled later
							Index = v.GetValue<ExpressionNode>(1),
							Position = CodePositionalInfo.From(v)
						}),
					// member access: obj.key
					b => b
						.Literal(".")
						.Token("identifier")
						.Transform(v => new IndexNode
						{
							Target = null!, // filled later
							Index = new LiteralNode
							{
								Literal = new LuaString(v.GetIntermediateValue<string>(1)),
								Position = CodePositionalInfo.From(v[1])
							},
							Position = CodePositionalInfo.From(v)
						}),
					// function call: obj(args)
					b => b
						.Literal("(")
						.ZeroOrMoreSeparated(
							a => a.Rule("expression"),
							s => s.Literal(","))
						.Literal(")")
						.Transform(v => new FunctionCallNode
						{
							Target = null!, // filled later
							Method = null,
							Arguments = v.Children[1].SelectValues<ExpressionNode>().ToArray(),
							Position = CodePositionalInfo.From(v)
						})
				))
				.Transform(v =>
				{
					ExpressionNode target = v.GetValue<ExpressionNode>(0);

					foreach (var child in v.Children[1])
					{
						var opNode = child.GetValue<ExpressionNode>();

						if (opNode is FunctionCallNode call)
						{
							call.Target = target;
							target = call;
						}
						else if (opNode is IndexNode index)
						{
							index.Target = target;
							target = index;
						}
					}

					return target;
				});

			// ── Unary operators ──────────────────────────────────────────

			builder.CreateRule("unary_expr")
				.ZeroOrMore(b => b.Choice(
					b => b.Token("minus"),
					b => b.Literal("#"),
					b => b.KeywordChoice("await", "not")))
				.Rule("power_expr")
				.Transform(v =>
				{
					ExpressionNode target = v.GetValue<ExpressionNode>(1);

					foreach (var _op in v.Children[0].Reverse())
					{
						var opStr = _op.GetIntermediateValue<string>();
						if (opStr == "await")
						{
							target = new AwaitExpressionNode
							{
								Expressions = new[] { target },
								Position = CodePositionalInfo.From(v)
							};
						}
						else
						{
							var type = opStr switch
							{
								"-" => UnaryOperatorType.Minus,
								"not" => UnaryOperatorType.LogicalNot,
								"#" => UnaryOperatorType.LengthOf,
								_ => throw new InvalidOperationException($"Unknown unary operator '{opStr}'")
							};
							target = new UnaryOperatorNode
							{
								Type = type,
								Operand = target,
								Position = CodePositionalInfo.From(v)
							};
						}
					}

					return target;
				});

			// ── Binary operators (precedence chain) ──────────────────────

			// Power (right-associative)
			builder.CreateRule("power_expr")
				.OneOrMoreSeparated(
					b => b.Rule("postfix_expr"),
					o => o.Literal("^"),
					includeSeparatorsInResult: true)
				.Transform(v =>
				{
					var children = v.Children;
					var expr = children.Last().GetValue<ExpressionNode>();
					for (int i = children.Count - 3; i >= 0; i -= 2)
					{
						var left = children[i].GetValue<ExpressionNode>();
						expr = new BinaryOperatorNode
						{
							Operator = BinaryOperatorType.Exponentiate,
							Left = left,
							Right = expr,
							Position = CodePositionalInfo.From(v)
						};
					}
					return expr;
				});

			// Multiplicative: *, /, //, %
			builder.CreateRule("multiplicative_expr")
				.OneOrMoreSeparated(
					b => b.Rule("unary_expr"),
					o => o.LiteralChoice("*", "/", "//", "%"),
					includeSeparatorsInResult: true)
				.Transform(v => FoldBinaryOperators(v, new Dictionary<string, BinaryOperatorType>
				{
					["*"] = BinaryOperatorType.Multiply,
					["/"] = BinaryOperatorType.Divide,
					["//"] = BinaryOperatorType.IntegerDivide,
					["%"] = BinaryOperatorType.Modulus,
				}));

			// Additive: +, -
			builder.CreateRule("additive_expr")
				.OneOrMoreSeparated(
					b => b.Rule("multiplicative_expr"),
					o => o.Choice(
						b => b.Token("minus"),
						b => b.Literal("+")
					),
					includeSeparatorsInResult: true)
				.Transform(v => FoldBinaryOperators(v, new Dictionary<string, BinaryOperatorType>
				{
					["+"] = BinaryOperatorType.Add,
					["-"] = BinaryOperatorType.Substract,
				}));

			// Concat: .. (right-associative)
			builder.CreateRule("concat_expr")
				.OneOrMoreSeparated(
					b => b.Rule("additive_expr"),
					o => o.Literal(".."),
					includeSeparatorsInResult: true)
				.Transform(v =>
				{
					var children = v.Children;
					var expr = children.Last().GetValue<ExpressionNode>();
					for (int i = children.Count - 3; i >= 0; i -= 2)
					{
						var left = children[i].GetValue<ExpressionNode>();
						expr = new BinaryOperatorNode
						{
							Operator = BinaryOperatorType.Concatenate,
							Left = left,
							Right = expr,
							Position = CodePositionalInfo.From(v)
						};
					}
					return expr;
				});

			// Bitwise shift operators: <<, >>
			builder.CreateRule("bitwise_shift_expr")
				.OneOrMoreSeparated(
					b => b.Rule("concat_expr"),
					o => o.LiteralChoice("<<", ">>"),
					includeSeparatorsInResult: true)
				.Transform(v => FoldBinaryOperators(v, new Dictionary<string, BinaryOperatorType>
				{
					["<<"] = BinaryOperatorType.BitShiftLeft,
					[">>"] = BinaryOperatorType.BitShiftRight,
				}));

			// Bitwise AND: &
			builder.CreateRule("bitwise_and_expr")
				.OneOrMoreSeparated(
					b => b.Rule("bitwise_shift_expr"),
					o => o.Literal("&"),
					includeSeparatorsInResult: true)
				.Transform(v => FoldBinaryOperators(v, new Dictionary<string, BinaryOperatorType>
				{
					["&"] = BinaryOperatorType.BitAnd,
				}));

			// Bitwise XOR: ~
			builder.CreateRule("bitwise_xor_expr")
				.OneOrMoreSeparated(
					b => b.Rule("bitwise_and_expr"),
					o => o.Literal("~"),
					includeSeparatorsInResult: true)
				.Transform(v => FoldBinaryOperators(v, new Dictionary<string, BinaryOperatorType>
				{
					["~"] = BinaryOperatorType.BitXor,
				}));

			// Bitwise OR: |
			builder.CreateRule("bitwise_or_expr")
				.OneOrMoreSeparated(
					b => b.Rule("bitwise_xor_expr"),
					o => o.Literal("|"),
					includeSeparatorsInResult: true)
				.Transform(v => FoldBinaryOperators(v, new Dictionary<string, BinaryOperatorType>
				{
					["|"] = BinaryOperatorType.BitOr,
				}));

			// Relational: <, >, <=, >=, ==, ~=
			builder.CreateRule("relational_expr")
				.OneOrMoreSeparated(
					b => b.Rule("bitwise_or_expr"),
					o => o.LiteralChoice("<", ">", "<=", ">=", "==", "~="),
					includeSeparatorsInResult: true)
				.Transform(v => FoldBinaryOperators(v, new Dictionary<string, BinaryOperatorType>
				{
					["<"] = BinaryOperatorType.LessThan,
					[">"] = BinaryOperatorType.GreaterThan,
					["<="] = BinaryOperatorType.LessThanEqual,
					[">="] = BinaryOperatorType.GreaterThanEqual,
					["=="] = BinaryOperatorType.Equals,
					["~="] = BinaryOperatorType.NotEquals,
				}));

			// Logical AND
			builder.CreateRule("and_expr")
				.OneOrMoreSeparated(
					b => b.Rule("relational_expr"),
					o => o.Keyword("and"),
					includeSeparatorsInResult: true)
				.Transform(v => FoldBinaryOperators(v, new Dictionary<string, BinaryOperatorType>
				{
					["and"] = BinaryOperatorType.LogicalAnd,
				}));

			// Logical OR
			builder.CreateRule("or_expr")
				.OneOrMoreSeparated(
					b => b.Rule("and_expr"),
					o => o.Keyword("or"),
					includeSeparatorsInResult: true)
				.Transform(v => FoldBinaryOperators(v, new Dictionary<string, BinaryOperatorType>
				{
					["or"] = BinaryOperatorType.LogicalOr,
				}));

			// ── Expression (top-level) ───────────────────────────────────

			builder.CreateRule("expression")
				.Rule("or_expr");
		}

		/// <summary>
		/// Helper for left-associative binary operator folding.
		/// Expects children to alternate between operands and operator tokens.
		/// </summary>
		private static object? FoldBinaryOperators(ParsedRuleResultBase result, Dictionary<string, BinaryOperatorType> opMap)
		{
			var children = result.Children;
			var expr = children[0].GetValue<ExpressionNode>();

			for (int i = 1; i < children.Count; i += 2)
			{
				var opStr = children[i].GetIntermediateValue<string>();
				var right = children[i + 1].GetValue<ExpressionNode>();

				if (!opMap.TryGetValue(opStr, out var opType))
					throw new InvalidOperationException($"Unknown operator '{opStr}'");

				expr = new BinaryOperatorNode
				{
					Operator = opType,
					Left = expr,
					Right = right,
					Position = CodePositionalInfo.From(result)
				};
			}

			return expr;
		}

		private static void DeclareStatements(ParserBuilder builder)
		{
			// ── Separators and blocks ─────────────────────────────────

			builder.CreateRule("statement_separator")
				.Choice(
					b => b.Literal(";"),
					b => b.Newline());

			builder.CreateRule("block")
				.ZeroOrMoreSeparated(b => b.Rule("statement"), s => s.OneOrMore(b => b.Rule("statement_separator")))
				.Transform(v => new BlockNode
				{
					Statements = v.SelectValues<StatementNode>().ToArray(),
					Position = CodePositionalInfo.From(v)
				});

			// ── L-value (left-hand side of assignment) ────────────────

			builder.CreateRule("lvalue")
				.Token("identifier")
				.ZeroOrMore(b => b.Choice(
					b => b
						.Literal(".")
						.Token("identifier")
						.Transform(v => new { Index = new LiteralNode
						{
							Literal = new LuaString(v.GetIntermediateValue<string>(1)),
							Position = CodePositionalInfo.From(v)
						} }),
					b => b
						.Literal("[")
						.Rule("expression")
						.Literal("]")
						.Transform(v => new { Index = v.GetValue<ExpressionNode>(1) })
				))
				.Transform(v =>
				{
					ExpressionNode target = new IdentifierNode { Name = v.GetIntermediateValue<string>(0) };

					foreach (var child in v.Children[1])
					{
						var d = child.Children[0].GetValue<object>();
						var idx = d.GetType().GetProperty("Index")!.GetValue(d);
						target = new IndexNode
						{
							Target = target,
							Index = (ExpressionNode)idx!,
							Position = CodePositionalInfo.From(v)
						};
					}

					return target;
				});

			// ── Simple statements ─────────────────────────────────────

			builder.CreateRule("break_statement")
				.Keyword("break")
				.Transform(v => new BreakNode { Position = CodePositionalInfo.From(v) });

			builder.CreateRule("continue_statement")
				.Keyword("continue")
				.Transform(v => new ContinueNode { Position = CodePositionalInfo.From(v) });

			builder.CreateRule("goto_statement")
				.Keyword("goto")
				.Token("identifier")
				.Transform(v => new GotoNode
				{
					LabelName = v.GetIntermediateValue<string>(1),
					Position = CodePositionalInfo.From(v)
				});

			builder.CreateRule("label_statement")
				.Literal("::")
				.Token("identifier")
				.Literal("::")
				.Transform(v => new LabelNode
				{
					Name = v.GetIntermediateValue<string>(1),
					Position = CodePositionalInfo.From(v)
				});

			builder.CreateRule("return_statement")
				.Keyword("return")
				.Optional(b => b
					.OneOrMoreSeparated(
						a => a.Rule("expression"),
						s => s.Literal(",")))
				.Transform(v =>
				{
					if (v.Children[1].Length > 0)
					{
						var values = v.Children[1].Children[0].SelectValues<ExpressionNode>();
						return new ReturnNode
						{
							Values = values.ToArray(),
							Position = CodePositionalInfo.From(v)
						};
					}
					return new ReturnNode { Position = CodePositionalInfo.From(v) };
				});

			builder.CreateRule("do_statement")
				.Keyword("do")
				.Rule("block")
				.Keyword("end")
				.Transform(v => new DoNode
				{
					Body = v.GetValue<BlockNode>(1),
					Position = CodePositionalInfo.From(v)
				});

			// ── If statement ──────────────────────────────────────────

			builder.CreateRule("if_statement")
				.Keyword("if")
				.Rule("expression")
				.Keyword("then")
				.Rule("block")
				.ZeroOrMore(b => b
					.Keyword("elseif")
					.Rule("expression")
					.Keyword("then")
					.Rule("block"))
				.Optional(b => b
					.Keyword("else")
					.Rule("block"))
				.Keyword("end")
				.Transform(v =>
				{
					var elseifClauses = v.Children[4].Select(child => new ElseIfClause
					{
						Condition = child.GetValue<ExpressionNode>(1),
						Body = child.GetValue<BlockNode>(3)
					}).ToArray();

					BlockNode? elseBlock = null;
					if (v.Children[5].Length > 0)
						elseBlock = v.Children[5].Children[0].GetValue<BlockNode>(1);

					return new IfNode
					{
						Condition = v.GetValue<ExpressionNode>(1),
						Body = v.GetValue<BlockNode>(3),
						ElseIfClauses = elseifClauses,
						ElseBlock = elseBlock,
						Position = CodePositionalInfo.From(v)
					};
				});

			// ── While / Repeat ────────────────────────────────────────

			builder.CreateRule("while_statement")
				.Keyword("while")
				.Rule("expression")
				.Keyword("do")
				.Rule("block")
				.Keyword("end")
				.Transform(v => new WhileNode
				{
					Condition = v.GetValue<ExpressionNode>(1),
					Body = v.GetValue<BlockNode>(3),
					Position = CodePositionalInfo.From(v)
				});

			builder.CreateRule("repeat_statement")
				.Keyword("repeat")
				.Rule("block")
				.Keyword("until")
				.Rule("expression")
				.Transform(v => new RepeatNode
				{
					Body = v.GetValue<BlockNode>(1),
					Condition = v.GetValue<ExpressionNode>(3),
					Position = CodePositionalInfo.From(v)
				});

			// ── For statements ────────────────────────────────────────

			builder.CreateRule("for_numeric_statement")
				.Keyword("for")
				.Token("identifier")
				.Literal("=")
				.Rule("expression")
				.Literal(",")
				.Rule("expression")
				.Optional(b => b
					.Literal(",")
					.Rule("expression"))
				.Keyword("do")
				.Rule("block")
				.Keyword("end")
				.Transform(v =>
				{
					ExpressionNode? step = null;
					if (v.Children[6].Length > 0)
						step = v.Children[6].Children[0].GetValue<ExpressionNode>(1);

					return new ForNumericNode
					{
						Variable = v.GetIntermediateValue<string>(1),
						Start = v.GetValue<ExpressionNode>(3),
						Limit = v.GetValue<ExpressionNode>(5),
						Step = step,
						Body = v.GetValue<BlockNode>(8),
						Position = CodePositionalInfo.From(v)
					};
				});

			builder.CreateRule("for_in_statement")
				.Keyword("for")
				.OneOrMoreSeparated(
					b => b.Token("identifier"),
					s => s.Literal(","))
				.Keyword("in")
				.OneOrMoreSeparated(
					b => b.Rule("expression"),
					s => s.Literal(","))
				.Keyword("do")
				.Rule("block")
				.Keyword("end")
				.Transform(v =>
				{
					var vars = v.Children[1].SelectValues<string>();
					var exprs = v.Children[3].SelectValues<ExpressionNode>();
					return new ForInNode
					{
						Variables = vars.ToArray(),
						Expressions = exprs.ToArray(),
						Body = v.GetValue<BlockNode>(5),
						Position = CodePositionalInfo.From(v)
					};
				});

			// ── Function declaration statements ───────────────────────

			builder.CreateRule("function_decl_statement")
				.Optional(b => b.Keyword("async"))
				.Keyword("function")
				.Token("identifier")
				.ZeroOrMore(b => b
					.Literal(".")
					.Token("identifier"))
				.Optional(b => b
					.Literal(":")
					.Token("identifier"))
				.Rule("func_params")
				.Rule("block")
				.Keyword("end")
				.Transform(v =>
				{
					var nameParts = new List<string> { v.GetIntermediateValue<string>(2) };
					foreach (var child in v.Children[3])
					{
						nameParts.Add(child.Children[0].GetIntermediateValue<string>(1));
					}

					ExpressionNode? targetObject = null;
					string? methodName = null;
					string funcName;

					if (v.Children[4].Length > 0)
					{
						// Method-style: function obj.method(...)
						targetObject = new IdentifierNode
						{
							Name = string.Join(".", nameParts),
							Position = CodePositionalInfo.From(v)
						};
						methodName = v.Children[4].Children[0].GetIntermediateValue<string>(1);
						funcName = methodName;
					}
					else
					{
						funcName = nameParts.Last();
						if (nameParts.Count > 1)
						{
							targetObject = new IdentifierNode {
								Name = string.Join(".", nameParts.Take(nameParts.Count - 1)),
								Position = CodePositionalInfo.From(v)
							};
						}
					}

					var parameters = v.GetValue<ParameterNode[]>(5);

					return new FunctionDeclStatementNode
					{
						Name = funcName,
						TargetObject = targetObject,
						MethodName = methodName,
						IsAsync = v.Children[0].Length > 0,
						Scope = null, // global
						Parameters = parameters.Where(p => !p.IsVarArg).ToArray(),
						HasVarArg = parameters.Any(p => p.IsVarArg),
						Body = v.GetValue<BlockNode>(6),
						Position = CodePositionalInfo.From(v)
					};
				});

			builder.CreateToken("varscope")
				.KeywordChoice(("local", VariableScope.Local), ("global", VariableScope.Global));

			builder.CreateRule("scoped_function_decl_statement")
				.Token("varscope")
				.Optional(b => b.Keyword("async"))
				.Keyword("function")
				.Token("identifier")
				.Rule("func_params")
				.Rule("block")
				.Keyword("end")
				.Transform(v =>
				{
					var parameters = v.GetValue<ParameterNode[]>(4);

					return new FunctionDeclStatementNode
					{
						Name = v.GetIntermediateValue<string>(3),
						IsAsync = v.Children[1].Length > 0,
						Scope = v.Children[0].GetValue<VariableScope>(),
						Parameters = parameters.Where(p => !p.IsVarArg).ToArray(),
						HasVarArg = parameters.Any(p => p.IsVarArg),
						Body = v.GetValue<BlockNode>(5),
						Position = CodePositionalInfo.From(v)
					};
				});

			// ── Local declaration ─────────────────────────────────────

			builder.CreateRule("scoped_declaration_statement")
				.Token("varscope")
				.OneOrMoreSeparated(
					b => b.Token("identifier"),
					s => s.Literal(","))
				.Optional(b => b
					.Literal("=")
					.OneOrMoreSeparated(
						a => a.Rule("expression"),
						s => s.Literal(",")))
				.Transform(v =>
				{
					var vars = v.Children[1].SelectValues<string>();
					ExpressionNode[] values = [];

					if (v.Children[2].Length > 0)
					{
						values = v.Children[2].Children[0].Children[1].SelectValues<ExpressionNode>().ToArray();
					}

					return new AssignmentNode
					{
						Scope = v.Children[0].GetValue<VariableScope>(),
						Targets = vars.Select(n => (ExpressionNode)new IdentifierNode { Name = n }).ToArray(),
						Values = values,
						Position = CodePositionalInfo.From(v)
					};
				});

			// ── AsyncLua extensions: lock / await ─────────────────────

			builder.CreateRule("lock_statement")
				.Keyword("lock")
				.Rule("expression")
				.Keyword("do")
				.Rule("block")
				.Keyword("end")
				.Transform(v => new LockNode
				{
					Target = v.GetValue<ExpressionNode>(1),
					Body = v.GetValue<BlockNode>(3),
					Position = CodePositionalInfo.From(v)
				});

			builder.CreateRule("await_statement")
				.Keyword("await")
				.OneOrMoreSeparated(
					b => b.Rule("expression"),
					s => s.Literal(","))
				.Transform(v => new AwaitStatementNode
				{
					AwaitExpression = new AwaitExpressionNode
					{
						Expressions = v.SelectArray<ExpressionNode>(1),
						Position = CodePositionalInfo.From(v[1])
					},
					Position = CodePositionalInfo.From(v)
				});

			// ── AsyncLua extensions: try-catch / throw ─────────────────

			builder.CreateRule("try_catch_statement")
				.Keyword("try")
				.Rule("block")
				.Keyword("catch")
				.Optional(b => b
					.Token("identifier")
					.Keyword("do")
				)
				.Rule("block")
				.Keyword("end")
				.Transform(v =>
				{
					var exceptionVarName = v.Children[3].Length > 0 ?
						v.Children[3].Children[0].Children[0].Text : null;

					return new TryCatchNode
					{
						TryBody = v.GetValue<BlockNode>(1),
						CatchBody = v.GetValue<BlockNode>(4),
						ExceptionMessageVariable = exceptionVarName,
						Position = CodePositionalInfo.From(v)
					};
				});

			builder.CreateRule("throw_statement")
				.Keyword("throw")
				.Rule("expression")
				.Transform(v =>
				{
					return new ThrowNode
					{
						Exception = v.GetValue<ExpressionNode>(1),
						Position = CodePositionalInfo.From(v)
					};
				});

			// ── Assignment or call statement ──────────────────────────

			builder.CreateRule("augassignment_statement")
				.Rule("lvalue")
				.LiteralChoice("+=", "-=", "*=", "/=", "//=", "%=", "^=", "..=", "<<=", ">>=", "&=", "|=", "~=")
				.Rule("expression")
				.Transform(v =>
				{
					return new AugassignmentNode
					{
						Left = v.GetValue<ExpressionNode>(0),
						Right = v.GetValue<ExpressionNode>(2),
						Operator = v.GetValue<string>(1) switch
						{
							"+=" => BinaryOperatorType.Add,
							"-=" => BinaryOperatorType.Substract,
							"*=" => BinaryOperatorType.Multiply,
							"/=" => BinaryOperatorType.Divide,
							"//=" => BinaryOperatorType.IntegerDivide,
							"%=" => BinaryOperatorType.Modulus,
							"^=" => BinaryOperatorType.Exponentiate,
							"..=" => BinaryOperatorType.Concatenate,
							"<<=" => BinaryOperatorType.BitShiftLeft,
							">>=" => BinaryOperatorType.BitShiftRight,
							"&=" => BinaryOperatorType.BitAnd,
							"|=" => BinaryOperatorType.BitOr,
							"~=" => BinaryOperatorType.BitXor,
							_ => throw new SemanticException(v, $"Unknown operator '{v.GetValue<string>(1)}'")
						},
						Position = CodePositionalInfo.From(v)
					};
				});

			builder.CreateRule("assignment_or_call_statement")
				.Choice(
					// Assignment: lvalue_list '=' explist
					b => b
						.OneOrMoreSeparated(
							a => a.Rule("lvalue"),
							s => s.Literal(","))
						.Literal("=")
						.OneOrMoreSeparated(
							a => a.Rule("expression"),
							s => s.Literal(","))
						.Transform(v =>
						{
							var targets = v.Children[0].SelectValues<ExpressionNode>();
							var values = v.Children[2].SelectValues<ExpressionNode>();
							return new AssignmentNode
							{
								Targets = targets.ToArray(),
								Values = values.ToArray(),
								Position = CodePositionalInfo.From(v)
							};
						}),
					// Call statement: just a function call
					b => b
						.Rule("postfix_expr")
						.ToSequence()
						.Transform(v =>
						{
							var expr = v.GetValue<ExpressionNode>(0);
							if (expr is FunctionCallNode call)
								return new CallStatementNode
								{
									Call = call,
									Position = CodePositionalInfo.From(v)
								};
							throw new SemanticException(v[0], "Expected a function call as a statement.");
						})
				);

			// ── Statement (top-level) ─────────────────────────────────

			builder.CreateRule("statement")
				.Choice(
					b => b.Rule("if_statement"),
					b => b.Rule("while_statement"),
					b => b.Rule("repeat_statement"),
					b => b.Rule("for_numeric_statement"),
					b => b.Rule("for_in_statement"),
					b => b.Rule("scoped_function_decl_statement"),
					b => b.Rule("scoped_declaration_statement"),
					b => b.Rule("function_decl_statement"),
					b => b.Rule("return_statement"),
					b => b.Rule("break_statement"),
					b => b.Rule("continue_statement"),
					b => b.Rule("goto_statement"),
					b => b.Rule("label_statement"),
					b => b.Rule("do_statement"),
					b => b.Rule("lock_statement"),
					b => b.Rule("await_statement"),
					b => b.Rule("try_catch_statement"),
					b => b.Rule("throw_statement"),
					b => b.Rule("augassignment_statement"),
					b => b.Rule("assignment_or_call_statement"));
		}
	}
}
