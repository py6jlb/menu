<script setup>
import { ref, watch, onMounted } from 'vue'
import { listRecipes } from '../api/recipes'
import { useAuth } from '../stores/auth'

const { isEmailVerified } = useAuth()

const scopes = [
  { value: 'all', label: 'Все' },
  { value: 'own', label: 'Свои' },
  { value: 'external', label: 'Внешние' }
]
const scope = ref('all')

const recipes = ref([])
const loading = ref(true)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  const { response, data } = await listRecipes(scope.value)
  if (response.status === 200) {
    recipes.value = data || []
  } else {
    error.value = data?.error || 'Не удалось загрузить рецепты.'
  }
  loading.value = false
}

watch(scope, load)
onMounted(load)
</script>

<template>
  <section>
    <div class="page-heading">
      <h2>Рецепты</h2>
      <router-link v-if="isEmailVerified" to="/recipes/new" class="btn btn--primary">Создать рецепт</router-link>
    </div>

    <p v-if="!isEmailVerified" class="notice">
      Подтвердите почту, чтобы создавать и редактировать рецепты.
      <router-link to="/verify">Ввести код</router-link>
    </p>

    <div class="filter-tabs" role="tablist" aria-label="Фильтр рецептов">
      <button
        v-for="item in scopes"
        :key="item.value"
        type="button"
        class="filter-tab"
        :class="{ 'filter-tab--active': scope === item.value }"
        role="tab"
        :aria-selected="scope === item.value"
        @click="scope = item.value"
      >
        {{ item.label }}
      </button>
    </div>

    <p v-if="loading" class="loading">Загрузка…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <div v-else-if="recipes.length === 0" class="card empty-state">
      <div class="empty-icon">🍲</div>
      <div class="empty-title">Пока нет ни одного рецепта</div>
      <p class="empty-desc">
        Если вы ещё не в семье — создайте или вступите в неё на странице
        <router-link to="/family">Семья</router-link>.
      </p>
      <router-link v-if="isEmailVerified" to="/recipes/new" class="btn btn--primary">Добавить первый рецепт</router-link>
    </div>

    <div v-else class="recipe-grid">
      <router-link
        v-for="recipe in recipes"
        :key="recipe.id"
        :to="`/recipes/${recipe.id}`"
        class="card recipe-card"
      >
        <div class="cover">
          <img v-if="recipe.photoUrl" :src="recipe.photoUrl" alt="" class="cover-img" />
          <div v-else class="cover-placeholder">🍲</div>
        </div>
        <div class="card-body">
          <h3 class="recipe-name">{{ recipe.name }}</h3>
          <div v-if="recipe.isExternal" class="origin">
            <span class="badge badge--external">Внешний</span>
            <span v-if="recipe.state === 'broken'" class="badge badge--broken">Недоступно</span>
            <span v-else-if="recipe.state === 'warning'" class="badge badge--warning">
              Ссылка отозвана
            </span>
            <span v-if="recipe.sourceFamilyName" class="origin-family">
              из семьи {{ recipe.sourceFamilyName }}
            </span>
          </div>
          <div class="meta">
            <span class="chip">⭐ {{ recipe.difficulty }}/5</span>
            <span class="chip">⏱ {{ recipe.cookTimeMinutes }} мин</span>
            <span class="chip">👥 {{ recipe.servings }}</span>
            <span v-if="recipe.calories !== null && recipe.calories !== undefined" class="chip">
              🔥 {{ recipe.calories }} ккал
            </span>
            <span
              v-if="recipe.repetitionCount > 0"
              class="chip chip--repetition"
              title="Готовилось за последние недели"
            >
              🔁 ×{{ recipe.repetitionCount }}
            </span>
          </div>
          <div v-if="recipe.tags.length" class="tags">
            <span v-for="tag in recipe.tags" :key="tag" class="tag">#{{ tag }}</span>
          </div>
        </div>
      </router-link>
    </div>
  </section>
</template>

<style scoped>
.recipe-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 1rem;
}

.recipe-card {
  display: flex;
  flex-direction: column;
  padding: 0;
  overflow: hidden;
  text-decoration: none;
  color: var(--text);
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}

.recipe-card:hover {
  transform: translateY(-3px);
  box-shadow: var(--shadow-md);
}

.cover {
  aspect-ratio: 16 / 9;
  width: 100%;
  overflow: hidden;
  background: var(--surface-2);
}

.cover-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.cover-placeholder {
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 3rem;
}

.card-body {
  padding: 1rem 1.1rem 1.1rem;
}

.filter-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin-bottom: 1rem;
}

.filter-tab {
  min-height: 40px;
  padding: 0.4rem 0.9rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  background: var(--surface);
  color: var(--text-soft);
  font-family: inherit;
  font-size: 0.9rem;
  font-weight: 700;
  cursor: pointer;
  transition: background 0.15s ease, color 0.15s ease;
}

.filter-tab:hover {
  background: var(--surface-2);
}

.filter-tab--active {
  background: var(--primary-soft);
  border-color: var(--primary);
  color: var(--primary);
}

.recipe-name {
  margin: 0 0 0.5rem;
  font-size: 1.1rem;
}

.origin {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.35rem;
  margin-bottom: 0.5rem;
}

.badge--external {
  background: var(--primary-soft);
  color: var(--primary);
}

.badge--broken {
  background: var(--danger-bg);
  color: var(--danger);
}

.badge--warning {
  background: var(--warning-bg);
  color: var(--warning);
}

.origin-family {
  color: var(--text-faint);
  font-size: 0.78rem;
  font-weight: 600;
  overflow-wrap: anywhere;
}

.meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}

.chip {
  background: var(--surface-2);
  color: var(--text-soft);
  border-radius: 999px;
  padding: 0.15rem 0.55rem;
  font-size: 0.78rem;
  font-weight: 600;
  white-space: nowrap;
}

.chip--repetition {
  background: var(--warning-bg);
  color: var(--warning);
}

.tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  margin-top: 0.7rem;
}
</style>
