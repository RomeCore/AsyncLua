using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using AsyncLua.Values;

namespace AsyncLua.Libraries
{
	/// <summary>
	/// Implements the time-related part of the standard Lua <c>os</c> library.
	/// </summary>
	/// <remarks>
	/// Only <c>os.time</c>, <c>os.date</c>, <c>os.clock</c> and <c>os.difftime</c> are provided. The
	/// process, filesystem and shell functions of the standard library are deliberately absent:
	/// AsyncLua runs inside a host application that owns those capabilities.
	/// </remarks>
	public sealed class OsTimeLibrary : LuaTableBaseLibrary
	{
		private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		/// <summary>
		/// Gets the namespace name <c>"os"</c>.
		/// </summary>
		public override string Namespace => "os";

		/// <summary>
		/// Populates the <c>os</c> table with the time functions.
		/// </summary>
		/// <param name="state">The Lua state the library is being imported into.</param>
		/// <param name="table">The table to populate.</param>
		protected override void PopulateTable(LuaState state, LuaTable table)
		{
			table.Set(new LuaString("time"), new LuaCallbackFunction(OsTime, "os.time"));
			table.Set(new LuaString("date"), new LuaCallbackFunction(OsDate, "os.date"));
			table.Set(new LuaString("clock"), new LuaCallbackFunction(OsClock, "os.clock"));
			table.Set(new LuaString("difftime"), new LuaCallbackFunction(OsDiffTime, "os.difftime"));
		}

		// -- os.time ---------------------------------------------------------

