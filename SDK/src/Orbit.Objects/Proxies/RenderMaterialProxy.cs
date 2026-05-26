using Newtonsoft.Json;
using Orbit.Objects.Other;

namespace Orbit.Objects.Proxies;

/// <summary>
/// Stores a <see cref="RenderMaterial"/> and the set of object applicationIds
/// that reference it. Proxies are stored at the root of the version object
/// tree, not nested within objects. This keeps geometry objects lightweight
/// and deduplicates material definitions.
///
/// NOTE: The working Speckle reference deployment <i>currently</i> attaches
/// <see cref="RenderMaterial"/> inline on each <see cref="Geometry.Mesh"/>
/// (via <c>Mesh.RenderMaterial</c>) and does <b>not</b> emit
/// <c>renderMaterialProxies</c> at the root. Proxies are kept in the schema
/// for future use (e.g. by the receive/bake pipeline that does its own
/// deduplication).
/// </summary>
public class RenderMaterialProxy : Base.OrbitBase
{
    public override string OrbitType => "Speckle.Core.Models.Proxies.RenderMaterialProxy";

    /// <summary>The material definition.</summary>
    [JsonProperty("value")]
    public RenderMaterial? Value { get; set; }

    /// <summary>
    /// applicationIds of all objects that use this material.
    /// </summary>
    [JsonProperty("objectIds")]
    public List<string>? ObjectIds { get; set; }
}
