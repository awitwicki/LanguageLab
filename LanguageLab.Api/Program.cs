using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageLab.Api;
using LanguageLab.Api.Endpoints;
using LanguageLab.Application.Services;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ICurrentUser, ConfigCurrentUser>();
builder.Services.AddScoped<BookImportService>();
builder.Services.AddScoped<WordSortingService>();
builder.Services.AddScoped<WordSelectionService>();
builder.Services.AddScoped<TrainingSessionService>();

builder.Services.AddRequestDecompression();

// SortStatus їздить рядком («known»), а не числом: JSON має читатись очима.
// CamelCase обов'язковий: без політики іменування серіалізація дала б «Known»,
// а клієнт типізований під 'known' | 'unknown' | 'excluded'.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// Книжка на 6k слів — це 1-2 МБ JSON, дефолтні 30 МБ Kestrel лишаємо з запасом,
// але явний ліміт краще, ніж сюрприз на великій книжці.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64L * 1024 * 1024);

var app = builder.Build();

// Схему веде веб: MigrateAsync прибрано з бота, щоб дві точки входу
// не намагались мігрувати одну базу одночасно.
await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseRequestDecompression();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapDictionaryEndpoints();
app.MapSortingEndpoints();

// SPA має власний роутинг: усе, що не /api і не файл, віддаємо index.html.
app.MapFallbackToFile("index.html");

app.Run();
