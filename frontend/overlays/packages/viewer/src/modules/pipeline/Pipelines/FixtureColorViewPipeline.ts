import SpeckleRenderer from '../../SpeckleRenderer.js'
import { BlendPass } from '../Passes/BlendPass.js'
import { ClearFlags, ObjectVisibility } from '../Passes/GPass.js'
import { GeometryPass } from '../Passes/GeometryPass.js'
import { ObjectLayers } from '../../../IViewer.js'
import { ProgressiveAOPass } from '../Passes/ProgressiveAOPass.js'
import { ProgressivePipeline } from './ProgressivePipeline.js'
import { ShadedPass } from '../Passes/ShadedPass.js'
import { StencilMaskPass } from '../Passes/StencilMaskPass.js'
import { StencilPass } from '../Passes/StencilPass.js'
import { DefaultPipelineOptions, PipelineOptions } from './Pipeline.js'
import { EdgesPipeline } from './EdgesPipeline.js'
import { WorldTree } from '../../tree/WorldTree.js'
import { DepthPass } from '../Passes/DepthPass.js'

/**
 * ORBIT fixture-colour view mode.
 *
 * Starts from the Arctic conceptual pipeline (edges + ambient occlusion) but
 * renders meshes through {@link ShadedPass} so per-object fixture colours from
 * PRISM/ORBIT metadata can be applied via FilteringExtension.setUserObjectColors.
 */
export class FixtureColorViewPipeline extends ProgressivePipeline {
  protected accumulationFrameCount: number = 16

  constructor(
    speckleRenderer: SpeckleRenderer,
    options: PipelineOptions = DefaultPipelineOptions,
    tree: WorldTree
  ) {
    super(speckleRenderer)

    const edgesPipeline = options.edges ? new EdgesPipeline(speckleRenderer) : null

    const depthPass = !options.edges ? new DepthPass() : null
    if (depthPass) {
      depthPass.setLayers([ObjectLayers.STREAM_CONTENT_MESH])
      depthPass.setVisibility(ObjectVisibility.DEPTH)
      depthPass.setJitter(true)
      depthPass.setClearColor(0x000000, 1)
      depthPass.setClearFlags(ClearFlags.COLOR | ClearFlags.DEPTH)
    }

    const depthTex = options.edges
      ? edgesPipeline?.depthPass.depthTexture
      : depthPass?.outputTarget?.texture

    const depthSubPipelineDynamic =
      (options.edges ? edgesPipeline?.dynamicPasses : []) || []
    const depthSubPipelineProgressive =
      (options.edges
        ? edgesPipeline?.progressivePasses
        : depthPass
          ? [depthPass]
          : []) || []

    const shadedPass = new ShadedPass(tree, speckleRenderer)
    shadedPass.setLayers([ObjectLayers.STREAM_CONTENT_MESH, ObjectLayers.PROPS])
    shadedPass.setClearColor(0x000000, 0)
    shadedPass.setClearFlags(ClearFlags.COLOR)
    shadedPass.outputTarget = null

    const nonMeshPass = new GeometryPass()
    nonMeshPass.setLayers([
      ObjectLayers.STREAM_CONTENT_LINE,
      ObjectLayers.STREAM_CONTENT_POINT,
      ObjectLayers.STREAM_CONTENT_POINT_CLOUD,
      ObjectLayers.STREAM_CONTENT_TEXT
    ])

    const progressiveAOPass = new ProgressiveAOPass()
    progressiveAOPass.setTexture('tDepth', depthTex)
    progressiveAOPass.accumulationFrames = this.accumulationFrameCount
    progressiveAOPass.options = {
      kernelRadius: 100,
      kernelSize: 64
    }
    progressiveAOPass.setClearColor(0xffffff, 1)

    const blendPass = new BlendPass()
    blendPass.options = { blendAO: true, blendEdges: options.edges }
    blendPass.setTexture('tAo', progressiveAOPass.outputTarget?.texture)
    blendPass.setTexture(
      'tEdges',
      options.edges ? edgesPipeline?.outputTexture : undefined
    )
    blendPass.accumulationFrames = this.accumulationFrameCount

    const blendPassDynamic = new BlendPass()
    blendPassDynamic.options = { blendAO: false, blendEdges: options.edges }
    blendPassDynamic.setTexture(
      'tEdges',
      options.edges ? edgesPipeline?.outputTextureDynamic : undefined
    )
    blendPassDynamic.accumulationFrames = this.accumulationFrameCount

    const stencilPass = new StencilPass()
    stencilPass.setVisibility(ObjectVisibility.STENCIL)
    stencilPass.setLayers([ObjectLayers.STREAM_CONTENT_MESH])

    const stencilMaskPass = new StencilMaskPass()
    stencilMaskPass.setVisibility(ObjectVisibility.STENCIL)
    stencilMaskPass.setLayers([ObjectLayers.STREAM_CONTENT_MESH])
    stencilMaskPass.setClearFlags(ClearFlags.DEPTH)

    const overlayPass = new GeometryPass()
    overlayPass.setLayers([ObjectLayers.OVERLAY, ObjectLayers.MEASUREMENTS])

    this.dynamicStage.push(
      ...depthSubPipelineDynamic,
      stencilPass,
      shadedPass,
      nonMeshPass,
      ...(options.edges ? [blendPassDynamic] : []),
      stencilMaskPass,
      overlayPass
    )
    this.progressiveStage.push(
      ...depthSubPipelineProgressive,
      stencilPass,
      shadedPass,
      nonMeshPass,
      stencilMaskPass,
      progressiveAOPass,
      blendPass,
      overlayPass
    )
    this.passthroughStage.push(
      stencilPass,
      shadedPass,
      nonMeshPass,
      stencilMaskPass,
      blendPass,
      overlayPass
    )

    this.passList = this.dynamicStage
  }
}
