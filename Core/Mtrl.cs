using System;
using System.Collections.Generic;
using System.Text;

namespace Wardrobe.Core;

/// <summary>One shader constant in a material: an id, and the floats it points at.</summary>
public sealed class MtrlConstant
{
    public uint Id;
    public int ByteOffset;
    public int ByteSize;
    public float[] Values = [];

    public string Name => MtrlFile.KnownConstants.TryGetValue(Id, out var n) ? n : $"0x{Id:X8}";
}

/// <summary>
/// A minimal reader and in-place editor for .mtrl files. It only rewrites shader constant floats, so the file's layout
/// never changes and a bad edit cannot corrupt the structure.
/// </summary>
public sealed class MtrlFile
{
    public static readonly Dictionary<uint, string> KnownConstants = new()
    {
        [0x2C2A34DD] = "g_DiffuseColor",
        [0x38A64362] = "g_EmissiveColor",
        [0xD62BF368] = "g_AlphaAperture",
        [0xD07A6A65] = "g_AlphaOffset",
        [0x29AC0223] = "g_AlphaThreshold",
        [0x7801E004] = "g_GlassIOR",
        [0xC4647F37] = "g_GlassThicknessMax",
        [0x7918D232] = "g_IrisOptionColorEmissiveIntensity",
        [0x8EA14846] = "g_IrisOptionColorEmissiveRate",
        [0x29253809] = "g_IrisOptionColorRate",
        [0x50E36D56] = "g_IrisRingColor",
        [0x7DABA471] = "g_IrisRingEmissiveIntensity",
        [0x58DE06E2] = "g_IrisRingForceColor",
        [0x285F72D2] = "g_IrisRingOddRate",
        [0x5B608CFE] = "g_IrisRingUvFadeWidth",
        [0xE18398AE] = "g_IrisRingUvRadius",
    };

    public const uint DiffuseColor = 0x2C2A34DD;
    public const uint EmissiveColor = 0x38A64362;

    private readonly byte[] data;
    private readonly int valuesStart;

    public List<string> Textures { get; } = new();
    public string ShaderName { get; private set; } = string.Empty;
    public int ColourTableBytes { get; private set; }
    public List<MtrlConstant> Constants { get; } = new();

    private MtrlFile(byte[] bytes, int valuesStart)
    {
        data = (byte[])bytes.Clone();
        this.valuesStart = valuesStart;
    }

    public static MtrlFile? Parse(byte[] bytes)
    {
        try
        {
            var pos = 0;
            ushort U16() { var v = BitConverter.ToUInt16(bytes, pos); pos += 2; return v; }
            uint U32() { var v = BitConverter.ToUInt32(bytes, pos); pos += 4; return v; }

            var version = U32();
            _ = version;
            U16(); // file size
            var dataSetSize = U16();
            var stringSize = U16();
            var shaderNameOffset = U16();
            int texCount = bytes[pos++];
            int uvCount = bytes[pos++];
            int colorSetCount = bytes[pos++];
            int additionalSize = bytes[pos++];

            var texOffsets = new List<int>();
            for (var i = 0; i < texCount; i++)
            {
                texOffsets.Add(U16());
                U16();
            }

            pos += uvCount * 4 + colorSetCount * 4;
            var stringsAt = pos;
            pos += stringSize;
            pos += additionalSize;
            var colourTableAt = pos;
            pos += dataSetSize;

            var valueListSize = U16();
            var keyCount = U16();
            var constCount = U16();
            var samplerCount = U16();
            U32(); // shader flags

            pos += keyCount * 8;
            var constAt = pos;
            pos += constCount * 8;
            pos += samplerCount * 12;
            var valuesAt = pos;

            if (valuesAt + valueListSize > bytes.Length)
                return null;

            var file = new MtrlFile(bytes, valuesAt) { ColourTableBytes = dataSetSize };
            file.ShaderName = ReadString(bytes, stringsAt + shaderNameOffset);
            foreach (var o in texOffsets)
                file.Textures.Add(ReadString(bytes, stringsAt + o));

            for (var i = 0; i < constCount; i++)
            {
                var c = constAt + i * 8;
                var id = BitConverter.ToUInt32(bytes, c);
                var offset = BitConverter.ToUInt16(bytes, c + 4);
                var size = BitConverter.ToUInt16(bytes, c + 6);
                if (offset + size > valueListSize || size % 4 != 0)
                    continue;

                var floats = new float[size / 4];
                for (var f = 0; f < floats.Length; f++)
                    floats[f] = BitConverter.ToSingle(bytes, valuesAt + offset + f * 4);
                file.Constants.Add(new MtrlConstant { Id = id, ByteOffset = offset, ByteSize = size, Values = floats });
            }

            _ = colourTableAt;
            return file;
        }
        catch
        {
            return null;
        }
    }

    public MtrlConstant? Find(uint id) => Constants.Find(c => c.Id == id);

    /// <summary>Writes new floats into a constant. The count must match the constant's size.</summary>
    public bool Set(uint id, float[] values)
    {
        var c = Find(id);
        if (c == null || c.Values.Length != values.Length)
            return false;

        for (var i = 0; i < values.Length; i++)
        {
            BitConverter.TryWriteBytes(data.AsSpan(valuesStart + c.ByteOffset + i * 4, 4), values[i]);
            c.Values[i] = values[i];
        }

        return true;
    }

    public byte[] ToBytes() => (byte[])data.Clone();

    private static string ReadString(byte[] bytes, int at)
    {
        var end = at;
        while (end < bytes.Length && bytes[end] != 0)
            end++;
        return Encoding.UTF8.GetString(bytes, at, end - at);
    }
}