		/// <summary>
		/// Returns the current time, or the time encoded by a date table, as seconds since the epoch.
		/// </summary>
		private static LuaTuple OsTime(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is LuaNil)
				return new LuaTuple(new LuaNumber(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

			if (args[0] is not LuaTable fields)
				throw new LuaRuntimeException("os.time: argument #1 must be a table or nil");

			double year = RequiredField(fields, "year");
			double month = RequiredField(fields, "month");
			double day = RequiredField(fields, "day");
			double hour = OptionalField(fields, "hour", 12);
			double min = OptionalField(fields, "min", 0);
			double sec = OptionalField(fields, "sec", 0);

			DateTime local;
			try
			{
				// Out-of-range values roll over, mirroring mktime in standard Lua.
				local = new DateTime((int)year, 1, 1, 0, 0, 0, DateTimeKind.Local)
					.AddMonths((int)month - 1)
					.AddDays(day - 1)
					.AddHours(hour)
					.AddMinutes(min)
					.AddSeconds(sec);
			}
			catch (ArgumentException exception)
			{
				throw new LuaRuntimeException($"os.time: invalid date ({exception.Message})");
			}

			return new LuaTuple(new LuaNumber(ToUnixSeconds(local)));
		}

		// -- os.date ---------------------------------------------------------

		/// <summary>
		/// Formats a time value, or returns its broken-down form when the format is <c>"*t"</c>.
		/// </summary>
		private static LuaTuple OsDate(LuaCallingContext ctx, LuaValue[] args)
		{
			var format = "%c";
			if (args.Length > 0 && args[0] is not LuaNil)
			{
				if (args[0] is not LuaString formatString)
					throw new LuaRuntimeException("os.date: argument #1 must be a string");

				format = formatString.Value;
			}

			// A leading '!' asks for UTC instead of local time.
			var utc = format.Length > 0 && format[0] == '!';
			if (utc)
				format = format.Substring(1);

			double seconds;
			if (args.Length > 1 && args[1] is not LuaNil)
			{
				if (!args[1].TryToNumber(out seconds))
					throw new LuaRuntimeException("os.date: argument #2 must be a number");
			}
			else
			{
				seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			}

			DateTime moment;
			try
			{
				moment = UnixEpoch.AddSeconds(seconds);
			}
			catch (ArgumentOutOfRangeException)
			{
				throw new LuaRuntimeException("os.date: time value is out of range");
			}

			if (!utc)
				moment = moment.ToLocalTime();

			if (format == "*t")
				return new LuaTuple(CreateDateTable(moment, utc));

			return new LuaTuple(new LuaString(FormatTime(moment, format, utc)));
		}

		/// <summary>
		/// Builds the table <c>os.date("*t")</c> returns.
		/// </summary>
		/// <remarks>
		/// Weekdays are numbered from 1 (Sunday) and days of the year from 1, as in standard Lua.
		/// </remarks>
		private static LuaTable CreateDateTable(DateTime moment, bool utc)
		{
			var table = new LuaTable();
			table.Set(new LuaString("year"), new LuaNumber(moment.Year));
			table.Set(new LuaString("month"), new LuaNumber(moment.Month));
			table.Set(new LuaString("day"), new LuaNumber(moment.Day));
			table.Set(new LuaString("hour"), new LuaNumber(moment.Hour));
			table.Set(new LuaString("min"), new LuaNumber(moment.Minute));
			table.Set(new LuaString("sec"), new LuaNumber(moment.Second));
			table.Set(new LuaString("wday"), new LuaNumber((int)moment.DayOfWeek + 1));
			table.Set(new LuaString("yday"), new LuaNumber(moment.DayOfYear));
			table.Set(new LuaString("isdst"), LuaBoolean.FromBoolean(!utc && moment.IsDaylightSavingTime()));
			return table;
		}

		/// <summary>
		/// Applies the strftime-style conversion specifiers Lua supports.
		/// </summary>
		/// <param name="moment">The moment to format.</param>
		/// <param name="format">The format string, with the leading <c>!</c> already stripped.</param>
		/// <param name="utc">Whether <c>moment</c> is expressed in UTC, which decides what <c>%Z</c> reports.</param>
		private static string FormatTime(DateTime moment, string format, bool utc)
		{
			var builder = new StringBuilder(format.Length + 16);

			for (int i = 0; i < format.Length; i++)
			{
				if (format[i] != '%' || i + 1 >= format.Length)
				{
					builder.Append(format[i]);
					continue;
				}

				char specifier = format[++i];
				switch (specifier)
				{
					case '%':
						builder.Append('%');
						break;
					case 'a':
						builder.Append(moment.ToString("ddd", CultureInfo.InvariantCulture));
						break;
					case 'A':
						builder.Append(moment.ToString("dddd", CultureInfo.InvariantCulture));
						break;
					case 'b':
						builder.Append(moment.ToString("MMM", CultureInfo.InvariantCulture));
						break;
					case 'B':
						builder.Append(moment.ToString("MMMM", CultureInfo.InvariantCulture));
						break;
					case 'c':
						builder.Append(moment.ToString("ddd MMM ", CultureInfo.InvariantCulture));
						builder.Append(moment.Day.ToString("D2", CultureInfo.InvariantCulture));
						builder.Append(moment.ToString(" HH:mm:ss yyyy", CultureInfo.InvariantCulture));
						break;
					case 'd':
						builder.Append(moment.Day.ToString("D2", CultureInfo.InvariantCulture));
						break;
					case 'D':
						builder.Append(moment.ToString("MM/dd/yy", CultureInfo.InvariantCulture));
						break;
					case 'e':
						builder.Append(moment.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2));
						break;
					case 'F':
						builder.Append(moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
						break;
					case 'H':
						builder.Append(moment.Hour.ToString("D2", CultureInfo.InvariantCulture));
						break;
					case 'I':
						builder.Append(((moment.Hour + 11) % 12 + 1).ToString("D2", CultureInfo.InvariantCulture));
						break;
					case 'j':
						builder.Append(moment.DayOfYear.ToString("D3", CultureInfo.InvariantCulture));
						break;
					case 'm':
						builder.Append(moment.Month.ToString("D2", CultureInfo.InvariantCulture));
						break;
					case 'M':
						builder.Append(moment.Minute.ToString("D2", CultureInfo.InvariantCulture));
						break;
					case 'n':
						builder.Append('\n');
						break;
					case 'p':
						builder.Append(moment.Hour < 12 ? "AM" : "PM");
						break;
					case 'R':
						builder.Append(moment.ToString("HH:mm", CultureInfo.InvariantCulture));
						break;
					case 'S':
						builder.Append(moment.Second.ToString("D2", CultureInfo.InvariantCulture));
						break;
					case 't':
						builder.Append('\t');
						break;
					case 'T':
					case 'X':
						builder.Append(moment.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
						break;
					case 'U':
						builder.Append(WeekOfYear(moment, mondayFirst: false).ToString("D2", CultureInfo.InvariantCulture));
						break;
					case 'w':
						builder.Append(((int)moment.DayOfWeek).ToString(CultureInfo.InvariantCulture));
						break;
					case 'W':
						builder.Append(WeekOfYear(moment, mondayFirst: true).ToString("D2", CultureInfo.InvariantCulture));
						break;
					case 'x':
						builder.Append(moment.ToString("MM/dd/yy", CultureInfo.InvariantCulture));
						break;
					case 'y':
						builder.Append((moment.Year % 100).ToString("D2", CultureInfo.InvariantCulture));
						break;
					case 'Y':
						builder.Append(moment.Year.ToString("D4", CultureInfo.InvariantCulture));
						break;
					case 'Z':
						builder.Append(utc
							? "UTC"
							: moment.IsDaylightSavingTime()
								? TimeZoneInfo.Local.DaylightName
								: TimeZoneInfo.Local.StandardName);
						break;
					default:
						throw new LuaRuntimeException($"os.date: invalid conversion specifier '%{specifier}'");
				}
			}

			return builder.ToString();
		}

		/// <summary>
		/// Returns the zero-based week number of the year, counting from Sunday or Monday.
		/// </summary>
		private static int WeekOfYear(DateTime moment, bool mondayFirst)
		{
			int dayOfYear = moment.DayOfYear - 1;
			int weekDay = (int)moment.DayOfWeek;
			if (mondayFirst)
				weekDay = (weekDay + 6) % 7;

			return (dayOfYear + 7 - weekDay) / 7;
		}

		// -- os.clock --------------------------------------------------------

		/// <summary>
		/// Returns the CPU time consumed by the process, in seconds.
		/// </summary>
		private static LuaTuple OsClock(LuaCallingContext ctx, LuaValue[] args)
		{
			// Lua reports CPU time, not wall-clock time.
			using var process = Process.GetCurrentProcess();
			return new LuaTuple(new LuaNumber(process.TotalProcessorTime.TotalSeconds));
		}

		// -- os.difftime -----------------------------------------------------

		/// <summary>
		/// Returns the difference between two time values, in seconds.
		/// </summary>
		private static LuaTuple OsDiffTime(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length < 2 || !args[0].TryToNumber(out var t2) || !args[1].TryToNumber(out var t1))
				throw new LuaRuntimeException("os.difftime: two numbers expected");

			return new LuaTuple(new LuaNumber(t2 - t1));
		}

		// -- Helpers ---------------------------------------------------------

		/// <summary>
		/// Reads a required numeric field of a date table.
		/// </summary>
		private static double RequiredField(LuaTable fields, string name)
		{
			if (!fields.TryGetValue(new LuaString(name), out var value) || value is LuaNil)
				throw new LuaRuntimeException($"os.time: field '{name}' missing in date table");

			if (!value.TryToNumber(out var number))
				throw new LuaRuntimeException($"os.time: field '{name}' must be a number");

			return number;
		}

		/// <summary>
		/// Reads an optional numeric field of a date table.
		/// </summary>
		private static double OptionalField(LuaTable fields, string name, double fallback)
		{
			if (!fields.TryGetValue(new LuaString(name), out var value) || value is LuaNil)
				return fallback;

			if (!value.TryToNumber(out var number))
				throw new LuaRuntimeException($"os.time: field '{name}' must be a number");

			return number;
		}

		/// <summary>
		/// Converts a moment to whole seconds since the Unix epoch.
		/// </summary>
		private static double ToUnixSeconds(DateTime moment)
		{
			var utc = moment.Kind == DateTimeKind.Utc ? moment : moment.ToUniversalTime();
			return Math.Floor((utc - UnixEpoch).TotalSeconds);
		}
	}
}
