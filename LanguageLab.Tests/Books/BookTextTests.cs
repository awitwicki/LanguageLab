using LanguageLab.Application.Books;

namespace LanguageLab.Tests.Books;

public class BookTextTests
{
    private const char Nbsp = (char)0xA0;
    private const char Bom = (char)0xFEFF;

    [Fact]
    public void Collapse_turns_every_whitespace_run_into_one_space_and_trims()
    {
        Assert.Equal("a b c", BookText.Collapse($"  a\t\n b{Nbsp}{Bom}c{Bom} "));
    }

    [Fact]
    public void Trim_drops_a_byte_order_mark_as_javascript_does()
    {
        Assert.Equal("a  b", BookText.Trim($"{Bom} a  b\n{Bom}"));
    }
}
