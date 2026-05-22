using Rhino.DocObjects;
using Rhino.Geometry;

namespace OrbitConnector.Rhino.Converters.ToOrbit;

/// <summary>
/// Extracts display meshes for Brep geometry in a way that preserves sharp
/// face boundaries (e.g. extruded text polysurfaces) AND ships UVs that
/// account for the object's <see cref="Rhino.Render.TextureMapping"/>
/// (planar / box / cylindrical / WCS / WCS-Box / custom).
/// </summary>
internal static class RhinoBrepDisplayMeshes
{
    /// <summary>
    /// Tessellation settings that avoid welding vertices at sharp creases.
    /// </summary>
    public static MeshingParameters TessellationParameters =>
        new(MeshingParameters.Default) { JaggedSeams = true };

    /// <summary>
    /// Returns non-empty display meshes for <paramref name="brep"/>.
    /// Prefers Rhino's cached render meshes (warming the cache via
    /// <see cref="RhinoObject.CreateMeshes(MeshType, MeshingParameters, bool)"/>
    /// when they are missing, which is the common case in PRISM's headless
    /// agent), then falls back to per-face / whole-Brep tessellation.
    /// <para>
    /// Whichever path produces the mesh, we then bake the object's
    /// <see cref="Rhino.Render.TextureMapping"/> into the mesh's UV channel
    /// via <see cref="RhinoMeshUvMapping.ApplyMapping"/> so the receiver
    /// sees the same texture placement that Rhino's interactive viewport
    /// renders. Without that step <c>Mesh.CreateFromBrep</c> produces raw
    /// surface-parameter UVs and a textured object arrives in the ORBIT
    /// viewer with the texture mis-mapped (the "REBUS icon stretched all
    /// over the letters" failure mode).
    /// </para>
    /// </summary>
    /// <remarks>
    /// <see cref="RhinoObject.GetMeshes(MeshType)"/> returns the array of
    /// per-face render meshes (one entry per Brep face when faces have
    /// independent mesh parameters or materials). The interactive Rhino
    /// viewport — and the working ORBIT plug-in — append those into a single
    /// mesh before sending. Without this, a polysurface arrives at the
    /// receiver as N independent <c>displayValue</c> meshes that the viewer
    /// then renders as visually disconnected fragments.
    /// </remarks>
    public static IReadOnlyList<Mesh> Extract(Brep brep, ConversionContext context)
    {
        var rhinoObj = context.CurrentObject;
        if (rhinoObj != null)
        {
            // PRISM's headless agent never paints a viewport, so render-mesh
            // caches are typically cold on first read. Mirror the 3DConvert
            // reference and ask Rhino to bake them if they're missing — this
            // is the only call that makes Rhino apply the object's
            // TextureMapping while it tessellates.
            var renderMeshes = TryGetRenderMeshes(rhinoObj, context);
            if (renderMeshes is { Count: > 0 })
            {
                var merged = MergePreservingTopology(renderMeshes);
                if (merged.Vertices.Count > 0)
                {
                    if (renderMeshes.Count > 1)
                        context.Log?.Invoke(
                            $"[ORBIT-DIAG] merged {renderMeshes.Count} render meshes for Brep {rhinoObj.Id} into 1");

                    // Bake TextureMapping → UVs. Idempotent when the render
                    // mesh already carries mapped UVs (Rhino re-projects to
                    // the same values); essential when it doesn't.
                    RhinoMeshUvMapping.ApplyMapping(merged, rhinoObj, context, "render-mesh");
                    return new List<Mesh> { merged };
                }
            }
        }

        // Fallback: tessellate the Brep ourselves. Whole-Brep first because
        // for closed solid Breps (extruded BLOCK letters with curved
        // outlines and interior holes) per-face is BROKEN — see
        // TessellateBrep's remarks. We then bake the TextureMapping onto the
        // result the same way the render-mesh path does.
        var tess = TessellateBrep(brep);
        var totalVerts = tess.Sum(m => m.Vertices.Count);
        var totalFaces = tess.Sum(m => m.Faces.Count);
        var idStr = rhinoObj?.Id.ToString() ?? "<no-rhino-obj>";

        if (tess.Count <= 1)
        {
            context.Log?.Invoke(
                $"[ORBIT-DIAG] tessellated Brep {idStr} via whole-Brep path " +
                $"→ {tess.Count} mesh, v={totalVerts}, f={totalFaces} " +
                $"(brep.Faces={brep.Faces.Count}, isSolid={brep.IsSolid})");

            if (tess.Count == 1 && rhinoObj != null)
                RhinoMeshUvMapping.ApplyMapping(tess[0], rhinoObj, context, "whole-Brep");
            return tess;
        }

        var fallbackMerged = new Mesh();
        foreach (var m in tess)
            fallbackMerged.Append(m);
        if (fallbackMerged.Vertices.Count == 0) return tess;

        context.Log?.Invoke(
            $"[ORBIT-DIAG] merged {tess.Count} per-face fallback meshes for Brep {idStr} " +
            $"into 1 (whole-Brep tessellation returned nothing; brep.Faces={brep.Faces.Count}, " +
            $"isSolid={brep.IsSolid})");

        if (rhinoObj != null)
            RhinoMeshUvMapping.ApplyMapping(fallbackMerged, rhinoObj, context, "per-face-merged");
        return new List<Mesh> { fallbackMerged };
    }

