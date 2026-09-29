using LanguageLab.Application.Books;
using static LanguageLab.Tests.Books.BookFixtures;

namespace LanguageLab.Tests.Books;

public class EpubTocTests
{
    private static Dictionary<string, List<TocEntry>> Toc(Dictionary<string, byte[]> files, string opf, Dictionary<string, ManifestItem> manifest) =>
        EpubToc.Entries(files, XhtmlDocument.Parse(opf, CancellationToken.None)!, manifest, CancellationToken.None);

    [Theory]
    [InlineData("OEBPS/", "text/ch01.xhtml", "OEBPS/text/ch01.xhtml")]
    [InlineData("OEBPS/", "text/ch%2002.xhtml#start", "OEBPS/text/ch 02.xhtml")]
    [InlineData("OEBPS/", "text/../text/./ch03.xhtml", "OEBPS/text/ch03.xhtml")]
    [InlineData("", "OEBPS//content.opf", "OEBPS/content.opf")]
    [InlineData("OEBPS/", "../../../etc/passwd", "etc/passwd")]
    public void Resolves_an_href_inside_the_archive(string @base, string href, string path)
    {
        Assert.Equal(path, EpubToc.ResolvePath(@base, href));
    }

    [Fact]
    public void Takes_the_directory_of_a_path()
    {
        Assert.Equal("OEBPS/text/", EpubToc.DirOf("OEBPS/text/nav.xhtml"));
        Assert.Equal("", EpubToc.DirOf("nav.xhtml"));
    }

    [Fact]
    public void Reads_the_navigation_document_keeping_the_first_entry_per_place()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["OEBPS/nav.xhtml"] = Utf8(XhtmlDoc("""
                <nav epub:type="landmarks"><ol><li><a href="text/ch01.xhtml">Start</a></li></ol></nav>
                <nav epub:type="toc"><ol>
                  <li><a href="text/ch01.xhtml">One</a></li>
                  <li><a href="text/ch01.xhtml">One again</a></li>
                  <li><a href="text/ch01.xhtml#part%20two">Two</a></li>
                  <li><a href="text/ch02.xhtml"> </a></li>
                </ol></nav>
                """)),
        };
        var manifest = new Dictionary<string, ManifestItem> { ["nav"] = new("OEBPS/nav.xhtml", "application/xhtml+xml", ["nav"]) };

        var toc = Toc(files, "<package><spine/></package>", manifest);

        Assert.Equal(new[] { "OEBPS/text/ch01.xhtml" }, toc.Keys);
        Assert.Equal(new[] { new TocEntry("", "One"), new TocEntry("part two", "Two") }, toc["OEBPS/text/ch01.xhtml"]);
    }

    [Fact]
    public void Falls_back_to_the_ncx_the_spine_names()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["OEBPS/toc.ncx"] = Utf8("""
                <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/"><navMap>
                  <navPoint id="p1"><navLabel><text>One</text></navLabel><content src="text/ch01.xhtml"/></navPoint>
                </navMap></ncx>
                """),
        };
        var manifest = new Dictionary<string, ManifestItem> { ["toc"] = new("OEBPS/toc.ncx", "text/plain", []) };

        var toc = Toc(files, """<package><spine toc="toc"/></package>""", manifest);

        Assert.Equal(new[] { new TocEntry("", "One") }, toc["OEBPS/text/ch01.xhtml"]);
    }

    [Fact]
    public void Finds_no_entries_without_a_navigation_document_or_an_ncx()
    {
        Assert.Empty(Toc([], "<package><spine/></package>", []));
    }
}
