import type { TreeNode, WorldTree } from '@speckle/viewer'

const HEX_COLOR = /^#([0-9a-fA-F]{6})$/
const SHORT_HEX_COLOR = /^#([0-9a-fA-F]{3})$/

/**
 * Parse PRISM/ORBIT category colour tokens written by the Rhino connector.
 * Accepts `#rrggbb` and `#rgb` hex strings.
 */
export function parseCategoryColor(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  if (!trimmed.length) return null

  if (HEX_COLOR.test(trimmed)) return trimmed.toLowerCase()

  const shortMatch = trimmed.match(SHORT_HEX_COLOR)
  if (shortMatch) {
    const [r, g, b] = shortMatch[1]
    return `#${r}${r}${g}${g}${b}${b}`.toLowerCase()
  }

  return null
}

/**
 * Resolve a fixture category colour from the same metadata locations the
 * connector writes (`categoryColor` on FixtureType, InstanceProxy properties,
 * root collection properties, etc.).
 */
export function resolveCategoryColor(
  raw: Record<string, unknown> | undefined | null
): string | null {
  if (!raw) return null

  const props = raw.properties as Record<string, unknown> | undefined
  const metadata = raw.metadata as Record<string, unknown> | undefined
  const definition = raw.definition as Record<string, unknown> | undefined
  const definitionMetadata = definition?.metadata as Record<string, unknown> | undefined

  const candidates = [
    raw.categoryColor,
    raw['@categoryColor'],
    props?.categoryColor,
    props?.['@categoryColor'],
    metadata?.categoryColor,
    definition?.categoryColor,
    definition?.['@categoryColor'],
    definitionMetadata?.categoryColor
  ]

  for (const candidate of candidates) {
    const parsed = parseCategoryColor(candidate)
    if (parsed) return parsed
  }

  return null
}

/**
 * Walk upward from a tree node to inherit a fixture category colour from an
 * ancestor FixtureType / collection when the leaf itself has none.
 */
export function resolveCategoryColorForNode(node: TreeNode): string | null {
  let current: TreeNode | null = node
  while (current) {
    const direct = resolveCategoryColor(current.model?.raw as Record<string, unknown>)
    if (direct) return direct
    current = current.parent ?? null
  }
  return null
}

const FIXTURE_TYPE_HINT =
  /FixtureType|Instances\.InstanceProxy|Lighting\.Fixture|Objects\.Lighting/i

function isFixtureRelatedNode(raw: Record<string, unknown>): boolean {
  const speckleType = String(raw.speckle_type ?? '')
  if (FIXTURE_TYPE_HINT.test(speckleType)) return true

  const props = raw.properties as Record<string, unknown> | undefined
  return Boolean(
    props?.prismFixtureTypeId ||
      props?.category ||
      props?.fixtureName ||
      props?.manufacturer
  )
}

/**
 * Build colour groups for FilteringExtension.setUserObjectColors from fixture
 * metadata on the loaded world tree.
 */
export function buildFixtureColorGroups(
  worldTree: WorldTree
): { objectIds: string[]; color: string }[] {
  const groups = new Map<string, string[]>()

  worldTree.walk((node: TreeNode) => {
    const raw = node.model?.raw as Record<string, unknown> | undefined
    const objectId = raw?.id
    if (typeof objectId !== 'string' || !objectId.length) return true

    const color = resolveCategoryColorForNode(node)
    if (!color) return true

    // Prefer fixture-related nodes; still honour explicit categoryColor elsewhere.
    if (!isFixtureRelatedNode(raw) && !resolveCategoryColor(raw)) return true

    const ids = groups.get(color) ?? []
    ids.push(objectId)
    groups.set(color, ids)
    return true
  })

  return [...groups.entries()].map(([color, objectIds]) => ({ color, objectIds }))
}
