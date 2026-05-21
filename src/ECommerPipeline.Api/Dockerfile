# =========================================================
#  Stage 1: Build
# =========================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files first to leverage Docker layer cache on dependency restore
COPY ECommerPipeline.sln ./
COPY src/ECommerPipeline.Domain/ECommerPipeline.Domain.csproj                 src/ECommerPipeline.Domain/
COPY src/ECommerPipeline.Application/ECommerPipeline.Application.csproj       src/ECommerPipeline.Application/
COPY src/ECommerPipeline.Infrastructure/ECommerPipeline.Infrastructure.csproj src/ECommerPipeline.Infrastructure/
COPY src/ECommerPipeline.Api/ECommerPipeline.Api.csproj                       src/ECommerPipeline.Api/
RUN dotnet restore src/ECommerPipeline.Api/ECommerPipeline.Api.csproj

# Copy everything else and publish
COPY src/ src/
RUN dotnet publish src/ECommerPipeline.Api/ECommerPipeline.Api.csproj \
    -c Release -o /app/publish \
    /p:UseAppHost=false

# =========================================================
#  Stage 2: Runtime
# =========================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN groupadd --system app && useradd --system --gid app app
USER app

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "ECommerPipeline.Api.dll"]
