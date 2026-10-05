FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/MenuPlanner.Api/MenuPlanner.Api.csproj src/MenuPlanner.Api/
RUN dotnet restore src/MenuPlanner.Api/MenuPlanner.Api.csproj

COPY src/ src/
WORKDIR /src/src/MenuPlanner.Api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "MenuPlanner.Api.dll"]
