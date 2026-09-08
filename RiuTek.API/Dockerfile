# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for optimal layer caching
COPY ["RiuTek.Core/RiuTek.Core.csproj", "RiuTek.Core/"]
COPY ["RiuTek.Application/RiuTek.Application.csproj", "RiuTek.Application/"]
COPY ["RiuTek.Infrastructure/RiuTek.Infrastructure.csproj", "RiuTek.Infrastructure/"]
COPY ["RiuTek.API/RiuTek.API.csproj", "RiuTek.API/"]

# Restore dependencies for production projects
RUN dotnet restore "RiuTek.API/RiuTek.API.csproj"

# Copy source code for production projects
COPY ["RiuTek.Core/", "RiuTek.Core/"]
COPY ["RiuTek.Application/", "RiuTek.Application/"]
COPY ["RiuTek.Infrastructure/", "RiuTek.Infrastructure/"]
COPY ["RiuTek.API/", "RiuTek.API/"]

# Publish Release artifacts without re-restoring
RUN dotnet publish "RiuTek.API/RiuTek.API.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

COPY --from=build /app/publish .

# Run as non-root user (app user UID 1654 in official .NET images)
USER $APP_UID

ENTRYPOINT ["dotnet", "RiuTek.API.dll"]
