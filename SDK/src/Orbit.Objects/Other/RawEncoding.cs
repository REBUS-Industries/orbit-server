using Newtonsoft.Json;

namespace Orbit.Objects.Other;

/// <summary>
/// Carries the native, host-application-specific encoding of a geometric
/// object. For the Rhino connector <see cref="Format"/> is <c>"3dm"</c> and
/// <see cref="Contents"/> is the base64-encoded bytes of a single-object
/// <c>.3dm</c> file produced by <c>Rhino.FileIO.File3dm.ToByteArray()</c>.
/// <para>
/// On receive, a Rhino-capable connector decodes the base64 string, loads
/// the bytes as a <c>Rhino.FileIO.File3dm</c>, and pulls the original
/// native geometry out — including NURBS surfaces, edges, tolerances, and
/// any other attributes that meshes cannot carry. This is what makes the
/// connector a real round-trip transport between 3D applications.
/// </para>
/// <para>
/// The viewer never reads this property directly; it renders the parent
/// object's <c>displayValue</c> mesh.
/// </para>
/// </summary>
public class RawEncoding : Base.OrbitBase
{
    public override string OrbitType => "Objects.Other.RawEncoding";

    /// <summary>
    /// Encoding format identifier. Currently always <c>"3dm"</c> from this
    /// connector. Receivers should sanity-check this before attempting to
    /// decode.
    /// </summary>
    [JsonProperty("format")]
    public string Format { get; set; } = "3dm";

    /// <summary>
    /// Base64-encoded native bytes (a single-object <c>.3dm</c> file when
    /// <see cref="Format"/> is <c>"3dm"</c>).
    /// </summary>
    [JsonProperty("contents")]
    public string Contents { get; set; } = string.Empty;
}
