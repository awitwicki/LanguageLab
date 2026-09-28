using LanguageLab.Application.Books;
using Microsoft.Extensions.DependencyInjection;

namespace LanguageLab.Tests.Import;

public class BookParserRegistrationTests
{
    [Fact]
    public void The_parser_is_one_shared_stateless_instance()
    {
        var provider = new ServiceCollection().AddBookParser().BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IBookParser>(), provider.GetRequiredService<IBookParser>());
    }
}
