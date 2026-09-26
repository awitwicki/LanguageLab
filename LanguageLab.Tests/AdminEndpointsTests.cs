using LanguageLab.Api.Endpoints;
using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;

namespace LanguageLab.Tests;

/// <summary>
/// <see cref="AdminEndpoints.ParseStatus"/> is what the moderation queue's `GET /dictionaries`
/// uses to read `?status=`, by hand, case-insensitively — minimal API's own query-string
/// binding for an enum is case-sensitive (`Enum.TryParse` without `ignoreCase: true`), unlike
/// this app's JSON body binding, so it would 400 on the lowercase `pending` the SPA actually
/// sends. No HTTP here: this is the pure parsing logic behind that endpoint.
/// </summary>
public class AdminEndpointsTests
{
    [Fact]
    public void Null_defaults_to_pending()
    {
        Assert.Equal(PublicationStatus.Pending, AdminEndpoints.ParseStatus(null));
    }

    [Fact]
    public void Empty_defaults_to_pending()
    {
        Assert.Equal(PublicationStatus.Pending, AdminEndpoints.ParseStatus(""));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("Pending")]
    [InlineData("PENDING")]
    public void Pending_parses_regardless_of_case(string status)
    {
        Assert.Equal(PublicationStatus.Pending, AdminEndpoints.ParseStatus(status));
    }

    [Fact]
    public void Published_parses_too()
    {
        Assert.Equal(PublicationStatus.Published, AdminEndpoints.ParseStatus("published"));
    }

    [Fact]
    public void An_unknown_name_is_invalid()
    {
        Assert.Null(AdminEndpoints.ParseStatus("nonsense"));
    }

    [Fact]
    public void A_numeric_string_outside_the_enum_range_is_invalid()
    {
        // Enum.TryParse alone accepts any integer-looking string, defined member or not —
        // ParseStatus must reject it rather than silently produce an out-of-range status that
        // ListAsync would then filter on and quietly return an empty page for.
        Assert.Null(AdminEndpoints.ParseStatus("9"));
    }
}

/// <summary>
/// <see cref="AdminEndpoints.TryParseShelfStatus"/> is the shelf admin panel's `?status=`
/// parser. Unlike the moderation queue's status, absent here means "no filter" (the "All"
/// list) rather than a default shelf — so it cannot reuse <see cref="AdminEndpoints.ParseStatus"/>.
/// </summary>
public class AdminEndpointsShelfStatusTests
{
    [Fact]
    public void Null_means_no_filter()
    {
        Assert.True(AdminEndpoints.TryParseShelfStatus(null, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Empty_means_no_filter()
    {
        Assert.True(AdminEndpoints.TryParseShelfStatus("", out var parsed));
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData("known")]
    [InlineData("Known")]
    [InlineData("KNOWN")]
    public void Known_parses_regardless_of_case(string status)
    {
        Assert.True(AdminEndpoints.TryParseShelfStatus(status, out var parsed));
        Assert.Equal(SortStatus.Known, parsed);
    }

    [Fact]
    public void An_unknown_name_is_invalid()
    {
        Assert.False(AdminEndpoints.TryParseShelfStatus("nonsense", out _));
    }

    [Fact]
    public void A_numeric_string_outside_the_enum_range_is_invalid()
    {
        Assert.False(AdminEndpoints.TryParseShelfStatus("9", out _));
    }
}
