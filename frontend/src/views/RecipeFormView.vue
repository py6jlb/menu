<script setup>
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter, onBeforeRouteLeave, onBeforeRouteUpdate } from 'vue-router'
import {
  UNITS,
  SEASONS,
  DIETS,
  PHOTO_ACCEPT,
  PHOTO_MAX_LABEL,
  PHOTO_TYPES_LABEL,
  validatePhotoFile
} from '../constants/recipe'
import { useRecipeDraft, newIngredientDraft } from '../composables/useRecipeDraft'
import { useIngredientAutocomplete } from '../composables/useIngredientAutocomplete'

const route = useRoute()
const router = useRouter()

const {
  editingId,
  isEdit,
  draft,
  dirty,
  loading,
  saving,
  conflictMessage,
  conflictRevision,
  photoSaving,
  photoPartial,
  photoUnknown,
  photoError,
  loadError,
  saveError,
  saveErrorField,
  setIdentity,
  load,
  save,
  retryPhoto,
  validate,
  confirmNavigation
} = useRecipeDraft({ initialId: route.params.id || null })

const autocomplete = useIngredientAutocomplete()

const error = ref('')
const photoValidationError = ref('')
const visibleError = computed(() => error.value || saveError.value)
const fieldErrorLabel = computed(() => fieldLabel(saveErrorField.value))

const FIELD_LABELS = {
  name: 'Название',
  description: 'Описание',
  cookTimeMinutes: 'Время приготовления',
  servings: 'Порции',
  difficulty: 'Сложность',
  calories: 'Калорийность',
  steps: 'Шаги',
  tags: 'Теги',
  seasonality: 'Сезонность',
  diet: 'Диеты',
  ingredients: 'Ингредиенты'
}

function fieldLabel(field) {
  if (!field) return ''
  const match = field.match(/^(ingredients|entries)\[(\d+)\](?:\.(\w+))?$/)
  if (match) {
    const base = match[1] === 'ingredients' ? 'Ингредиент' : 'Запись плана'
    return `${base} ${Number(match[2]) + 1}${match[3] ? `.${match[3]}` : ''}`
  }
  return FIELD_LABELS[field] || field
}

function ingredientState(ing) {
  return autocomplete.stateFor(ing.uid)
}

const selectedPhotoPreview = ref('')

watch(
  () => draft.value.photo.selected,
  (file) => {
    if (selectedPhotoPreview.value) URL.revokeObjectURL(selectedPhotoPreview.value)
    selectedPhotoPreview.value = file ? URL.createObjectURL(file) : ''
  }
)

const photoActionLabel = computed(() =>
  draft.value.photo.selected || (draft.value.photo.existing && !draft.value.photo.removed)
    ? 'Заменить фото'
    : 'Выбрать фото'
)

function onPhotoSelected(event) {
  const file = event.target.files?.[0]
  if (!file) return
  const validationError = validatePhotoFile(file)
  if (validationError) {
    photoValidationError.value = validationError
    event.target.value = ''
    return
  }
  photoValidationError.value = ''
  draft.value.photo.selected = file
  draft.value.photo.removed = false
  event.target.value = ''
}

function clearSelectedPhoto() {
  draft.value.photo.selected = null
  photoValidationError.value = ''
}

function removeExistingPhoto() {
  draft.value.photo.selected = null
  draft.value.photo.removed = true
}

function undoPhotoRemoval() {
  draft.value.photo.removed = false
}

function addStep() {
  draft.value.steps.push('')
}

function removeStep(index) {
  draft.value.steps.splice(index, 1)
}

function moveStep(index, delta) {
  const target = index + delta
  if (target < 0 || target >= draft.value.steps.length) return
  const tmp = draft.value.steps[index]
  draft.value.steps[index] = draft.value.steps[target]
  draft.value.steps[target] = tmp
}

function addIngredient() {
  draft.value.ingredients.push(newIngredientDraft())
}

