using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.RawRecordBytes;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A gendered field whose male and female subrecords are separated by other subrecords
// must keep both halves, in the direct parse and in the overlay.
public class GenderedSplitPairTests
{
    private static readonly GameConstants Constants = GameConstants.Starfield;

    private static IEnumerable<IArmorAddonGetter> ReadArmaBoth(byte[] bytes)
    {
        var meta = Meta(Constants);
        yield return ArmorAddon.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, meta)));
        yield return ArmorAddonBinaryOverlay.ArmorAddonFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    // MOD2 and MOD3 share their arm with the unconverted FLLD and XFLG, which are routed by the last
    // parsed field. MOD3 after MOD4 must still reach the WorldModel arm.
    [Fact]
    public void ArmorAddon_InterleavedPairs_ReadsAllFourModels()
    {
        var bytes = Record(Constants, "ARMA",
            ("EDID", Str("TestArma")),
            ("DNAM", new byte[13]),
            ("MOD2", Str("male.nif")),
            ("MO2T", new byte[] { 1, 2, 3, 4 }),
            ("MOD4", Str("male1st.nif")),
            ("MO4T", new byte[] { 5, 6, 7, 8 }),
            ("MOD3", Str("female.nif")),
            ("MOD5", Str("female1st.nif")));

        foreach (var arma in ReadArmaBoth(bytes))
        {
            arma.WorldModel.ShouldNotBeNull();
            arma.WorldModel.Male.ShouldNotBeNull();
            arma.WorldModel.Male.File.GivenPath.ShouldBe("male.nif");
            arma.WorldModel.Female.ShouldNotBeNull();
            arma.WorldModel.Female.File.GivenPath.ShouldBe("female.nif");
            arma.FirstPersonModel.ShouldNotBeNull();
            arma.FirstPersonModel.Male.ShouldNotBeNull();
            arma.FirstPersonModel.Male.File.GivenPath.ShouldBe("male1st.nif");
            arma.FirstPersonModel.Female.ShouldNotBeNull();
            arma.FirstPersonModel.Female.File.GivenPath.ShouldBe("female1st.nif");
        }
    }

    // An FLLD after SkinTexture is routed to the WorldModel arm by count. It is not a male model subrecord,
    // so it must not replace the male world model read earlier; it fills the half that is still empty.
    [Fact]
    public void ArmorAddon_UnconvertedSubrecordOnReentry_KeepsMaleModel()
    {
        var bytes = Record(Constants, "ARMA",
            ("EDID", Str("TestArma")),
            ("DNAM", new byte[13]),
            ("MOD2", Str("male.nif")),
            ("MOD6", Str("alt.hkx")),
            ("NAM0", U32(0x801)),
            ("FLLD", U32(5)));

        foreach (var arma in ReadArmaBoth(bytes))
        {
            arma.WorldModel.ShouldNotBeNull();
            arma.WorldModel.Male.ShouldNotBeNull();
            arma.WorldModel.Male.File.GivenPath.ShouldBe("male.nif");
            arma.WorldModel.Male.LightLayer.ShouldBeNull();
            arma.WorldModel.Female.ShouldNotBeNull();
            arma.WorldModel.Female.LightLayer.ShouldBe(5u);
        }
    }

    // The subrecord after a male-only world model is not a model subrecord, so no female half is made from it.
    [Fact]
    public void ArmorAddon_MaleOnlyWorldModel_HasNoFemale()
    {
        var bytes = Record(Constants, "ARMA",
            ("EDID", Str("TestArma")),
            ("DNAM", new byte[13]),
            ("MOD2", Str("male.nif")),
            ("MOD6", Str("alt.hkx")));

        foreach (var arma in ReadArmaBoth(bytes))
        {
            arma.WorldModel.ShouldNotBeNull();
            arma.WorldModel.Male.ShouldNotBeNull();
            arma.WorldModel.Male.File.GivenPath.ShouldBe("male.nif");
            arma.WorldModel.Female.ShouldBeNull();
            arma.AltSkeleton.ShouldNotBeNull();
            arma.AltSkeleton.Male.ShouldBe("alt.hkx");
        }
    }
}
