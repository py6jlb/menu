<script setup>
import { ref, reactive, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getRecipe, createRecipe, updateRecipe, uploadRecipePhoto, deleteRecipePhoto } from '../api/recipes'
import { autocompleteIngredients } from '../api/ingredients'
import { UNITS, SEASONS, parseList, joinList } from '../constants/recipe'

const route = useRoute()
const router = useRouter()

const editingId = computed(() => route.params.id || null)
const isEdit = computed(() => Boolean(editingId.value))

const form = reactive({
  name: '',
  description: '',
  cookTimeMinutes: 30,
  servings: 4,
  difficulty: 3,
  calories: '',
  tagsText: '',
  seasonality: [],
  dietText: '',
  steps: [''],
  ingredients: [newIngredient()]
})

const loading = ref(isEdit.value)
const saving = ref(false)
const error = ref('')

const debounceTimers = new Map()

function newIngredient() {
  return { name: '', amount: '', unit: 'g', note: '', suggestions: [], showSuggestions: false, activeIndex: -1, loadingSuggestions: false, noSuggestions: false }
}

const existingPhotoUrl = ref(null)
const selectedPhoto = ref(null)
const selectedPhotoPreview = ref('')
const photoRemoved = ref(false)

const photoActionLabel = computed(() =>
  selectedPhoto.value || (existingPhotoUrl.value && !photoRemoved.value) ? 'Заменить фото' : 'Выбрать фото'
)

function onPhotoSelected(event) {
  const file = event.target.files?.[0]
  if (!file) return
  selectedPhoto.value = file
  if (selectedPhotoPreview.value) URL.revokeObjectURL(selectedPhotoPreview.value)
  selectedPhotoPreview.value = URL.createObjectURL(file)
  event.target.value = ''
}

function clearSelectedPhoto() {
  selectedPhoto.value = null
  if (selectedPhotoPreview.value) {
    URL.revokeObjectURL(selectedPhotoPreview.value)
    selectedPhotoPreview.value = ''
  }
}

function removeExistingPhoto() {
  photoRemoved.value = true
}

function undoPhotoRemoval() {
  photoRemoved.value = false
}

onBeforeUnmount(() => {
  if (selectedPhotoPreview.value) URL.revokeObjectURL(selectedPhotoPreview.value)
})

function addStep() {
  form.steps.push('')
}

function removeStep(index) {
  form.steps.splice(index, 1)
}

function moveStep(index, delta) {
  const target = index + delta
  if (target < 0 || target >= form.steps.length) return
  const tmp = form.steps[index]
  form.steps[index] = form.steps[target]
  form.steps[target] = tmp
}

function addIngredient() {
  form.ingredients.push(newIngredient())
}

function removeIngredient(index) {
  clearDebounce(index)
  form.ingredients.splice(index, 1)
}

function moveIngredient(index, delta) {
  const target = index + delta
  if (target < 0 || target >= form.ingredients.length) return
  const tmp = form.ingredients[index]
  form.ingredients[index] = form.ingredients[target]
  form.ingredients[target] = tmp
}

function clearDebounce(index) {
  const timer = debounceTimers.get(index)
  if (timer) {
    clearTimeout(timer)
    debounceTimers.delete(index)
  }
}

function closeSuggestions(index) {
  clearDebounce(index)
  form.ingredients[index].showSuggestions = false
  form.ingredients[index].activeIndex = -1
}

function onIngredientInput(index) {
  const ing = form.ingredients[index]
  if (!ing.name.trim()) {
    clearDebounce(index)
    ing.suggestions = []
    ing.showSuggestions = false
    ing.noSuggestions = false
    ing.activeIndex = -1
    return
  }
  clearDebounce(index)
  debounceTimers.set(index, setTimeout(() => fetchSuggestions(index), 150))
}

async function fetchSuggestions(index) {
  const ing = form.ingredients[index]
  const query = ing.name.trim()
  if (!query) return
  ing.loadingSuggestions = true
  ing.noSuggestions = false
  const { response, data } = await autocompleteIngredients(query)
  if (response.status === 200 && data) {
    ing.suggestions = data.items || []
    ing.showSuggestions = true
    ing.activeIndex = -1
    ing.noSuggestions = ing.suggestions.length === 0
  } else {
    ing.suggestions = []
    ing.showSuggestions = false
    ing.noSuggestions = false
  }
  ing.loadingSuggestions = false
}

