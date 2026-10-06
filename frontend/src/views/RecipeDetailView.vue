<script setup>
import { ref, computed, watch, onMounted } from 'vue'
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
import { useRouteResource } from '../composables/useRouteResource'
import RecipeBody from '../components/RecipeBody.vue'
import ExternalStateBadge from '../components/ExternalStateBadge.vue'

const route = useRoute()
const router = useRouter()
const { state, isEmailVerified } = useAuth()

const { resource: recipe, loading, error, load } = useRouteResource(getRecipe, {
  notFound: 'Рецепт не найден.'
})

const deleting = ref(false)
const removingLocal = ref(false)
const copying = ref(false)
const copyError = ref('')
const actionError = ref('')

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

function resetResourceState() {
  deleting.value = false
  removingLocal.value = false
  copying.value = false
  copyError.value = ''
  actionError.value = ''
  share.value = null
  shareLoading.value = false
  shareError.value = ''
  copied.value = false
}

async function loadFamily() {
  const { response, data } = await getMyFamily()
  if (response.status === 200) {
    family.value = data
  }
}

async function runShareAction(action, fallbackMessage) {
  const id = recipe.value?.id
  if (!id) return
  shareError.value = ''
  shareLoading.value = true
  try {
    const { response, data } = await action(id)
    if (recipe.value?.id !== id) return
    if (response.status === 200) {
      share.value = data
    } else {
      shareError.value = data?.error || fallbackMessage
    }
  } catch {
    if (recipe.value?.id === id) shareError.value = 'Сервер недоступен. Попробуйте ещё раз.'
  } finally {
    if (recipe.value?.id === id) shareLoading.value = false
  }
}

function onShare() {
  return runShareAction(getRecipeShare, 'Не удалось получить ссылку.')
}

function onRevoke() {
  if (!window.confirm('Отозвать ссылку? Новые семьи не смогут добавить рецепт.')) return
  return runShareAction(revokeRecipeShare, 'Не удалось отозвать ссылку.')
}

function onRegenerate() {
  if (!window.confirm('Перегенерировать ссылку? Старая перестанет работать.')) return
  return runShareAction(regenerateRecipeShare, 'Не удалось перегенерировать ссылку.')
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

async function onDelete() {
  const id = recipe.value?.id
  if (!id) return
  if (!window.confirm(`Удалить рецепт «${recipe.value.name}»?`)) return
  deleting.value = true
  actionError.value = ''
  try {
    const { response } = await deleteRecipe(id)
    if (recipe.value?.id !== id) return
    if (response.status === 204) {
      router.push('/recipes')
      return
    }
    actionError.value = 'Не удалось удалить рецепт.'
  } catch {
    if (recipe.value?.id === id) actionError.value = 'Сервер недоступен. Попробуйте ещё раз.'
  } finally {
    if (recipe.value?.id === id) deleting.value = false
  }
}

async function onRemoveExternal() {
  const id = recipe.value?.id
  if (!id) return
  if (
    !window.confirm(
      `Убрать рецепт «${recipe.value.name}» из вашей семьи? Семья-источник не пострадает.`
    )
  )
    return
  removingLocal.value = true
  actionError.value = ''
  try {
    const { response } = await removeExternalRecipe(id)
    if (recipe.value?.id !== id) return
    if (response.status === 204) {
      router.push('/recipes')
      return
    }
    actionError.value = 'Не удалось убрать рецепт из семьи.'
  } catch {
    if (recipe.value?.id === id) actionError.value = 'Сервер недоступен. Попробуйте ещё раз.'
  } finally {
    if (recipe.value?.id === id) removingLocal.value = false
  }
}

async function onCopy() {
  const id = recipe.value?.id
  if (!id) return
  copyError.value = ''
  copying.value = true
  try {
    const { response, data } = await copyRecipe(id)
    if (recipe.value?.id !== id) return
    if (response.status === 200 || response.status === 201) {
      recipe.value = data
    } else {
      copyError.value = data?.error || 'Сохранение копии пока недоступно.'
    }
  } catch {
    if (recipe.value?.id === id) copyError.value = 'Сервер недоступен. Попробуйте ещё раз.'
  } finally {
    if (recipe.value?.id === id) copying.value = false
  }
}

watch(
  () => route.params.id,
  (id) => {
    if (!id || id === recipe.value?.id) return
    resetResourceState()
    load(id)
  }
)

onMounted(() => {
  load(route.params.id)
  loadFamily()
})
</script>

<template>
  <section>
    <p v-if="loading" class="loading">Загрузка…</p>

    <div v-else-if="error" class="card empty-state">
      <div class="empty-icon">🍽️</div>
      <p class="empty-title">{{ error }}</p>
      <div class="actions">
        <button type="button" class="btn btn--primary" @click="load()">Повторить</button>
        <router-link to="/recipes" class="btn btn--ghost">К списку рецептов</router-link>
      </div>
    </div>

    <div v-else-if="recipe">
      <div class="page-heading">
        <h2>{{ recipe.name }}</h2>
        <div v-if="isEmailVerified && !isExternal" class="page-heading-actions">
          <router-link :to="`/recipes/${recipe.id}/edit`" class="btn btn--primary">Редактировать</router-link>
          <button type="button" class="btn btn--danger" :disabled="deleting" @click="onDelete">Удалить</button>
        </div>
      </div>

      <p v-if="actionError" class="error">{{ actionError }}</p>

      <div v-if="isExternal" class="card external-banner">
        <div class="external-badges">
          <span class="badge badge--external">Внешний</span>
          <ExternalStateBadge :state="recipe.state" />
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

      <div v-if="!isExternal && recipe.copiedFromFamilyName" class="card copied-origin">
        <span class="badge badge--external">Скопировано</span>
        <span class="copied-origin-text">из семьи {{ recipe.copiedFromFamilyName }}</span>
      </div>

      <p v-if="!isEmailVerified && !isExternal" class="notice">
        Подтвердите почту, чтобы редактировать рецепт.
        <router-link to="/verify">Ввести код</router-link>
      </p>

      <RecipeBody :recipe="recipe" />

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

.external-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.75rem;
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
