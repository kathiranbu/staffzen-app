# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY APM.StaffZen.Blazor.csproj .
RUN dotnet restore

# Copy everything and publish
COPY . .
RUN dotnet publish APM.StaffZen.Blazor.csproj -c Release -o /app/publish

# ── Stage 2: Runtime ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Render uses port 10000 by default for free tier
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "APM.StaffZen.Blazor.dll"]
