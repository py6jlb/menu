FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY MenuPlanner.sln ./
COPY src/MenuPlanner.Api/MenuPlanner.Api.csproj src/MenuPlanner.Api/
RUN dotnet restore MenuPlanner.sln

COPY src/ src/
WORKDIR /src/src/MenuPlanner.Api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "MenuPlanner.Api.dll"]
