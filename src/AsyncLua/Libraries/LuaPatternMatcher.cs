using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AsyncLua.Values;

namespace AsyncLua.Libraries
{
	/*
	 * PORTED FROM ORIGINAL LUA 5.5
	 * SOURCE - https://github.com/lua/lua/blob/master/lstrlib.c
	 */

	/// <summary>
	/// Implements Lua 5.5 pattern matching for the string library.
	/// Provides <c>find</c>, <c>match</c>, <c>gmatch</c>, and <c>gsub</c> operations.
	/// </summary>
	public static class LuaPatternMatcher
	{
		private const char L_ESC = '%';
		private const int MAX_CAPTURES = 32;
		private const int MAX_CCALLS = 200;
		private const string SPECIALS = "^$*+?.([%-";

		// ── MatchState ───────────────────────────────────────────────

		private struct Capture
		{
			public int Init;    // start index in the source string
			public int Length;  // length or special value (CAP_*)
		}

		private sealed class MatchState
		{
			public string Source { get; }
			public int SourceStart { get; }
			public int SourceEnd { get; }
			public string Pattern;
			public int PatternEnd;
			public int MatchDepth { get; set; }
			public int Level { get; set; }
			public Capture[] Captures { get; } = new Capture[MAX_CAPTURES];
			public List<LuaValue>? ExtraArgs { get; set; }

			public MatchState(string source, int sourceStart, int sourceEnd, string pattern, int patternEnd)
			{
				Source = source;
				SourceStart = sourceStart;
				SourceEnd = sourceEnd;
				Pattern = pattern;
				PatternEnd = patternEnd;
				MatchDepth = MAX_CCALLS;
				Level = 0;
			}
		}

		private const int CAP_UNFINISHED = -1;
		private const int CAP_POSITION = -2;

		// ── Class matching ───────────────────────────────────────────

		private static bool MatchClass(int c, int cl)
		{
			bool res;
			switch (char.ToLowerInvariant((char)cl))
			{
				case 'a': res = char.IsLetter((char)c); break;
				case 'c': res = char.IsControl((char)c); break;
				case 'd': res = char.IsDigit((char)c); break;
				case 'g': res = char.IsLetterOrDigit((char)c) || char.IsPunctuation((char)c) || char.IsSymbol((char)c); break; // %g: any printable character except space
				case 'l': res = char.IsLower((char)c); break;
				case 'p': res = char.IsPunctuation((char)c); break;
				case 's': res = char.IsWhiteSpace((char)c); break;
				case 'u': res = char.IsUpper((char)c); break;
				case 'w': res = char.IsLetterOrDigit((char)c); break;
				case 'x': res = IsHexDigit((char)c); break;
				case 'z': res = (c == 0); break; // deprecated
				default: return (cl == c);
			}
			return (char.IsLower((char)cl) ? res : !res);
		}