    /// <summary>
    /// Tessellates a Brep without a parent <see cref="RhinoObject"/> (e.g.
    /// planar breps built from exploded text curves, or block-instance
    /// members where the source sub-object has no cached render mesh).
    /// </summary>
    /// <remarks>
    /// Whole-Brep first, per-face second.
    ///
    /// Per-face was tried initially because for trivial planar polysurfaces
    /// (extruded text) `Mesh.CreateFromBrep(brep)` could return null in some
    /// Rhino builds, but for closed solid Breps (e.g. extruded BLOCK letters
    /// with curved outlines and interior holes) per-face is BROKEN: each
    /// face's trim loops reference vertices/edges of adjacent faces, and
    /// <see cref="BrepFace.ToBrep"/> drops that context. The planar top and
    /// bottom faces still tessellate cleanly, but the curved side faces
    /// silently produce empty meshes — leaving the user with floating
    /// "letter tops" with no sides. The whole-Brep call has access to the
    /// full topology and produces the correct closed mesh in one shot, the
    /// same way Rhino's UI renders it.
    /// </remarks>
    public static IReadOnlyList<Mesh> TessellateBrep(Brep brep)
    {
        var mp = TessellationParameters;

        var whole = Mesh.CreateFromBrep(brep, mp);
        if (whole != null)
        {
            var nonEmpty = whole.Where(m => m != null && m.Vertices.Count > 0).ToList();
            if (nonEmpty.Count > 0) return nonEmpty;
        }

        var result = new List<Mesh>();
        for (int i = 0; i < brep.Faces.Count; i++)
        {
            var faceBrep = brep.Faces[i].ToBrep();
            var faceMeshes = Mesh.CreateFromBrep(faceBrep, mp);
            if (faceMeshes == null) continue;
            result.AddRange(faceMeshes.Where(m => m != null && m.Vertices.Count > 0));
        }
        return result;
    }

    /// <summary>
    /// Fetch the object's render meshes; if the cache is cold (typical in
    /// PRISM's headless agent where no viewport ever paints), explicitly
    /// ask Rhino to bake them with
    /// <see cref="RhinoObject.CreateMeshes(MeshType, MeshingParameters, bool)"/>
    /// and re-read. The bake step is what gives RhinoCommon a chance to
    /// apply the object's <see cref="Rhino.Render.TextureMapping"/> while
    /// it tessellates — matching what the 3DConvert IronPython reference
    /// does.
    /// </summary>
    private static List<Mesh> TryGetRenderMeshes(RhinoObject obj, ConversionContext context)
    {
        Mesh[]? renderMeshes;
        try
        {
            renderMeshes = obj.GetMeshes(MeshType.Render);
        }
        catch (Exception ex)
        {
            context.Log?.Invoke(
                $"[ORBIT-UV] GetMeshes(Render) threw for obj={obj.Id}: {ex.GetType().Name}: {ex.Message}");
            renderMeshes = null;
        }

        var nonEmpty = FilterNonEmpty(renderMeshes);
        if (nonEmpty.Count > 0)
            return nonEmpty;

        // Cache cold — bake.
        try
        {
            obj.CreateMeshes(MeshType.Render, TessellationParameters, ignoreCustomParameters: false);
            renderMeshes = obj.GetMeshes(MeshType.Render);
            nonEmpty = FilterNonEmpty(renderMeshes);
            context.Log?.Invoke(
                $"[ORBIT-UV] warmed render-mesh cache for obj={obj.Id} → {nonEmpty.Count} mesh(es)");
        }
        catch (Exception ex)
        {
            context.Log?.Invoke(
                $"[ORBIT-UV] CreateMeshes(Render) threw for obj={obj.Id}: {ex.GetType().Name}: {ex.Message}");
        }

        return nonEmpty;
    }

    /// <summary>
    /// Append every input mesh into a single output mesh without welding —
    /// preserves the per-face vertex/topology layout (needed for sharp
    /// creases AND for per-vertex UVs from box/planar mapping, which
    /// assigns different UVs to coincident vertices on different faces).
    /// </summary>
    private static Mesh MergePreservingTopology(IReadOnlyList<Mesh> parts)
    {
        if (parts.Count == 1)
            return parts[0];

        var merged = new Mesh();
        foreach (var m in parts)
            merged.Append(m);
        return merged;
    }

    private static List<Mesh> FilterNonEmpty(Mesh[]? meshes)
    {
        if (meshes == null || meshes.Length == 0)
            return new List<Mesh>();
        return meshes
            .Where(m => m != null && m.Vertices.Count > 0)
            .ToList();
    }
}
