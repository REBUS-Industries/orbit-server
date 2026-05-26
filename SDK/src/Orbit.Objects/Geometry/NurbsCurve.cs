using Newtonsoft.Json;

namespace Orbit.Objects.Geometry;

public class NurbsCurve : Base.OrbitBase
{
    // Speckle viewer dispatch (see SpeckleConverter.NodeConverterMapping)
    // expects "Curve" as the final segment for NURBS-style curves. Naming
    // this "Objects.Geometry.NurbsCurve" still works (getSpeckleTypeChain
    // strips namespace prefixes), but the canonical Speckle wire type is
    // "Objects.Geometry.Curve" — keep that for cross-tool compatibility.
    public override string OrbitType => "Objects.Geometry.Curve";

    [JsonProperty("degree")]     public int Degree    { get; set; }
    [JsonProperty("periodic")]   public bool Periodic  { get; set; }
    [JsonProperty("rational")]   public bool Rational  { get; set; }
    [JsonProperty("closed")]     public bool Closed    { get; set; }

    /// <summary>Flat control point array: x0,y0,z0, x1,y1,z1, ...</summary>
    [JsonProperty("points")]     public List<double>? Points  { get; set; }

    /// <summary>Weight per control point (same count as Points.Count/3).</summary>
    [JsonProperty("weights")]    public List<double>? Weights { get; set; }

    /// <summary>Knot vector.</summary>
    [JsonProperty("knots")]      public List<double>? Knots   { get; set; }

    [JsonProperty("domain")]     public Primitives.Interval? Domain { get; set; }
    [JsonProperty("units")]      public string? Units { get; set; }

    /// <summary>Polyline display value for viewers that don't handle NURBS.</summary>
    [JsonProperty("displayValue")]
    public Polyline? DisplayValue { get; set; }
}
