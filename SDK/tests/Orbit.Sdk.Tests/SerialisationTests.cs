using FluentAssertions;
using Newtonsoft.Json.Linq;
using Orbit.Objects.Base;
using Orbit.Objects.BuiltElements;
using Orbit.Objects.Geometry;
using Orbit.Sdk.Serialisation;
using Xunit;

namespace Orbit.Sdk.Tests;

public class SerialisationTests
{
    [Fact]
    public async Task Serialise_SimpleMesh_ProducesValidJson()
    {
        var mesh = new Mesh
        {
            Vertices = new List<double> { 0, 0, 0,  1, 0, 0,  1, 1, 0,  0, 1, 0 },
            Faces    = new List<int>    { 4, 0, 1, 2, 3 },
            Units    = "m"
        };

        var serialiser = new OrbitSerializer();
        var result = await serialiser.SerialiseAsync(mesh);

        result.Should().NotBeEmpty();
        result.Should().ContainKey(mesh.Id!);
    }

    [Fact]
    public async Task Serialise_SameObject_ProducesSameId()
    {
        var mesh1 = new Mesh { Vertices = new List<double> { 0, 0, 0 }, Faces = new List<int> { 3, 0, 0, 0 } };
        var mesh2 = new Mesh { Vertices = new List<double> { 0, 0, 0 }, Faces = new List<int> { 3, 0, 0, 0 } };

        var s1 = new OrbitSerializer();
        var s2 = new OrbitSerializer();

        await s1.SerialiseAsync(mesh1);
        await s2.SerialiseAsync(mesh2);

        mesh1.Id.Should().Be(mesh2.Id, "same content should produce same hash");
    }

    [Fact]
    public async Task Serialise_DifferentObjects_ProduceDifferentIds()
    {
        var mesh1 = new Mesh { Vertices = new List<double> { 0, 0, 0 } };
        var mesh2 = new Mesh { Vertices = new List<double> { 1, 1, 1 } };

        var s = new OrbitSerializer();
        await s.SerialiseAsync(mesh1);
        await s.SerialiseAsync(mesh2);

        mesh1.Id.Should().NotBe(mesh2.Id);
    }

    [Fact]
    public void ComputeHash_IsStable()
    {
        var json = "{\"speckle_type\":\"Orbit.Objects.Geometry.Mesh\",\"vertices\":[0,0,0]}";
        var h1 = OrbitSerializer.ComputeHash(json);
        var h2 = OrbitSerializer.ComputeHash(json);
        h1.Should().Be(h2);
    }

    /// <summary>
    /// Regression test for the layers-and-views fix (2026-05-20).
    /// Verifies the serialised root produces the exact structure the Speckle/ORBIT viewer
    /// needs to render the layer sidebar and named-views panel.
    /// </summary>
    [Fact]
    public async Task Serialise_RootWithLayersAndViews_MatchesSpeckleStructure()
    {
        var mesh = new Mesh
        {
            Vertices  = new List<double> { 0, 0, 0, 1, 0, 0, 1, 1, 0 },
            Faces     = new List<int>    { 3, 0, 1, 2 },
            Units     = "mm",
            LayerPath = "Default",
            LayerColor = 4278222592L,
            ColorSource = "layer",
        };

        var layer = new OrbitObject
        {
            Name           = "Default",
            CollectionType = "layer",
            LayerPath      = "Default",
            LayerColor     = 4278222592L,
            Elements       = new List<OrbitBase> { mesh },
        };

        var view = new View3D
        {
            Name             = "Front",
            Lens             = 35,
            IsOrthogonal     = true,
            Units            = "mm",
            Origin           = new Point(0, -10, 0, "mm"),
            Target           = new Point(0, 0, 0, "mm"),
            UpDirection      = new Vector(0, 0, 1, "mm"),
            ForwardDirection = new Vector(0, 10, 0, "mm"),
        };

        var root = new OrbitObject
        {
            Name              = "test root",
            CollectionType    = "model",
            SourceApplication = "OrbitRhino",
            Units             = "mm",
            Elements          = new List<OrbitBase> { layer },
            Views             = new List<View3D>    { view },
        };

        var serialiser = new OrbitSerializer();
        var batch = await serialiser.SerialiseAsync(root);

        // Root + layer + mesh should be in the batch (3 detached rows: root, layer, mesh).
        // Inline objects (views, points, vectors) must NOT be in the batch.
        batch.Should().HaveCount(3);

        var rootJson = JObject.Parse(batch[root.Id!]);

        rootJson["speckle_type"]!.ToString().Should().Be("Speckle.Core.Models.Collections.Collection");
        rootJson["collectionType"]!.ToString().Should().Be("model");

        // `elements` is detached — children must be reference stubs WITH speckle_type.
        var elements = (JArray)rootJson["@elements"]!;
        elements.Should().HaveCount(1);
        elements[0]["referencedId"].Should().NotBeNull();
        elements[0]["speckle_type"]!.ToString().Should().Be("reference");

        // `views` is inline — full View3D object must remain in the root JSON.
        var views = (JArray)rootJson["views"]!;
        views.Should().HaveCount(1);
        views[0]["speckle_type"]!.ToString().Should().Be("Objects.BuiltElements.View.View3D");
        views[0]["origin"]!["speckle_type"]!.ToString().Should().Be("Objects.Geometry.Point");
        views[0]["target"]!["speckle_type"]!.ToString().Should().Be("Objects.Geometry.Point");
        views[0]["upDirection"]!["speckle_type"]!.ToString().Should().Be("Objects.Geometry.Vector");
        views[0]["forwardDirection"]!["speckle_type"]!.ToString().Should().Be("Objects.Geometry.Vector");

        // Inline objects must have content-hash IDs.
        views[0]["id"].Should().NotBeNull();
        views[0]["origin"]!["id"].Should().NotBeNull();

        // Closure table must include only the detached layer + mesh (2 entries, not 5).
        var closure = (JObject)rootJson["__closure"]!;
        closure.Properties().Should().HaveCount(2);

        // Layer collection must carry layerPath/layerColor (so the viewer can colour the sidebar).
        var layerId = elements[0]["referencedId"]!.ToString();
        var layerJson = JObject.Parse(batch[layerId]);
        layerJson["speckle_type"]!.ToString().Should().Be("Speckle.Core.Models.Collections.Collection");
        layerJson["layerPath"]!.ToString().Should().Be("Default");
        layerJson["layerColor"]!.Value<long>().Should().Be(4278222592L);
    }
}
