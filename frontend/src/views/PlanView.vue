<script setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { onBeforeRouteLeave, useRoute } from 'vue-router'
import { DAYS, MEALS, mondayOf, weekDays, weekRangeLabel, parseIso, toIso } from '../constants/plan'
import { useWeekDraft } from '../composables/useWeekDraft'
import { useRecipePicker } from '../composables/useRecipePicker'
import ExternalStateBadge from '../components/ExternalStateBadge.vue'
import { SEASONS, DIETS } from '../constants/recipe'
import { externalState } from '../constants/external'
import { repetitionChip, repetitionTitle, repetitionWindowNote } from '../constants/repetition'
import { useDialog } from '../composables/useDialog'
import { useAuth } from '../stores/auth'

const { isEmailVerified } = useAuth()

const route = useRoute()

/** Неделя из адреса (переход из списка покупок): принимаем только валидную дату. */
function weekFromQuery(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null
  const date = parseIso(value)
  return Number.isNaN(date.getTime()) ? null : mondayOf(date)
}

const {
  weekStart: monday,
  draft,
  conflict,
  conflictDiff,
  dirty,
  loading,
  saving,
  loadError: loadingError,
  saveError,
  savedMessage,
  slotEntry,
  setSlot,
  removeSlot: removeDraftSlot,
  loadWeek: load,
  save,
  reloadServerVersion,
  goToWeek,
  confirmNavigation
} = useWeekDraft({ initialWeek: weekFromQuery(route.query.week) || mondayOf(new Date()) })

const editing = ref(null)

const {
  container: pickerDialog,
  open: openDialog,
  close: closeDialog,
  onKeydown: onDialogKeydown
} = useDialog({
  onClose: () => {
    editing.value = null
  }
})

let searchTimer = null
function scheduleSearch(run) {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(run, 250)
}

const {
  recipes,
  loading: pickerLoading,
  error: pickerError,
  search,
  draftFilters: filter,
  hasFilters,
  selection,
  selectionWarning,
  portions: pickerPortions,
  repetitionWindowWeeks: pickerWindowWeeks,
  open: openPickerState,
  select: selectRecipe,
  setSearch,
  apply: applyFilters,
  reset: resetFilters,
  confirm: confirmPicker,
  reload: reloadRecipes
} = useRecipePicker({ schedule: scheduleSearch })

const weekLabel = computed(() => weekRangeLabel(monday.value))
const days = computed(() => weekDays(monday.value))

const mobileDay = ref((new Date().getDay() + 6) % 7)

function toggleInList(list, value) {
  const index = list.indexOf(value)
  if (index >= 0) list.splice(index, 1)
  else list.push(value)
}

function openPicker(day, mealType, event) {
  editing.value = { day, mealType }
  // Повторяемость считаем до выбранной недели включительно, а не до текущей недели сервера.
  openPickerState(slotEntry(day, mealType), toIso(monday.value))
  openDialog(event?.currentTarget)
}

function isSelected(recipe) {
  return selection.value?.recipeId === recipe.id
}

function isSelectionOutsideResults() {
  return Boolean(selection.value) && !recipes.value.some((r) => r.id === selection.value.recipeId)
}

function closePicker() {
  closeDialog()
}

function confirmSlot() {
  const entry = confirmPicker()
  if (!entry) return
  setSlot(editing.value.day, editing.value.mealType, entry)
  closeDialog()
}

function removeSlot(day, mealType) {
  removeDraftSlot(day, mealType)
  if (editing.value && editing.value.day === day && editing.value.mealType === mealType) {
    closePicker()
  }
}

function handleBeforeUnload(event) {
  if (!dirty.value) return
  event.preventDefault()
  event.returnValue = ''
}

function dayHeader(index) {
  return `${DAYS[index].label} ${String(days.value[index].getDate()).padStart(2, '0')}`
}

function mealLabel(code) {
  return MEALS.find((m) => m.code === code)?.label || code
}

