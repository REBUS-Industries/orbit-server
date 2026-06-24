import { DatumGizmoExtension } from '@speckle/viewer'
import { useInjectedViewer, useInjectedViewerInterfaceState } from '~/lib/viewer/composables/setup'

export function useDatumGizmoPostSetup() {
  if (import.meta.server) return

  const { instance } = useInjectedViewer()
  const {
    filters: { selectedObjects }
  } = useInjectedViewerInterfaceState()

  const extension = computed(() => instance.getExtension(DatumGizmoExtension))

  watch(
    () => selectedObjects.value.map((o) => o.id).filter(Boolean),
    (ids) => {
      extension.value?.updateSelection(ids as string[])
    },
    { immediate: true, deep: true }
  )
}
