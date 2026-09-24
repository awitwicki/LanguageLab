using LanguageLab.Domain;

namespace LanguageLab.Tests;

public class ReaderHashTests
{
    [Fact]
    public void A_hex_hash_is_kept_lowercase()
    {
        Assert.Equal(new string('a', 64), ReaderHash.Normalize(new string('A', 64)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Anything_else_is_null(string? raw)
    {
        Assert.Null(ReaderHash.Normalize(raw));
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        Assert.Equal(new string('0', 64), ReaderHash.Normalize($" {new string('0', 64)}\n"));
    }
}