function slotLabel(name, portions) {
  if (!name) return 'пусто'
  return portions ? `${name} (${portions} порц.)` : name
}

function slotDayLabel(index) {
  return `${DAYS[index].label} ${String(days.value[index].getDate()).padStart(2, '0')}`
}

function slotButtonLabel(index, mealCode) {
  const entry = slotEntry(index, mealCode)
  const prefix = `${slotDayLabel(index)}, ${mealLabel(mealCode)}`
  if (!entry) return `${prefix}: пусто. Добавить блюдо`
  const state = externalState(entry.state)?.label
  return `${prefix}: ${entry.recipeName}, ${entry.portions} порц.${state ? `, ${state}` : ''}. Изменить`
}

function slotRemoveButtonLabel(index, mealCode) {
  const entry = slotEntry(index, mealCode)
  return `Убрать ${entry?.recipeName || 'блюдо'} из плана: ${slotDayLabel(index)}, ${mealLabel(mealCode)}`
}

onBeforeRouteLeave(() => confirmNavigation())

onMounted(() => {
  window.addEventListener('beforeunload', handleBeforeUnload)
  load()
  reloadRecipes()
})

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
  clearTimeout(searchTimer)
})
</script>

<template>
  <section>
    <div class="page-heading plan-heading">
      <div>
        <h2>План на неделю</h2>
        <span v-if="dirty" class="dirty-badge">● есть изменения</span>
      </div>
      <button
        v-if="isEmailVerified"
        type="button"
        class="btn btn--primary"
        :disabled="saving || loading || !dirty"
        @click="save"
      >
        {{ saving ? 'Сохранение…' : 'Сохранить' }}
      </button>
    </div>

    <p v-if="!isEmailVerified" class="notice">
      Подтвердите почту, чтобы менять план недели.
      <router-link to="/verify">Ввести код</router-link>
    </p>

    <div class="nav">
      <button
        type="button"
        class="btn btn--ghost btn--small"
        aria-label="Предыдущая неделя"
        @click="goToWeek(-1)"
      >
        ←
      </button>
      <span class="week-label">{{ weekLabel }}</span>
      <button
        type="button"
        class="btn btn--ghost btn--small"
        aria-label="Следующая неделя"
        @click="goToWeek(1)"
      >
        →
      </button>
    </div>

    <p v-if="savedMessage" class="success" role="status">{{ savedMessage }}</p>
    <div v-else-if="conflict" class="conflict">
      <p class="error" role="alert">{{ saveError }}</p>
      <p v-if="conflict.revision !== null" class="conflict-server">
        Актуальная версия на сервере: ревизия {{ conflict.revision }}.
      </p>
      <ul v-if="conflictDiff.length" class="conflict-diff">
        <li v-for="item in conflictDiff" :key="`${item.day}:${item.mealType}`">
          <strong>{{ DAYS[item.day].label }} · {{ mealLabel(item.mealType) }}</strong>:
          на сервере — {{ slotLabel(item.serverName, item.serverPortions) }},
          у вас — {{ slotLabel(item.localName, item.localPortions) }}
        </li>
      </ul>
      <p v-else class="conflict-server">Различий по записям нет.</p>
      <button type="button" class="btn btn--ghost btn--small" @click="reloadServerVersion">
        Загрузить актуальную версию
      </button>
    </div>
    <p v-else-if="saveError" class="error" role="alert">{{ saveError }}</p>

    <p v-if="loading" class="loading">Загрузка…</p>
    <p v-else-if="loadingError" class="error" role="alert">
      {{ loadingError }}
      <router-link to="/family">Перейти на страницу «Семья»</router-link>
    </p>

    <template v-else>
      <div v-if="recipes.length === 0" class="card empty-state">
        <div class="empty-icon">🍽️</div>
        <div class="empty-title">В семье пока нет рецептов</div>
        <p class="empty-desc">Добавьте рецепты, чтобы планировать неделю.</p>
        <router-link v-if="isEmailVerified" to="/recipes/new" class="btn btn--primary">Добавить рецепт</router-link>
      </div>

      <template v-else>
        <div class="desktop-grid">
          <div class="corner">Приём пищи</div>
          <div v-for="(day, index) in days" :key="`head-${day.getTime()}`" class="day-head">
            {{ dayHeader(index) }}
          </div>

          <template v-for="meal in MEALS" :key="meal.code">
            <div class="meal-label">{{ meal.label }}</div>
            <div
              v-for="(day, index) in days"
              :key="`${meal.code}-${day.getTime()}`"
              class="slot-wrap"
            >
              <button
                type="button"
                class="slot"
                :class="{ filled: slotEntry(index, meal.code), readonly: !isEmailVerified }"
                :disabled="!isEmailVerified"
                :aria-label="slotButtonLabel(index, meal.code)"
                @click="openPicker(index, meal.code, $event)"
              >
                <template v-if="slotEntry(index, meal.code)">
                  <span class="slot-recipe">{{ slotEntry(index, meal.code).recipeName }}</span>
                  <span class="slot-portions">{{ slotEntry(index, meal.code).portions }} порц.</span>
                  <ExternalStateBadge
                    class="slot-state"
                    variant="slot"
                    :state="slotEntry(index, meal.code).state"
                  />
                </template>
                <span v-else class="slot-empty">+</span>
              </button>
              <button
                v-if="isEmailVerified && slotEntry(index, meal.code)"
                type="button"
                class="slot-remove"
                :aria-label="slotRemoveButtonLabel(index, meal.code)"
                @click="removeSlot(index, meal.code)"
              >
                ✕
              </button>
            </div>
          </template>
        </div>

        <div class="mobile-view">
          <div class="day-tabs">
            <button
              v-for="(day, index) in days"
              :key="`tab-${day.getTime()}`"
              type="button"
              class="day-tab"
              :class="{ active: mobileDay === index }"
              :aria-current="mobileDay === index ? 'true' : undefined"
              @click="mobileDay = index"
            >
              {{ DAYS[index].label }}
              <span class="day-tab-date">{{ String(day.getDate()).padStart(2, '0') }}</span>
            </button>
          </div>

          <div class="mobile-day-list">
            <div
              v-for="meal in MEALS"
              :key="`${mobileDay}-${meal.code}`"
              class="mobile-slot-wrap"
            >
              <button
                type="button"
                class="mobile-slot"
                :class="{ filled: slotEntry(mobileDay, meal.code), readonly: !isEmailVerified }"
                :disabled="!isEmailVerified"
                :aria-label="slotButtonLabel(mobileDay, meal.code)"
                @click="openPicker(mobileDay, meal.code, $event)"
              >
                <span class="mobile-meal">{{ meal.label }}</span>
                <template v-if="slotEntry(mobileDay, meal.code)">
                  <span class="mobile-recipe">{{ slotEntry(mobileDay, meal.code).recipeName }}</span>
                  <ExternalStateBadge
                    class="slot-state"
                    variant="mobile"
                    :state="slotEntry(mobileDay, meal.code).state"
                  />
                  <span class="mobile-meta">
                    {{ slotEntry(mobileDay, meal.code).portions }} порц.
                  </span>
                </template>
                <span v-else class="mobile-empty">+ Добавить</span>
              </button>
              <button
                v-if="isEmailVerified && slotEntry(mobileDay, meal.code)"
                type="button"
                class="mobile-remove"
                :aria-label="slotRemoveButtonLabel(mobileDay, meal.code)"
                @click="removeSlot(mobileDay, meal.code)"
              >
                ✕
              </button>
            </div>
          </div>
        </div>
      </template>
    </template>

    <div v-if="editing" class="overlay" @click.self="closePicker">
      <div
        ref="pickerDialog"
        class="picker"
        role="dialog"
        aria-modal="true"
        aria-labelledby="plan-picker-title"
        tabindex="-1"
        @keydown="onDialogKeydown"
      >
        <h3 id="plan-picker-title">
          {{ mealLabel(editing.mealType) }} ·
          {{ DAYS.find((d) => d.code === editing.day)?.label }}
        </h3>

        <label class="field">
          <span>Поиск рецепта</span>
          <input
            :value="search"
            type="text"
            placeholder="Поиск рецепта…"
            @input="setSearch($event.target.value)"
          />
        </label>

        <details class="filter-panel">
          <summary>Фильтры и предпочтения</summary>

          <div class="filter-row">
            <label class="field">
              <span>Макс. сложность</span>
              <select v-model.number="filter.maxDifficulty">
                <option :value="null">Любая</option>
                <option v-for="level in 5" :key="level" :value="level">{{ level }}</option>
              </select>
            </label>
            <label class="field">
              <span>Макс. калории</span>
              <input v-model.number="filter.maxCalories" type="number" min="0" placeholder="ккал" />
            </label>
            <label class="field">
              <span>Макс. время (мин)</span>
              <input v-model.number="filter.maxCookTime" type="number" min="1" placeholder="мин" />
            </label>
          </div>

          <div class="filter-group">
            <span class="filter-label">Сезон (жёсткий):</span>
            <label v-for="season in SEASONS" :key="season.code" class="chip">
              <input class="sr-only" type="checkbox" :value="season.code" :checked="filter.seasons.includes(season.code)" @change="toggleInList(filter.seasons, season.code)" />
              {{ season.label }}
            </label>
          </div>

          <div class="filter-group">
            <span class="filter-label">Диета (жёсткий):</span>
            <label v-for="diet in DIETS" :key="diet.code" class="chip">
              <input class="sr-only" type="checkbox" :value="diet.code" :checked="filter.diets.includes(diet.code)" @change="toggleInList(filter.diets, diet.code)" />
              {{ diet.label }}
            </label>
          </div>

          <div class="filter-row">
            <label class="field">
              <span>Ингредиент</span>
              <input v-model="filter.ingredient" type="text" placeholder="напр. лук" />
            </label>
            <label class="field">
              <span>Тег</span>
              <input v-model="filter.tag" type="text" placeholder="напр. быстро" />
            </label>
          </div>

          <div class="filter-group">
            <span class="filter-label">Предпочтения (сорт.):</span>
            <label class="chip">
              <input class="sr-only" type="checkbox" v-model="filter.preferLowCalories" />
              Низкокалорийные
            </label>
            <label class="chip">
              <input class="sr-only" type="checkbox" v-model="filter.preferLowComplexity" />
              Простые
            </label>
          </div>

          <div class="filter-group">
            <span class="filter-label">Предпочт. сезоны:</span>
            <label v-for="season in SEASONS" :key="season.code" class="chip">
              <input class="sr-only" type="checkbox" :value="season.code" :checked="filter.preferSeasons.includes(season.code)" @change="toggleInList(filter.preferSeasons, season.code)" />
              {{ season.label }}
            </label>
          </div>

          <div class="filter-group">
            <span class="filter-label">Предпочт. диеты:</span>
            <label v-for="diet in DIETS" :key="diet.code" class="chip">
              <input class="sr-only" type="checkbox" :value="diet.code" :checked="filter.preferDiets.includes(diet.code)" @change="toggleInList(filter.preferDiets, diet.code)" />
              {{ diet.label }}
            </label>
          </div>

          <div class="filter-actions">
            <button type="button" class="btn btn--primary" @click="applyFilters">Применить</button>
            <button
              type="button"
              class="btn btn--ghost"
              :disabled="!hasFilters && !search"
              @click="resetFilters"
            >
              Сбросить
            </button>
          </div>
        </details>

        <p v-if="pickerLoading" class="picker-loading">Загрузка…</p>
        <p v-else-if="pickerError" class="error" role="alert">{{ pickerError }}</p>

        <ul v-if="recipes.length" class="recipe-options" aria-label="Результаты подбора">
          <li v-for="recipe in recipes" :key="recipe.id" class="recipe-option-item">
            <button
              type="button"
              class="recipe-option"
              :class="{ selected: isSelected(recipe) }"
              :aria-pressed="isSelected(recipe)"
              @click="selectRecipe(recipe)"
            >
              <span class="recipe-name">
                {{ recipe.name }}
                <ExternalStateBadge v-if="recipe.state" :state="recipe.state" />
              </span>
              <span v-if="recipe.isExternal" class="recipe-origin">
                внешний · из семьи {{ recipe.sourceFamilyName }}
              </span>
              <span class="recipe-meta">
                <template v-if="recipe.difficulty">Сл.: {{ recipe.difficulty }}</template>
                <template v-if="recipe.calories !== null && recipe.calories !== undefined"> · {{ recipe.calories }} ккал</template>
                <template v-if="recipe.cookTimeMinutes"> · {{ recipe.cookTimeMinutes }} мин</template>
                <span v-if="recipe.repetitionCount > 0" class="repetition" :title="repetitionTitle()">
                  {{ repetitionChip(recipe.repetitionCount) }}
                </span>
              </span>
            </button>
          </li>
        </ul>
        <p v-else class="no-results" role="status">Ничего не найдено.</p>

        <p v-if="recipes.length && pickerWindowWeeks > 0" class="repetition-note">
          {{ repetitionWindowNote(pickerWindowWeeks) }}
        </p>

        <p v-if="isSelectionOutsideResults()" class="selection-kept" role="status">
          Выбрано: <strong>{{ selection.name }}</strong> — вне текущей выдачи.
        </p>

        <label class="portions field">
          <span>Порции</span>
          <input v-model.number="pickerPortions" type="number" min="1" max="100" />
        </label>

        <p v-if="selectionWarning" class="selection-warning" role="alert">{{ selectionWarning }}</p>

        <div class="picker-actions">
          <button type="button" class="btn btn--primary" :disabled="!selection || pickerPortions < 1" @click="confirmSlot">
            Назначить
          </button>
          <button
            v-if="slotEntry(editing.day, editing.mealType)"
            type="button"
            class="btn btn--danger"
            @click="removeSlot(editing.day, editing.mealType)"
          >
            Убрать из плана
          </button>
          <button type="button" class="btn btn--ghost" @click="closePicker">Отмена</button>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.plan-heading {
  align-items: flex-end;
}

