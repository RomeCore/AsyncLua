using RCParsing.TokenPatterns;

namespace AsyncLua.Parsing
{
    /// <summary>
    /// Implements Lua-compatible string escaping logic for use with RCParsing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supports the following escape sequences:
    /// <list type="bullet">
    ///   <item><description><c>\a</c> — bell (alert).</description></item>
    ///   <item><description><c>\b</c> — backspace.</description></item>
    ///   <item><description><c>\f</c> — form feed.</description></item>
    ///   <item><description><c>\n</c> — newline.</description></item>
    ///   <item><description><c>\r</c> — carriage return.</description></item>
    ///   <item><description><c>\t</c> — horizontal tab.</description></item>
    ///   <item><description><c>\v</c> — vertical tab.</description></item>
    ///   <item><description><c>\\</c> — backslash.</description></item>
    ///   <item><description><c>\"</c> — double quote.</description></item>
    ///   <item><description><c>\'</c> — single quote.</description></item>
    ///   <item><description><c>\xHH</c> — hex escape (two hex digits, Lua 5.2+).</description></item>
    ///   <item><description><c>\u{HHHH}</c> — Unicode escape (Lua 5.3+).</description></item>
    ///   <item><description><c>\ddd</c> — decimal escape (up to 3 digits, value modulo 256).</description></item>
    ///   <item><description><c>\&lt;newline&gt;</c> — escaped newline (ignored).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Unrecognised escape sequences are treated as the literal character following the backslash
    /// (standard Lua behaviour).
    /// </para>
    /// </remarks>

	public class AsyncLuaStringEscapingStrategy : EscapingStrategy
	{
		public bool IsSingleQuote { get; set; } = false;

		public override bool TryEscape(string input, int position, int maxPosition, out int consumedLength, out string replacement)
		{
			if (position + 1 >= maxPosition) // Escape sequence requires at least two characters
			{
				consumedLength = 0;
				replacement = string.Empty;
				return false;
			}

			if (input[position] != '\\')
			{
				consumedLength = 0;
				replacement = string.Empty;
				return false;
			}

			char next = input[position + 1];

			switch (next)
			{
				case 'n':
					consumedLength = 2;
					replacement = "\n";
					return true;
				case 't':
					consumedLength = 2;
					replacement = "\t";
					return true;
				case 'r':
					consumedLength = 2;
					replacement = "\r";
					return true;
				case '\\':
					consumedLength = 2;
					replacement = "\\";
					return true;
				case '\"':
					consumedLength = 2;
					replacement = "\"";
					return true;
				case '\'':
					consumedLength = 2;
					replacement = "'";
					return true;
				case 'f':
					consumedLength = 2;
					replacement = "\f";
					return true;
				case 'b':
					consumedLength = 2;
					replacement = "\b";
					return true;
				case 'a':
					consumedLength = 2;
					replacement = "\a";
					return true;
				case 'v':
					consumedLength = 2;
					replacement = "\v";
					return true;

				// \xHH — hex escape (two hexadecimal digits)
				case 'x':
					return TryEscapeHex(input, position, maxPosition, out consumedLength, out replacement);

				// \u{HHHH} — Unicode escape (Lua 5.3+)
				case 'u':
					return TryEscapeUnicode(input, position, maxPosition, out consumedLength, out replacement);

				// \ddd — decimal escape (one to three digits, value 0–255, larger values reduced modulo 256)
				case '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9':
					return TryEscapeDecimal(input, position, maxPosition, out consumedLength, out replacement);

				// Line break inside a string: \<newline> is ignored (escaped newline).
				// Handles \n, \r, and \r\n.
				case '\n':
					consumedLength = 2;
					replacement = string.Empty;
					return true;
				case '\r':
					// Check for \r\n (Windows line ending)
					if (position + 2 < maxPosition && input[position + 2] == '\n')
						consumedLength = 3;
					else
						consumedLength = 2;
					replacement = string.Empty;
					return true;

				// Unrecognised escape — keep the backslash and the character as-is (Lua behaviour)
				default:
					consumedLength = 2;
					replacement = new string(new[] { next });
					return true;
			}
		}

		private static bool TryEscapeHex(string input, int position, int maxPosition, out int consumedLength, out string replacement)
		{
			// Need at least \x + 2 hex digits
			if (position + 3 >= maxPosition)
			{
				consumedLength = 0;
				replacement = string.Empty;
				return false;
			}

			char d1 = input[position + 2];
			char d2 = input[position + 3];
			int hi = HexValue(d1);
			int lo = HexValue(d2);

			if (hi < 0 || lo < 0)
			{
				consumedLength = 0;
				replacement = string.Empty;
				return false;
			}

			consumedLength = 4;
			replacement = new string((char)((hi << 4) | lo), 1);
			return true;
		}

		private static bool TryEscapeUnicode(string input, int position, int maxPosition, out int consumedLength, out string replacement)
		{
			// Need at least \u{ + one hex digit + }
			if (position + 4 >= maxPosition || input[position + 2] != '{')
			{
				consumedLength = 0;
				replacement = string.Empty;
				return false;
			}

			int endBrace = position + 3;
			while (endBrace < maxPosition && input[endBrace] != '}')
				endBrace++;

			if (endBrace >= maxPosition)
			{
				consumedLength = 0;
				replacement = string.Empty;
				return false;
			}

			// Parse hex digits between '{' and '}'
			int codePoint = 0;
			for (int i = position + 3; i < endBrace; i++)
			{
				int val = HexValue(input[i]);
				if (val < 0)
				{
					consumedLength = 0;
					replacement = string.Empty;
					return false;
				}
				codePoint = (codePoint << 4) | val;
			}

			consumedLength = endBrace - position + 1; // include the closing '}'
			replacement = char.ConvertFromUtf32(codePoint);
			return true;
		}

		private static bool TryEscapeDecimal(string input, int position, int maxPosition, out int consumedLength, out string replacement)
		{
			// Read up to 3 decimal digits starting at position + 1
			int value = 0;
			int digitsRead = 0;

			for (int i = position + 1; i < maxPosition && digitsRead < 3; i++)
			{
				char c = input[i];
				if (c < '0' || c > '9')
					break;

				value = value * 10 + (c - '0');
				digitsRead++;
			}

			if (digitsRead == 0)
			{
				consumedLength = 0;
				replacement = string.Empty;
				return false;
			}

			// In Lua, if the value is larger than 255, it is reduced modulo 256
			byte byteValue = (byte)(value & 0xFF);

			consumedLength = 1 + digitsRead; // backslash + digits
			replacement = new string((char)byteValue, 1);
			return true;
		}

		private static int HexValue(char c)
		{
			if (c >= '0' && c <= '9') return c - '0';
			if (c >= 'a' && c <= 'f') return c - 'a' + 10;
			if (c >= 'A' && c <= 'F') return c - 'A' + 10;
			return -1;
		}

		public override bool TryStop(string input, int position, int maxPosition, out int consumedLength)
		{
			if (position >= maxPosition)
			{
				consumedLength = 0;
				return false;
			}

			switch (input[position])
			{
				case '\\':
					consumedLength = 1;
					return true;
				case '\"' when (!IsSingleQuote):
					consumedLength = 1;
					return true;
				case '\'' when (IsSingleQuote):
					consumedLength = 1;
					return true;
			}

			consumedLength = 0;
			return false;
		}
	}
}
