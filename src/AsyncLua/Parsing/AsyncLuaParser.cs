using System;
using System.Collections.Generic;
using System.Text;
using RCParsing;

namespace AsyncLua.Parsing
{
	public class AsyncLuaParserConfig
	{
		/// <summary>
		/// Determines if variables is local if not explicit keyword is specified.
		/// </summary>
		public bool LocalByDefault { get; set; } = false;
	}

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

		protected virtual void ModifyParserBuilder(ParserBuilder builder)
		{
		}

		private static void FillWithDefaultRules(ParserBuilder builder)
		{

			builder.CreateRule("block")
				.ZeroOrMoreSeparated(b => b.Rule("statement"), s => s.Rule("statement_separator"));



			builder.NameMainRule("block");
		}
	}
}
