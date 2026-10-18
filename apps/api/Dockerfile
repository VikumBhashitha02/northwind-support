# Multi-stage build for Northwind Support Desk API (.NET 10)

# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy central package management, build props, and SDK global config for restore caching
COPY global.json Directory.Build.props Directory.Packages.props ./

# Copy csproj files preserving relative paths for dependency restoration
COPY apps/api/src/SupportDesk.Domain/SupportDesk.Domain.csproj apps/api/src/SupportDesk.Domain/
COPY apps/api/src/SupportDesk.Application/SupportDesk.Application.csproj apps/api/src/SupportDesk.Application/
COPY apps/api/src/SupportDesk.Infrastructure/SupportDesk.Infrastructure.csproj apps/api/src/SupportDesk.Infrastructure/
COPY apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj apps/api/src/SupportDesk.Presentation/

# Restore dependencies separately to leverage Docker layer caching
RUN dotnet restore apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj

# Copy remaining source code
COPY apps/api/src/ apps/api/src/

# Compile and publish optimized release bundle
RUN dotnet publish apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# Run container as non-root user 'app'
USER app

COPY --from=build --chown=app:app /app/publish .

ENTRYPOINT ["dotnet", "SupportDesk.Presentation.dll"]
