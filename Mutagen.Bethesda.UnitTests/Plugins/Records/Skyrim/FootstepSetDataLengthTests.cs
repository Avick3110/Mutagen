using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

public class FootstepSetDataLengthTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.SkyrimSE, TestModKey, masters);
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    private static byte[] Counts(params uint[] counts)
    {
        return counts.SelectMany(BitConverter.GetBytes).ToArray();
    }

    private static byte[] FormIds(params uint[] ids)
    {
        return ids.SelectMany(BitConverter.GetBytes).ToArray();
    }

    private static byte[] MakeFootstepSetBytes(byte[] xcnt, byte[] data, bool edidLast = false)
    {
        var content = new MemoryStream();
        if (!edidLast)
        {
            WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestFootstepSet\0"));
        }
        WriteSubrecord(content, "XCNT", xcnt);
        WriteSubrecord(content, "DATA", data);
        if (edidLast)
        {
            WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestFootstepSet\0"));
        }
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("FSTS"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)44));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static IFootstepSetGetter ReadDirect(byte[] bytes)
    {
        return FootstepSet.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static IFootstepSetGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return FootstepSetBinaryOverlay.FootstepSetFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static byte[] Write(IFootstepSetGetter footstepSet)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var bundle = new WritingBundle(GameConstants.SkyrimSE)
        {
            MasterReferences = masters,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
        };
        var memStream = new MemoryStream();
        using (var writer = new MutagenWriter(memStream, bundle, dispose: false))
        {
            footstepSet.WriteToBinary(writer);
        }
        return memStream.ToArray();
    }

    private static byte[] Subrecord(byte[] bytes, string type)
    {
        var target = Encoding.ASCII.GetBytes(type);
        for (int i = 24; i + 6 <= bytes.Length; )
        {
            var len = BitConverter.ToUInt16(bytes, i + 4);
            if (bytes.AsSpan(i, 4).SequenceEqual(target))
            {
                return bytes.AsSpan(i + 6, len).ToArray();
            }
            i += 6 + len;
        }
        throw new InvalidOperationException($"No {type} subrecord found.");
    }

    private static IEnumerable<IFootstepSetGetter> ReadBoth(byte[] bytes)
    {
        yield return ReadDirect(bytes);
        yield return ReadOverlay(bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroCountsWithSurplusData_ReadsEmptyLists(bool edidLast)
    {
        var bytes = MakeFootstepSetBytes(Counts(0, 0, 0, 0, 0), new byte[4], edidLast);

        foreach (var footstepSet in ReadBoth(bytes))
        {
            footstepSet.EditorID.ShouldBe("TestFootstepSet");
            footstepSet.WalkForwardFootsteps.ShouldBeEmpty();
            footstepSet.RunForwardFootsteps.ShouldBeEmpty();
            footstepSet.WalkForwardAlternateFootsteps.ShouldBeEmpty();
            footstepSet.RunForwardAlternateFootsteps.ShouldBeEmpty();
            footstepSet.WalkForwardAlternateFootsteps2.ShouldBeEmpty();
            Subrecord(Write(footstepSet), "DATA").ShouldBeEmpty();
        }
    }

    [Fact]
    public void ShortData_ReadsWholeFormIdsPresent()
    {
        var data = FormIds(0x801).Concat(new byte[2]).ToArray();
        var bytes = MakeFootstepSetBytes(Counts(2, 0, 0, 0, 1), data, edidLast: true);

        foreach (var footstepSet in ReadBoth(bytes))
        {
            footstepSet.EditorID.ShouldBe("TestFootstepSet");
            footstepSet.WalkForwardFootsteps.Select(x => x.FormKey)
                .ShouldBe(new[] { new FormKey(TestModKey, 0x801) });
            footstepSet.RunForwardFootsteps.ShouldBeEmpty();
            footstepSet.WalkForwardAlternateFootsteps2.ShouldBeEmpty();
        }
    }

    [Fact]
    public void MatchingData_RoundTrips()
    {
        var bytes = MakeFootstepSetBytes(Counts(2, 1, 0, 0, 1), FormIds(0x801, 0x802, 0x803, 0x804));

        foreach (var footstepSet in ReadBoth(bytes))
        {
            footstepSet.WalkForwardFootsteps.Select(x => x.FormKey)
                .ShouldBe(new[] { new FormKey(TestModKey, 0x801), new FormKey(TestModKey, 0x802) });
            footstepSet.RunForwardFootsteps.Select(x => x.FormKey)
                .ShouldBe(new[] { new FormKey(TestModKey, 0x803) });
            footstepSet.WalkForwardAlternateFootsteps2.Select(x => x.FormKey)
                .ShouldBe(new[] { new FormKey(TestModKey, 0x804) });
            Write(footstepSet).ShouldBe(bytes);
        }
    }
}
