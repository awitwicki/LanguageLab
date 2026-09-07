FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# SPA збирається окремим шаром: зміна C#-коду не має перезапускати npm install.
FROM node:24-alpine AS spa
WORKDIR /spa
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["LanguageLab.Api/LanguageLab.Api.csproj", "LanguageLab.Api/"]
COPY ["LanguageLab.Application/LanguageLab.Application.csproj", "LanguageLab.Application/"]
COPY ["LanguageLab.Infrastructure/LanguageLab.Infrastructure.csproj", "LanguageLab.Infrastructure/"]
COPY ["LanguageLab.Domain/LanguageLab.Domain.csproj", "LanguageLab.Domain/"]
RUN dotnet restore "LanguageLab.Api/LanguageLab.Api.csproj"
COPY . .
WORKDIR "/src/LanguageLab.Api"
RUN dotnet publish "./LanguageLab.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=spa /spa/dist ./wwwroot
ENTRYPOINT ["dotnet", "LanguageLab.Api.dll"]
