<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getSharedRecipe, importSharedRecipe } from '../api/recipes'
import { getMyFamily } from '../api/families'
import { useAuth } from '../stores/auth'
import { unitLabel, seasonLabel } from '../constants/recipe'

const route = useRoute()
const router = useRouter()
const { isAuthenticated } = useAuth()

const recipe = ref(null)
const loading = ref(true)
const error = ref('')

const family = ref(null)
const familyLoaded = ref(false)
const importing = ref(false)
const addError = ref('')

const isSourceFamily = computed(() =>
  Boolean(family.value && recipe.value?.sourceFamilyId === family.value.id)
)

async function load() {
  const { response, data } = await getSharedRecipe(route.params.token)
  if (response.status === 200) {
    recipe.value = data
  } else if (response.status === 404) {
    error.value = data?.error || 'Ссылка недействительна.'
  } else {
    error.value = data?.error || 'Не удалось загрузить рецепт.'
  }
  loading.value = false
}

async function loadFamily() {
  const { response, data } = await getMyFamily()
  family.value = response.status === 200 ? data : null
  familyLoaded.value = true
}

async function onAdd() {
  const confirmed = window.confirm(
    'Добавить рецепт в вашу семью? Он может быть потерян при удалении у владельца.'
  )
  if (!confirmed) return

  addError.value = ''
  importing.value = true
  const { response, data } = await importSharedRecipe(route.params.token)
  if (response.status === 201 || response.status === 409) {
    router.push(`/recipes/${data.recipeId}`)
  } else {
    addError.value = data?.error || 'Не удалось добавить рецепт.'
  }
  importing.value = false
}

onMounted(async () => {
  await load()
  if (isAuthenticated.value) await loadFamily()
})
</script>

<template>
  <section>
    <p v-if="loading" class="loading">Загрузка…</p>

    <div v-else-if="error" class="card empty-state">
      <div class="empty-icon">🔗</div>
      <p class="empty-title">{{ error }}</p>
      <p class="empty-desc">
        Возможно, ссылку отозвали или рецепт больше не существует.
      </p>
    </div>

    <div v-else-if="recipe">
      <div class="shared-heading">
        <span class="badge">Рецепт по ссылке</span>
      </div>

      <div class="page-heading">
        <h2>{{ recipe.name }}</h2>
      </div>

      <div v-if="!isAuthenticated" class="card invite">
        <p class="invite-text">Хотите сохранить рецепт в своё меню?</p>
        <p class="invite-desc">Войдите или зарегистрируйтесь, затем добавьте рецепт в свою семью.</p>
        <div class="invite-actions">
          <router-link to="/login" class="btn btn--ghost btn--small">Войти</router-link>
          <router-link to="/register" class="btn btn--primary btn--small">Регистрация</router-link>
        </div>
      </div>

      <div v-else-if="familyLoaded && !family" class="card invite">
        <p class="invite-text">Сначала создайте семью</p>
        <p class="invite-desc">
          Рецепт добавляется в семью. Создайте свою или присоединитесь по коду.
        </p>
        <div class="invite-actions">
          <router-link to="/family" class="btn btn--primary btn--small">Перейти к семье</router-link>
        </div>
      </div>

      <div v-else-if="isSourceFamily" class="card invite invite--muted">
        <p class="invite-text">Это рецепт вашей семьи</p>
        <p class="invite-desc">Он уже доступен вам и другим участникам.</p>
      </div>

      <div v-else-if="familyLoaded" class="card invite">
        <p class="invite-text">Добавить в мою семью</p>
        <p class="invite-desc">
          ⚠️ Рецепт может быть потерян при удалении у владельца — храните важное отдельно.
        </p>
        <p v-if="addError" class="error">{{ addError }}</p>
        <div class="invite-actions">
          <button
            type="button"
            class="btn btn--primary btn--small"
            :disabled="importing"
            @click="onAdd"
          >
            {{ importing ? 'Добавление…' : 'Добавить в мою семью' }}
          </button>
        </div>
      </div>

      <div class="hero-photo">
        <img v-if="recipe.photoUrl" :src="recipe.photoUrl" alt="Фото рецепта" class="photo" />
        <div v-else class="photo-placeholder">🍲</div>
      </div>

      <p v-if="recipe.description" class="description">{{ recipe.description }}</p>

      <div class="meta">
        <span class="chip">⭐ Сложность {{ recipe.difficulty }}/5</span>
        <span class="chip">⏱ {{ recipe.cookTimeMinutes }} мин</span>
        <span class="chip">👥 {{ recipe.servings }} порц.</span>
        <span v-if="recipe.calories !== null && recipe.calories !== undefined" class="chip">
          🔥 {{ recipe.calories }} ккал/порция
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

      <div class="detail-columns">
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
    </div>
  </section>
</template>

<style scoped>
.shared-heading {
  margin-bottom: 0.25rem;
}

.invite {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 1rem;
  background: var(--primary-soft);
  border-color: var(--primary-soft);
}

.invite--muted {
  background: var(--surface-2);
  border-color: var(--border);
}

.invite-text {
  margin: 0;
  font-weight: 700;
  color: var(--text);
}

.invite-desc {
  margin: 0.25rem 0 0;
  color: var(--text-soft);
  font-size: 0.9rem;
}

.invite-actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-top: 0.75rem;
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
  overflow-wrap: anywhere;
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
  overflow-wrap: anywhere;
}

.ingredient:last-child {
  border-bottom: none;
}

.amount {
  font-weight: 700;
  color: var(--primary);
  white-space: nowrap;
}

.name {
  overflow-wrap: anywhere;
}

.note {
  color: var(--text-faint);
  font-size: 0.85rem;
  overflow-wrap: anywhere;
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
  overflow-wrap: anywhere;
}
</style>
