using Orbit.Objects.Base;
using Orbit.Objects.Data;
using Orbit.Objects.Geometry;
using Orbit.Objects.Other;
using Orbit.Objects.Proxies;

namespace Orbit.Sdk.Transport;

/// <summary>
/// Walks an ORBIT object tree and replaces <c>@blob:SHA256</c> texture placeholders
/// on <see cref="RenderMaterial"/> with server-assigned blob ids. Clears pre-built
/// <c>*Url</c> fields so the viewer hydrates URLs from the bare blob id.
/// </summary>
public static class TextureBlobPatcher
{
    public static void Patch(OrbitBase root, IReadOnlyDictionary<string, string> hashToServerId)
    {
        if (hashToServerId.Count == 0) return;

        var lookup = hashToServerId.ToDictionary(
            kv => "@blob:" + kv.Key,
            kv => kv.Value,
            StringComparer.OrdinalIgnoreCase);

        Walk(root, lookup);
    }

    private static void Walk(OrbitBase obj, Dictionary<string, string> lookup)
    {
        switch (obj)
        {
            case Mesh mesh when mesh.RenderMaterial != null:
                PatchMaterial(mesh.RenderMaterial, lookup);
                break;

            case Brep brep:
                if (brep.RenderMaterial != null)
                    PatchMaterial(brep.RenderMaterial, lookup);
                if (brep.DisplayValue != null)
                {
                    foreach (var dv in brep.DisplayValue)
                    {
                        if (dv is Mesh dm && dm.RenderMaterial != null)
                            PatchMaterial(dm.RenderMaterial, lookup);
                        else if (dv != null)
                            Walk(dv, lookup);
                    }
                }
                break;

            case Instance instance:
                if (instance.DisplayValue != null)
                {
                    foreach (var dv in instance.DisplayValue)
                        if (dv != null) Walk(dv, lookup);
                }
                break;

            case RhinoDataObject rhinoObj:
                // Native-Rhino wrapper — texture refs live on the per-face
                // display meshes only. Without this case the wrapper acts as
                // an opaque box and `@blob:HASH` placeholders leak unpatched.
                if (rhinoObj.DisplayValue != null)
                {
                    foreach (var dv in rhinoObj.DisplayValue)
                        if (dv != null) Walk(dv, lookup);
                }
                break;

            case OrbitObject container:
                if (container.DisplayValue != null)
                {
                    foreach (var dv in container.DisplayValue)
                        if (dv != null) Walk(dv, lookup);
                }
                if (container.Elements != null)
                {
                    foreach (var child in container.Elements)
                        if (child != null) Walk(child, lookup);
                }
                if (container.DefinitionProxies != null)
                {
                    foreach (var proxy in container.DefinitionProxies)
                        if (proxy != null) Walk(proxy, lookup);
                }
                if (container.RenderMaterialProxies != null)
                {
                    foreach (var proxy in container.RenderMaterialProxies)
                        if (proxy != null) Walk(proxy, lookup);
                }
                break;

            case DefinitionProxy definition:
                if (definition.Objects != null)
                {
                    foreach (var child in definition.Objects)
                        if (child != null) Walk(child, lookup);
                }
                break;

            case RenderMaterialProxy materialProxy:
                if (materialProxy.Value != null)
                    PatchMaterial(materialProxy.Value, lookup);
                break;
        }
    }

    private static void PatchMaterial(RenderMaterial rm, Dictionary<string, string> lookup)
    {
        foreach (var field in RenderMaterial.TextureRefFields)
        {
            var value = GetTextureRef(rm, field);
            if (value == null || !lookup.TryGetValue(value, out var serverBlobId))
                continue;

            SetTextureRef(rm, field, serverBlobId);
            SetTextureUrl(rm, field, null);
        }
    }

    private static string? GetTextureRef(RenderMaterial rm, string field) => field switch
    {
        "diffuseTexture"        => rm.DiffuseTexture,
        "baseColorTexture"      => rm.BaseColorTexture,
        "emissiveTexture"       => rm.EmissiveTexture,
        "pbrEmissionTexture"    => rm.PbrEmissionTexture,
        "roughnessTexture"      => rm.RoughnessTexture,
        "metalnessTexture"      => rm.MetalnessTexture,
        "normalTexture"         => rm.NormalTexture,
        "opacityTexture"        => rm.OpacityTexture,
        _                       => rm[field]?.ToString(),
    };

    private static void SetTextureRef(RenderMaterial rm, string field, string blobId)
    {
        switch (field)
        {
            case "diffuseTexture":     rm.DiffuseTexture = blobId; break;
            case "baseColorTexture":   rm.BaseColorTexture = blobId; break;
            case "emissiveTexture":    rm.EmissiveTexture = blobId; break;
            case "pbrEmissionTexture": rm.PbrEmissionTexture = blobId; break;
            case "roughnessTexture":   rm.RoughnessTexture = blobId; break;
            case "metalnessTexture":   rm.MetalnessTexture = blobId; break;
            case "normalTexture":      rm.NormalTexture = blobId; break;
            case "opacityTexture":     rm.OpacityTexture = blobId; break;
            default:                   rm[field] = blobId; break;
        }
    }

    private static void SetTextureUrl(RenderMaterial rm, string field, string? url)
    {
        switch (field)
        {
            case "diffuseTexture":     rm.DiffuseTextureUrl = url; break;
            case "baseColorTexture":   rm.BaseColorTextureUrl = url; break;
            case "emissiveTexture":    rm.EmissiveTextureUrl = url; break;
            case "pbrEmissionTexture": rm.PbrEmissionTextureUrl = url; break;
            case "roughnessTexture":   rm.RoughnessTextureUrl = url; break;
            case "metalnessTexture":   rm.MetalnessTextureUrl = url; break;
            case "normalTexture":      rm.NormalTextureUrl = url; break;
            case "opacityTexture":     rm.OpacityTextureUrl = url; break;
        }
    }
}
