<script setup>
// Презентационная навигация по неделям: вью владеет своим контейнером
// (`.nav` в плане, `.week-nav` в списке покупок), а компонент — кнопками и подписью.
defineProps({
  label: { type: String, default: '' }
})

defineEmits(['prev', 'next'])
</script>

<template>
  <button
    type="button"
    class="btn btn--ghost btn--small"
    aria-label="Предыдущая неделя"
    @click="$emit('prev')"
  >
    ←
  </button>
  <span class="week-label">{{ label }}</span>
  <button
    type="button"
    class="btn btn--ghost btn--small"
    aria-label="Следующая неделя"
    @click="$emit('next')"
  >
    →
  </button>
</template>

<style scoped>
.week-label {
  font-weight: 700;
  font-size: 0.95rem;
  text-align: center;
  flex: 1;
}
</style>
