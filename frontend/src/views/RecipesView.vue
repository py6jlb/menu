<script setup>
import { ref, onMounted } from 'vue'
import { listRecipes } from '../api/recipes'

const recipes = ref([])
const loading = ref(true)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  const { response, data } = await listRecipes()
  if (response.status === 200) {
    recipes.value = data || []
  } else {
    error.value = data?.error || 'Не удалось загрузить рецепты.'
  }
  loading.value = false
}

onMounted(load)
</script>

<template>
  <section>
    <div class="page-heading">
      <h2>Рецепты</h2>
      <router-link to="/recipes/new" class="btn btn--primary">Создать рецепт</router-link>
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
      <router-link to="/recipes/new" class="btn btn--primary">Добавить первый рецепт</router-link>
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

.recipe-name {
  margin: 0 0 0.5rem;
  font-size: 1.1rem;
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
