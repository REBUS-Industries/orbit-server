#!/usr/bin/env node
/**
 * Self-hosted ORBIT: allow Saved Views on projects that are not in a workspace.
 *
 * Speckle Cloud gates Saved Views behind workspace plan features. The rebus-dev
 * fork unlocks this for self-hosted by passing `allowUnworkspaced: true` to
 * ensureCanUseProjectWorkspacePlanFeatureFragment in @speckle/shared authz.
 *
 * If the base ghcr.io/rebus-orbit/orbit-server image was built from that fork,
 * these files already contain the flag and this script is a no-op. If the base
 * is stock upstream, we patch the compiled JS in-place (same tweak as commit
 * 167111505 on CheekiSkrub/speckle-server-dev).
 */
const fs = require('fs')

const ROOT = process.env.SPECKLE_ROOT || '/speckle-server'
const TARGETS = [
  `${ROOT}/packages/shared/dist/authz/policies/project/savedViews/canCreate.js`,
  `${ROOT}/packages/shared/dist/authz/fragments/savedViews.js`
]

const ALREADY_PATCHED =
  /feature:\s*WorkspacePlanFeatures\.SavedViews,\s*\n\s*allowUnworkspaced:\s*true/

let patchedAny = false

for (const path of TARGETS) {
  if (!fs.existsSync(path)) {
    console.warn(`[saved-views-authz] skip missing ${path}`)
    continue
  }

  const original = fs.readFileSync(path, 'utf8')
  if (ALREADY_PATCHED.test(original)) {
    console.log(`[saved-views-authz] already patched ${path}`)
    continue
  }

  if (!original.includes('WorkspacePlanFeatures.SavedViews')) {
    console.warn(`[saved-views-authz] no SavedViews sites in ${path}`)
    continue
  }

  const updated = original.replace(
    /feature:\s*WorkspacePlanFeatures\.SavedViews(?!\s*,\s*\n\s*allowUnworkspaced:\s*true)/g,
    'feature: WorkspacePlanFeatures.SavedViews,\n      allowUnworkspaced: true'
  )

  if (updated === original) {
    console.error(`[saved-views-authz] ERR: expected to patch ${path} but pattern did not match`)
    process.exit(1)
  }

  fs.writeFileSync(path, updated)
  patchedAny = true
  console.log(`[saved-views-authz] patched ${path}`)
}

if (!patchedAny) {
  console.log('[saved-views-authz] no changes needed (base image already self-hosted unlocked)')
}
