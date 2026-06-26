import type { SpeckleObject } from '~/lib/viewer/helpers/sceneExplorer'

export type HeaderSubheader = {
  header: string
  subheader: string
}

export function getHeaderAndSubheaderForSpeckleObject(
  object: SpeckleObject
): HeaderSubheader {
  const rawSpeckleData = object
  const speckleData = object
  const speckleType = speckleData.speckle_type as string
  // PRISM fixtures carry an optional custom "pretty" display name; prefer it
  // over the canonical name wherever we render a fixture's human label
  // (rule: displayName ?? name). Display-only — never key logic on this.
  const prettyName = resolveDisplayName(rawSpeckleData)
  if (!speckleType)
    return {
      header: cleanName(
        prettyName ||
          (rawSpeckleData.name as string) ||
          (rawSpeckleData.Name as string) ||
          (rawSpeckleData.speckle_type as string)
      ),
      subheader: ''
    } as HeaderSubheader

  // Handle revit objects
  if (speckleType.toLowerCase().includes('revit')) {
    if (speckleType.toLowerCase().includes('familyinstance')) {
      // TODO
      const famHeader = `${rawSpeckleData.family as string} (${
        rawSpeckleData.category as string
      })`
      const famSubheader = rawSpeckleData.type as string
      return { header: cleanName(famHeader), subheader: famSubheader }
    }

    if (speckleType.toLowerCase().includes('revitelementtype')) {
      return {
        header: cleanName(rawSpeckleData.family as string),
        subheader: `${rawSpeckleData.type as string} / ${
          rawSpeckleData.category as string
        }` //rawSpeckleData.type + ' / ' + rawSpeckleData.category
      }
    }
    const anyHeader = speckleType.split('.').reverse()[0]
    const anySubheaderParts = [rawSpeckleData.category, rawSpeckleData.type].filter(
      (part) => !!part
    )
    return {
      header: cleanName(anyHeader),
      subheader: anySubheaderParts.join(' / ')
    } as HeaderSubheader
  }

  // Handle ifc objects
  if (speckleType.toLowerCase().includes('ifc')) {
    const name = (rawSpeckleData.Name || rawSpeckleData.name) as string
    return {
      header: cleanName((name as string) || (rawSpeckleData.speckle_type as string)),
      subheader: name ? rawSpeckleData.speckle_type : rawSpeckleData.id
    } as HeaderSubheader
  }

  // Handle geometry objects
  if (speckleType.toLowerCase().includes('objects.geometry')) {
    return {
      header: cleanName(speckleType.split('.').reverse()[0]),
      subheader: rawSpeckleData.id
    } as HeaderSubheader
  }

  // Handle collections (layers, levels, IFC groups, etc.)
  if (speckleType.includes('Collections.Collection')) {
    const collectionType = rawSpeckleData.collectionType as string | undefined
    // Compound speckle_type e.g. ...Collection:Speckle.Core.Models.Layer (old Rhino connector)
    const isLegacyLayer = speckleType.includes(':Speckle.Core.Models.Layer')
    const typeLabel =
      isLegacyLayer ? 'Layer' :
      collectionType === 'layer' ? 'Layer' :
      collectionType === 'rhino layer' ? 'Layer' :
      collectionType === 'model' ? 'Model' :
      collectionType === 'rhino model' ? 'Model' :
      collectionType === 'root' ? 'Model' :
      collectionType === 'level' ? 'Level' :
      collectionType === 'type' ? 'Type' :
      'Collection'
    return {
      header: cleanName(
        prettyName ||
          (rawSpeckleData.name as string) ||
          (rawSpeckleData.Name as string) ||
          'Collection'
      ),
      subheader: typeLabel
    } as HeaderSubheader
  }

  // LAST DITCH EFFORT
  return {
    header: cleanName(
      prettyName ||
        (rawSpeckleData.name as string) ||
        (rawSpeckleData.Name as string) ||
        (rawSpeckleData.speckle_type as string).split('.').reverse()[0]
    ),
    subheader: speckleType.split('.').reverse()[0]
  } as HeaderSubheader
}

/**
 * PRISM fixtures (published via publish-orbit) carry an optional custom
 * "pretty" display name, separate from the canonical `name`. It is written to:
 *  - root `Collection` `properties.displayName`
 *  - `Orbit.Objects.Lighting.FixtureType` top-level `displayName`
 *  - `FixtureType` `metadata.displayName`
 * Returns the trimmed, non-empty pretty name when present, else undefined.
 * Display-only — never key dedup/routing/branch logic on this value.
 */
export function resolveDisplayName(
  object: Record<string, unknown> | SpeckleObject
): string | undefined {
  const obj = object as Record<string, unknown>
  const props = obj.properties as Record<string, unknown> | undefined
  const metadata = obj.metadata as Record<string, unknown> | undefined
  const candidates = [obj.displayName, props?.displayName, metadata?.displayName]
  for (const candidate of candidates) {
    if (typeof candidate === 'string' && candidate.trim().length) {
      return candidate.trim()
    }
  }
  return undefined
}

/**
 * Human label for a fixture: the custom display name when set, else the
 * canonical name. Mirrors the PRISM handoff rule `displayName ?? name`.
 */
export function fixtureLabel(props: {
  displayName?: string | null
  name: string
}): string {
  const pretty = props.displayName?.trim()
  return pretty && pretty.length ? pretty : props.name
}

function cleanName(name: string) {
  if (!name) return 'Unnamed'
  let cleanName = name.trim()

  if (cleanName.startsWith('@')) cleanName = cleanName.substring(1) // remove "@" signs
  // TODO check if this is all we need
  return cleanName
}

/**
 * Encodes a bunch of conventions around getting target object ids from random speckle objects or created
 * @param object
 */
export function getTargetObjectIds(object: Record<string, unknown> | SpeckleObject) {
  // Handle array collections (generated on the fly in the tree explorer)
  if (object.speckle_type === 'Array Collection' && Array.isArray(object.children)) {
    return object.children
      .map((k) => (k as { referencedId: string }).referencedId)
      .filter((id) => !!id && typeof id === 'string')
  }
  // Handles both actual collection objecs( ala IFC) and individual objects
  if (object.id && typeof object.id === 'string') {
    // Extract object ID from URL if it's a full URL
    // or return the ID as-is if it's already just an object ID
    const objectId = object.id.includes('/objects/')
      ? object.id.split('/').reverse()[0]
      : object.id
    return [objectId]
  }
  return []
}