@media (max-width: 899px) {
  .plan-heading {
    position: sticky;
    top: 56px;
    z-index: 30;
    background: var(--bg);
    padding: 0.75rem 0.5rem;
    margin: -0.75rem -0.5rem 0;
    border-bottom: 1px solid var(--border);
  }

  .plan-heading h2 {
    font-size: 1.25rem;
  }
}

.dirty-badge {
  font-size: 0.8rem;
  color: var(--warning);
  font-weight: 700;
  display: inline-flex;
  align-items: center;
}

.conflict {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.5rem;
  margin: 0 0 1rem;
}

.conflict .error {
  margin: 0;
}

.conflict-server {
  margin: 0;
  color: var(--text-soft);
  font-size: 0.9rem;
}

.conflict-diff {
  margin: 0;
  padding-left: 1.1rem;
  color: var(--text-soft);
  font-size: 0.9rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.nav {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  margin: 0 0 1rem;
}

.week-label {
  font-weight: 700;
  font-size: 0.95rem;
  text-align: center;
  flex: 1;
}

.desktop-grid {
  display: none;
}

.mobile-view {
  display: block;
}

@media (min-width: 900px) {
  .desktop-grid {
    display: grid;
    grid-template-columns: 110px repeat(7, minmax(92px, 1fr));
    gap: 6px;
  }

  .mobile-view {
    display: none;
  }
}

.corner,
.day-head {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--text-soft);
  padding: 0.35rem 0.2rem;
  text-align: center;
}

