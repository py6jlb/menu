FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/MenuPlanner.Api/MenuPlanner.Api.csproj src/MenuPlanner.Api/
RUN dotnet restore src/MenuPlanner.Api/MenuPlanner.Api.csproj

COPY src/ src/
WORKDIR /src/src/MenuPlanner.Api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Backend работает от встроенного непривилегированного пользователя app
# (uid 1654 в образе aspnet:10.0). PHOTOS_DIR создаётся заранее и принадлежит
# ему: named volume наследует владельца каталога образа.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/photos /app/documents \
    && chown -R app:app /app
COPY --from=build --chown=app:app /app/publish .
RUN chown -R app:app /app/photos /app/documents
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "MenuPlanner.Api.dll"]
