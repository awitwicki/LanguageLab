FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "./"]
COPY ["LanguageLab.TgBot/LanguageLab.TgBot.csproj", "LanguageLab.TgBot/"]
RUN dotnet restore "LanguageLab.TgBot/LanguageLab.TgBot.csproj"
# The bot references no other project, so only its own folder goes in: a change to the API
# or the SPA must not rebuild this image.
COPY LanguageLab.TgBot/ LanguageLab.TgBot/
WORKDIR "/src/LanguageLab.TgBot"
RUN dotnet publish "./LanguageLab.TgBot.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LanguageLab.TgBot.dll"]
