# 86: Политика загрузки файла рецепта — в RecipeFileUpload

**What to build:** Размер, чтение в seekable-поток и проба содержимого файла (фото/PDF) переезжают из `RecipeEndpoints.UploadPhotoAsync`/`UploadDocumentAsync` в module; endpoint мапит исход в HTTP и передаёт поток дальше, без `byte[]` и двух `MemoryStream`.

**Blocked by:** 80 — Единый модуль файлов рецепта (дескриптор вида и `IRecipeFileStore`).

**Status:** ready-for-agent

## Design (из grilling)

- `Recipes/Files/RecipeFileUpload.cs`: владеет размером, чтением `IFormFile` в seekable-поток и пробой содержимого; возвращает `{ stream, extension }` или ошибку.
- Правила вида (макс. байты, валидатор содержимого, выбор расширения) берутся из дескриптора `RecipeFileCatalog` (#80) — единый источник с lifecycle/уборкой.
- `RecipeEndpoints.UploadPhotoAsync`/`UploadDocumentAsync` убирают буферизацию `byte[]` + два `MemoryStream` и мапят исход в `RecipeErrorDto`.

## Checklist

- [x] `RecipeFileUpload` вынесен; дескриптор `RecipeFileCatalog` заполнен макс. байтами и валидатором для обоих видов.
- [x] `RecipeEndpoints` upload-хендлеры без доменной политики; `RecipeMutationService` получает seekable-поток.
- [x] Один seekable-поток на загрузку вместо `byte[]` + двух `MemoryStream`.
- [x] HTTP-контракты и тексты ошибок (`Выберите файл…`, `Размер … не должен превышать …`, `NotPdfError`) не изменились.
- [x] Тесты: размер, неверный формат, битый файл, корректный файл — на фейковом `IFormFile`.
- [x] `dotnet test` зелёный.

## Evidence

_Реализация — ветка `ticket/86-recipe-file-upload`._

- Новый `Recipes/Files/RecipeFileUpload.cs`: `RecipeFileUpload.ReadAsync(catalog, IFormFile?)` владеет размером (против `catalog.MaxBytes`), читает `OpenReadStream()` в **один** `MemoryStream`, прогоняет пробу `catalog.Probe` по содержимому и возвращает `RecipeFileUploadResult { Outcome, Content, Extension, Error }`. Ни `byte[]`, ни второй поток.
- `RecipeFileCatalog` расширен полями `MaxBytes`, `EmptyError`, `TooLargeError`, `Probe` (`RecipeFileContentProbe`). Фото использует `RecipeImageValidator.TryValidate` как есть; документ — обёртку над `RecipePdfValidator.TryValidate` (расширение `.pdf`, ошибка `RecipePdfValidator.NotPdfError`). Валидаторы не переписывались.
- `RecipeEndpoints.UploadPhotoAsync`/`UploadDocumentAsync`: family-check → `RecipeFileUpload.ReadAsync` → маппинг `!Succeeded` в `RecipeErrorDto` (400) → тот же поток в `RecipeMutationService`. Размер/проба/`MemoryStream` из хендлеров ушли. Endpoint'ы отдачи файлов, `ExternalRecipePromotionService` и схема БД не тронуты.
- Тесты `RecipeFileUploadTests` (10, фейковый `IFormFile`): null/пустой файл, превышение размера фото и PDF, не-image/не-PDF, битый PNG, валидные фото и PDF (расширение из содержимого, поток seekable, читается с начала). `RecipePhotoTests`/`RecipeDocumentTests`/`RecipePdfValidatorTests` без изменения смысла.
- Прогон: `docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test` → **673 passed, 0 failed, 62 skipped** (Postgres-suite без `MENU_PLANNER_TEST_POSTGRES`). Фокусный прогон по затронутым файлам — 40 passed, 0 failed.

## Comments