.meal-label {
  display: flex;
  align-items: center;
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--text-soft);
}

.slot-wrap {
  position: relative;
  display: flex;
}

.slot {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  align-items: flex-start;
  min-height: 64px;
  padding: 0.45rem;
  border: 1px dashed var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  font-size: 0.85rem;
  text-align: left;
  width: 100%;
  transition: border-color 0.15s ease, background 0.15s ease;
}

.slot:hover {
  border-color: var(--primary);
}

.slot.readonly,
.mobile-slot.readonly {
  cursor: default;
}

.slot.readonly:hover {
  border-color: var(--border);
}

.slot.readonly.filled:hover {
  border-color: var(--success);
}

.slot.filled {
  border-style: solid;
  border-color: var(--success);
  background: var(--success-bg);
}

.slot-empty {
  margin: auto;
  color: var(--text-faint);
  font-size: 1.1rem;
}

.slot-recipe {
  font-weight: 700;
  color: var(--text);
  padding-right: 1.1rem;
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.slot-portions {
  color: var(--success);
  font-size: 0.8rem;
}

.slot-state {
  border-radius: 999px;
  padding: 0.05rem 0.45rem;
  font-size: 0.7rem;
  font-weight: 700;
  white-space: nowrap;
}

.slot-remove {
  position: absolute;
  top: 0.15rem;
  right: 0.2rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  border: none;
  border-radius: 50%;
  background: var(--surface);
  color: var(--danger);
  font-weight: 700;
  line-height: 1;
}

.slot-remove:hover {
  background: var(--danger-bg);
}

/* mobile day tabs */
.day-tabs {
  display: flex;
  gap: 0.4rem;
  overflow-x: auto;
  padding-bottom: 0.5rem;
  margin-bottom: 0.75rem;
  -webkit-overflow-scrolling: touch;
  scrollbar-width: thin;
}

.day-tab {
  flex: 0 0 auto;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.1rem;
  min-height: 52px;
  min-width: 56px;
  padding: 0.35rem 0.6rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text-soft);
  font-weight: 700;
  font-size: 0.9rem;
}

