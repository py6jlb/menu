<script setup>
import { computed } from 'vue'
import { externalState } from '../constants/external'

const props = defineProps({
  state: { type: String, default: null },
  // label — полная метка (карточки/детальная), slot — короткая для ячейки плана,
  // mobile — подпись для мобильного слота.
  variant: { type: String, default: 'label' }
})

const badge = computed(() => externalState(props.state))

const text = computed(() => {
  if (!badge.value) return ''
  if (props.variant === 'slot') return badge.value.slotLabel
  if (props.variant === 'mobile') return badge.value.mobileLabel
  return badge.value.label
})
</script>

<template>
  <span
    v-if="badge"
    class="badge"
    :class="`badge--${badge.variant}`"
    :title="variant === 'slot' ? badge.slotTitle : undefined"
  >
    {{ text }}
  </span>
</template>
