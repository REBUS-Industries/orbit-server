using Newtonsoft.Json;

namespace Orbit.Objects.Geometry;

/// <summary>
/// A placed instance of a block definition.
/// <para>
/// Wire <c>speckle_type</c> is <c>Objects.Other.Collections.Collection</c> with
/// <c>collectionType: "block"</c> so the viewer's layer-tree sidebar treats it
/// as a walkable collection — the user can expand the block in the sidebar and
/// see every Brep / SubD / Surface that makes up the placement. The original
/// instance metadata (<see cref="DefinitionId"/>, <see cref="Transform"/>) is
/// still carried on the object as plain fields so a receiver can recognise it
/// as a block and rebuild a native Rhino <c>InstanceDefinition</c> when the
/// round-trip is implemented.
/// </para>
/// </summary>
public class Instance : Base.OrbitBase
{
    public override string OrbitType => "Objects.Other.Collections.Collection";

    /// <summary>
    /// Distinguishes a block-instance collection from a layer collection in
    /// the same tree. Receivers branch on this; the viewer happily treats
    /// any non-null collectionType as a generic tree-walkable group.
    /// </summary>
    [JsonProperty("collectionType")]
    public string CollectionType => "block";

    /// <summary>Display name shown in the viewer's layer-tree sidebar.</summary>
    [JsonProperty("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The applicationId of the corresponding DefinitionProxy.
    /// </summary>
    [JsonProperty("definitionId")]
    public string? DefinitionId { get; set; }

    /// <summary>
    /// 4×4 world transform for this instance.
    /// </summary>
    [JsonProperty("transform")]
    public Primitives.Transform? Transform { get; set; }

    [JsonProperty("units")]
    public string? Units { get; set; }

    /// <summary>Mesh display fallback for this instance placement.</summary>
    [JsonProperty("displayValue")]
    public List<Mesh>? DisplayValue { get; set; }

    /// <summary>
    /// Block members as expandable tree children. Each entry is a real geometry
    /// object (typically a <see cref="Data.RhinoDataObject"/> Brep/Extrusion/SubD
    /// wrapper) pre-transformed into the instance's placement so the viewer can
    /// render each member at the correct world position.
    /// <para>
    /// Same shape and serialiser treatment as the root layer collections'
    /// <c>@elements</c> — each member detaches into its own DB row with its
    /// own <c>__closure</c>, the parent only carries
    /// <c>{ referencedId, speckle_type: "reference" }</c> stubs inline. The
    /// viewer's tree walker recognises the parent as a collection (via
    /// <see cref="CollectionType"/>) and follows each stub to render the
    /// member in the layer-tree sidebar.
    /// </para>
    /// </summary>
    [JsonProperty("@elements")]
    public List<Base.OrbitBase>? Elements { get; set; }
}
