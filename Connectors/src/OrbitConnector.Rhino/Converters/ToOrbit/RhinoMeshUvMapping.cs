using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Render;

namespace OrbitConnector.Rhino.Converters.ToOrbit;

/// <summary>
/// Bake a Rhino <see cref="RhinoObject"/>'s object-level
/// <see cref="TextureMapping"/> (Planar / Box / Cylindrical / Spherical /
/// WCS / WCS-Box / custom) into a tessellated <see cref="Mesh"/>'s
/// <see cref="Mesh.TextureCoordinates"/>.
///
/// <para>
/// Why this exists: <see cref="Mesh.CreateFromBrep(Brep, MeshingParameters)"/>
/// produces a mesh whose UVs are raw surface-parameter UVs — they do NOT
/// account for the object's <see cref="TextureMapping"/>. For textured
/// objects that look correct in Rhino's interactive viewport but ship to
/// the ORBIT viewer with mis-mapped textures, this is the missing step:
/// after tessellation we have to call
/// <see cref="Mesh.SetTextureCoordinates(TextureMapping, Transform, bool)"/>
/// with the mapping we read off the <see cref="RhinoObject"/> so that
/// Rhino re-projects each vertex through the mapping primitive (box,
/// planar, etc.) and the resulting UVs match what the renderer would have
/// computed.
/// </para>
///
/// <para>
/// This mirrors what the 3DConvert IronPython pipeline does in
/// <c>rhino_conv.py::_apply_mapping_to_mesh</c> — the reference whose
/// commits land in Orbit with correct UVs.
/// </para>
/// </summary>
internal static class RhinoMeshUvMapping
{
    /// <summary>
    /// Resolve the first <see cref="TextureMapping"/> attached to
    /// <paramref name="obj"/>. Channel 1 is tried first (Rhino's default
    /// channel for the base-colour mapping); if that returns nothing we
    /// fall back to whichever channels the object enumerates.
    /// </summary>
    /// <returns>
    /// The mapping, or <c>null</c> when the object has no mapping (which
    /// is the typical case for surface-parameter mapping — the caller
    /// should leave the mesh's existing UVs untouched).
    /// </returns>
    public static TextureMapping? GetObjectMapping(RhinoObject obj)
    {
        if (obj == null) return null;

        try
        {
            var m = obj.GetTextureMapping(1);
            if (m != null) return m;
        }
        catch { /* fall through to channel iteration */ }

        try
        {
            var channels = obj.GetTextureChannels();
            if (channels != null)
            {
                foreach (var chId in channels)
                {
                    try
                    {
                        var m = obj.GetTextureMapping(chId);
                        if (m != null) return m;
                    }
                    catch { /* try next channel */ }
                }
            }
        }
        catch { /* no channels enumerator available */ }

        return null;
    }

    /// <summary>
    /// Apply <paramref name="obj"/>'s <see cref="TextureMapping"/> (if any)
    /// to <paramref name="mesh"/> in-place, baking the resulting UVs into
    /// <see cref="Mesh.TextureCoordinates"/>. Emits an
    /// <c>[ORBIT-UV]</c> diagnostic line through <paramref name="context"/>
    /// so the result is visible in the agent log and the admin UI.
    /// </summary>
    /// <param name="source">
    /// Short label identifying the meshing path that produced
    /// <paramref name="mesh"/> (e.g. <c>"render-mesh"</c>,
    /// <c>"whole-Brep"</c>, <c>"per-face-merged"</c>). Helps correlate
    /// diagnostics in <c>job_logs</c>.
    /// </param>
    /// <returns><c>true</c> when a mapping was applied; <c>false</c> when the
    /// object has no mapping or the call failed.</returns>
    public static bool ApplyMapping(
        Mesh mesh,
        RhinoObject obj,
        ConversionContext context,
        string source)
    {
        if (mesh == null || obj == null) return false;

        var mapping = GetObjectMapping(obj);
        if (mapping == null)
        {
            // No mapping = surface-parameter UVs are intended. Log once
            // per object so we know we considered it (but don't mutate the
            // mesh).
            context.Log?.Invoke(
                $"[ORBIT-UV] no TextureMapping on obj={obj.Id} ({source}); " +
                $"leaving {mesh.TextureCoordinates.Count} surface-param UV(s) intact " +
                $"(vertices={mesh.Vertices.Count})");
            return false;
        }

        try
        {
            // Transform.Identity matches the 3DConvert reference. The
            // mapping is already in object space; RhinoCommon resolves
            // world-space mapping (WCS / WCS-Box) internally using the
            // object's transform — no extra xform from the caller needed.
            // The bool-returning overload was removed somewhere on the
            // RhinoCommon 8.x line; the void overload below remains. We
            // infer success from the post-call TC count matching the V
            // count (which is how the 3DConvert IronPython reference
            // verifies the apply too).
            mesh.SetTextureCoordinates(mapping, Transform.Identity, false);
        }
        catch (Exception ex)
        {
            context.Log?.Invoke(
                $"[ORBIT-UV] SetTextureCoordinates THREW on obj={obj.Id} ({source}) " +
                $"mappingType={mapping.MappingType}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }

        var tcCount = mesh.TextureCoordinates.Count;
        var vCount  = mesh.Vertices.Count;
        var applied = tcCount > 0 && tcCount == vCount;
        var sample  = SampleUvs(mesh, 4);

        context.Log?.Invoke(
            $"[ORBIT-UV] applied {mapping.MappingType} mapping to {source} mesh " +
            $"for obj={obj.Id}: appliedOk={applied} tcCount={tcCount} vCount={vCount} " +
            $"firstUVs={sample}");

        return applied;
    }

    /// <summary>
    /// Format the first <paramref name="n"/> UV pairs as a compact string
    /// like <c>[(0.00,0.00),(1.00,0.00),(1.00,1.00),(0.00,1.00)]</c>.
    /// </summary>
    private static string SampleUvs(Mesh mesh, int n)
    {
        var tcl = mesh.TextureCoordinates;
        int count = Math.Min(n, tcl.Count);
        if (count == 0) return "[]";

        var sb = new System.Text.StringBuilder(64);
        sb.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            var tc = tcl[i];
            sb.Append('(').Append(tc.X.ToString("F3"))
              .Append(',').Append(tc.Y.ToString("F3"))
              .Append(')');
        }
        sb.Append(']');
        return sb.ToString();
    }
}
