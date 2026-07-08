using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AsyncLua.Parsing;
using AsyncLua.Values;

namespace AsyncLua.Interpreting
{
	/// <summary>
	/// Provides binary serialization and deserialization of <see cref="FunctionPrototype"/>
	/// objects for <c>string.dump</c> and <c>load</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Format (AsyncLua bytecode v1):
	/// <list type="bullet">
	///   <item><description><c>1B 41 73 4C</c> — magic bytes ("\x1bAsL")</description></item>
	///   <item><description>byte format version (1)</description></item>
	///   <item><description>byte endianness flag (1 = LE, 2 = BE)</description></item>
	///   <item><description>uint reserved (4 bytes, zero)</description></item>
	///   <item><description>the root <see cref="FunctionPrototype"/></description></item>
	/// </list>
	/// </para>
	/// </remarks>
	public static class PrototypeSerializer
	{
		/// <summary>
		/// Magic number identifying AsyncLua bytecode: <c>\x1bAsL</c>.
		/// </summary>
		public static readonly byte[] Magic = { 0x1B, 0x41, 0x73, 0x4C };

		/// <summary>
		/// Current bytecode format version.
		/// </summary>
		public const byte FormatVersion = 1;

		/// <summary>
		/// Serializes a <see cref="FunctionPrototype"/> into a binary byte array.
		/// </summary>
		/// <param name="prototype">The function prototype to serialize.</param>
		/// <param name="strip">
		/// If <see langword="true"/>, debug information (source positions) is not included.
		/// </param>
		/// <returns>A byte array containing the serialized prototype.</returns>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="prototype"/> is <see langword="null"/>.</exception>
		public static byte[] Serialize(FunctionPrototype prototype, bool strip = false)
		{
			if (prototype is null)
				throw new ArgumentNullException(nameof(prototype));

			using var ms = new MemoryStream();
			using var writer = new BinaryWriter(ms);

			// Header
			writer.Write(Magic);
			writer.Write(FormatVersion);
			writer.Write((byte)(BitConverter.IsLittleEndian ? 1 : 2));
			writer.Write((uint)0); // reserved

			WritePrototype(writer, prototype, strip);

			return ms.ToArray();
		}

		/// <summary>
		/// Deserializes a <see cref="FunctionPrototype"/> from a binary byte array.
		/// </summary>
		/// <param name="data">The byte array containing the serialized prototype.</param>
		/// <returns>The deserialized function prototype.</returns>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is <see langword="null"/>.</exception>
		/// <exception cref="InvalidDataException">Thrown when the data is not valid AsyncLua bytecode.</exception>
		public static FunctionPrototype Deserialize(byte[] data)
		{
			if (data is null)
				throw new ArgumentNullException(nameof(data));

			using var ms = new MemoryStream(data, false);
			using var reader = new BinaryReader(ms);

			// Header
			for (int i = 0; i < Magic.Length; i++)
			{
				if (reader.ReadByte() != Magic[i])
					throw new InvalidDataException("Invalid magic number in bytecode.");
			}

			byte version = reader.ReadByte();
			if (version != FormatVersion)
				throw new InvalidDataException($"Unsupported bytecode format version: {version}.");

			byte endianness = reader.ReadByte();
			// We currently assume host endianness matches the data.
			// In the future, we could swap bytes if needed.
			_ = endianness;

			uint reserved = reader.ReadUInt32();
			_ = reserved;

			return ReadPrototype(reader, strip: false);
		}

		private static void WritePrototype(BinaryWriter writer, FunctionPrototype proto, bool strip)
		{
			// SourceName
			WriteString(writer, proto.SourceName ?? "chunk");

			// Metadata
			writer.Write(proto.IsAsync);
			writer.Write(proto.ParameterCount);
			writer.Write(proto.IsVararg);
			writer.Write(proto.MaxRegSize);

			// UpvalueDescriptions
			var upvs = proto.UpvalueDescriptions;
			writer.Write(upvs.Length);
			for (int i = 0; i < upvs.Length; i++)
			{
				writer.Write(upvs[i].RegisterIndex);
				writer.Write(upvs[i].IsLocal);
			}

			// Constants
			var consts = proto.Constants;
			writer.Write(consts.Length);
			for (int i = 0; i < consts.Length; i++)
				WriteValue(writer, consts[i]);

			// Instructions
			var instrs = proto.Instructions;
			writer.Write(instrs.Length);
			for (int i = 0; i < instrs.Length; i++)
			{
				var inst = instrs[i];
				writer.Write((byte)inst.Code);
				writer.Write(inst.A);
				writer.Write(inst.B);
				writer.Write(inst.C);
				writer.Write((ushort)inst.Flags);
			}

			// Positions (optional, stripped if strip == true or no positions)
			if (!strip && proto.Positions != null && proto.Positions.Length == instrs.Length)
			{
				writer.Write(true); // has positions
				for (int i = 0; i < proto.Positions.Length; i++)
				{
					var pos = proto.Positions[i];
					// Store only StartIndex and Length; SourceCode and TabSize are not preserved.
					writer.Write(pos.StartIndex);
					writer.Write(pos.Length);
				}
			}
			else
			{
				writer.Write(false); // no positions
			}

			// Inner prototypes
			var inners = proto.InnerPrototypes;
			writer.Write(inners.Length);
			for (int i = 0; i < inners.Length; i++)
				WritePrototype(writer, inners[i], strip);
		}

