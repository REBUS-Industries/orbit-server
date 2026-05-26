using Newtonsoft.Json;

namespace Orbit.Objects.Geometry;

public class Line : Base.OrbitBase
{
    public override string OrbitType => "Objects.Geometry.Line";

    [JsonProperty("start")]  public Point? Start  { get; set; }
    [JsonProperty("end")]    public Point? End    { get; set; }
    [JsonProperty("units")]  public string? Units { get; set; }
    [JsonProperty("domain")] public Primitives.Interval? Domain { get; set; }
}
