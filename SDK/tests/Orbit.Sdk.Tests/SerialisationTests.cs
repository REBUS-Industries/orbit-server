using FluentAssertions;
using Orbit.Objects.Base;
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
}
