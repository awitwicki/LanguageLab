using LanguageLab.TgBot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

// Read up front: a missing key fails here, with its name, before anything talks to Telegram.
var options = BotOptions.Read(builder.Configuration);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(options.BotToken));
builder.Services.AddHostedService<BotService>();

await builder.Build().RunAsync();
