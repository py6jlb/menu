# 79: PDF-документ рецепта

**What to build:** Пользователь может приложить к рецепту готовый PDF с описанием — когда нет времени расписывать шаги и ингредиенты. Такой рецепт можно смотреть (встроенный PDF-вьюер), он участвует в плане и подборе, но в список покупок ничего не добавляет. Шаги становятся необязательными: метод может быть в описании или в документе.

**Blocked by:** None (can start immediately)

**Status:** in progress (ticket/79-recipe-pdf)

- [ ] Необязательный PDF-документ у рецепта (один, сосуществует с фото): только `application/pdf` по сигнатуре `%PDF-`, до 20 МБ.
- [ ] Шаги необязательны; обязательным остаётся только название (время/порции/сложность — как есть).
- [ ] Встроенный просмотрщик на детальной странице + ссылка «открыть в новой вкладке»; в списках без пометок.
- [ ] Внешний/расшаренный рецепт: документ читается от источника, копируется при промоушене, виден в анонимном просмотре.
- [ ] Отдача анонимная по неугадываемому имени (`/api/documents/{file}`, inline); отдельный `documents`-том и уборка, зеркально фото.
- [ ] Документы включены в backup-набор (архив, manifest, upload/verify/rotation, restore-drill, rollback).
- [ ] Тесты backend/frontend/deploy и документация (ADR, CONTEXT, README).

## Evidence

- Домен/БД: `Recipe.DocumentPath` (`varchar(500)`, nullable) + миграция `20261009131938_AddRecipeDocumentPath`; `RecipeCatalog.DocumentMaxBytes` (20 МБ), `DocumentContentType`.
- Файловый слой (зеркало фото): `Recipes/Documents/` — `IDocumentStore`/`DocumentStorage` (`DOCUMENTS_DIR`), `DocumentFileNames` (managed `.pdf`), `DocumentLifecycle`, `DocumentGarbageCollector`; уборка документов подключена к существующему cleanup-worker. Валидатор `RecipePdfValidator` (сигнатура `%PDF-`).
- API: `PUT/DELETE /api/recipes/{id}/document` (verified, `revision`), анонимный `GET /api/documents/{fileName}` (`application/pdf`, range). `RecipeDto.DocumentUrl`; `RecipeMutationService.UploadDocumentAsync`/`DeleteDocumentAsync`; удаление рецепта ретайрит и фото, и документ.
- Внешние: `ExternalRecipeContentResolver.Materialize` несёт `DocumentPath`; `ExternalRecipePromotionService` копирует документ (relational и tracked пути); анонимный просмотр возвращает `documentUrl` через общий `ToDto`.
- Валидация: убран `steps_required` (шаги необязательны), остальные правила без изменений.
- Frontend: `validateDocumentFile`/`DOCUMENT_*`; `uploadRecipeDocument`/`deleteRecipeDocument`; `useRecipeDraft` несёт состояние `document` (черновик, сериализация, отдельный intent, частичный успех); fieldset «PDF-рецепт» и встроенный вьюер в `RecipeBody`; шаги помечены необязательными.
- Инфраструктура: `DOCUMENTS_DIR` + том `documents_data` в dev/prod compose; `/app/documents` в Dockerfile; `backup.sh`/`backup-lib.sh` (архив, manifest `documentsName`/`documentsSha256`, upload/verify/rotation), `restore-drill.sh` и `rollback.sh` (обратно совместимо: набор без документов пропускается).
- Документация: ADR `docs/adr/0016-recipe-pdf-attachment.md`, термин «Документ рецепта» в `CONTEXT.md`, раздел «PDF-документ рецепта» и env в `README.md`, упоминание в `deploy/README.md`.
- Тесты: backend fast 627 passed / 61 skipped + PostgreSQL-suite 66 passed; frontend 250 passed; deploy-suite 184 passed; `npm run build` — чисто.