function selectSuggestion(index, suggestion) {
  const ing = form.ingredients[index]
  ing.name = suggestion
  closeSuggestions(index)
}

function onSuggestionKeydown(index, event) {
  const ing = form.ingredients[index]
  if (!ing.showSuggestions || ing.suggestions.length === 0) return
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    ing.activeIndex = Math.min(ing.activeIndex + 1, ing.suggestions.length - 1)
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    ing.activeIndex = Math.max(ing.activeIndex - 1, 0)
  } else if (event.key === 'Enter' && ing.activeIndex >= 0) {
    event.preventDefault()
    selectSuggestion(index, ing.suggestions[ing.activeIndex])
  } else if (event.key === 'Escape') {
    event.preventDefault()
    closeSuggestions(index)
  }
}

function toggleSeason(code) {
  const index = form.seasonality.indexOf(code)
  if (index >= 0) form.seasonality.splice(index, 1)
  else form.seasonality.push(code)
}

function buildPayload() {
  return {
    name: form.name.trim(),
    description: form.description.trim() || null,
    cookTimeMinutes: Number(form.cookTimeMinutes),
    servings: Number(form.servings),
    difficulty: Number(form.difficulty),
    calories: form.calories === '' ? null : Number(form.calories),
    tags: parseList(form.tagsText),
    seasonality: form.seasonality,
    diet: parseList(form.dietText),
    steps: form.steps.map((s) => s.trim()).filter((s) => s.length > 0).map((text) => ({ text })),
    ingredients: form.ingredients
      .filter((i) => i.name.trim() || i.amount || i.unit || i.note)
      .map((i) => ({
        name: i.name.trim(),
        amount: Number(i.amount),
        unit: i.unit,
        note: i.note.trim() || null
      }))
  }
}

async function loadRecipe() {
  const { response, data } = await getRecipe(editingId.value)
  if (response.status === 404) {
    error.value = 'Рецепт не найден.'
    loading.value = false
    return
  }
  if (response.status !== 200) {
    error.value = data?.error || 'Не удалось загрузить рецепт.'
    loading.value = false
    return
  }

  form.name = data.name
  form.description = data.description || ''
  existingPhotoUrl.value = data.photoUrl || null
  form.cookTimeMinutes = data.cookTimeMinutes
  form.servings = data.servings
  form.difficulty = data.difficulty
  form.calories = data.calories === null || data.calories === undefined ? '' : String(data.calories)
  form.tagsText = joinList(data.tags)
  form.seasonality = [...data.seasonality]
  form.dietText = joinList(data.diet)
  form.steps = data.steps.length ? [...data.steps] : ['']
  form.ingredients = data.ingredients.length
    ? data.ingredients.map((i) => ({
        name: i.name,
        amount: String(i.amount),
        unit: i.unit,
        note: i.note || ''
      }))
    : [newIngredient()]

  loading.value = false
}

async function submit() {
  error.value = ''
  if (!form.name.trim()) {
    error.value = 'Укажите название рецепта.'
    return
  }
  saving.value = true
  try {
    const payload = buildPayload()
    const { response, data } = isEdit.value
      ? await updateRecipe(editingId.value, payload)
      : await createRecipe(payload)
    if (response.status === 200 || response.status === 201) {
      const id = data.id
      try {
        if (photoRemoved.value && !selectedPhoto.value && existingPhotoUrl.value) {
          const removed = await deleteRecipePhoto(id)
          if (removed.response.status !== 204) {
            console.error('Не удалось удалить фото:', removed.data)
          }
        } else if (selectedPhoto.value) {
          const uploaded = await uploadRecipePhoto(id, selectedPhoto.value)
          if (uploaded.response.status !== 200) {
            console.error('Не удалось загрузить фото:', uploaded.data)
          }
        }
      } catch (err) {
        console.error('Ошибка при работе с фото:', err)
      }
      router.push(`/recipes/${id}`)
    } else {
      error.value = data?.error || 'Не удалось сохранить рецепт.'
    }
  } catch (err) {
    error.value = err.message || 'Сервер недоступен.'
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  if (isEdit.value) loadRecipe()
})