.day-tab .day-tab-date {
  font-size: 0.75rem;
  color: var(--text-faint);
  font-weight: 600;
}

.day-tab.active {
  background: var(--primary);
  border-color: var(--primary);
  color: var(--on-primary);
}

.day-tab.active .day-tab-date {
  color: rgba(255, 255, 255, 0.85);
}

.mobile-day-list {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

.mobile-slot-wrap {
  position: relative;
}

.mobile-slot {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 0.25rem;
  text-align: left;
  padding: 0.8rem 0.9rem;
  border: 1px dashed var(--border);
  border-radius: var(--radius);
  background: var(--surface);
  font-size: 0.95rem;
  min-height: 64px;
  width: 100%;
}

.mobile-slot.filled {
  border-style: solid;
  border-color: var(--success);
  background: var(--success-bg);
}

.mobile-meal {
  font-size: 0.78rem;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-faint);
}

.mobile-slot.filled .mobile-meal {
  color: var(--success);
}

.mobile-recipe {
  font-weight: 800;
  color: var(--text);
  font-size: 1rem;
}

.mobile-meta {
  display: flex;
  align-items: center;
  color: var(--success);
  font-size: 0.85rem;
  font-weight: 600;
}

.mobile-remove {
  position: absolute;
  top: 0.4rem;
  right: 0.4rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  padding: 0;
  border: none;
  border-radius: 50%;
  background: var(--surface);
  color: var(--danger);
  font-weight: 800;
  font-size: 1rem;
  line-height: 1;
}

