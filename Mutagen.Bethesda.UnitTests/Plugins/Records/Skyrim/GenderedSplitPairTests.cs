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

// A gendered field whose male and female subrecords are separated by other subrecords
// must keep both halves, in the direct parse and in the overlay.
public class GenderedSplitPairTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.SkyrimSE, TestModKey, masters);
    }

    private static byte[] Str(string s) => Encoding.ASCII.GetBytes(s + "\0");

    private static byte[] MakeRecord(string type, params (string Type, byte[] Content)[] subrecords)
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
        record.Write(BitConverter.GetBytes((ushort)44));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] Write(ISkyrimMajorRecordGetter record)
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
            record.WriteToBinary(writer);
        }
        return memStream.ToArray();
    }

    private static List<string> SubrecordTypes(byte[] bytes)
    {
        var ret = new List<string>();
        for (int i = 24; i + 6 <= bytes.Length; )
        {
            ret.Add(Encoding.ASCII.GetString(bytes, i, 4));
            i += 6 + BitConverter.ToUInt16(bytes, i + 4);
        }
        return ret;
    }

    private static IArmorAddonGetter ReadArmaDirect(byte[] bytes) =>
        ArmorAddon.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));

    private static IArmorAddonGetter ReadArmaOverlay(byte[] bytes)
    {
        var meta = Meta();
        return ArmorAddonBinaryOverlay.ArmorAddonFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IArmorAddonGetter> ReadArmaBoth(byte[] bytes) =>
        new[] { ReadArmaDirect(bytes), ReadArmaOverlay(bytes) };

    // The order in houseCARL #961: CBBEtoUBE v1.5 writes the male world and first-person models first.
    // DNAM is there because every real ARMA has one, and the overlay reads it when writing.
    private static byte[] ArmaInterleaved() => MakeRecord("ARMA",
        ("EDID", Str("TestArma")),
        ("DNAM", new byte[12]),
        ("MOD2", Str("male.nif")),
        ("MO2T", new byte[] { 1, 2, 3, 4 }),
        ("MOD4", Str("male1st.nif")),
        ("MO4T", new byte[] { 5, 6, 7, 8 }),
        ("MOD3", Str("female.nif")),
        ("MOD5", Str("female1st.nif")));

    // The Creation Kit order, where each pair is adjacent.
    private static byte[] ArmaAdjacent() => MakeRecord("ARMA",
        ("EDID", Str("TestArma")),
        ("DNAM", new byte[12]),
        ("MOD2", Str("male.nif")),
        ("MO2T", new byte[] { 1, 2, 3, 4 }),
        ("MOD3", Str("female.nif")),
        ("MOD4", Str("male1st.nif")),
        ("MO4T", new byte[] { 5, 6, 7, 8 }),
        ("MOD5", Str("female1st.nif")));

    private static void AssertAllFourModels(IArmorAddonGetter arma)
    {
        arma.WorldModel.ShouldNotBeNull();
        arma.WorldModel.Male.ShouldNotBeNull();
        arma.WorldModel.Male.File.GivenPath.ShouldBe("male.nif");
        arma.WorldModel.Male.Data.ShouldNotBeNull();
        arma.WorldModel.Male.Data.Value.ToArray().ShouldBe(new byte[] { 1, 2, 3, 4 });
        arma.WorldModel.Female.ShouldNotBeNull();
        arma.WorldModel.Female.File.GivenPath.ShouldBe("female.nif");

        arma.FirstPersonModel.ShouldNotBeNull();
        arma.FirstPersonModel.Male.ShouldNotBeNull();
        arma.FirstPersonModel.Male.File.GivenPath.ShouldBe("male1st.nif");
        arma.FirstPersonModel.Male.Data.ShouldNotBeNull();
        arma.FirstPersonModel.Male.Data.Value.ToArray().ShouldBe(new byte[] { 5, 6, 7, 8 });
        arma.FirstPersonModel.Female.ShouldNotBeNull();
        arma.FirstPersonModel.Female.File.GivenPath.ShouldBe("female1st.nif");
    }

    [Fact]
    public void ArmorAddon_AdjacentPairs_ReadsAllFourModels()
    {
        foreach (var arma in ReadArmaBoth(ArmaAdjacent()))
        {
            AssertAllFourModels(arma);
        }
    }

    [Fact]
    public void ArmorAddon_InterleavedPairs_ReadsAllFourModels()
    {
        foreach (var arma in ReadArmaBoth(ArmaInterleaved()))
        {
            AssertAllFourModels(arma);
        }
    }

    [Fact]
    public void ArmorAddon_InterleavedPairs_WriteKeepsAllModelSubrecords()
    {
        foreach (var arma in ReadArmaBoth(ArmaInterleaved()))
        {
            var types = SubrecordTypes(Write(arma));
            types.ShouldContain("MOD2");
            types.ShouldContain("MO2T");
            types.ShouldContain("MOD3");
            types.ShouldContain("MOD4");
            types.ShouldContain("MO4T");
            types.ShouldContain("MOD5");
            AssertAllFourModels(ReadArmaDirect(Write(arma)));
        }
    }

    [Fact]
    public void ArmorAddon_InterleavedFormLinkPairs_ReadsAllFour()
    {
        var bytes = MakeRecord("ARMA",
            ("EDID", Str("TestArma")),
            ("DNAM", new byte[12]),
            ("NAM0", BitConverter.GetBytes(0x801)),
            ("NAM2", BitConverter.GetBytes(0x803)),
            ("NAM1", BitConverter.GetBytes(0x802)),
            ("NAM3", BitConverter.GetBytes(0x804)));

        foreach (var arma in ReadArmaBoth(bytes))
        {
            arma.SkinTexture.ShouldNotBeNull();
            arma.SkinTexture.Male.FormKey.ShouldBe(new FormKey(TestModKey, 0x801));
            arma.SkinTexture.Female.FormKey.ShouldBe(new FormKey(TestModKey, 0x802));
            arma.TextureSwapList.ShouldNotBeNull();
            arma.TextureSwapList.Male.FormKey.ShouldBe(new FormKey(TestModKey, 0x803));
            arma.TextureSwapList.Female.FormKey.ShouldBe(new FormKey(TestModKey, 0x804));
        }
    }

    [Fact]
    public void Armor_SeparatedWorldModelPair_ReadsBothHalves()
    {
        var bytes = MakeRecord("ARMO",
            ("EDID", Str("TestArmo")),
            ("MOD2", Str("male.nif")),
            ("EAMT", BitConverter.GetBytes((ushort)7)),
            ("MOD4", Str("female.nif")));
        var meta = Meta();
        var direct = Armor.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, meta)));
        var overlay = ArmorBinaryOverlay.ArmorFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));

        foreach (var armo in new IArmorGetter[] { direct, overlay })
        {
            armo.EnchantmentAmount.ShouldBe((ushort)7);
            armo.WorldModel.ShouldNotBeNull();
            armo.WorldModel.Male.ShouldNotBeNull();
            armo.WorldModel.Male.Model.ShouldNotBeNull();
            armo.WorldModel.Male.Model.File.GivenPath.ShouldBe("male.nif");
            armo.WorldModel.Female.ShouldNotBeNull();
            armo.WorldModel.Female.Model.ShouldNotBeNull();
            armo.WorldModel.Female.Model.File.GivenPath.ShouldBe("female.nif");
        }
    }
}
