using Rhino.DocObjects;
using Rhino.Geometry;

namespace OrbitConnector.Rhino.Converters.ToOrbit;

/// <summary>
/// Extracts display meshes for Brep geometry in a way that preserves sharp
/// face boundaries (e.g. extruded text polysurfaces).
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
    /// Prefers Rhino's cached render meshes (matches viewport + working Speckle
    /// connector), then per-face tessellation, then whole-Brep tessellation.
    /// </summary>
    public static IReadOnlyList<Mesh> Extract(Brep brep, ConversionContext context)
    {
        // Block-instance members arrive here already transformed into the
        // placement; the parent object on the context is the definition member
        // whose cached render mesh lives in definition-local space and would
        // place the geometry at the block origin. Tessellate the transformed
        // Brep directly in that case.
        var rhinoObj = context.GeometryIsPreTransformed ? null : context.CurrentObject;
        if (rhinoObj != null)
        {
            var renderMeshes = rhinoObj.GetMeshes(MeshType.Render);
            if (renderMeshes is { Length: > 0 })
            {
                var fromRender = renderMeshes
                    .Where(m => m != null && m.Vertices.Count > 0)
                    .ToList();
                if (fromRender.Count > 0)
                    return fromRender;
            }
        }

        return TessellateBrep(brep);
    }

    /// <summary>
    /// Tessellates a Brep without a parent <see cref="RhinoObject"/> (e.g.
    /// planar breps built from exploded text curves).
    /// </summary>
    public static IReadOnlyList<Mesh> TessellateBrep(Brep brep)
    {
        var mp = TessellationParameters;
        var result = new List<Mesh>();

        for (int i = 0; i < brep.Faces.Count; i++)
        {
            var faceBrep = brep.Faces[i].ToBrep();
            var faceMeshes = Mesh.CreateFromBrep(faceBrep, mp);
            if (faceMeshes == null) continue;
            result.AddRange(faceMeshes.Where(m => m != null && m.Vertices.Count > 0));
        }

        if (result.Count > 0)
            return result;

        var whole = Mesh.CreateFromBrep(brep, mp);
        if (whole == null)
            return Array.Empty<Mesh>();

        return whole.Where(m => m != null && m.Vertices.Count > 0).ToList();
    }
}
