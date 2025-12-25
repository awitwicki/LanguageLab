FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["LanguageLab.TgBot/LanguageLab.TgBot.csproj", "LanguageLab.TgBot/"]
RUN dotnet restore "LanguageLab.TgBot/LanguageLab.TgBot.csproj"
COPY . .
WORKDIR "/src/LanguageLab.TgBot"
RUN dotnet build "./LanguageLab.TgBot.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LanguageLab.TgBot.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LanguageLab.TgBot.dll"]