function removeIngredient(index) {
  const ing = draft.value.ingredients[index]
  if (ing) autocomplete.release(ing.uid)
  draft.value.ingredients.splice(index, 1)
}

function moveIngredient(index, delta) {
  const target = index + delta
  if (target < 0 || target >= draft.value.ingredients.length) return
  const tmp = draft.value.ingredients[index]
  draft.value.ingredients[index] = draft.value.ingredients[target]
  draft.value.ingredients[target] = tmp
}

function closeSuggestions(ing) {
  autocomplete.close(ing.uid)
}

function onIngredientInput(ing) {
  autocomplete.input(ing.uid, ing.name)
}

function selectSuggestion(ing, suggestion) {
  ing.name = suggestion
  autocomplete.close(ing.uid)
}

function onSuggestionKeydown(ing, event) {
  const state = autocomplete.stateFor(ing.uid)
  if (!state.showSuggestions || state.suggestions.length === 0) return
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    autocomplete.moveActive(ing.uid, 1)
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    autocomplete.moveActive(ing.uid, -1)
  } else if (event.key === 'Enter') {
    const pick = autocomplete.activeSuggestion(ing.uid)
    if (pick) {
      event.preventDefault()
      selectSuggestion(ing, pick)
    }
  } else if (event.key === 'Escape') {
    event.preventDefault()
    closeSuggestions(ing)
  }
}

function toggleSeason(code) {
  const index = draft.value.seasonality.indexOf(code)
  if (index >= 0) draft.value.seasonality.splice(index, 1)
  else draft.value.seasonality.push(code)
}

function toggleDiet(code) {
  const index = draft.value.diets.indexOf(code)
  if (index >= 0) draft.value.diets.splice(index, 1)
  else draft.value.diets.push(code)
}

async function submit() {
  error.value = validate()
  if (error.value) return

  // save() сам фиксирует id и действие с фото до первого await; при частичном
  // успехе (текст сохранён, фото — нет) он возвращает textSaved и мы остаёмся
  // на форме, чтобы повторить только фото.
  const result = await save()
  if (result.ok) router.push(`/recipes/${result.id}`)
}

async function onRetryPhoto() {
  const result = await retryPhoto()
  if (result.ok) router.push(`/recipes/${editingId.value}`)
}

function reloadLatest() {
  if (!window.confirm('Загрузить актуальную версию? Локальные изменения будут потеряны.')) return
  load()
}

function handleBeforeUnload(event) {
  if (!dirty.value) return
  event.preventDefault()
  event.returnValue = ''
}

watch(
  () => route.params.id || null,
  (id) => {
    if (id === editingId.value) return
    setIdentity(id)
    error.value = ''
    autocomplete.reset()
    load()
  }
)

// Черновик заменяется при загрузке и сохранении: строки получают новую identity,
// поэтому прежние таймеры и поздние ответы автодополнения обесцениваются.
watch(
  () => draft.value.ingredients,
  () => autocomplete.reset()
)

onBeforeRouteLeave(() => confirmNavigation())

onBeforeRouteUpdate((to, from) => {
  if ((to.params.id || null) === (from.params.id || null)) return true
  return confirmNavigation()
})

onMounted(() => {
  window.addEventListener('beforeunload', handleBeforeUnload)
  if (isEdit.value) load()
})

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
  autocomplete.reset()
  if (selectedPhotoPreview.value) URL.revokeObjectURL(selectedPhotoPreview.value)
})
</script>

