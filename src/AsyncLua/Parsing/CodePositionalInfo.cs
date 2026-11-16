using System.Text;
using RCParsing;

namespace AsyncLua.Parsing
{
	public struct CodePositionalInfo
	{
		public string SourceCode;
		public int TabSize;
		public readonly bool IsValid => SourceCode != null
			&& StartIndex >= 0
			&& StartIndex + Length <= SourceCode.Length;

		public int StartIndex, Length;
		public readonly int EndIndex => StartIndex + Length;

		public static CodePositionalInfo From(ParsedRuleResultBase node)
		{
			var source = node.Context.input;
			var tabSize = node.Context.parser.MainSettings.tabSize;

			return new CodePositionalInfo()
			{
				SourceCode = source,
				TabSize = tabSize,
				StartIndex = node.StartIndex,
				Length = node.Length
			};
		}

		/// <summary>
		/// Returns a human-readable representation of the source position.
		/// </summary>
		public override readonly string ToString()
		{
			if (!IsValid)
				return "(unknown)";

			var sb = new StringBuilder();

			string fullSubstring = SourceCode.Substring(StartIndex, Length);
			if (fullSubstring.Length > 30)
				fullSubstring = fullSubstring.Substring(0, 12) + "..." + fullSubstring.Substring(fullSubstring.Length - 12);
			fullSubstring = fullSubstring.Replace("\n", "\\n").Replace("\r", "\\r");
			sb.AppendLine($"Location (at '{fullSubstring}'):");

			if (Length is 0 or 1)
			{
				var info = PositionalFormatter.Decompose(SourceCode, StartIndex, TabSize);
				string annotation = $"line {info.LineNumber}, column {info.ColumnNumber}";
				sb.AppendLine(PositionalFormatter.Format(SourceCode, info, cursor: "^", cursorAnnotation: annotation));
			}
			else
			{
				var startInfo = PositionalFormatter.Decompose(SourceCode, StartIndex, TabSize);
				var endInfo = PositionalFormatter.Decompose(SourceCode, EndIndex - 1, TabSize);

				if (startInfo.LineNumber == endInfo.LineNumber)
				{
					// At the same line
					string annotation = $"line {startInfo.LineNumber}, column {startInfo.ColumnNumber}, length {Length}";
					sb.AppendLine(PositionalFormatter.Format(SourceCode, startInfo, endInfo, cursor: "^", cursorAnnotation: annotation));
				}
				else
				{
					// At different lines
					sb.AppendLine($"  (from line {startInfo.LineNumber}, column {startInfo.ColumnNumber} " +
						$"to line {endInfo.LineNumber}, column {endInfo.ColumnNumber}; length {Length} characters):");
					sb.AppendLine(PositionalFormatter.Format(SourceCode, startInfo, endInfo, addMultilineNumber: true, cursor: "^"));
				}
			}

			return sb.ToString();
		}
	}
}
