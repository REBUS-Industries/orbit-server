using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Orbit.Sdk.Serialisation;

public static class OrbitJsonSettings
{
    // DefaultValueHandling deliberately set to Include: numeric properties
    // (e.g. Point.Y after a Y/Z swap that flattens 2D DWG geometry to the
    // XZ plane) hit their CLR default of 0.0 legitimately, and the Speckle
    // viewer reads coordinates with `obj.x/y/z` directly. If a field is
    // omitted from the JSON, the viewer gets `undefined` and produces NaN
    // positions, rendering the geometry invisible. NullValueHandling.Ignore
    // is preserved so reference-typed properties (string?, List<>?, sub-
    // objects) still drop out when null, keeping the wire format compact.
    public static JsonSerializerSettings Default => new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Include,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.None,
        TypeNameHandling = TypeNameHandling.None,
    };

    public static JsonSerializer CreateSerializer() =>
        JsonSerializer.Create(Default);
}
