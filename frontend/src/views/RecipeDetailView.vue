<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  getRecipe,
  deleteRecipe,
  removeExternalRecipe,
  copyRecipe,
  getRecipeShare,
  revokeRecipeShare,
  regenerateRecipeShare
} from '../api/recipes'
import { getMyFamily } from '../api/families'
import { useAuth } from '../stores/auth'
import { unitLabel, seasonLabel } from '../constants/recipe'

const route = useRoute()
const router = useRouter()
const { state, isEmailVerified } = useAuth()

const recipe = ref(null)
const loading = ref(true)
const error = ref('')
const deleting = ref(false)

const removingLocal = ref(false)
const copying = ref(false)
const copyError = ref('')

const family = ref(null)
const share = ref(null)
const shareLoading = ref(false)
const shareError = ref('')
const copied = ref(false)

const isOwner = computed(() => Boolean(family.value && family.value.ownerId === state.user?.id))
const isExternal = computed(() => recipe.value?.isExternal === true)

const shareLink = computed(() => {
  if (!share.value) return ''
  const url = share.value.url || share.value.path || ''
  return url.startsWith('http') ? url : `${window.location.origin}${url}`
})

async function load() {
  const { response, data } = await getRecipe(route.params.id)
  if (response.status === 404) {
    error.value = 'Рецепт не найден.'
  } else if (response.status === 200) {
    recipe.value = data
  } else {
    error.value = data?.error || 'Не удалось загрузить рецепт.'
  }
  loading.value = false
}

async function loadFamily() {
  const { response, data } = await getMyFamily()
  if (response.status === 200) {
    family.value = data
  }
}

async function onShare() {
  shareError.value = ''
  shareLoading.value = true
  const { response, data } = await getRecipeShare(recipe.value.id)
  if (response.status === 200) {
    share.value = data
  } else {
    shareError.value = data?.error || 'Не удалось получить ссылку.'
  }
  shareLoading.value = false
}

async function onCopyLink() {
  try {
    await navigator.clipboard.writeText(shareLink.value)
    copied.value = true
    setTimeout(() => (copied.value = false), 1500)
  } catch {
    shareError.value = 'Не удалось скопировать ссылку.'
  }
}

async function onRevoke() {
  if (!window.confirm('Отозвать ссылку? Новые семьи не смогут добавить рецепт.')) return
  shareError.value = ''
  shareLoading.value = true
  const { response, data } = await revokeRecipeShare(recipe.value.id)
  if (response.status === 200) {
    share.value = data
  } else {
    shareError.value = data?.error || 'Не удалось отозвать ссылку.'
  }
  shareLoading.value = false
}

async function onRegenerate() {
  if (!window.confirm('Перегенерировать ссылку? Старая перестанет работать.')) return
  shareError.value = ''
  shareLoading.value = true
  const { response, data } = await regenerateRecipeShare(recipe.value.id)
  if (response.status === 200) {
    share.value = data
  } else {
    shareError.value = data?.error || 'Не удалось перегенерировать ссылку.'
  }
  shareLoading.value = false
}

async function onDelete() {
  if (!window.confirm(`Удалить рецепт «${recipe.value.name}»?`)) return
  deleting.value = true
  error.value = ''
  const { response } = await deleteRecipe(recipe.value.id)
  if (response.status === 204) {
    router.push('/recipes')
  } else {
    error.value = 'Не удалось удалить рецепт.'
    deleting.value = false
  }
}

async function onRemoveExternal() {
  if (
    !window.confirm(
      `Убрать рецепт «${recipe.value.name}» из вашей семьи? Семья-источник не пострадает.`
    )
  )
    return
  removingLocal.value = true
  error.value = ''
  const { response } = await removeExternalRecipe(recipe.value.id)
  if (response.status === 204) {
    router.push('/recipes')
  } else {
    error.value = 'Не удалось убрать рецепт из семьи.'
    removingLocal.value = false
  }
}