<template>
  <section>
    <div class="page-heading">
      <h2>{{ isEdit ? 'Редактирование рецепта' : 'Новый рецепт' }}</h2>
      <span v-if="photoPartial" class="dirty-badge">● текст сохранён, фото — нет</span>
      <span v-else-if="dirty" class="dirty-badge">● есть изменения</span>
    </div>

    <p v-if="loading" class="loading">Загрузка…</p>

    <div v-else-if="loadError" class="card empty-state">
      <div class="empty-icon">🍽️</div>
      <p class="empty-title">{{ loadError }}</p>
      <div class="actions">
        <button type="button" class="btn btn--primary" @click="load()">Повторить</button>
        <router-link :to="isEdit ? `/recipes/${editingId}` : '/recipes'" class="btn btn--ghost"
          >Отмена</router-link
        >
      </div>
    </div>

    <form v-else @submit.prevent="submit" class="recipe-form">
      <div class="card form-section">
        <label class="field">
          <span>Название *</span>
          <input v-model="draft.name" type="text" maxlength="200" required />
        </label>

        <label class="field">
          <span>Описание</span>
          <textarea v-model="draft.description" rows="3" maxlength="2000"></textarea>
        </label>

        <div class="row">
          <label class="field">
            <span>Время (мин) *</span>
            <input v-model.number="draft.cookTimeMinutes" type="number" min="1" max="1440" required />
          </label>
          <label class="field">
            <span>Порции *</span>
            <input v-model.number="draft.servings" type="number" min="1" max="100" required />
          </label>
          <label class="field">
            <span>Сложность *</span>
            <select v-model.number="draft.difficulty" required>
              <option v-for="level in 5" :key="level" :value="level">{{ level }}</option>
            </select>
          </label>
        </div>

        <label class="field">
          <span>Калорийность на порцию (ккал)</span>
          <input v-model.number="draft.calories" type="number" min="0" max="10000" />
        </label>
      </div>

      <fieldset class="card fieldset">
        <legend>Фото</legend>
        <div v-if="draft.photo.selected" class="photo-preview">
          <img :src="selectedPhotoPreview" alt="Предпросмотр фото" class="photo-thumb" />
          <div class="photo-actions">
            <span class="hint">Новое фото будет загружено после сохранения.</span>
            <button type="button" class="btn btn--ghost" @click="clearSelectedPhoto">Убрать</button>
          </div>
        </div>
        <div v-else-if="draft.photo.existing && !draft.photo.removed" class="photo-preview">
          <img :src="draft.photo.existing" alt="Фото рецепта" class="photo-thumb" />
          <div class="photo-actions">
            <button type="button" class="btn btn--ghost text-danger" @click="removeExistingPhoto">
              Удалить фото
            </button>
          </div>
        </div>
        <p v-else-if="draft.photo.removed" class="hint">
          Фото будет удалено после сохранения.
          <button type="button" class="btn btn--subtle" @click="undoPhotoRemoval">Отменить удаление</button>
        </p>
        <label class="file-label btn btn--ghost">
          {{ photoActionLabel }}
          <input type="file" :accept="PHOTO_ACCEPT" class="file-input" @change="onPhotoSelected" />
        </label>
        <p class="hint">
          Допустимы {{ PHOTO_TYPES_LABEL }}, размер — до {{ PHOTO_MAX_LABEL }}. Проверка на сервере
          остаётся окончательной.
        </p>
        <p v-if="photoValidationError" class="error">{{ photoValidationError }}</p>
      </fieldset>

      <div class="card form-section">
        <label class="field">
          <span>Теги (через запятую)</span>
          <input v-model="draft.tagsText" type="text" placeholder="суп, первое, быстрый" />
        </label>

        <fieldset class="fieldset-inline">
          <legend>Сезонность</legend>
          <div class="chips">
            <label v-for="season in SEASONS" :key="season.code" class="chip">
              <input
                type="checkbox"
                :value="season.code"
                :checked="draft.seasonality.includes(season.code)"
                @change="toggleSeason(season.code)"
              />
              {{ season.label }}
            </label>
          </div>
        </fieldset>

        <fieldset class="fieldset-inline">
          <legend>Стандартные диеты</legend>
          <div class="chips">
            <label v-for="diet in DIETS" :key="diet.code" class="chip">
              <input
                type="checkbox"
                :value="diet.code"
                :checked="draft.diets.includes(diet.code)"
                @change="toggleDiet(diet.code)"
              />
              {{ diet.label }}
            </label>
          </div>
        </fieldset>

        <label class="field">
          <span>Свои метки диеты (через запятую)</span>
          <input v-model="draft.dietText" type="text" placeholder="напр. без лактозы" />
        </label>
      </div>

      <fieldset class="card fieldset">
        <legend>Ингредиенты</legend>
        <p class="hint">Количество и единица измерения указываются вместе. Примечание (например, «по вкусу») не влияет на расчёт.</p>
        <div v-for="(ing, index) in draft.ingredients" :key="ing.uid" class="ingredient-row">
          <div class="ing-name-wrap">
            <input
              v-model="ing.name"
              type="text"
              placeholder="Название"
              class="ing-name"
              @input="onIngredientInput(ing)"
              @keydown="onSuggestionKeydown(ing, $event)"
              @blur="closeSuggestions(ing)"
            />
            <ul v-if="ingredientState(ing).showSuggestions" class="suggestions">
              <li
                v-for="(suggestion, sIndex) in ingredientState(ing).suggestions"
                :key="suggestion"
                :class="{ active: sIndex === ingredientState(ing).activeIndex }"
                @mousedown.prevent="selectSuggestion(ing, suggestion)"
              >
                {{ suggestion }}
              </li>
              <li v-if="ingredientState(ing).noSuggestions" class="no-suggestions">Нет подсказок</li>
            </ul>
          </div>
          <input v-model.number="ing.amount" type="number" min="0" step="0.01" placeholder="Кол-во" class="ing-amount" />
          <select v-model="ing.unit" class="ing-unit">
            <option v-for="unit in UNITS" :key="unit.code" :value="unit.code">{{ unit.label }}</option>
          </select>
          <input v-model="ing.note" type="text" placeholder="Примечание" class="ing-note" />
          <div class="row-actions">
            <button type="button" class="btn btn--subtle icon-btn" @click="moveIngredient(index, -1)" :disabled="index === 0">↑</button>
            <button type="button" class="btn btn--subtle icon-btn" @click="moveIngredient(index, 1)" :disabled="index === draft.ingredients.length - 1">↓</button>
            <button type="button" class="btn btn--ghost" @click="removeIngredient(index)">Удалить</button>
          </div>
        </div>
        <button type="button" class="btn btn--ghost" @click="addIngredient">+ Добавить ингредиент</button>
      </fieldset>

      <fieldset class="card fieldset">
        <legend>Шаги приготовления *</legend>
        <p class="hint">Порядок шагов соответствует порядку в списке.</p>
        <div v-for="(step, index) in draft.steps" :key="index" class="step-row">
          <textarea v-model="draft.steps[index]" rows="2" placeholder="Шаг приготовления" maxlength="2000" class="step-textarea"></textarea>
          <div class="row-actions">
            <button type="button" class="btn btn--subtle icon-btn" @click="moveStep(index, -1)" :disabled="index === 0">↑</button>
            <button type="button" class="btn btn--subtle icon-btn" @click="moveStep(index, 1)" :disabled="index === draft.steps.length - 1">↓</button>
            <button type="button" class="btn btn--ghost" @click="removeStep(index)">Удалить</button>
          </div>
        </div>
        <button type="button" class="btn btn--ghost" @click="addStep">+ Добавить шаг</button>
      </fieldset>

      <div v-if="conflictMessage" class="error conflict-box">
        <p>{{ conflictMessage }}</p>
        <p v-if="conflictRevision" class="hint">Актуальная версия на сервере: {{ conflictRevision }}</p>
        <button type="button" class="btn btn--ghost" @click="reloadLatest">
          Загрузить актуальную версию
        </button>
      </div>

      <div v-if="photoError" class="error conflict-box">
        <p>{{ photoError }}</p>
        <button type="button" class="btn btn--ghost" :disabled="photoSaving" @click="onRetryPhoto">
          {{ photoSaving ? 'Повтор…' : photoUnknown ? 'Проверить и повторить' : 'Повторить фото' }}
        </button>
      </div>

      <p v-if="visibleError" class="error">
        {{ visibleError }}
        <span v-if="fieldErrorLabel" class="hint">Поле: {{ fieldErrorLabel }}</span>
      </p>

      <div class="actions">
        <button type="submit" class="btn btn--primary" :disabled="saving || photoSaving">{{ saving ? 'Сохранение…' : 'Сохранить' }}</button>
        <router-link :to="isEdit ? `/recipes/${editingId}` : '/recipes'" class="btn btn--ghost">Отмена</router-link>
      </div>
    </form>
  </section>