.mobile-remove:hover {
  background: var(--danger-bg);
}

.mobile-empty {
  color: var(--text-faint);
  font-weight: 600;
}

/* picker overlay */
.overlay {
  position: fixed;
  inset: 0;
  background: rgba(59, 51, 43, 0.45);
  display: flex;
  align-items: flex-end;
  justify-content: center;
  z-index: 50;
}

@media (min-width: 900px) {
  .overlay {
    align-items: center;
  }
}

.picker {
  background: var(--surface);
  border-radius: var(--radius) var(--radius) 0 0;
  padding: 1.25rem;
  width: 100%;
  max-width: 92vw;
  max-height: 88vh;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  box-shadow: var(--shadow-md);
  animation: sheetUp 0.2s ease;
}

@keyframes sheetUp {
  from {
    transform: translateY(30px);
    opacity: 0.6;
  }
  to {
    transform: translateY(0);
    opacity: 1;
  }
}

@media (min-width: 900px) {
  .picker {
    width: 480px;
    border-radius: var(--radius);
  }
}

.recipe-options {
  list-style: none;
  margin: 0;
  padding: 0;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  max-height: 260px;
  overflow-y: auto;
  background: var(--surface);
}

.recipe-options li {
  display: block;
}

.recipe-option {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  width: 100%;
  padding: 0.6rem 0.75rem;
  border: none;
  border-bottom: 1px solid var(--border);
  background: var(--surface);
  font-family: inherit;
  font-size: 1rem;
  color: var(--text);
  text-align: left;
  cursor: pointer;
}

