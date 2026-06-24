import { Group, Matrix4, Vector3 } from 'three'
import { TransformControls } from './TransformControls.js'
import { Extension } from './Extension.js'
import { CameraController } from './CameraController.js'
import { ObjectLayers, type IViewer } from '../../IViewer.js'

/**
 * Read-only transform gizmo anchored at the selected object datum (bbox centre
 * or instance transform matrix). Uses the same TransformControls visuals as the
 * section box tool but does not allow dragging.
 */
export class DatumGizmoExtension extends Extension {
  public get inject() {
    return [CameraController]
  }

  private anchor: Group | null = null
  private transformControls: TransformControls | null = null
  private _visible = false
  private objectIds: string[] = []

  public constructor(viewer: IViewer, protected cameraProvider: CameraController) {
    super(viewer)
  }

  public get visible(): boolean {
    return this._visible
  }

  public setVisible(visible: boolean): void {
    this._visible = visible
    if (!visible) {
      this.detach()
    } else if (this.objectIds.length) {
      this.updateGizmo(this.objectIds)
    }
    this.viewer.requestRender()
  }

  public updateSelection(objectIds: string[]): void {
    this.objectIds = objectIds
    if (this._visible && objectIds.length) {
      this.updateGizmo(objectIds)
    } else {
      this.detach()
    }
  }

  private detach(): void {
    const scene = this.viewer.getRenderer().scene
    if (this.transformControls) {
      this.transformControls.detach()
      // The visual gizmo is `_root` (a three.js Object3D), not the controls object.
      scene.remove(this.transformControls._root)
      this.transformControls = null
    }
    if (this.anchor) {
      scene.remove(this.anchor)
      this.anchor = null
    }
  }

  private updateGizmo(objectIds: string[]): void {
    const renderer = this.viewer.getRenderer()
    const box = renderer.boxFromObjects(objectIds)
    if (box.isEmpty()) {
      this.detach()
      return
    }

    const center = box.getCenter(new Vector3())
    const size = box.getSize(new Vector3())
    const gizmoScale = Math.min(Math.max(Math.max(size.x, size.y, size.z) * 0.2, 0.4), 3)

    const tree = this.viewer.getWorldTree()
    const nodes = objectIds.flatMap((id) => tree.findId(id) || [])
    const matrix = new Matrix4()
    const raw = nodes[0]?.model?.raw as Record<string, unknown> | undefined
    const rawMatrix =
      (raw?.matrix as number[] | undefined) || (raw?.transform as number[] | undefined)
    const hasMatrix = Array.isArray(rawMatrix) && rawMatrix.length === 16

    this.detach()

    const camera = this.cameraProvider.renderingCamera
    const controls = new TransformControls(
      camera,
      renderer.renderer.domElement
    )
    // Read-only: show the gizmo but ignore pointer interaction.
    controls.enabled = false
    controls.setMode('translate')
    controls.setSize(gizmoScale)
    // Layers are NOT recursive in three.js — Speckle's pipeline only renders the
    // PROPS layer, so every gizmo child object must be assigned to it (mirrors
    // SectionTool), otherwise the gizmo never renders.
    for (let k = 0; k < controls._root.children.length; k++) {
      controls._root.children[k].traverse((obj: { layers: { set: (n: number) => void } }) =>
        obj.layers.set(ObjectLayers.PROPS)
      )
    }

    const anchor = new Group()
    if (hasMatrix) {
      anchor.applyMatrix4(matrix.fromArray(rawMatrix as number[]))
    } else {
      anchor.position.copy(center)
    }
    anchor.layers.set(ObjectLayers.PROPS)
    renderer.scene.add(anchor)

    renderer.scene.add(controls._root)
    controls.attach(anchor)

    this.transformControls = controls
    this.anchor = anchor
    this.viewer.requestRender()
  }

  public onRender(): void {
    this.transformControls?._root?.updateMatrixWorld()
  }
}