onBeforeUnmount(() => {
  debounceTimers.forEach((timer) => clearTimeout(timer))
  debounceTimers.clear()
})
</script>

<template>
  <section>
    <h2>{{ isEdit ? 'Редактирование рецепта' : 'Новый рецепт' }}</h2>

    <p v-if="loading">Загрузка…</p>

    <form v-else @submit.prevent="submit" class="recipe-form">
      <label>
        Название *
        <input v-model="form.name" type="text" maxlength="200" required />
      </label>

      <label>
        Описание
        <textarea v-model="form.description" rows="3" maxlength="2000"></textarea>
      </label>

      <div class="row">
        <label>
          Время приготовления (мин) *
          <input v-model.number="form.cookTimeMinutes" type="number" min="1" max="1440" required />
        </label>
        <label>
          Порции *
          <input v-model.number="form.servings" type="number" min="1" max="100" required />
        </label>
        <label>
          Сложность *
          <select v-model.number="form.difficulty" required>
            <option v-for="level in 5" :key="level" :value="level">{{ level }}</option>
          </select>
        </label>
      </div>

      <label>
        Калорийность на порцию (ккал)
        <input v-model.number="form.calories" type="number" min="0" max="10000" />
      </label>

      <fieldset>
        <legend>Фото</legend>
        <div v-if="selectedPhoto" class="photo-preview">
          <img :src="selectedPhotoPreview" alt="Предпросмотр фото" class="photo-thumb" />
          <span class="hint">Новое фото будет загружено после сохранения.</span>
          <button type="button" class="ghost" @click="clearSelectedPhoto">Убрать</button>
        </div>
        <div v-else-if="existingPhotoUrl && !photoRemoved" class="photo-preview">
          <img :src="existingPhotoUrl" alt="Фото рецепта" class="photo-thumb" />
          <button type="button" class="ghost danger-text" @click="removeExistingPhoto">Удалить фото</button>
        </div>
        <p v-else-if="photoRemoved" class="hint">
          Фото будет удалено после сохранения.
          <button type="button" class="ghost" @click="undoPhotoRemoval">Отменить удаление</button>
        </p>
        <label class="file-label">
          {{ photoActionLabel }}
          <input type="file" accept="image/jpeg,image/png,image/webp,image/gif" class="file-input" @change="onPhotoSelected" />
        </label>
      </fieldset>

      <label>
        Теги (через запятую)
        <input v-model="form.tagsText" type="text" placeholder="суп, первое, быстрый" />
      </label>

      <fieldset>
        <legend>Сезонность</legend>
        <div class="chips">
          <label v-for="season in SEASONS" :key="season.code" class="chip">
            <input
              type="checkbox"
              :value="season.code"
              :checked="form.seasonality.includes(season.code)"
              @change="toggleSeason(season.code)"
            />
            {{ season.label }}
          </label>
        </div>
      </fieldset>

      <label>
        Диета (через запятую)
        <input v-model="form.dietText" type="text" placeholder="вегетарианское, безглютеновое" />
      </label>

      <fieldset>
        <legend>Ингредиенты</legend>
        <p class="hint">Количество и единица измерения указываются вместе. Примечание (например, «по вкусу») не влияет на расчёт.</p>
        <div v-for="(ing, index) in form.ingredients" :key="index" class="ingredient-row">
          <div class="ing-name-wrap">
            <input
              v-model="ing.name"
              type="text"
              placeholder="Название"
              class="ing-name"
              @input="onIngredientInput(index)"
              @keydown="onSuggestionKeydown(index, $event)"
              @blur="closeSuggestions(index)"
            />
            <ul v-if="ing.showSuggestions" class="suggestions">
              <li
                v-for="(suggestion, sIndex) in ing.suggestions"
                :key="suggestion"
                :class="{ active: sIndex === ing.activeIndex }"
                @mousedown.prevent="selectSuggestion(index, suggestion)"
              >
                {{ suggestion }}
              </li>
              <li v-if="ing.noSuggestions" class="no-suggestions">Нет подсказок</li>
            </ul>
          </div>
          <input v-model.number="ing.amount" type="number" min="0" step="0.01" placeholder="Кол-во" class="ing-amount" />
          <select v-model="ing.unit" class="ing-unit">
            <option v-for="unit in UNITS" :key="unit.code" :value="unit.code">{{ unit.label }}</option>
          </select>
          <input v-model="ing.note" type="text" placeholder="Примечание" class="ing-note" />
          <div class="row-actions">
            <button type="button" class="ghost" @click="moveIngredient(index, -1)" :disabled="index === 0">↑</button>
            <button type="button" class="ghost" @click="moveIngredient(index, 1)" :disabled="index === form.ingredients.length - 1">↓</button>
            <button type="button" class="ghost danger-text" @click="removeIngredient(index)">Удалить</button>
          </div>
        </div>
        <button type="button" class="ghost" @click="addIngredient">+ Добавить ингредиент</button>
      </fieldset>

      <fieldset>
        <legend>Шаги приготовления *</legend>
        <p class="hint">Порядок шагов соответствует порядку в списке.</p>
        <div v-for="(step, index) in form.steps" :key="index" class="step-row">
          <textarea v-model="form.steps[index]" rows="2" placeholder="Шаг приготовления" maxlength="2000"></textarea>
          <div class="row-actions">
            <button type="button" class="ghost" @click="moveStep(index, -1)" :disabled="index === 0">↑</button>
            <button type="button" class="ghost" @click="moveStep(index, 1)" :disabled="index === form.steps.length - 1">↓</button>
            <button type="button" class="ghost danger-text" @click="removeStep(index)">Удалить</button>
          </div>
        </div>
        <button type="button" class="ghost" @click="addStep">+ Добавить шаг</button>
      </fieldset>

      <p v-if="error" class="error">{{ error }}</p>

      <div class="actions">
        <button type="submit" :disabled="saving">{{ saving ? 'Сохранение…' : 'Сохранить' }}</button>
        <router-link :to="isEdit ? `/recipes/${editingId}` : '/recipes'" class="cancel">Отмена</router-link>
      </div>
    </form>
  </section>
