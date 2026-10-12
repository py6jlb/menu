# 86: Политика загрузки файла рецепта — в RecipeFileUpload

**What to build:** Размер, чтение в seekable-поток и проба содержимого файла (фото/PDF) переезжают из `RecipeEndpoints.UploadPhotoAsync`/`UploadDocumentAsync` в module; endpoint мапит исход в HTTP и передаёт поток дальше, без `byte[]` и двух `MemoryStream`.

**Blocked by:** 80 — Единый модуль файлов рецепта (дескриптор вида и `IRecipeFileStore`).

**Status:** ready-for-agent

## Design (из grilling)

- `Recipes/Files/RecipeFileUpload.cs`: владеет размером, чтением `IFormFile` в seekable-поток и пробой содержимого; возвращает `{ stream, extension }` или ошибку.
- Правила вида (макс. байты, валидатор содержимого, выбор расширения) берутся из дескриптора `RecipeFileCatalog` (#80) — единый источник с lifecycle/уборкой.
- `RecipeEndpoints.UploadPhotoAsync`/`UploadDocumentAsync` убирают буферизацию `byte[]` + два `MemoryStream` и мапят исход в `RecipeErrorDto`.

## Checklist

- [ ] `RecipeFileUpload` вынесен; дескриптор `RecipeFileCatalog` заполнен макс. байтами и валидатором для обоих видов.
- [ ] `RecipeEndpoints` upload-хендлеры без доменной политики; `RecipeMutationService` получает seekable-поток.
- [ ] Один seekable-поток на загрузку вместо `byte[]` + двух `MemoryStream`.
- [ ] HTTP-контракты и тексты ошибок (`Выберите файл…`, `Размер … не должен превышать …`, `NotPdfError`) не изменились.
- [ ] Тесты: размер, неверный формат, битый файл, корректный файл — на фейковом `IFormFile`.
- [ ] `dotnet test` зелёный.

## Evidence

_(заполняется при реализации)_

## Comments
