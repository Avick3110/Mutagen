using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

// Builds one major record's raw bytes, for tests that read it through the direct parse and the overlay.
internal static class RawRecordBytes
{
    public static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    public static ParsingMeta Meta(GameConstants constants)
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(constants, TestModKey, masters);
    }

    public static byte[] Str(string s) => Encoding.ASCII.GetBytes(s + "\0");

    public static byte[] U32(uint value) => BitConverter.GetBytes(value);

    public static byte[] Record(GameConstants constants, string type, params (string Type, byte[] Content)[] subrecords)
    {
        var content = new MemoryStream();
        foreach (var (subType, subContent) in subrecords)
        {
            content.Write(Encoding.ASCII.GetBytes(subType));
            content.Write(BitConverter.GetBytes(checked((ushort)subContent.Length)));
            content.Write(subContent);
        }
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(type));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        if (constants.MajorConstants.HeaderLength == 24)
        {
            record.Write(BitConverter.GetBytes((ushort)(constants.DefaultFormVersion ?? 0)));
            record.Write(BitConverter.GetBytes((ushort)0));
        }
        record.Write(body);
        return record.ToArray();
    }

    // The subrecord types of a written record, in order.
    public static List<string> SubrecordTypes(GameConstants constants, byte[] bytes)
    {
        var ret = new List<string>();
        for (int i = constants.MajorConstants.HeaderLength; i + 6 <= bytes.Length; )
        {
            ret.Add(Encoding.ASCII.GetString(bytes, i, 4));
            i += 6 + BitConverter.ToUInt16(bytes, i + 4);
        }
        return ret;
    }
}
