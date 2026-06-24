import { Box3, Group, Matrix4, Vector3 } from 'three'
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
    if (this.transformControls) {
      this.transformControls.detach()
      this.viewer.getRenderer().scene.remove(this.transformControls)
      this.transformControls = null
    }
    this.anchor = null
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

    if (rawMatrix?.length === 16) {
      matrix.fromArray(rawMatrix)
    } else {
      matrix.setPosition(center)
    }

    this.detach()

    const camera = this.cameraProvider.renderingCamera
    this.transformControls = new TransformControls(
      camera,
      this.viewer.getContainer()
    )
    this.transformControls.enabled = false
    this.transformControls.setMode('translate')
    this.transformControls.setSize(gizmoScale)
    this.transformControls.layers.set(ObjectLayers.PROPS)

    this.anchor = new Group()
    this.anchor.applyMatrix4(matrix)
    if (!rawMatrix?.length) {
      this.anchor.position.copy(center)
    }

    renderer.scene.add(this.transformControls)
    this.transformControls.attach(this.anchor)
    this.viewer.requestRender()
  }

  public onRender(): void {
    if (this.transformControls?.visible) {
      this.transformControls.updateMatrixWorld()
    }
  }
}
