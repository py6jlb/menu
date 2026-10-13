<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getSharedRecipe, importSharedRecipe } from '../api/recipes'
import { getMyFamily } from '../api/families'
import { useAuth } from '../stores/auth'
import { useRouteResource } from '../composables/common/useRouteResource'
import { useRecipeAction } from '../composables/recipes/useRecipeAction'
import RecipeBody from '../components/RecipeBody.vue'

const route = useRoute()
const router = useRouter()
const { isAuthenticated, isEmailVerified } = useAuth()

const { key, resource: recipe, loading, error, load } = useRouteResource(getSharedRecipe, {
  notFound: 'Ссылка недействительна.'
})

const family = ref(null)
const familyLoaded = ref(false)

// Импорт по ссылке: у действия собственные pending/error; ревизии и конфликта
// у импорта нет, поэтому 409 — тоже принятый исход (рецепт уже добавлен).
const {
  pending: importing,
  error: addError,
  run: runImport,
  reset: resetImport
} = useRecipeAction()

const isSourceFamily = computed(() =>
  Boolean(family.value && recipe.value?.sourceFamilyId === family.value.id)
)

function resetResourceState() {
  resetImport()
}

async function loadFamily() {
  const { response, data } = await getMyFamily()
  family.value = response.status === 200 ? data : null
  familyLoaded.value = true
}

async function onAdd() {
  const token = route.params.token
  if (
    !window.confirm(
      'Добавить рецепт в вашу семью? Он может быть потерян при удалении у владельца.'
    )
  )
    return
  return runImport(() => importSharedRecipe(token), {
    key: token,
    acceptedStatuses: [201, 409],
    onSuccess: (data) => router.push(`/recipes/${data.recipeId}`),
    fallbackMessage: 'Не удалось добавить рецепт.'
  })
}

watch(
  () => route.params.token,
  (token) => {
    if (!token || token === key.value) return
    resetResourceState()
    load(token)
  }
)

onMounted(async () => {
  await load(route.params.token)
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
      <div class="actions">
        <button type="button" class="btn btn--primary" @click="load()">Повторить</button>
      </div>
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
          <router-link
            :to="{ name: 'login', query: { returnTo: route.fullPath } }"
            class="btn btn--ghost btn--small"
            >Войти</router-link
          >
          <router-link
            :to="{ name: 'register', query: { returnTo: route.fullPath } }"
            class="btn btn--primary btn--small"
            >Регистрация</router-link
          >
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

      <div v-else-if="familyLoaded && !isEmailVerified" class="card invite">
        <p class="invite-text">Подтвердите почту, чтобы добавить рецепт</p>
        <p class="invite-desc">
          Импорт рецепта в семью доступен после подтверждения почты.
        </p>
        <div class="invite-actions">
          <router-link
            :to="{ name: 'verify', query: { returnTo: route.fullPath } }"
            class="btn btn--primary btn--small"
            >Подтвердить почту</router-link
          >
        </div>
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

      <RecipeBody :recipe="recipe" />
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
</style>
