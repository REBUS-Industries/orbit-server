using Newtonsoft.Json;

namespace Orbit.Objects.Proxies;

/// <summary>
/// Stores a block definition — geometry that can be placed multiple times as instances.
/// Stored once at the root of the version tree; instances reference it via
/// <see cref="Base.OrbitBase.ApplicationId"/> matching
/// <see cref="Geometry.Instance.DefinitionId"/>.
/// </summary>
public class DefinitionProxy : Base.OrbitBase
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The geometry objects that make up this block definition.
    /// <para>
    /// The <c>@</c> prefix tells the serialiser to detach each member into its
    /// own DB row and replace it inline with a <c>{ referencedId, speckle_type:
    /// "reference" }</c> stub. Without detachment the full geometry sits inline
    /// in the proxy JSON, and the viewer's tree walker picks up the inline
    /// object ids as orphan tree items (one ghost entry per Brep in the
    /// definition). After detachment those ids are tied to real DB rows that
    /// the viewer only fetches when resolving an <c>Instance</c>.
    /// </para>
    /// </summary>
    [JsonProperty("@objects")]
    public List<Base.OrbitBase>? Objects { get; set; }

    [JsonProperty("basePoint")]
    public Geometry.Point? BasePoint { get; set; }

    [JsonProperty("units")]
    public string? Units { get; set; }
}
