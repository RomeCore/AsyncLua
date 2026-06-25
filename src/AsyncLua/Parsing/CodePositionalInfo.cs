using System;
using System.Text;
using RCParsing;

namespace AsyncLua.Parsing
{
	public struct CodePositionalInfo
	{
		public string SourceCode;
		public int TabSize;
		public readonly bool IsValid => SourceCode != null;

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

			if (Length is 0 or 1)
			{
				PositionalFormatter.Decompose(SourceCode, StartIndex,
					out int lineStart, out int lineLength, out int lineNumber, out int columnNumber, out int visualColumnNumber,
					TabSize);

				string fullSubstring = SourceCode.Substring(StartIndex, Length);
				sb.AppendLine($"Location (at '{fullSubstring}'):");

				string lineAndColumn = $"line {lineNumber}, column {columnNumber}";
				string pointerLine;
				if (visualColumnNumber <= lineAndColumn.Length + 2)
					pointerLine = new string(' ', visualColumnNumber - 1) + '^' + ' ' + lineAndColumn;
				else
					pointerLine = new string(' ', visualColumnNumber - 2 - lineAndColumn.Length) + lineAndColumn + ' ' + '^';

				sb.AppendLine(SourceCode.Substring(lineStart, lineLength));
				sb.AppendLine(pointerLine);
			}
			else
			{
				PositionalFormatter.Decompose(SourceCode, StartIndex,
					out int lineStart1, out int lineLength1, out int lineNumber1, out int columnNumber1, out int visualColumnNumber1,
					TabSize);

				PositionalFormatter.Decompose(SourceCode, EndIndex - 1,
					out int lineStart2, out int lineLength2, out int lineNumber2, out int columnNumber2, out int visualColumnNumber2,
					TabSize);

				if (lineNumber1 == lineNumber2)
				{
					string fullSubstring = SourceCode.Substring(StartIndex, Length);
					if (fullSubstring.Length > 30)
						fullSubstring = fullSubstring.Substring(0, 12) + "..." + fullSubstring.Substring(fullSubstring.Length - 12);
					fullSubstring = fullSubstring.Replace("\n", "\\n").Replace("\r", "\\r");
					sb.AppendLine($"Location (at '{fullSubstring}'):");

					// At the same line
					string lineAndColumn = $"line {lineNumber1}, column {columnNumber1}, length {Length}";
					string pointerLine;
					if (visualColumnNumber1 <= lineAndColumn.Length + 2)
						pointerLine = new string(' ', visualColumnNumber1 - 1) + new string('^', Length) + ' ' + lineAndColumn;
					else
						pointerLine = new string(' ', visualColumnNumber1 - 2 - lineAndColumn.Length) + lineAndColumn + ' ' + new string('^', Length);

					sb.AppendLine(SourceCode.Substring(lineStart1, lineLength1));
					sb.AppendLine(pointerLine);
				}
				else
				{
					sb.AppendLine($"Location (from line {lineNumber1}, column {columnNumber1} to line {lineNumber2}, column {columnNumber2}; length {Length} characters):");

					int maxLineNumberLength = Math.Max(lineNumber1.ToString().Length, lineNumber2.ToString().Length);

					// At different lines
					sb.Append(lineNumber1.ToString().PadLeft(maxLineNumberLength) + ": ");
					sb.AppendLine(SourceCode.Substring(lineStart1, lineLength1));
					sb.Append(new string(' ', maxLineNumberLength + 2)); // + 2 from ": "
					sb.AppendLine(new string('^', lineLength1 - visualColumnNumber1 + 1).PadLeft(lineLength1));

					if (lineNumber1 + 1 != lineNumber2)
						sb.AppendLine("...");

					sb.Append(lineNumber2.ToString().PadLeft(maxLineNumberLength) + ": ");
					sb.AppendLine(SourceCode.Substring(lineStart2, lineLength2));
					sb.Append(new string(' ', maxLineNumberLength + 2));
					sb.AppendLine(new string('^', visualColumnNumber2));
				}
			}

			return sb.ToString();
		}
	}
}
