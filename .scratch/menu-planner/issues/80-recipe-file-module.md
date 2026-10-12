# 80: Единый модуль файлов рецепта (фото и документ)

**What to build:** Фото и PDF-документ рецепта обслуживает один module `Recipes/Files/`, параметризованный видом `RecipeFileKind`; каталоги и env-ключи остаются раздельными, байтовая валидация — вне module. Дизайн и словарь зафиксированы ADR-0017 и `CONTEXT.md` (термины «Файл рецепта», «Фото рецепта», «Жизненный цикл файла рецепта»).

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

## Design (из grilling)

- Один **module**, а не два: глубокая реализация хранилища, жизненного цикла и уборки — общая; виды (`RecipeFileKind.Photo` / `Document`) — два adapter'а над ней.
- **Interface**: `IRecipeFileStore` (`SaveAsync`/`CopyAsync`/`Delete`/`ResolveReadPath`/`List`) с `kind`-параметром; `RecipeFileLifecycle` (`ReplaceAsync<T>`/`CopyFromAsync<T>`/`Retire`/`Discard`/`StageCopyAsync`, `RecipeFileCommit<T>`); `RecipeGarbageCollector.CollectAsync(kind, minimumAge)`.
- **Дескриптор вида** (`RecipeFileCatalog`): ключ каталога, regex управляемого имени, селектор ссылки (`Expression<Func<Recipe,string?>>`: `PhotoPath`/`DocumentPath`), лог-префикс.
- **За seam**: разрешение каталога/safe-path, компенсация частичной записи, чтение «бесхозных» имён из БД, правило управляемого имени, обход обоих видов в worker'е.
- **Не за seam**: байтовая валидация (image-проба / `%PDF-`), контент-типы отдачи, колонки БД `PhotoPath`/`DocumentPath`.
- `RecipeMutationService` сохраняет четыре public-метода (`UploadPhotoAsync`/`DeletePhotoAsync`/`UploadDocumentAsync`/`DeleteDocumentAsync`), внутри — один `RecipeFileLifecycle` с видом.
- Env-схема: канон `RECIPE_FILES_PHOTOS_DIR`, `RECIPE_FILES_DOCUMENTS_DIR`, `RECIPE_FILE_CLEANUP_*`; чтение старых `PHOTOS_DIR`/`DOCUMENTS_DIR`/`PHOTO_CLEANUP_*` — как fallback с deprecation-логом (чтобы живой VPS не подменил каталог молча).

## Checklist

- [x] ADR `docs/adr/0017-single-recipe-file-module.md` и правки `CONTEXT.md` на месте (термин «Файл рецепта», «Фото рецепта», «Жизненный цикл файла рецепта»; «Документ рецепта» и раздел «Разделение кода» обновлены).
- [x] `Recipes/Files/`: `RecipeFileKind`, `RecipeFileCatalog`, `IRecipeFileStore` + `StoredFile` + `FileStorage`, `RecipeFileNames`, `RecipeFileLifecycle` + `RecipeFileCommit<T>`, `RecipeGarbageCollector` + `RecipeFileCleanupResult`, `RecipeFileCleanupWorker`, `RecipeFileCleanupOptions`.
- [x] Удалены `Recipes/Photos/` и `Recipes/Documents/`; поведение и HTTP-контракты не изменились, схема БД не меняется.
- [x] `ExternalRecipePromotionService` держит один `RecipeFileLifecycle`; endpoint'ы отдачи (`/api/photos/{file}`, `/api/documents/{file}`) зовут `ResolveReadPath(kind, name)`; контент-типы остаются в endpoint'ах.
- [x] DI в `Program.cs`: `AddSingleton<IRecipeFileStore, FileStorage>`, scoped `RecipeFileLifecycle`/`RecipeGarbageCollector`, singleton `RecipeFileCleanupOptions`, hosted `RecipeFileCleanupWorker`; startup-валидация сохраняется.
- [x] Конфигурация: новые ключи + fallback на старые; обновлены `deploy/` (в т.ч. `deploy/README.md`), `docker-compose.yml`, `docker-compose.prod.yml`, `README.md`.
- [x] Тесты сведены в один параметризованный suite по видам (жизненный цикл, уборка, имена, хранилище); `InMemoryRecipeFileStore` обслуживает оба вида; Postgres-тест жизненного цикла параметризован; тесты валидации изображения/PDF не тронуты.
- [x] Прогоны: `docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test`; `scripts/test-postgres.sh` — зелёные.

## Evidence

Реализовано в ветке `ticket/80-recipe-file-module` (worktree `.worktrees/80-recipe-file-module`).

**Новый модуль** `backend/src/MenuPlanner.Api/Recipes/Files/`: `RecipeFileKind.cs`, `RecipeFileCatalog.cs`, `IRecipeFileStore.cs` (со `StoredFile`), `FileStorage.cs`, `RecipeFileNames.cs`, `RecipeFileLifecycle.cs` (с `RecipeFileCommit<T>`), `RecipeGarbageCollector.cs` (с `RecipeFileCleanupResult`), `RecipeFileCleanupWorker.cs`, `RecipeFileCleanupOptions.cs`. `kind` присутствует на методах `IRecipeFileStore` и `RecipeFileLifecycle`; `RecipeGarbageCollector.CollectAsync(kind, minimumAge)`.

**Потребители**: `RecipeMutationService` держит один `RecipeFileLifecycle` и сохраняет четыре public-метода; `ExternalRecipePromotionService` — один; `RecipeEndpoints.GetPhotoFileAsync`/`GetDocumentFileAsync` зовут `ResolveReadPath(RecipeFileKind.Photo|Document, fileName)`, байтовая валидация и контент-типы остались в endpoint'ах; `Program.cs` — DI и startup-валидация через `IRecipeFileStore`.

**Конфигурация**: канон `RECIPE_FILES_PHOTOS_DIR`, `RECIPE_FILES_DOCUMENTS_DIR`, `RECIPE_FILE_CLEANUP_ENABLED/INTERVAL_HOURS/MIN_AGE_HOURS`; старые `PHOTOS_DIR`, `DOCUMENTS_DIR`, `PHOTO_CLEANUP_*` читаются как fallback с deprecation-логом. Обновлены `docker-compose.yml`, `docker-compose.prod.yml`, `deploy/restore-drill.sh`, `deploy/README.md`, `deploy/tests/README.md`, `backend/Dockerfile`, `README.md`.

**Тесты**: `RecipeFileLifecycleServiceTests`, `RecipeGarbageCollectorTests`, `RecipeFileNamesTests`, `FileStorageTests`, `Postgres/PostgresRecipeFileLifecycleTests` — параметризованы по видам; общий `InMemoryRecipeFileStore`; старые раздельные тесты и test-double'ы удалены; тесты валидации изображения/PDF не тронуты.

**Прогоны**:
- `docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test` → `Failed: 0, Passed: 636, Skipped: 61`.
- `scripts/test-postgres.sh` → `Failed: 0, Passed: 69, Skipped: 0` (прогнан 4× — стабильно зелёный).

Схема БД (`Recipe.PhotoPath`/`DocumentPath`), HTTP-контракты и поведение не изменились; миграции не добавлялись.

## Comments

