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
            var renderMeshes = rhinoObj.GetMeshes(MeshType.Render);
            if (renderMeshes is { Length: > 0 })
            {
                var nonEmpty = renderMeshes
                    .Where(m => m != null && m.Vertices.Count > 0)
                    .ToList();
                if (nonEmpty.Count == 1)
                    return nonEmpty;
                if (nonEmpty.Count > 1)
                {
                    var merged = new Mesh();
                    foreach (var m in nonEmpty)
                        merged.Append(m);
                    if (merged.Vertices.Count > 0)
                    {
                        context.Log?.Invoke(
                            $"[ORBIT-DIAG] merged {nonEmpty.Count} render meshes for Brep {rhinoObj.Id} into 1");
                        return new List<Mesh> { merged };
                    }
                }
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
