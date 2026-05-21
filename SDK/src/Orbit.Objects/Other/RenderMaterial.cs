using Newtonsoft.Json;

namespace Orbit.Objects.Other;

/// <summary>
/// Inline material definition attached to geometry objects (typically as a
/// <c>renderMaterial</c> property on <see cref="Geometry.Mesh"/> or
/// <see cref="Geometry.Brep"/>).
///
/// IMPORTANT: <c>diffuse</c> and <c>emissive</c> are encoded as <b>unsigned</b>
/// 32-bit ARGB packed into a <see cref="long"/> (matching the Speckle Python
/// SDK convention <c>(long)(uint)Color.ToArgb()</c>). Using a signed
/// <see cref="int"/> would produce values like <c>-65536</c> for opaque red,
/// which the working Speckle reference deployment shows as <c>4294901760</c>.
/// The viewer expects the unsigned form.
///
/// Texture bitmaps are uploaded as Speckle blobs before send. During conversion
/// placeholders use <c>@blob:SHA256HEX</c>; after upload they become the
/// server-assigned short blob id (viewer hydrates full URLs from that id).
/// </summary>
public class RenderMaterial : Base.OrbitBase
{
    public override string OrbitType => "Objects.Other.RenderMaterial";

    [JsonProperty("name", DefaultValueHandling = DefaultValueHandling.Include)]
    public string Name { get; set; } = "default";

    [JsonProperty("opacity", DefaultValueHandling = DefaultValueHandling.Include)]
    public double Opacity { get; set; } = 1.0;

    [JsonProperty("metalness", DefaultValueHandling = DefaultValueHandling.Include)]
    public double Metalness { get; set; } = 0.0;

    [JsonProperty("roughness", DefaultValueHandling = DefaultValueHandling.Include)]
    public double Roughness { get; set; } = 0.5;

    /// <summary>Diffuse colour as unsigned ARGB packed into a long.</summary>
    [JsonProperty("diffuse", DefaultValueHandling = DefaultValueHandling.Include)]
    public long Diffuse { get; set; } = 4294967295L; // 0xFFFFFFFF — opaque white

    /// <summary>Emissive colour as unsigned ARGB packed into a long.</summary>
    [JsonProperty("emissive", DefaultValueHandling = DefaultValueHandling.Include)]
    public long Emissive { get; set; } = 4278190080L; // 0xFF000000 — opaque black

    [JsonProperty("emissiveIntensity")]
    public double? EmissiveIntensity { get; set; }

    // ── Texture references (bare blob id after upload; @blob:HASH during conversion) ──

    [JsonProperty("diffuseTexture")]
    public string? DiffuseTexture { get; set; }

    [JsonProperty("baseColorTexture")]
    public string? BaseColorTexture { get; set; }

    [JsonProperty("emissiveTexture")]
    public string? EmissiveTexture { get; set; }

    [JsonProperty("pbrEmissionTexture")]
    public string? PbrEmissionTexture { get; set; }

    [JsonProperty("roughnessTexture")]
    public string? RoughnessTexture { get; set; }

    [JsonProperty("metalnessTexture")]
    public string? MetalnessTexture { get; set; }

    [JsonProperty("normalTexture")]
    public string? NormalTexture { get; set; }

    [JsonProperty("opacityTexture")]
    public string? OpacityTexture { get; set; }

    // ── Optional hydrated URLs (viewer can build these from blob ids) ──

    [JsonProperty("diffuseTextureUrl")]
    public string? DiffuseTextureUrl { get; set; }

    [JsonProperty("baseColorTextureUrl")]
    public string? BaseColorTextureUrl { get; set; }

    [JsonProperty("emissiveTextureUrl")]
    public string? EmissiveTextureUrl { get; set; }

    [JsonProperty("pbrEmissionTextureUrl")]
    public string? PbrEmissionTextureUrl { get; set; }

    [JsonProperty("roughnessTextureUrl")]
    public string? RoughnessTextureUrl { get; set; }

    [JsonProperty("metalnessTextureUrl")]
    public string? MetalnessTextureUrl { get; set; }

    [JsonProperty("normalTextureUrl")]
    public string? NormalTextureUrl { get; set; }

    [JsonProperty("opacityTextureUrl")]
    public string? OpacityTextureUrl { get; set; }

    [JsonProperty("emissiveTextureOffset")]
    public List<double>? EmissiveTextureOffset { get; set; }

    [JsonProperty("emissiveTextureRepeat")]
    public List<double>? EmissiveTextureRepeat { get; set; }

    [JsonProperty("diffuseTextureOffset")]
    public List<double>? DiffuseTextureOffset { get; set; }

    [JsonProperty("diffuseTextureRepeat")]
    public List<double>? DiffuseTextureRepeat { get; set; }

    /// <summary>
    /// Build a viewer-safe material with every required field populated.
    /// </summary>
    public static RenderMaterial Create(
        string name,
        long diffuse,
        long? emissive = null,
        double? opacity = null,
        double? roughness = null,
        double? metalness = null)
    {
        return new RenderMaterial
        {
            Name      = string.IsNullOrWhiteSpace(name) ? "default" : name,
            Diffuse   = diffuse,
            Emissive  = emissive ?? 4278190080L,
            Opacity   = opacity ?? 1.0,
            Roughness = roughness ?? 0.5,
            Metalness = metalness ?? 0.0,
        };
    }

    /// <summary>
    /// Helper: pack a System.Drawing.Color into the unsigned-ARGB-long form
    /// the viewer expects.
    /// </summary>
    public static long PackArgb(System.Drawing.Color c) => (long)(uint)c.ToArgb();

    /// <summary>
    /// Helper: pack a raw signed ARGB int (from
    /// <c>System.Drawing.Color.ToArgb()</c>) into the unsigned-ARGB-long form.
    /// </summary>
    public static long PackArgb(int argb) => (long)(uint)argb;

    /// <summary>All texture reference field names patched after blob upload.</summary>
    public static readonly string[] TextureRefFields =
    {
        "diffuseTexture", "baseColorTexture",
        "emissiveTexture", "pbrEmissionTexture",
        "roughnessTexture", "metalnessTexture",
        "normalTexture", "opacityTexture",
    };
}
