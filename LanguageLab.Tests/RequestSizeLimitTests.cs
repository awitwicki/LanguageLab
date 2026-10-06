using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LanguageLab.Tests;

/// <summary>
/// Program.cs caps every request body at 256 KB and lets book import (16 MB) and bulk personal
/// words (1 MB) raise it with RequestSizeLimitAttribute metadata. That only works if real Kestrel
/// honours the metadata above its server-wide limit — TestServer enforces neither, so this runs
/// Kestrel itself on a free port.
/// </summary>
public class RequestSizeLimitTests
{
    [Fact]
    public async Task Endpoint_metadata_raises_the_server_wide_body_limit()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256L * 1024);
        await using var app = builder.Build();

        app.MapPost("/small", async (HttpRequest request) => Results.Ok((await new StreamReader(request.Body).ReadToEndAsync()).Length));
        app.MapPost("/large", async (HttpRequest request) => Results.Ok((await new StreamReader(request.Body).ReadToEndAsync()).Length))
            .WithMetadata(new RequestSizeLimitAttribute(16L * 1024 * 1024));

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var oneMegabyte = new string('a', 1024 * 1024);

        var large = await client.PostAsync("/large", new StringContent(oneMegabyte));
        var small = await client.PostAsync("/small", new StringContent(oneMegabyte));

        Assert.Equal(HttpStatusCode.OK, large.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, small.StatusCode);

        await app.StopAsync();
    }
}
