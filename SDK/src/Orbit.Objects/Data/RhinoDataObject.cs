using Newtonsoft.Json;
using Orbit.Objects.Base;
using Orbit.Objects.Geometry;
using Orbit.Objects.Other;

namespace Orbit.Objects.Data;

/// <summary>
/// Native-Rhino round-trip wrapper.
/// <para>
/// Mirrors the wire format produced by the Speckle Rhino8 connector — the
/// connector emits one of these per source <c>Rhino.DocObjects.RhinoObject</c>
/// when the geometry can be encoded natively (Brep, Extrusion, SubD, Surface,
/// curve groups, etc.). The wrapper carries:
/// <list type="bullet">
///   <item>A <see cref="RawEncoding"/> stub pointing at a detached <see cref="Other.RawEncoding"/>
///         object containing base64-encoded <c>.3dm</c> bytes — the receiver decodes this
///         to reconstruct the original native geometry.</item>
///   <item>A <see cref="DisplayValue"/> mesh array so viewers can render the geometry
///         without understanding the native format. Each mesh carries its own
///         <c>renderMaterial</c>.</item>
///   <item>The Rhino source <see cref="Type"/> string (e.g. <c>"Brep"</c>, <c>"Extrusion"</c>)
///         so receivers can branch on the original type without parsing the 3dm.</item>
/// </list>
/// </para>
/// <para>
/// The <c>speckle_type</c> is the dual <c>Objects.Data.DataObject:Objects.Data.RhinoObject</c>
/// string used by the Speckle SDK to encode a class hierarchy. Receivers register
/// handlers for either segment.
/// </para>
/// </summary>
public class RhinoDataObject : OrbitBase
{
    /// <summary>
    /// Dual <c>base:derived</c> Speckle type name. Both segments are real type
    /// identifiers that receivers can dispatch on.
    /// </summary>
    public override string OrbitType => "Objects.Data.DataObject:Objects.Data.RhinoObject";

    /// <summary>Human-readable name (typically the Rhino object name or its type).</summary>
    [JsonProperty("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Original Rhino object type as a short string: <c>"Brep"</c>, <c>"Extrusion"</c>,
    /// <c>"SubD"</c>, <c>"Surface"</c>, <c>"Mesh"</c>, <c>"PointCloud"</c>, etc.
    /// Used by receivers to short-circuit before decoding <see cref="RawEncoding"/>.
    /// </summary>
    [JsonProperty("type")]
    public string? Type { get; set; }

    /// <summary>Schema units of the contained geometry.</summary>
    [JsonProperty("units")]
    public string? Units { get; set; }

    /// <summary>
    /// Arbitrary key/value bag for Rhino UserStrings and other per-object metadata.
    /// Always present (even if empty) to match the reference wire format.
    /// </summary>
    [JsonProperty("properties")]
    public Dictionary<string, object?> Properties { get; set; } = new();

    /// <summary>
    /// Native-encoding payload. Detached by the serialiser so the (potentially
    /// large) base64 contents do not bloat the parent JSON.
    /// </summary>
    [JsonProperty("@rawEncoding")]
    public RawEncoding? RawEncoding { get; set; }

    /// <summary>
    /// Display mesh array. Each mesh carries its own <c>renderMaterial</c>, UVs,
    /// and vertex normals. Detached so the viewer can stream them on demand.
    /// </summary>
    [JsonProperty("@displayValue")]
    public List<Mesh>? DisplayValue { get; set; }
}