		private static FunctionPrototype ReadPrototype(BinaryReader reader, bool strip)
		{
			// SourceName
			string sourceName = ReadString(reader);

			// Metadata
			bool isAsync = reader.ReadBoolean();
			byte paramCount = reader.ReadByte();
			bool isVararg = reader.ReadBoolean();
			int maxRegSize = reader.ReadInt32();

			// UpvalueDescriptions
			int upvCount = reader.ReadInt32();
			var upvs = new UpvalueDescription[upvCount];
			for (int i = 0; i < upvCount; i++)
			{
				byte regIndex = reader.ReadByte();
				bool isLocal = reader.ReadBoolean();
				upvs[i] = new UpvalueDescription(regIndex, isLocal);
			}

			// Constants
			int constCount = reader.ReadInt32();
			var constants = new LuaValue[constCount];
			for (int i = 0; i < constCount; i++)
				constants[i] = ReadValue(reader);

			// Instructions
			int instrCount = reader.ReadInt32();
			var instructions = new Instruction[instrCount];
			for (int i = 0; i < instrCount; i++)
			{
				var code = (OpCode)reader.ReadByte();
				byte a = reader.ReadByte();
				ushort b = reader.ReadUInt16();
				ushort c = reader.ReadUInt16();
				var flags = (OpFlags)reader.ReadUInt16();
				instructions[i] = new Instruction(code, a, b, c, flags);
			}

			// Positions
			CodePositionalInfo[]? positions = null;
			bool hasPositions = reader.ReadBoolean();
			if (hasPositions)
			{
				positions = new CodePositionalInfo[instrCount];
				bool anyValid = false;
				for (int i = 0; i < instrCount; i++)
				{
					var pos = new CodePositionalInfo
					{
						SourceCode = sourceName,
						StartIndex = reader.ReadInt32(),
						Length = reader.ReadInt32(),
					};
					positions[i] = pos;
					if (pos.IsValid)
						anyValid = true;
				}
				// Если ни одна позиция не валидна (sourceName короче позиций),
				// отбрасываем их полностью, чтобы избежать ошибок форматирования.
				if (!anyValid)
					positions = null;
			}

			// Inner prototypes
			int innerCount = reader.ReadInt32();
			var inners = new FunctionPrototype[innerCount];
			for (int i = 0; i < innerCount; i++)
				inners[i] = ReadPrototype(reader, strip);

			return new FunctionPrototype(
				instructions,
				maxRegSize,
				isAsync,
				constants,
				inners,
				paramCount,
				isVararg,
				sourceName,
				positions,
				upvs);
		}

		private static void WriteValue(BinaryWriter writer, LuaValue value)
		{
			switch (value)
			{
				case LuaNil:
					writer.Write((byte)0);
					break;
				case LuaBoolean b:
					writer.Write((byte)1);
					writer.Write(b.Value);
					break;
				case LuaNumber n:
					writer.Write((byte)2);
					writer.Write(n.Value);
					break;
				case LuaString s:
					writer.Write((byte)3);
					WriteString(writer, s.Value);
					break;
				default:
					throw new InvalidOperationException($"Cannot serialize constant of type {value.TypeName}.");
			}
		}

		private static LuaValue ReadValue(BinaryReader reader)
		{
			byte tag = reader.ReadByte();
			switch (tag)
			{
				case 0: return LuaNil.Instance;
				case 1: return LuaBoolean.FromBoolean(reader.ReadBoolean());
				case 2: return new LuaNumber(reader.ReadDouble());
				case 3:
					{
						string s = ReadString(reader);
						return new LuaString(s);
					}
				default:
					throw new InvalidDataException($"Unknown constant type tag: {tag}.");
			}
		}

		private static void WriteString(BinaryWriter writer, string value)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(value);
			writer.Write(bytes.Length);
			writer.Write(bytes);
		}

		private static string ReadString(BinaryReader reader)
		{
			int length = reader.ReadInt32();
			byte[] bytes = reader.ReadBytes(length);
			return Encoding.UTF8.GetString(bytes);
		}
	}
}
