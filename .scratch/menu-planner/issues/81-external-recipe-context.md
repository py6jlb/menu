# 81: Единое разрешение внешнего рецепта (состояние и контент)

**What to build:** Состояние, имя семьи-источника, обновление кэша имени и живой контент внешнего рецепта разрешает один module `ExternalRecipeContext`; `RecipeReader` и `WeekPlanReader` перестают каждый собирать свою оркестрацию, правило `broken` становится единым и race-честным (ADR-0005), а перенос полей источника перестаёт дублироваться.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

## Design (из grilling)

- Один module `ExternalRecipeContext` (`Recipes/External/`) с входом `ResolveAsync(links, projection)` (проекция `Summary` / `Ingredients` / `Full`), возвращающий по `wrapperId`: `State`, `SourceAlive`, `SourceFamilyName`, `Recipe? Content` (материализованный).
- Модуль владеет: выбором загрузчика по проекции, обновлением кэша имени, **единым правилом состояния**, разрешением имён семей. Внутри композирует `ExternalRecipeStateResolver`, `ExternalRecipeSourceLoader`, `ExternalRecipeNameCache`, `SourceFamilyNameResolver` (их не удаляем — внутренние collaborator'ы).
- **Единое правило состояния:** `SourceAlive` — из фактической загрузки; `Warning` — токен не совпал или шеринг отозван; иначе `Ok`. Источник, исчезнувший между разрешением и загрузкой, даёт `broken` (приведение к ADR-0005).
- `ExternalRecipeContentResolver` расширяется `Project(wrapper, RecipeSourceSummary)` (проекция `Summary`) и общей `CopySourceContent(target, source)`; `Materialize` строится через неё. Промоушен зовёт `CopySourceContent`, но сохраняет свой однократный claim, транзакцию и файловые эффекты.
- `ExternalPlanContent` и `RecipeReader.ExternalLinkInfo` сворачиваются в один тип связи (с `CachedName` для детекции устаревшего имени).
- Автодополнение ингредиентов берёт живые источники через module (единое правило `broken`); SQL-агрегация и подписи не меняются.
- Публичные records/методы `RecipeReader`/`WeekPlanReader` и HTTP-контракты **не меняются**; меняется только исход в гонке.

## Checklist

- [ ] ADR-0005 дополнен уточнением про первичный сигнал `broken` (сделано).
- [ ] `ExternalRecipeContext` с `ResolveAsync(links, projection)` и `ResolvedExternalRecipe`; `ExternalRecipeProjection` (`Summary`/`Ingredients`/`Full`).
- [ ] Один тип связи (вместо `ExternalPlanContent` + `ExternalLinkInfo`), с `CachedName`.
- [ ] `RecipeReader` (summary → `Summary`, match/detail → `Full`) и `WeekPlanReader` (карточка → `Summary`, покупки → `Ingredients`) ходят через module; приватная `ResolveExternalContextAsync` удалена.
- [ ] `ExternalRecipeContentResolver.Project`/`CopySourceContent`; `Materialize` и `ApplyCopiedContent` больше не дублируют набор полей.
- [ ] Автодополнение отбирает живые источники через module; `ResolveLiveSourceIdsAsync` удалён.
- [ ] Поведение вне гонки и HTTP-контракты не изменились; схема БД не меняется.
- [ ] Регрессии: (1) источник удалён между разрешением состояния и загрузкой контента ⇒ `broken` и без устаревшего контента; (2) карточка плана и деталь рецепта согласованы по состоянию одной ссылки.
- [ ] Прогоны: `docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test`; `scripts/test-postgres.sh` — зелёные.

## Evidence

_(заполняется при реализации)_

## Comments
