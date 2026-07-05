using Acvc.Core.Acd;

namespace Acvc.Tests;

public class AcdKeyTests
{
    // Expected keys derived by hand-tracing CarTuner/files/getdata.bms with QuickBMS
    // semantics (signed 32-bit wraparound, truncate-toward-zero division), independent
    // of the implementation under test. "abarth500" and "abc" were additionally
    // verified octet-by-octet with manual arithmetic.
    [Theory]
    [InlineData("abarth500", "7-248-6-221-246-250-21-49")]
    [InlineData("bmw_m3_e30", "108-96-216-121-166-192-73-49")]
    [InlineData("ks_toyota_supra_mkiv", "125-2-225-113-64-171-64-119")]
    [InlineData("ferrari_458", "235-167-105-254-54-143-17-57")]
    [InlineData("lotus_elise_sc", "221-249-82-18-54-223-63-100")]
    // Short names: the key3/key5 loops have negative bounds and must simply not run.
    [InlineData("abc", "38-158-0-190-66-4-74-100")]
    public void Generate_matches_bms_hand_trace(string folderName, string expected)
        => Assert.Equal(expected, AcdKey.Generate(folderName));

    [Fact]
    public void Generate_is_case_sensitive_by_design()
        // Case folding is the caller's job (AcdUnpacker lowercases); the raw algorithm
        // must reproduce the .bms byte-for-byte, so case changes the key.
        => Assert.NotEqual(AcdKey.Generate("abarth500"), AcdKey.Generate("Abarth500"));

    [Fact]
    public void Generate_rejects_empty_name()
        => Assert.Throws<ArgumentException>(() => AcdKey.Generate(""));

    [Fact]
    public void Generate_rejects_non_ascii_name()
        => Assert.Throws<ArgumentException>(() => AcdKey.Generate("abarth500é"));
}