.recipe-options li:last-child .recipe-option {
  border-bottom: none;
}

.recipe-option.selected {
  background: var(--primary-soft);
}

.recipe-name {
  font-weight: 700;
}

.recipe-meta {
  font-size: 0.8rem;
  color: var(--text-soft);
}

.recipe-origin {
  font-size: 0.78rem;
  color: var(--text-faint);
}

.selection-kept {
  margin: 0;
  font-size: 0.85rem;
  color: var(--text-soft);
}

.selection-warning {
  margin: 0;
  padding: 0.5rem 0.65rem;
  border-radius: var(--radius-sm);
  background: var(--warning-bg);
  color: var(--warning);
  font-size: 0.85rem;
  font-weight: 600;
}

.repetition {
  background: var(--warning-bg);
  color: var(--warning);
  border-radius: 999px;
  padding: 0.05rem 0.5rem;
  font-size: 0.8rem;
  font-weight: 700;
  margin-left: 0.4rem;
}

.no-results {
  margin: 0;
  padding: 0.6rem 0.75rem;
  color: var(--text-soft);
}

.repetition-note {
  margin: 0;
  padding: 0 0.75rem;
  color: var(--text-soft);
  font-size: 0.82rem;
}

.portions input {
  max-width: 110px;
}

.picker-actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  position: sticky;
  bottom: 0;
  background: var(--surface);
  padding-top: 0.5rem;
}

.filter-panel {
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  padding: 0.5rem 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

.filter-panel summary {
  cursor: pointer;
  font-weight: 700;
  font-size: 0.9rem;
}

.filter-row {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0.6rem;
}

@media (min-width: 480px) {
  .filter-row {
    grid-template-columns: repeat(3, 1fr);
  }
}

.filter-row .field input,
.filter-row .field select {
  width: 100%;
}

.filter-group {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.4rem;
  font-size: 0.85rem;
}

.filter-label {
  font-size: 0.8rem;
  color: var(--text-soft);
  font-weight: 600;
}

.filter-group .chip {
  position: relative;
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  font-weight: 600;
}

.filter-group .chip:has(input:focus-visible) {
  outline: 2px solid var(--primary);
  outline-offset: 2px;
}

.filter-actions {
  display: flex;
  gap: 0.5rem;
}

.picker-loading {
  color: var(--text-soft);
  font-size: 0.9rem;
  margin: 0;
}
</style>