async function onCopy() {
  copyError.value = ''
  copying.value = true
  const { response, data } = await copyRecipe(recipe.value.id)
  if (response.status === 200 || response.status === 201) {
    recipe.value = data
  } else {
    copyError.value = data?.error || 'Сохранение копии пока недоступно.'
  }
  copying.value = false
}

onMounted(() => {
  load()
  loadFamily()
})
</script>

<template>
  <section>
    <p v-if="loading" class="loading">Загрузка…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <div v-else-if="recipe">
      <div class="page-heading">
        <h2>{{ recipe.name }}</h2>
        <div v-if="isEmailVerified && !isExternal" class="page-heading-actions">
          <router-link :to="`/recipes/${recipe.id}/edit`" class="btn btn--primary">Редактировать</router-link>
          <button type="button" class="btn btn--danger" :disabled="deleting" @click="onDelete">Удалить</button>
        </div>
      </div>

      <div v-if="isExternal" class="card external-banner">
        <div class="external-badges">
          <span class="badge badge--external">Внешний</span>
          <span v-if="recipe.state === 'broken'" class="badge badge--broken">Недоступно</span>
          <span v-else-if="recipe.state === 'warning'" class="badge badge--warning">Ссылка отозвана</span>
          <span v-if="recipe.sourceFamilyName" class="external-source">
            из семьи {{ recipe.sourceFamilyName }}
          </span>
        </div>

        <template v-if="recipe.state === 'broken'">
          <p class="external-broken">
            Источник удалил рецепт — содержимое недоступно. Имя сохранено, но рецепт больше
            нельзя готовить. Уберите его из семьи или замените в плане недели.
          </p>
          <p v-if="copyError" class="error">{{ copyError }}</p>
          <div class="external-actions">
            <button type="button" class="btn btn--primary" disabled title="Источник удалён">
              Сделать копию
            </button>
            <button
              type="button"
              class="btn btn--danger"
              :disabled="removingLocal"
              @click="onRemoveExternal"
            >
              {{ removingLocal ? 'Удаление…' : 'Убрать из моей семьи' }}
            </button>
          </div>
        </template>

        <template v-else-if="recipe.state === 'warning'">
          <p class="external-warning">
            Ссылка отозвана или перегенерирована. Рецепт пока доступен, но может пропасть —
            сделайте копию, чтобы сохранить.
          </p>
          <p v-if="copyError" class="error">{{ copyError }}</p>
          <div class="external-actions">
            <button type="button" class="btn btn--primary" :disabled="copying" @click="onCopy">
              {{ copying ? 'Сохранение…' : 'Сделать копию, чтобы сохранить' }}
            </button>
            <button
              type="button"
              class="btn btn--ghost"
              :disabled="removingLocal"
              @click="onRemoveExternal"
            >
              Убрать из моей семьи
            </button>
          </div>
        </template>

        <template v-else>
          <p class="external-note">
            Рецепт доступен только для чтения — изменения вносит семья-источник.
          </p>
          <p v-if="copyError" class="error">{{ copyError }}</p>
          <div class="external-actions">
            <button type="button" class="btn btn--primary" :disabled="copying" @click="onCopy">
              {{ copying ? 'Сохранение…' : 'Сделать копию' }}
            </button>
            <button
              type="button"
              class="btn btn--ghost"
              :disabled="removingLocal"
              @click="onRemoveExternal"
            >
              Убрать из моей семьи
            </button>
          </div>
        </template>
      </div>

      <div
        v-if="!isExternal && recipe.copiedFromFamilyName"
        class="card copied-origin"
      >
        <span class="badge badge--external">Скопировано</span>
        <span class="copied-origin-text">из семьи {{ recipe.copiedFromFamilyName }}</span>
      </div>

      <p v-if="!isEmailVerified && !isExternal" class="notice">
        Подтвердите почту, чтобы редактировать рецепт.
        <router-link to="/verify">Ввести код</router-link>
      </p>

      <div class="hero-photo">
        <img v-if="recipe.photoUrl" :src="recipe.photoUrl" alt="Фото рецепта" class="photo" />
        <div v-else class="photo-placeholder">🍲</div>
      </div>

      <p v-if="recipe.description" class="description">{{ recipe.description }}</p>

      <div v-if="recipe.state !== 'broken'" class="meta">
        <span class="chip">⭐ Сложность {{ recipe.difficulty }}/5</span>
        <span class="chip">⏱ {{ recipe.cookTimeMinutes }} мин</span>
        <span class="chip">👥 {{ recipe.servings }} порц.</span>
        <span v-if="recipe.calories !== null && recipe.calories !== undefined" class="chip">
          🔥 {{ recipe.calories }} ккал/порция
        </span>
        <span v-if="recipe.repetitionCount > 0" class="chip chip--repetition">
          🔁 Готовилось ×{{ recipe.repetitionCount }}
        </span>
      </div>

      <div v-if="recipe.tags.length || recipe.seasonality.length || recipe.diet.length" class="badges">
        <span v-for="tag in recipe.tags" :key="`tag-${tag}`" class="tag">#{{ tag }}</span>
        <span v-for="season in recipe.seasonality" :key="`season-${season}`" class="badge badge--season">
          🍂 {{ seasonLabel(season) }}
        </span>
        <span v-for="diet in recipe.diet" :key="`diet-${diet}`" class="badge badge--diet">
          {{ diet }}
        </span>
      </div>

      <div v-if="recipe.state === 'broken'" class="card broken-content">
        <p>Содержимое рецепта недоступно, потому что семья-источник удалила его.</p>
      </div>

      <div v-else class="detail-columns">
        <div class="card column">
          <h3>Ингредиенты</h3>
          <ul class="ingredients">
            <li v-for="ingredient in recipe.ingredients" :key="ingredient.id" class="ingredient">
              <span class="amount">{{ ingredient.amount }} {{ unitLabel(ingredient.unit) }}</span>
              <span class="name">{{ ingredient.name }}</span>
              <span v-if="ingredient.note" class="note">{{ ingredient.note }}</span>
            </li>
          </ul>
        </div>

        <div class="card column">
          <h3>Шаги приготовления</h3>
          <ol class="steps">
            <li v-for="(step, index) in recipe.steps" :key="index" class="step">
              <span class="step-num">{{ index + 1 }}</span>
              <span class="step-text">{{ step }}</span>
            </li>
          </ol>
        </div>
      </div>

      <div v-if="!isExternal" class="card share-card">
        <div class="share-header">
          <h3>Поделиться</h3>
          <span v-if="share && share.revoked" class="badge badge--revoked">Ссылка отозвана</span>
        </div>

        <p v-if="shareError" class="error">{{ shareError }}</p>

        <div v-else-if="!share" class="share-empty">
          <p class="share-hint">Создайте ссылку, чтобы поделиться рецептом с другой семьёй.</p>
          <button type="button" class="btn btn--primary" :disabled="shareLoading" @click="onShare">
            {{ shareLoading ? 'Создание…' : 'Поделиться' }}
          </button>
        </div>

        <div v-else class="share-content">
          <p v-if="share.revoked" class="share-revoked">
            Ссылка отозвана: новые семьи больше не смогут добавить этот рецепт.
          </p>
          <input
            :value="shareLink"
            type="text"
            readonly
            class="share-link"
            @focus="$event.target.select()"
          />
          <div class="share-actions">
            <button type="button" class="btn btn--primary" @click="onCopyLink">
              {{ copied ? 'Скопировано!' : 'Скопировать' }}
            </button>
            <template v-if="isOwner">
              <button type="button" class="btn btn--ghost" :disabled="shareLoading" @click="onRegenerate">
                Перегенерировать
              </button>
              <button
                v-if="!share.revoked"
                type="button"
                class="btn btn--danger"
                :disabled="shareLoading"
                @click="onRevoke"
              >
                Отозвать
              </button>
            </template>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.external-banner {
  margin-bottom: 1rem;
}