		private static bool IsHexDigit(char c) =>
			(c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

		/// <summary>
		/// Matches a single character against a bracket class <c>[...]</c>.
		/// </summary>
		/// <param name="c">The character code to test.</param>
		/// <param name="pattern">The whole pattern.</param>
		/// <param name="start">The index of the opening <c>'['</c>.</param>
		/// <param name="end">The index of the closing <c>']'</c>.</param>
		/// <remarks>
		/// Both indices are absolute positions in <paramref name="pattern"/> - the class is not a
		/// substring, so the scan must begin at <paramref name="start"/> instead of at index 0.
		/// </remarks>
		private static bool MatchBracketClass(int c, string pattern, int start, int end)
		{
			bool sig = true;
			int idx = start + 1; // first char after '['

			if (idx < end && pattern[idx] == '^')
			{
				sig = false;
				idx++; // skip '^'
			}

			while (idx < end)
			{
				if (pattern[idx] == L_ESC)
				{
					idx++;
					if (idx < end && MatchClass(c, pattern[idx]))
						return sig;
				}
				else if (idx + 2 < end && pattern[idx + 1] == '-')
				{
					// range: a-z
					if (pattern[idx] <= c && c <= pattern[idx + 2])
						return sig;
					idx += 2;
				}
				else if (pattern[idx] == c)
				{
					return sig;
				}
				idx++;
			}

			return !sig;
		}

		// ── Class end ────────────────────────────────────────────────

		private static int ClassEnd(MatchState ms, int p)
		{
			switch (ms.Pattern[p++])
			{
				case L_ESC:
				{
					if (p >= ms.PatternEnd)
						throw new LuaRuntimeException("malformed pattern (ends with '%')");
					return p + 1;
				}
				case '[':
				{
					if (p < ms.PatternEnd && ms.Pattern[p] == '^') p++;

					while (true)
					{
						if (p >= ms.PatternEnd)
							throw new LuaRuntimeException("malformed pattern (missing ']')");

						if (ms.Pattern[p] == L_ESC && p + 1 < ms.PatternEnd)
							p++; // skip the escaped character

						p++;

						// A ']' right after '[' or '[^' is a literal member, not the terminator,
						// so the terminator is only the ']' that follows a consumed member.
						if (p < ms.PatternEnd && ms.Pattern[p] == ']')
							return p + 1; // points after ']'
					}
				}
				default:
					return p;
			}
		}

		// ── Check capture ────────────────────────────────────────────

		private static int CheckCapture(MatchState ms, int l)
		{
			l -= '1';
			if (l < 0 || l >= ms.Level || ms.Captures[l].Length == CAP_UNFINISHED)
				throw new LuaRuntimeException($"invalid capture index %{l + 1}");
			return l;
		}

		private static int CaptureToClose(MatchState ms)
		{
			for (int level = ms.Level - 1; level >= 0; level--)
				if (ms.Captures[level].Length == CAP_UNFINISHED)
					return level;
			throw new LuaRuntimeException("invalid pattern capture");
		}

		// ── Single match ─────────────────────────────────────────────

		private static bool SingleMatch(MatchState ms, int s, int p, int ep)
		{
			if (s >= ms.SourceEnd)
				return false;

			int c = ms.Source[s];
			switch (ms.Pattern[p])
			{
				case '.': return true; // any char
				case L_ESC: return MatchClass(c, (int)ms.Pattern[p + 1]);
				case '[': return MatchBracketClass(c, ms.Pattern, p, ep - 1);
				default: return (ms.Pattern[p] == c);
			}
		}

		// ── Balance ──────────────────────────────────────────────────

		private static int? MatchBalance(MatchState ms, int s, int p)
		{
			if (p >= ms.PatternEnd - 1)
				throw new LuaRuntimeException("malformed pattern (missing arguments to '%b')");

			if (ms.Source[s] != ms.Pattern[p])
				return null;

			int b = ms.Pattern[p];
			int e = ms.Pattern[p + 1];
			int cont = 1;

			s++;
			while (s < ms.SourceEnd)
			{
				if (ms.Source[s] == e)
				{
					cont--;
					if (cont == 0)
						return s + 1;
				}
				else if (ms.Source[s] == b)
				{
					cont++;
				}
				s++;
			}
			return null;
		}

		// ── Max expand (greedy) ──────────────────────────────────────

		private static int? MaxExpand(MatchState ms, int s, int p, int ep)
		{
			int i = 0;
			while (SingleMatch(ms, s + i, p, ep))
				i++;

			while (i >= 0)
			{
				int? res = Match(ms, s + i, ep + 1);
				if (res.HasValue)
					return res;
				i--;
			}
			return null;
		}

		// ── Min expand (lazy) ────────────────────────────────────────

		private static int? MinExpand(MatchState ms, int s, int p, int ep)
		{
			for (;;)
			{
				int? res = Match(ms, s, ep + 1);
				if (res.HasValue)
					return res;
				else if (SingleMatch(ms, s, p, ep))
					s++;
				else
					return null;
			}
		}

		// ── Captures ─────────────────────────────────────────────────

		private static int? StartCapture(MatchState ms, int s, int p, int what)
		{
			int level = ms.Level;
			if (level >= MAX_CAPTURES)
				throw new LuaRuntimeException("too many captures");

			ms.Captures[level] = new Capture { Init = s, Length = what };
			ms.Level = level + 1;

			int? res = Match(ms, s, p);
			if (res == null)
				ms.Level--; // undo capture
			return res;
		}

		private static int? EndCapture(MatchState ms, int s, int p)
		{
			int l = CaptureToClose(ms);
			var cap = ms.Captures[l];
			ms.Captures[l] = new Capture { Init = cap.Init, Length = s - cap.Init };

			int? res = Match(ms, s, p);
			if (res == null)
				ms.Captures[l] = new Capture { Init = cap.Init, Length = CAP_UNFINISHED };
			return res;
		}

		private static int? MatchCapture(MatchState ms, int s, int l)
		{
			int ci = CheckCapture(ms, l);
			int len = ms.Captures[ci].Length;
			int init = ms.Captures[ci].Init;

			if (ms.SourceEnd - s >= len && ms.Source.Substring(init, len) == ms.Source.Substring(s, len))
				return s + len;
			return null;
		}

		// ── Main match function ──────────────────────────────────────

		private static int? Match(MatchState ms, int s, int p)
		{
			if (ms.MatchDepth-- == 0)
				throw new LuaRuntimeException("pattern too complex");

		init:
			if (p < ms.PatternEnd)
			{
				switch (ms.Pattern[p])
				{
					case '(': // start capture
					{
						if (p + 1 < ms.PatternEnd && ms.Pattern[p + 1] == ')')
							s = StartCapture(ms, s, p + 2, CAP_POSITION) ?? -1;
						else
							s = StartCapture(ms, s, p + 1, CAP_UNFINISHED) ?? -1;
						break;
					}
					case ')': // end capture
					{
						s = EndCapture(ms, s, p + 1) ?? -1;
						break;
					}
					case '$':
					{
						if (p + 1 >= ms.PatternEnd) // $ is last char
						{
							s = (s >= ms.SourceEnd) ? s : -1;
							break;
						}
						goto dflt;
					}
					case L_ESC:
					{
						if (p + 1 < ms.PatternEnd)
						{
							switch (ms.Pattern[p + 1])
							{
								case 'b': // balanced string
								{
									int? balRes = MatchBalance(ms, s, p + 2);
									if (balRes.HasValue)
									{
										s = balRes.Value;
										p += 4;
										goto init;
									}
									s = -1; // fail
									break;
								}
								case 'f': // frontier
								{
									p += 2;
									if (p >= ms.PatternEnd || ms.Pattern[p] != '[')
										throw new LuaRuntimeException("missing '[' after '%f' in pattern");
									int ep = ClassEnd(ms, p);
									char previous = (s == ms.SourceStart) ? '\0' : ms.Source[s - 1];
									char current = (s >= ms.SourceEnd) ? '\0' : ms.Source[s];
									if (!MatchBracketClass(previous, ms.Pattern, p, ep - 1) &&
										MatchBracketClass(current, ms.Pattern, p, ep - 1))
									{
										p = ep;
										goto init;
									}
									s = -1; // fail
									break;
								}
								case '0': case '1': case '2': case '3':
								case '4': case '5': case '6': case '7':
								case '8': case '9': // capture back-reference (%0-%9)
								{
									int? capRes = MatchCapture(ms, s, (int)ms.Pattern[p + 1]);
									if (capRes.HasValue)
									{
										s = capRes.Value;
										p += 2;
										goto init;
									}
									s = -1; // fail
									break;
								}
								default:
									goto dflt;
							}
						}
						else
						{
							goto dflt;
						}
						break;
					}
					default:
					dflt:
					{
						int ep = ClassEnd(ms, p);
						if (!SingleMatch(ms, s, p, ep))
						{
							if (ep < ms.PatternEnd && (ms.Pattern[ep] == '*' || ms.Pattern[ep] == '?' || ms.Pattern[ep] == '-'))
							{
								p = ep + 1;
								goto init;
							}
							else
							{
								s = -1; // fail
							}
						}
						else
						{
							if (ep < ms.PatternEnd)
							{
								switch (ms.Pattern[ep])
								{
									case '?':
									{
										int? res = Match(ms, s + 1, ep + 1);
										if (res.HasValue)
											s = res.Value;
										else
										{
											p = ep + 1;
											goto init;
										}
										break;
									}
									case '+':
										s++;
										goto case '*';
									case '*':
									{
										int? maxRes = MaxExpand(ms, s, p, ep);
										s = maxRes ?? -1;
										break;
									}
									case '-':
									{
										int? minRes = MinExpand(ms, s, p, ep);
										s = minRes ?? -1;
										break;
									}
									default: // no suffix
										s++;
										p = ep;
										goto init;
								}
							}
							else
							{
								s++;
								p = ep;
								goto init;
							}
						}
						break;
					}
				}
			}

			ms.MatchDepth++;
			return s >= 0 ? s : null;
		}

		// ── Helper: get one capture ──────────────────────────────────

		private static int GetOneCapture(MatchState ms, int i, int s, int e, out int capInit)
		{
			if (i >= ms.Level)
			{
				if (i != 0)
					throw new LuaRuntimeException($"invalid capture index %{i + 1}");
				capInit = s;
				return e - s;
			}
			else
			{
				int capl = ms.Captures[i].Length;
				capInit = ms.Captures[i].Init;
				if (capl == CAP_UNFINISHED)
					throw new LuaRuntimeException("unfinished capture");
				// If it's a position capture, we need to store it separately
				return capl;
			}
		}

		// ── Helper: push captures ────────────────────────────────────

		private static List<LuaValue> PushCaptures(MatchState ms, int s, int e)
		{
			var result = new List<LuaValue>();
			int nlevels = (ms.Level == 0 && s >= 0) ? 1 : ms.Level;
			for (int i = 0; i < nlevels; i++)
			{
				int init = GetOneCapture(ms, i, s, e, out int capInit);
				if (init == CAP_POSITION)
				{
					result.Add(new LuaNumber(capInit - ms.SourceStart + 1));
				}
				else
				{
					result.Add(new LuaString(ms.Source.Substring(capInit, init)));
				}
			}
			return result;
		}

		// ── Check for special chars ──────────────────────────────────

		private static bool NoSpecials(string p, int len)
		{
			for (int i = 0; i < len; i++)
			{
				if (SPECIALS.IndexOf(p[i]) >= 0)
					return false;
			}
			return true;
		}

		// ── Simple search (no pattern) ───────────────────────────────

		private static int? SimpleFind(string source, int sourceStart, int sourceEnd, string pattern, int patternLen)
		{
			if (patternLen == 0)
				return sourceStart;
			if (patternLen > sourceEnd - sourceStart)
				return null;

			int maxPos = sourceEnd - patternLen;
			for (int i = sourceStart; i <= maxPos; i++)
			{
				if (source[i] == pattern[0])
				{
					bool found = true;
					for (int j = 1; j < patternLen; j++)
					{
						if (source[i + j] != pattern[j])
						{
							found = false;
							break;
						}
					}
					if (found)
						return i;
				}
			}
			return null;
		}

		// ── Find auxiliary ───────────────────────────────────────────

		/// <summary>
		/// Implements <c>string.find</c>: locates the first match and reports where it lies.
		/// </summary>
		/// <param name="source">The string to search.</param>
		/// <param name="pattern">A Lua pattern, or a literal substring when <paramref name="plain"/> is set.</param>
		/// <param name="init">The 1-based index to start searching from; a negative index counts from the end.</param>
		/// <param name="plain">Whether to treat the pattern as a literal substring.</param>
		/// <returns>
		/// The 1-based start and the end-exclusive end, then one value per capture; <c>nil</c> when nothing matched.
		/// </returns>
		public static LuaTuple Find(string source, string pattern, int init, bool plain)
		{
			int startPos = NormaliseIndex(init, source.Length) - 1; // convert to 0-based
			if (!TrySearch(source, pattern, startPos, plain, out int s, out int e, out var ms))
				return new LuaTuple(LuaNil.Instance);

			var result = new List<LuaValue>
			{
				new LuaNumber(s + 1),
				new LuaNumber(e)
			};
			// Only add captures if pattern has explicit captures
			if (ms is not null && ms.Level > 0)
				result.AddRange(PushCaptures(ms, s, e));
			return new LuaTuple(result.ToArray());
		}

		// ── Search core ─────────────────────────────────────────────

		/// <summary>
		/// Searches from a 0-based position and reports the bounds of the first match.
		/// </summary>
		/// <param name="source">The string to search.</param>
		/// <param name="pattern">A Lua pattern, or a literal substring when <paramref name="plain"/> is set.</param>
		/// <param name="startPos">The 0-based index to start searching from.</param>
		/// <param name="plain">Whether to treat the pattern as a literal substring.</param>
		/// <param name="start">The 0-based match start.</param>
		/// <param name="end">The 0-based, end-exclusive match end.</param>
		/// <param name="state">
		/// The match state carrying the captures, or <c>null</c> when the pattern had no specials and the
		/// search short-circuited to a literal one.
		/// </param>
		/// <returns><c>true</c> when a match was found.</returns>
		private static bool TrySearch(string source, string pattern, int startPos, bool plain, out int start, out int end, out MatchState? state)
		{
			start = 0;
			end = 0;
			state = null;

			int sourceEnd = source.Length;
			if (startPos > sourceEnd)
				return false;

			int patternLen = pattern.Length;
			if (plain || NoSpecials(pattern, patternLen))
			{
				int? found = SimpleFind(source, startPos, sourceEnd, pattern, patternLen);
				if (!found.HasValue)
					return false;

				start = found.Value;
				end = found.Value + patternLen;
				return true;
			}

			var ms = new MatchState(source, 0, sourceEnd, pattern, patternLen);
			bool anchor = pattern[0] == '^';
			if (anchor)
			{
				ms.Pattern = pattern.Substring(1);
				ms.PatternEnd--;
			}

			int s = startPos;
			do
			{
				ms.MatchDepth = MAX_CCALLS;
				ms.Level = 0;
				int? res = Match(ms, s, 0);
				if (res.HasValue)
				{
					start = s;
					end = res.Value;
					state = ms;
					return true;
				}
			} while (s++ < sourceEnd && !anchor);

			return false;
		}

		// ── Match ────────────────────────────────────────────────────

		/// <summary>
		/// Implements <c>string.match</c>: returns the captures of the first match, or the whole match when the pattern has none.
		/// </summary>
		/// <param name="source">The string to search.</param>
		/// <param name="pattern">The Lua pattern to match.</param>
		/// <param name="init">The 1-based index to start searching from; a negative index counts from the end.</param>
		/// <returns>The captures, or the matched text, or <c>nil</c> when nothing matched.</returns>
		public static LuaTuple Match(string source, string pattern, int init)
		{
			int startPos = NormaliseIndex(init, source.Length) - 1; // 0-based
			if (!TrySearch(source, pattern, startPos, false, out int s, out int e, out var ms))
				return new LuaTuple(LuaNil.Instance);

			// TrySearch short-circuits a pattern without specials to a literal search, which keeps no capture state.
			if (ms is null)
				return new LuaTuple(new LuaString(source.Substring(s, e - s)));

			var captures = PushCaptures(ms, s, e);
			if (captures.Count == 0)
				return new LuaTuple(new LuaString(source.Substring(s, e - s)));
			return new LuaTuple(captures.ToArray());
		}

		// ── Host-facing match enumeration ───────────────────────

		/// <summary>
		/// Enumerates the matches of <paramref name="pattern"/> in <paramref name="source"/> as 0-based spans.
		/// </summary>
		/// <param name="source">The string to search.</param>
		/// <param name="pattern">A Lua pattern, or a literal substring when <paramref name="plain"/> is set.</param>
		/// <param name="init">The 1-based index to start searching from.</param>
		/// <param name="plain">Whether to treat the pattern as a literal substring.</param>
		/// <returns>The matches in order, each as a start index and an end-exclusive index.</returns>
		/// <remarks>
		/// A match that consumes nothing is reported once at its position and the search then moves past it,
		/// so the enumeration always advances - a host splitting on the result cannot spin on an empty match.
		/// </remarks>
		public static IEnumerable<(int Start, int End)> EnumerateMatches(string source, string pattern, int init = 1, bool plain = false)
		{
			int position = NormaliseIndex(init, source.Length) - 1; // 0-based
			while (TrySearch(source, pattern, position, plain, out int start, out int end, out _))
			{
				yield return (start, end);
				position = end > start ? end : start + 1;
			}
		}

		// ── GMatch (returns iterator function) ───────────────────────

		/// <summary>
		/// Implements <c>string.gmatch</c>: builds an iterator over the matches of a pattern.
		/// </summary>
		/// <param name="source">The string to scan.</param>
		/// <param name="pattern">The Lua pattern to match.</param>
		/// <param name="init">The 1-based index to start scanning from.</param>
		/// <returns>A function that yields the captures of each match, or its text, and nothing once the scan is spent.</returns>
		/// <remarks>
		/// A match that consumes nothing is skipped when it starts where the previous one ended, which is the rule
		/// that keeps an empty-matching pattern from yielding the same position forever.
		/// </remarks>
		public static LuaFunction GMatch(string source, string pattern, int init)
		{
			int sourceEnd = source.Length;
			int startPos = NormaliseIndex(init, source.Length) - 1; // 0-based
			if (startPos > sourceEnd)
				startPos = sourceEnd + 1; // avoid overflows

			int patternLen = pattern.Length;
			var ms = new MatchState(source, 0, sourceEnd, pattern, patternLen) { Pattern = pattern };

			// State for the iterator
			int currentPos = startPos;
			int lastMatch = -1; // end of last match

			return new LuaCallbackFunction((ctx, args) =>
			{
				if (currentPos > sourceEnd)
					return new LuaTuple();

				for (int src = currentPos; src <= sourceEnd; src++)
				{
					ms.MatchDepth = MAX_CCALLS;
					ms.Level = 0;
					int? e = Match(ms, src, 0);
					if (e.HasValue && e.Value != lastMatch)
					{
						currentPos = e.Value;
						lastMatch = e.Value;
						var captures = PushCaptures(ms, src, e.Value);
						if (captures.Count == 0)
							return new LuaTuple(new LuaString(source.Substring(src, e.Value - src)));
						return new LuaTuple(captures.ToArray());
					}
				}
				currentPos = sourceEnd + 1; // no more matches
				return new LuaTuple();
			}, "gmatch iterator");
		}

		// ── GSub ─────────────────────────────────────────────────────

		/// <summary>
		/// Implements <c>string.gsub</c>: replaces up to <paramref name="maxReplacements"/> matches.
		/// </summary>
		/// <param name="source">The string to rewrite.</param>
		/// <param name="pattern">The Lua pattern to match.</param>
		/// <param name="repl">A replacement string, a table keyed by the first capture, or a function called with the captures.</param>
		/// <param name="maxReplacements">The maximum number of replacements to make.</param>
		/// <param name="context">The calling context, needed only when <paramref name="repl"/> is a function.</param>
		/// <returns>The rewritten string and the number of replacements made.</returns>
		public static LuaTuple GSub(string source, string pattern, LuaValue repl, int maxReplacements, LuaCallingContext? context = null)
		{
			int sourceLen = source.Length;
			int patternLen = pattern.Length;
			bool anchor = patternLen > 0 && pattern[0] == '^';
			var ms = new MatchState(source, 0, sourceLen, pattern, patternLen);
			if (anchor)
			{
				ms.Pattern = pattern.Substring(1);
				ms.PatternEnd--;
			}

			var result = new StringBuilder();
			int n = 0;
			bool anyChange = false;
			int src = 0;
			int lastMatch = -1;

			while (n < maxReplacements)
			{
				ms.MatchDepth = MAX_CCALLS;
				ms.Level = 0;
				int? e = Match(ms, src, 0);
				if (e.HasValue && e.Value != lastMatch)
				{
					n++;
					int beforeLen = result.Length;
					bool changed = false;
					AddValue(ms, result, src, e.Value, repl, ref changed, context);
					if (!changed)
					{
						// replacement returned nil/false, keep original text
						result.Append(source.Substring(src, e.Value - src));
					}
					else
					{
						anyChange = true;
					}
					src = e.Value;
					lastMatch = e.Value;
				}
				else if (src < ms.SourceEnd)
				{
					result.Append(source[src]);
					src++;
				}
				else
				{
					break;
				}
				if (anchor)
					break;
			}

			if (!anyChange)
			{
				return new LuaTuple(new LuaString(source), new LuaNumber(n));
			}

			if (src < ms.SourceEnd)
				result.Append(source.Substring(src));

			return new LuaTuple(new LuaString(result.ToString()), new LuaNumber(n));
		}

		private static void AddValue(MatchState ms, StringBuilder buffer, int s, int e, LuaValue repl, ref bool changed, LuaCallingContext? context = null)
		{
			switch (repl)
			{
				case LuaFunction func:
				{
					var captures = PushCaptures(ms, s, e);
					var result = func.Invoke(context!, captures.ToArray());
					if (result.First is LuaNil || (result.First is LuaBoolean b && !b.Value))
					{
						// keep original
						return;
					}
					buffer.Append(result.First?.ToString() ?? "");
					changed = true;
					break;
				}
				case LuaTable table:
				{
					var captures = PushCaptures(ms, s, e);
					LuaValue firstCapture = captures.Count > 0 ? captures[0] : LuaNil.Instance;
					LuaValue val = table.Get(firstCapture);
					if (val is LuaNil || (val is LuaBoolean bv && !bv.Value))
					{
						return; // keep original
					}
					buffer.Append(val.ToString());
					changed = true;
					break;
				}
				case LuaString str:
				{
					AddS(ms, buffer, s, e, str.Value);
					changed = true;
					break;
				}
				case LuaNumber num:
				{
					AddS(ms, buffer, s, e, num.Value.ToString(CultureInfo.InvariantCulture));
					changed = true;
					break;
				}
				default:
				{
					AddS(ms, buffer, s, e, repl?.ToString() ?? "");
					changed = true;
					break;
				}
			}
		}

		private static void AddS(MatchState ms, StringBuilder buffer, int s, int e, string news)
		{
			int l = news.Length;
			int p = 0;
			while (p < l)
			{
				int escIdx = news.IndexOf(L_ESC, p);
				if (escIdx < 0)
				{
					buffer.Append(news.Substring(p));
					break;
				}
				buffer.Append(news.Substring(p, escIdx - p));
				p = escIdx + 1;
				if (p >= l)
					break;

				if (news[p] == L_ESC) // '%%'
				{
					buffer.Append(L_ESC);
				}
				else if (news[p] == '0') // '%0'
				{
					buffer.Append(ms.Source.Substring(s, e - s));
				}
				else if (news[p] >= '1' && news[p] <= '9') // '%n'
				{
					int ci = news[p] - '1';
					int init = GetOneCapture(ms, ci, s, e, out int capInit);
					if (init == CAP_POSITION)
					{
						buffer.Append(capInit - ms.SourceStart + 1);
					}
					else
					{
						buffer.Append(ms.Source.Substring(capInit, init));
					}
				}
				else
				{
					throw new LuaRuntimeException($"invalid use of '%c' in replacement string");
				}
				p++;
			}
		}

		private static int NormaliseIndex(int idx, int length)
		{
			if (idx < 0)
				idx = length + idx + 1;
			return idx;
		}
	}
}
