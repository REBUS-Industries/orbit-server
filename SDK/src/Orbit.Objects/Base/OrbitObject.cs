using Newtonsoft.Json;

namespace Orbit.Objects.Base;

/// <summary>
/// A named container object — equivalent to a "DataObject" or "Collection" in the ORBIT data model.
/// Used to represent Rhino layers, project roots, and any logical grouping of geometry.
///
/// Geometry is stored in <see cref="DisplayValue"/> as an array of displayable primitives
/// (typically <see cref="Orbit.Objects.Geometry.Mesh"/> objects) for viewers that cannot
/// handle native geometry types. Native geometry types (Brep, NurbsCurve etc.) are stored
/// as typed children and referenced via the closure table.
/// </summary>
public class OrbitObject : OrbitBase
{
    /// <summary>Human-readable name (e.g. layer name, project name).</summary>
    [JsonProperty("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Array of displayable geometry primitives. Used by the 3D viewer and by
    /// host applications that receive an unknown type — they fall back to rendering
    /// whatever is in displayValue.
    /// </summary>
    [JsonProperty("displayValue")]
    public List<OrbitBase>? DisplayValue { get; set; }

    /// <summary>Child objects (nested collections, geometry objects).</summary>
    [JsonProperty("elements")]
    public List<OrbitBase>? Elements { get; set; }

    /// <summary>Source application identifier (e.g. "OrbitRhino").</summary>
    [JsonProperty("sourceApplication")]
    public string? SourceApplication { get; set; }

    /// <summary>Schema units (e.g. "mm", "m", "ft").</summary>
    [JsonProperty("units")]
    public string? Units { get; set; }
}