</template>

<style scoped>
.recipe-form {
  max-width: 760px;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.form-section {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.row {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0.75rem;
}

@media (min-width: 640px) {
  .row {
    grid-template-columns: repeat(3, 1fr);
  }
}

.fieldset {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.fieldset legend {
  font-weight: 800;
  font-size: 1rem;
  color: var(--text);
}

.fieldset-inline {
  border: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.fieldset-inline legend {
  font-weight: 800;
  font-size: 0.95rem;
  color: var(--text);
  margin-bottom: 0.25rem;
}

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.chip {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.45rem 0.85rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  background: var(--surface);
  font-weight: 600;
  font-size: 0.9rem;
  cursor: pointer;
  min-height: 40px;
  transition: background 0.15s ease, border-color 0.15s ease, color 0.15s ease;
}

.chip input {
  display: none;
}

.chip:has(input:checked) {
  background: var(--primary-soft);
  border-color: var(--primary);
  color: var(--primary);
}

.ingredient-row,
.step-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.5rem;
  padding: 0.85rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}

.ing-name-wrap {
  position: relative;
  grid-column: 1 / -1;
}

.step-textarea {
  grid-column: 1 / -1;
}

.row-actions {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  justify-content: flex-end;
}

.icon-btn {
  width: 44px;
  min-height: 44px;
  padding: 0;
}

@media (min-width: 640px) {
  .ingredient-row {
    grid-template-columns: 1fr 6rem 7.5rem 1fr auto;
    align-items: center;
    background: transparent;
    border: none;
    padding: 0.4rem 0;
    border-top: 1px solid var(--border);
    border-radius: 0;
  }

  .ing-name-wrap {
    grid-column: 1 / 2;
  }

  .ing-amount {
    grid-column: 2 / 3;
  }

  .ing-unit {
    grid-column: 3 / 4;
  }

  .ing-note {
    grid-column: 4 / 5;
  }

  .row-actions {
    grid-column: 5 / 6;
    justify-content: flex-start;
    flex-wrap: nowrap;
    gap: 0.25rem;
  }

  .step-row {
    grid-template-columns: 1fr auto;
    align-items: center;
    background: transparent;
    border: none;
    padding: 0.4rem 0;
    border-top: 1px solid var(--border);
    border-radius: 0;
  }

  .step-textarea {
    grid-column: 1 / 2;
  }

  .step-row .row-actions {
    grid-column: 2 / 3;
    justify-content: flex-end;
  }
}

.suggestions {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  z-index: 10;
  margin: 0;
  padding: 0;
  list-style: none;
  background: var(--surface);
  border: 1px solid var(--border);
  border-top: none;
  border-radius: 0 0 var(--radius-sm) var(--radius-sm);
  max-height: 12rem;
  overflow-y: auto;
  box-shadow: var(--shadow-md);
}

.suggestions li {
  padding: 0.55rem 0.7rem;
  cursor: pointer;
  font-size: 0.9rem;
}

.suggestions li.active {
  background: var(--primary-soft);
}

.suggestions li.no-suggestions {
  cursor: default;
  color: var(--text-soft);
}

.photo-preview {
  display: flex;
  align-items: flex-start;
  gap: 0.9rem;
  flex-wrap: wrap;
}

.photo-actions {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  align-items: flex-start;
}

.photo-thumb {
  max-width: 240px;
  max-height: 180px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
}

.file-label {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
  align-self: flex-start;
}

.file-input {
  max-width: 100%;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}
</style>
