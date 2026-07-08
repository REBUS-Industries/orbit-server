import { FilteringExtension, ViewMode } from '@speckle/viewer'
import { useInjectedViewerState } from '~/lib/viewer/composables/setup'
import { useOnViewerLoadComplete } from '~/lib/viewer/composables/viewer'
import { buildFixtureColorGroups } from '~/lib/viewer/helpers/fixtureColor'

/**
 * When the ORBIT fixture-colour view mode is active, tint renderable objects
 * using PRISM/ORBIT `categoryColor` metadata via FilteringExtension.
 */
export function useFixtureColorViewPostSetup() {
  if (import.meta.server) return

  const {
    ui: { viewMode },
    viewer: { instance }
  } = useInjectedViewerState()

  const filteringExtension = () => instance.getExtension(FilteringExtension)

  const clearFixtureColors = () => {
    filteringExtension()?.removeUserObjectColors()
  }

  const applyFixtureColors = () => {
    if (viewMode.mode.value !== ViewMode.FIXTURE_COLOR) return

    const tree = instance.getWorldTree()
    if (!tree) return

    const groups = buildFixtureColorGroups(tree)
    if (!groups.length) {
      clearFixtureColors()
      return
    }

    filteringExtension()?.setUserObjectColors(groups)
  }

  useOnViewerLoadComplete(
    () => {
      applyFixtureColors()
    },
    { initialOnly: true }
  )

  watch(
    () => viewMode.mode.value,
    (mode, previousMode) => {
      if (mode === ViewMode.FIXTURE_COLOR) {
        // Wait for ViewModes to swap pipelines before applying filter colours.
        nextTick(() => applyFixtureColors())
        return
      }

      if (previousMode === ViewMode.FIXTURE_COLOR) {
        clearFixtureColors()
      }
    }
  )

  onBeforeUnmount(() => {
    clearFixtureColors()
  })
}
