// See https://aka.ms/new-console-template for more information
using Autofac;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using PowerBot.Lite;
using LanguageLab.TgBot.Handlers;

Console.WriteLine("Starting LanguageLabBot");

var botToken = Environment.GetEnvironmentVariable("TELEGRAM_TOKEN")!;

// Run bot
var botClient = new CoreBot(botToken);
 
// Create database if not exists
// var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
// optionsBuilder.UseNpgsql(Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")!);
//
// var dbContextOptions = optionsBuilder.Options;
//
// await using (var dbContext = new ApplicationDbContext(dbContextOptions))
// {
//     await dbContext.Database.MigrateAsync();
//     Console.WriteLine("Database is synchronized");
// }

// Register middlewares and handlers
botClient.RegisterHandler<BotHandler>();

// Register services
// botClient.RegisterContainers(x =>
// {
//     x.Register(ctx => dbContextOptions)
//         .As<DbContextOptions<ApplicationDbContext>>()
//         .SingleInstance();
//
//     x.RegisterType<ApplicationDbContext>()
//         .AsSelf()
//         .InstancePerLifetimeScope();
// });

botClient.Build();

await botClient.StartReceiving();

// Wait for eternity
await Task.Delay(-1);