</template>

<style scoped>
.recipe-form {
  max-width: 640px;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.9rem;
}

.row {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 0.75rem;
}

textarea {
  padding: 0.5rem 0.6rem;
  border: 1px solid #ccc;
  border-radius: 6px;
  font-size: 1rem;
  font-family: inherit;
}

select {
  padding: 0.5rem 0.6rem;
  border: 1px solid #ccc;
  border-radius: 6px;
  font-size: 1rem;
}

fieldset {
  border: 1px solid #e5e5e5;
  border-radius: 8px;
  padding: 0.75rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

legend {
  font-weight: 600;
  font-size: 0.9rem;
}

.hint {
  font-size: 0.8rem;
  color: #666;
  margin: 0;
}

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.chip {
  display: inline-flex;
  flex-direction: row;
  align-items: center;
  gap: 0.3rem;
}

.ingredient-row,
.step-row {
  display: flex;
  align-items: flex-start;
  gap: 0.4rem;
}

.ing-name {
  width: 100%;
}

.ing-name-wrap {
  position: relative;
  flex: 2;
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
  background: #fff;
  border: 1px solid #ccc;
  border-top: none;
  border-radius: 0 0 6px 6px;
  max-height: 12rem;
  overflow-y: auto;
  box-shadow: 0 4px 8px rgba(0, 0, 0, 0.08);
}

.suggestions li {
  padding: 0.4rem 0.6rem;
  cursor: pointer;
  font-size: 0.9rem;
}

.suggestions li.active {
  background: #eef2ff;
}

.suggestions li.no-suggestions {
  cursor: default;
  color: #666;
}

.ing-amount {
  flex: 0 0 5.5rem;
}

.ing-unit {
  flex: 0 0 7.5rem;
}

.ing-note {
  flex: 2;
}

.step-row textarea {
  flex: 1;
}

.row-actions {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.ghost {
  background: none;
  border: 1px solid #ccc;
  border-radius: 6px;
  padding: 0.3rem 0.6rem;
  font-size: 0.85rem;
}

.danger-text {
  color: #b91c1c;
  border-color: #f3c6c6;
}

.actions {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.photo-preview {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.photo-thumb {
  max-width: 240px;
  max-height: 180px;
  border-radius: 8px;
  border: 1px solid #e5e5e5;
}

.file-label {
  display: inline-flex;
  flex-direction: row;
  gap: 0.5rem;
}

.file-input {
  max-width: 100%;
}

.cancel {
  font-size: 0.9rem;
}
</style>