.external-badges {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
}

.badge--external {
  background: var(--primary-soft);
  color: var(--primary);
}

.external-source {
  color: var(--text-faint);
  font-size: 0.85rem;
  font-weight: 600;
  overflow-wrap: anywhere;
}

.copied-origin {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.copied-origin-text {
  color: var(--text-faint);
  font-size: 0.85rem;
  font-weight: 600;
  overflow-wrap: anywhere;
}

.external-note {
  margin: 0;
  color: var(--text-soft);
  font-size: 0.9rem;
}

.external-broken {
  margin: 0;
  color: var(--danger);
  font-size: 0.9rem;
}

.external-warning {
  margin: 0;
  color: var(--warning);
  font-size: 0.9rem;
  font-weight: 600;
}

.badge--broken {
  background: var(--danger-bg);
  color: var(--danger);
}

.badge--warning {
  background: var(--warning-bg);
  color: var(--warning);
}

.external-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.broken-content {
  color: var(--text-soft);
  font-size: 0.95rem;
}

.broken-content p {
  margin: 0;
}

.hero-photo {
  margin-bottom: 1rem;
}

.photo {
  width: 100%;
  max-width: 600px;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  border-radius: var(--radius);
  display: block;
}

.photo-placeholder {
  width: 100%;
  max-width: 600px;
  aspect-ratio: 16 / 9;
  border-radius: var(--radius);
  background: linear-gradient(135deg, #f9ead9, #faf6f0);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 4rem;
}

.description {
  margin: 0 0 1rem;
  color: var(--text-soft);
  font-size: 1.05rem;
}

.meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
  margin-bottom: 1rem;
}

.chip {
  background: var(--surface-2);
  color: var(--text-soft);
  border-radius: 999px;
  padding: 0.3rem 0.7rem;
  font-size: 0.85rem;
  font-weight: 600;
  white-space: nowrap;
}

.chip--repetition {
  background: var(--warning-bg);
  color: var(--warning);
}

.badges {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
  margin-bottom: 1.5rem;
}

.detail-columns {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1rem;
}

@media (min-width: 800px) {
  .detail-columns {
    grid-template-columns: 1fr 1fr;
    align-items: start;
  }
}

.ingredients {
  list-style: none;
  margin: 0;
  padding: 0;
}

.ingredient {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
  padding: 0.5rem 0;
  border-bottom: 1px solid var(--border);
  font-size: 0.95rem;
}

.ingredient:last-child {
  border-bottom: none;
}

.amount {
  font-weight: 700;
  color: var(--primary);
  white-space: nowrap;
}

.note {
  color: var(--text-faint);
  font-size: 0.85rem;
}

.steps {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.step {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
}

.step-num {
  flex: 0 0 auto;
  width: 26px;
  height: 26px;
  border-radius: 50%;
  background: var(--primary-soft);
  color: var(--primary);
  font-weight: 800;
  font-size: 0.85rem;
  display: flex;
  align-items: center;
  justify-content: center;
}

.step-text {
  font-size: 0.95rem;
}

.share-card {
  margin-top: 1rem;
}

.share-header {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  flex-wrap: wrap;
  margin-bottom: 0.75rem;
}

.share-header h3 {
  margin: 0;
}

.badge--revoked {
  background: var(--danger-bg);
  color: var(--danger);
}

.share-hint {
  margin: 0 0 0.75rem;
  color: var(--text-soft);
  font-size: 0.9rem;
}

.share-revoked {
  margin: 0 0 0.75rem;
  padding: 0.6rem 0.8rem;
  border-radius: var(--radius-sm);
  background: var(--danger-bg);
  color: var(--danger);
  font-size: 0.9rem;
}

.share-link {
  width: 100%;
  font-family: inherit;
  font-size: 0.9rem;
  color: var(--text-soft);
}

.share-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.75rem;
}
</style>
