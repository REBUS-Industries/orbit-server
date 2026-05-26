using Rhino;
using Rhino.Geometry;
using Rhino.DocObjects;
using Orbit.Objects.Base;
using Orbit.Objects.BuiltElements;
using Orbit.Objects.Data;
using Orbit.Sdk.Serialisation;
using OrbitPoint  = Orbit.Objects.Geometry.Point;
using OrbitVector = Orbit.Objects.Geometry.Vector;
using OM          = Orbit.Objects.Geometry;
using Orbit.Sdk.Transport;
using Orbit.Sdk.Api;
using OrbitConnector.Rhino.Converters;
using OrbitConnector.Rhino.Converters.ToOrbit;
using OrbitConnector.Rhino.Models;

namespace OrbitConnector.Rhino.Pipeline;

/// <summary>
/// Orchestrates the full send pipeline:
/// Extract → Convert → Assemble tree → Serialise → Transport → Create version
/// </summary>
public class RhinoSendPipeline
{
    private readonly List<IRhinoToOrbitConverter> _converters;
    private readonly RhinoFallbackConverter _fallback = new();
    private readonly OrbitSerializer _serialiser = new();

    public RhinoSendPipeline()
    {
        // Order matters: first matching converter wins. Most-specific types
        // before generic ones (e.g. Extrusion before Brep would matter if
        // Extrusion derived from Brep — it does not, but the order is still
        // chosen for clarity).
        _converters = new List<IRhinoToOrbitConverter>
        {
            new RhinoMeshConverter(),
            new RhinoSurfaceConverter(),
            new RhinoBrepConverter(),
            new RhinoExtrusionConverter(),
            new RhinoSubDConverter(),
            new RhinoCurveConverter(),
            new RhinoPointConverter(),
            new RhinoPointCloudConverter(),
            new RhinoTextConverter(),
            new RhinoInstanceConverter(),
        };
    }

    /// <summary>
    /// Execute a full send. Returns the created version id.
    /// </summary>
    /// <param name="log">
    /// Optional diagnostic sink. When non-null, every material/texture decision
    /// and every blob upload result is forwarded to it. Lines are also written
    /// to <see cref="RhinoApp.WriteLine"/> for visibility inside an interactive
    /// Rhino session. PRISM's headless agent injects an <c>ILogger</c>-backed
    /// callback so the lines land in the agent's Serilog file and the admin UI.
    /// </param>
    public async Task<string> SendAsync(
        ConnectorCard card,
        RhinoDoc doc,
        IOrbitTransport transport,
        OrbitClient client,
        IProgress<(string status, int percent)>? progress = null,
        CancellationToken ct = default,
        Action<string>? log = null)
    {
        // Fan-out to both Rhino's command output and the caller-supplied sink.
        // RhinoApp.WriteLine remains the legacy connector convention; the
        // caller sink (PRISM agent) writes to Serilog → admin UI.
        void Diag(string line)
        {
            try { RhinoApp.WriteLine(line); } catch { /* headless host without Rhino UI */ }
            try { log?.Invoke(line); }       catch { /* never let logging fail a send */ }
        }

        Diag($"[ORBIT-DIAG] SendAsync start: card type={card.Type} mode={card.LayerMode} " +
             $"project={card.ProjectId} model={card.ModelId} docObjects={doc.Objects.Count}");

        // Reset the per-job doc probe so it fires once per conversion (instead
        // of once per agent process lifetime).
        Converters.RhinoMaterialHelper.ResetDocProbe();

        progress?.Report(("Extracting objects…", 0));
        var context = new ConversionContext(doc);
        context.Log = Diag;

        // 1. EXTRACT objects from document
        var rhinoObjects = ExtractObjects(card, doc);
        Diag($"[ORBIT-DIAG] extracted {rhinoObjects.Count} sendable objects");
        progress?.Report(($"Found {rhinoObjects.Count} objects…", 5));
        if (rhinoObjects.Count == 0)
            throw new InvalidOperationException(
                $"No objects to send. Layer mode: {card.LayerMode}, doc objects: {doc.Objects.Count}");

        progress?.Report(("Converting geometry…", 10));

        // 2. CONVERT — build layer tree
        var root = BuildObjectTree(rhinoObjects, doc, context, card);
        var totalConverted = root.Elements?.Sum(e => (e as OrbitObject)?.Elements?.Count ?? 0) ?? 0;
        progress?.Report(($"Converted {totalConverted} objects, serialising…", 35));

        // Summary of texture extraction — the single most important diagnostic
        // for Phase 1 of the PRISM texture-loss investigation.
        long totalBlobBytes = 0;
        foreach (var path in context.PendingBlobFiles.Values)
        {
            try { totalBlobBytes += new FileInfo(path).Length; } catch { /* missing file logged below */ }
        }
        Diag($"[ORBIT-DIAG] conversion summary: convertedObjects={totalConverted} " +
             $"pendingBlobs={context.PendingBlobFiles.Count} totalBlobBytes={totalBlobBytes} " +
             $"registeredMaterials={context.RegisteredMaterials.Count}");

        // 2.5 UPLOAD TEXTURE BLOBS + PATCH PLACEHOLDERS
        // The converters left `@blob:SHA256HEX` placeholders on every textured
        // RenderMaterial and collected the on-disk file paths in
        // context.PendingBlobFiles. We must upload the blobs to the ORBIT
        // server BEFORE serialising — the server returns short blob ids that
        // get swapped into the placeholders, after which content-hash ids are
        // computed over the final (patched) JSON. Skipping this leaves
        // `@blob:HASH` strings in the wire data and the viewer cannot resolve
        // them to image URLs (objects render with their fallback diffuse,
        // which is black for textured materials by design).
        if (context.PendingBlobFiles.Count > 0)
        {
            progress?.Report(($"Uploading {context.PendingBlobFiles.Count} texture(s)…", 36));
            Diag($"[ORBIT-DIAG] uploading {context.PendingBlobFiles.Count} blobs " +
                 $"totalling {totalBlobBytes} bytes to {client.ServerUrl}/api/stream/{card.ProjectId}/blob");
            using var blobUploader = new OrbitBlobUploader(
                client.ServerUrl, card.ProjectId!, client.AuthToken);
            var hashToServerId = await blobUploader.UploadAsync(context.PendingBlobFiles, ct, Diag);
            Diag($"[ORBIT-DIAG] uploader returned {hashToServerId.Count}/{context.PendingBlobFiles.Count} blob ids");
            foreach (var (hash, blobId) in hashToServerId)
                Diag($"[ORBIT-DIAG]   blob {hash[..16]}… → server id '{blobId}'");
            var missing = context.PendingBlobFiles.Keys.Where(h => !hashToServerId.ContainsKey(h)).ToList();
            foreach (var hash in missing)
                Diag($"[ORBIT-DIAG]   blob {hash[..16]}… UPLOAD FAILED " +
                     $"(file: {context.PendingBlobFiles[hash]})");

            TextureBlobPatcher.Patch(root, hashToServerId);
            Diag($"[ORBIT-DIAG] patched {hashToServerId.Count} @blob:HASH placeholders → server blob ids");
        }
        else
        {
            Diag("[ORBIT-DIAG] PendingBlobFiles is empty — no texture upload step");
        }

        // 3. SERIALISE
        progress?.Report(("Serialising…", 40));
        var objectBatch = await _serialiser.SerialiseAsync(root, ct);

        // 4. DEDUP — parallel HEAD probes against the server, with progress.
        // Previously this loop did 6137 sequential `await HasObjectAsync(id)`
        // HEADs for a typical 2D DWG, taking ~7 minutes of completely silent
        // wall-clock (UI shows "Uploading…" at 50% with no updates). Speckle
        // has no bulk diff endpoint in this SDK build, so we fan out HEAD
        // requests in fixed-size chunks via Task.WhenAll. Net effect on a
        // 6137-object send vs PROD's ORBIT server is ~30x speedup, plus
        // continuous progress / log feedback so the user can see it's
        // making progress.
        const int DedupParallelism = 16;
        // Materialise to a fixed-order list so we can chunk by index and zip
        // results back to the source (Dictionary<,> has no stable order
        // contract before .NET 8 and we want deterministic chunking anyway).
        var batchList = objectBatch.Select(kv => (id: kv.Key, json: kv.Value)).ToList();
        progress?.Report(($"Checking server for {batchList.Count} objects…", 42));
        Diag($"[ORBIT-DIAG] dedup: probing server for {batchList.Count} object ids " +
             $"(parallelism={DedupParallelism})");
        var toUpload = new List<(string id, string json)>();
        int dedupChecked = 0;
        var dedupStart = DateTime.UtcNow;
        for (int i = 0; i < batchList.Count; i += DedupParallelism)
        {
            ct.ThrowIfCancellationRequested();
            int chunkSize = Math.Min(DedupParallelism, batchList.Count - i);
            var existsTasks = new Task<bool>[chunkSize];
            for (int k = 0; k < chunkSize; k++)
                existsTasks[k] = transport.HasObjectAsync(batchList[i + k].id, ct);
            var exists = await Task.WhenAll(existsTasks);
            for (int k = 0; k < chunkSize; k++)
                if (!exists[k]) toUpload.Add(batchList[i + k]);
            dedupChecked += chunkSize;
            // 42% -> 48% across the dedup phase
            var pct = 42 + (int)(dedupChecked * 6.0 / Math.Max(batchList.Count, 1));
            progress?.Report(($"Checking server… {dedupChecked}/{batchList.Count} ({toUpload.Count} new)", pct));
        }
        Diag($"[ORBIT-DIAG] dedup: {toUpload.Count}/{batchList.Count} new in " +
             $"{(DateTime.UtcNow - dedupStart).TotalSeconds:F1}s, " +
             $"{batchList.Count - toUpload.Count} already on server");

        // 4. UPLOAD
        if (toUpload.Count == 0)
        {
            progress?.Report(("All objects already on server", 90));
            Diag("[ORBIT-DIAG] upload: nothing to upload (all objects deduped)");
        }
        else
        {
            progress?.Report(($"Uploading {toUpload.Count} objects…", 50));
            var uploadStart = DateTime.UtcNow;
            await transport.SaveObjectBatchAsync(toUpload,
                new Progress<int>(n =>
                {
                    var pct = 50 + (int)(n * 40.0 / Math.Max(toUpload.Count, 1));
                    progress?.Report(($"Uploading… {n}/{toUpload.Count}", pct));
                }), ct);
            Diag($"[ORBIT-DIAG] upload: pushed {toUpload.Count} objects in " +
                 $"{(DateTime.UtcNow - uploadStart).TotalSeconds:F1}s");
        }

        // 5. CREATE VERSION
        progress?.Report(("Creating version…", 92));
        var version = await client.CreateVersionAsync(
            card.ProjectId!, card.ModelId!, root.Id!,
            message: "Sent from ORBIT Rhino Connector",
            sourceApplication: "OrbitRhino",
            totalChildrenCount: objectBatch.Count,
            ct);

        // Intentionally do NOT report ("Done", 100) here. progress is a
        // System.Progress<T>, which posts callbacks to the captured
        // SynchronizationContext (== thread-pool for a service host) and
        // runs them fire-and-forget. The PRISM agent immediately awaits a
        // synchronous SendAsync(MessageType.Complete, ...) once we return,
        // and that Complete frame wins the race to the wire. A late "Done"
        // progress arriving after Complete makes the server's progress
        // handler downgrade status: 'complete' -> 'processing' and stage
        // 'complete' -> 'Done', leaving the row stuck in the admin UI even
        // though result_url, outputs and completed_at are populated.
        // (Server has a defensive guard for this too in agentProtocol.ts.)
        return version.Id!;
    }

    /// <summary>
    /// Geometry types that must NEVER be sent: they produce misleading
    /// flat-plane artefacts or have no meaningful 3D representation.
    /// <list type="bullet">
    ///   <item><see cref="ClippingPlaneSurface"/> — section-cut helpers; convert as a giant flat plane.</item>
    ///   <item><see cref="Hatch"/> — 2-D fill patterns; produce flat mesh in viewport but have no 3-D volume.</item>
    ///   <item><see cref="AnnotationBase"/> — dimensions, leaders, text; convert to near-flat bboxes.</item>
    /// </list>
    /// </summary>
    private static bool ShouldSkipGeometry(GeometryBase? g) =>
        g is ClippingPlaneSurface
        || g is Hatch
        || g is AnnotationBase;

    private List<RhinoObject> ExtractObjects(ConnectorCard card, RhinoDoc doc)
    {
        // Use ObjectEnumeratorSettings — the correct Rhino API for filtering doc objects
        var settings = new ObjectEnumeratorSettings()
        {
            NormalObjects            = true,
            LockedObjects            = false,
            HiddenObjects            = false,
            DeletedObjects           = false,
            IncludeLights            = false,
            IncludeGrips             = false,
            IncludePhantoms          = false,
        };

        var allNormal = doc.Objects.GetObjectList(settings)
                          .Where(o => o.Geometry != null && !ShouldSkipGeometry(o.Geometry))
                          .ToList();

        return card.LayerMode switch
        {
            LayerMode.All => allNormal,

            LayerMode.ByLayer => allNormal
                .Where(o => card.IncludedLayers.Contains(
                    doc.Layers[o.Attributes.LayerIndex].FullPath))
                .ToList(),

            // Selection: use the GUIDs snapshotted when the user set the selection filter
            LayerMode.Selection => card.SelectedObjectIds.Count > 0
                ? allNormal
                    .Where(o => card.SelectedObjectIds.Contains(o.Id.ToString()))
                    .ToList()
                : doc.Objects.GetSelectedObjects(false, false)
                    .Where(o => o.Geometry != null)
                    .ToList(),

            _ => new List<RhinoObject>()
        };
    }

    private OrbitObject BuildObjectTree(
        List<RhinoObject> rhinoObjects, RhinoDoc doc,
        ConversionContext context, ConnectorCard card)
    {
        // Root collection — must be CollectionType="model" to match the working Speckle reference
        // (the previous "root" value prevented the viewer from rendering the sidebar tree).
        var root = new OrbitObject
        {
            Name              = card.ModelName ?? card.ProjectName ?? "ORBIT Send",
            CollectionType    = "model",
            SourceApplication = "OrbitRhino",
            Units             = context.Units,
            Elements          = new List<OrbitBase>()
        };

        // Group by layer, build one Collection per layer
        var byLayer = rhinoObjects.GroupBy(o => o.Attributes.LayerIndex);
        foreach (var group in byLayer)
        {
            var layer = doc.Layers[group.Key];
            var layerColor = ArgbToUnsignedLong(layer.Color.ToArgb());
            var layerCollection = new OrbitObject
            {
                Name           = layer.FullPath,
                CollectionType = "layer",
                LayerPath      = layer.FullPath,
                LayerColor     = layerColor,
                Elements       = new List<OrbitBase>()
            };

            foreach (var obj in group)
            {
                // Pin the parent RhinoObject onto the context so converters
                // can read material/colour off the object's attributes.
                context.CurrentObject = obj;

                // ── PER-SOURCE diagnostic ──
                // Log every Rhino object we feed into the conversion pipeline
                // with its native type, layer, and bounding-box dimensions so
                // a flat / degenerate source ("bbox 12x8x0") shows up in the
                // log and can be correlated to the corresponding ORBIT object
                // id rendered in the viewer.
                context.Log?.Invoke(
                    $"[ORBIT-OBJ] source id={obj.Id} type={DescribeRhinoObject(obj)} " +
                    $"layer='{layer.FullPath}' bbox={DescribeBBox(obj)}");

                var converted = ConvertWithFallback(obj, context);
                context.CurrentObject = null;
                if (converted == null)
                {
                    context.Log?.Invoke($"[ORBIT-OBJ]   → converter returned null (skipped)");
                    continue;
                }

                converted.ApplicationId = obj.Id.ToString();
                TagWithLayerInfo(converted, layer.FullPath, layerColor);

                // Block instance flattening — the user wants each block member
                // to appear as a direct sibling under its layer, NOT nested
                // inside an intermediate "Block 01" tree node. The Instance
                // converter already pre-transforms members into the placement
                // position (so geometry is identical either way) and stores
                // them on Instance.Elements. We unpack those here so the layer
                // tree shows:
                //   Block (layer)
                //   ├── Extrusion (member 1)
                //   ├── Extrusion (member 2)
                //   └── …
                // instead of
                //   Block (layer)
                //   └── Block 01 (instance-as-collection)
                //       ├── Extrusion …
                if (converted is OM.Instance inst && inst.Elements is { Count: > 0 } members)
                {
                    context.Log?.Invoke(
                        $"[ORBIT-OBJ]   → Instance expanded into {members.Count} members " +
                        "(wrapper dropped, members appended to layer collection)");
                    for (int i = 0; i < members.Count; i++)
                    {
                        var member = members[i];
                        // Members inherit the layer's path/colour for the
                        // sidebar (they no longer have an instance parent to
                        // group them). Their own renderMaterial / per-mesh
                        // layer colour set inside the wrapper is unchanged.
                        TagWithLayerInfo(member, layer.FullPath, layerColor);
                        layerCollection.Elements.Add(member);
                        context.Log?.Invoke(
                            $"[ORBIT-OBJ]     member[{i}] {DescribeConverted(member)}");
                    }
                    // Drop the Instance wrapper itself — round-trip metadata
                    // (definitionId / transform) lives on the
                    // DefinitionProxies + a future InstanceProxy collection
                    // that ships separately when block round-trip is wired.
                }
                else
                {
                    layerCollection.Elements.Add(converted);
                    context.Log?.Invoke($"[ORBIT-OBJ]   → {DescribeConverted(converted)}");
                }
            }

            if (layerCollection.Elements.Count > 0)
                root.Elements.Add(layerCollection);
        }

        // Attach proxies as detached root properties. Inline definition
        // geometry is traversable by the viewer and shows up as duplicate
        // block contents; detached proxies remain available for receive.
        if (context.MaterialProxies.Count   > 0) root.RenderMaterialProxies = context.MaterialProxies;
        if (context.ColorProxies.Count      > 0) root.ColorProxies          = context.ColorProxies;
        if (context.GroupProxies.Count      > 0) root.GroupProxies          = context.GroupProxies;
        if (context.DefinitionProxies.Count > 0) root.DefinitionProxies     = context.DefinitionProxies;

        // Named views — stored INLINE under `views` (no @ prefix). The full View3D objects
        // sit directly in the root JSON, matching the working Speckle reference.
        var views = ExtractNamedViews(doc, context.Units);
        if (views.Count > 0)
            root.Views = views;

        return root;
    }

    /// <summary>
    /// Extracts all named views from the Rhino document as <see cref="View3D"/> objects.
    /// Each view carries inline <c>origin</c>/<c>target</c> Points and <c>upDirection</c>/
    /// <c>forwardDirection</c> Vectors, matching the structure the Speckle/ORBIT viewer
    /// expects to populate its saved-views panel.
    /// </summary>
    private static List<View3D> ExtractNamedViews(RhinoDoc doc, string? units)
    {
        var views = new List<View3D>();
        foreach (var info in doc.NamedViews)
        {
            try
            {
                var vp = info.Viewport;

                // CameraLocation — world position of the camera.
                var camLoc = vp.CameraLocation;
                var origin = new OrbitPoint(camLoc.X, camLoc.Y, camLoc.Z, units);

                // Target = origin + (forward direction). Rhino's CameraZ points FROM target
                // TOWARD camera, so the look direction is -CameraZ.
                var camZ = vp.CameraZ;
                var targetPt = camLoc - camZ;
                var target = new OrbitPoint(targetPt.X, targetPt.Y, targetPt.Z, units);

                // Up vector (Rhino's CameraY).
                var camY = vp.CameraY;
                var up   = new OrbitVector(camY.X, camY.Y, camY.Z, units);

                // Forward vector (from camera to target). This is the field the previous
                // implementation was missing entirely.
                var forward = new OrbitVector(-camZ.X, -camZ.Y, -camZ.Z, units);

                double lens = vp.IsParallelProjection ? 0 : vp.Camera35mmLensLength;

                views.Add(new View3D
                {
                    Name             = info.Name,
                    Origin           = origin,
                    Target           = target,
                    UpDirection      = up,
                    ForwardDirection = forward,
                    IsOrthogonal     = vp.IsParallelProjection,
                    Lens             = lens,
                    Units            = units,
                });
            }
            catch
            {
                // Skip malformed views rather than aborting the whole send.
            }
        }
        return views;
    }

    /// <summary>
    /// Packs a signed 32-bit ARGB integer (as returned by <c>System.Drawing.Color.ToArgb</c>)
    /// into an unsigned long, matching the Speckle Python SDK convention. This avoids the
    /// sign-bit mismatch that would otherwise produce wrong colours in the viewer.
    /// </summary>
    private static long ArgbToUnsignedLong(int argb) => (long)(uint)argb;

    /// <summary>
    /// Short native-Rhino type label for <see cref="RhinoObject"/> — picks a
    /// granular tag (Brep, Extrusion, SubD, InstanceObject, Mesh, …) so the
    /// per-source diagnostic line is grep-friendly.
    /// </summary>
    private static string DescribeRhinoObject(RhinoObject obj)
    {
        if (obj is InstanceObject) return "InstanceObject";
        var g = obj.Geometry;
        return g switch
        {
            Brep                          => "Brep",
            Extrusion                     => "Extrusion",
            SubD                          => "SubD",
            Surface                       => "Surface",
            global::Rhino.Geometry.Mesh   => "Mesh",
            Curve                         => "Curve",
            PointCloud                    => "PointCloud",
            Point                         => "Point",
            InstanceReferenceGeometry     => "InstanceReference",
            null                          => "<no geometry>",
            _                             => g.GetType().Name,
        };
    }

    /// <summary>
    /// Source bounding-box dimensions in the doc's units, formatted to a
    /// stable "WxHxD" string. A flat plane shows up as e.g. "12.0x8.0x0.0"
    /// which makes degenerate / planar sources trivial to spot in the log.
    /// </summary>
    private static string DescribeBBox(RhinoObject obj)
    {
        try
        {
            var bb = obj.Geometry?.GetBoundingBox(accurate: false) ?? BoundingBox.Empty;
            if (!bb.IsValid) return "<invalid>";
            var d = bb.Diagonal;
            return $"{d.X:F2}x{d.Y:F2}x{d.Z:F2}";
        }
        catch (Exception ex)
        {
            return $"<bbox-err {ex.GetType().Name}>";
        }
    }

    /// <summary>
    /// Brief description of a converted ORBIT object — its runtime type, the
    /// applicationId (source Rhino GUID for correlation), and per-displayValue
    /// vertex/face counts. Lets us match the "extra flat planes" reported in
    /// the viewer back to specific source RhinoObjects and tells us whether
    /// a Brep wrapper exploded into N display-mesh fragments (one of the two
    /// likely causes of the 8→13 expansion in v0.1.14).
    /// </summary>
    private static string DescribeConverted(OrbitBase converted)
    {
        var typeName = converted.GetType().Name;
        var appId = converted.ApplicationId is { Length: > 0 } id ? id : "<no-appId>";

        var dv = ExtractDisplayValue(converted);
        if (dv == null || dv.Count == 0)
            return $"type={typeName} appId={appId} dv=0";

        var sb = new System.Text.StringBuilder(96);
        sb.Append("type=").Append(typeName)
          .Append(" appId=").Append(appId)
          .Append(" dv=").Append(dv.Count);
        // Cap per-line detail at 5 fragments so a runaway Brep with hundreds
        // of faces doesn't produce a 10kB log line.
        for (int i = 0; i < dv.Count && i < 5; i++)
        {
            var m = dv[i];
            sb.Append(" dv[").Append(i).Append("]=v=").Append(m?.VertexCount ?? 0)
              .Append(",f=").Append(m?.Faces?.Count ?? 0);
        }
        if (dv.Count > 5) sb.Append(" …");
        return sb.ToString();
    }

    /// <summary>
    /// Returns the converted object's <c>displayValue</c> list when it has
    /// one, or wraps a single <see cref="OM.Mesh"/> leaf so the per-object
    /// describer has one uniform shape to print.
    /// </summary>
    private static List<OM.Mesh>? ExtractDisplayValue(OrbitBase converted) => converted switch
    {
        RhinoDataObject wrap => wrap.DisplayValue,
        OM.Mesh leaf         => new List<OM.Mesh> { leaf },
        OM.Brep brep         => brep.DisplayValue,
        _                    => null,
    };

    /// <summary>
    /// Set <c>layerPath</c>, <c>layerColor</c>, and <c>colorSource</c> on a
    /// converted leaf. <see cref="OM.Mesh"/> and <see cref="OM.Brep"/> have
    /// these as typed properties — set those directly to avoid emitting
    /// duplicate keys via <see cref="OrbitBase.DynamicProperties"/>. For all
    /// other ORBIT types we write through the dynamic indexer so the wire
    /// JSON shape is identical.
    /// </summary>
    private static void TagWithLayerInfo(OrbitBase converted, string layerPath, long layerColor)
    {
        switch (converted)
        {
            case RhinoDataObject wrapper:
                // The wrapper itself stays clean (matches the Speckle Rhino8
                // reference), but its display-mesh fragments need layer info —
                // the viewer renders THOSE and falls back to a black diffuse
                // when `colorSource: layer` is set with no `layerColor`.
                if (wrapper.DisplayValue != null)
                {
                    foreach (var dv in wrapper.DisplayValue)
                    {
                        dv.LayerPath    = layerPath;
                        dv.LayerColor   = layerColor;
                        dv.ColorSource ??= "layer";
                    }
                }
                break;

            case OM.Mesh mesh:
                mesh.LayerPath  = layerPath;
                mesh.LayerColor = layerColor;
                mesh.ColorSource ??= "layer";
                break;

            case OM.Brep brep:
                brep.LayerPath  = layerPath;
                brep.LayerColor = layerColor;
                brep.ColorSource ??= "layer";
                break;

            default:
                converted["layerPath"]   = layerPath;
                converted["layerColor"]  = layerColor;
                if (converted["colorSource"] == null)
                    converted["colorSource"] = "layer";
                break;
        }
    }

    private OrbitBase? ConvertWithFallback(RhinoObject rhinoObj, ConversionContext context)
    {
        var geometry = rhinoObj.Geometry;
        if (geometry == null)
        {
            RhinoApp.WriteLine($"[ORBIT] skipped {rhinoObj.Id}: no geometry");
            return null;
        }

        string? lastReason = null;

        OrbitBase? TryConvert(Func<OrbitBase> fn, string stage)
        {
            try
            {
                return fn();
            }
            catch (Exception ex)
            {
                lastReason = $"{stage}: {ex.Message}";
                return null;
            }
        }

        var converter = _converters.FirstOrDefault(c => c.CanConvert(geometry));
        var primary = TryConvert(
            () => converter != null
                ? converter.Convert(geometry, context)
                : _fallback.Convert(geometry, context),
            converter?.GetType().Name ?? "RhinoFallbackConverter");

        if (primary != null)
            return primary;

        var fallbackMesh = TryConvert(
            () => _fallback.Convert(geometry, context),
            "RhinoFallbackConverter");

        if (fallbackMesh != null)
            return fallbackMesh;

        var extracted = RhinoObjectMeshes.ExtractFromObject(rhinoObj, context);
        if (extracted.Count > 0)
        {
            var meshConverter = new RhinoMeshConverter();
            var orbitMeshes = extracted
                .Select(m => meshConverter.Convert(m, context))
                .Cast<OrbitBase>()
                .ToList();

            if (orbitMeshes.Count == 1)
                return orbitMeshes[0];

            return new OrbitObject
            {
                DisplayValue = orbitMeshes,
            };
        }

        if (geometry is Curve curve)
        {
            var curveConverter = new RhinoCurveConverter();
            var curveObj = TryConvert(
                () => curveConverter.Convert(geometry, context),
                "RhinoCurveConverter");
            if (curveObj != null)
                return curveObj;
        }

        if (geometry is Surface surface)
        {
            var surfaceConverter = new RhinoSurfaceConverter();
            var surfaceObj = TryConvert(
                () => surfaceConverter.Convert(surface, context),
                "RhinoSurfaceConverter");
            if (surfaceObj != null)
                return surfaceObj;
        }

        var bboxMesh = RhinoObjectMeshes.BoundingBoxMesh(geometry);
        if (bboxMesh != null)
        {
            RhinoApp.WriteLine(
                $"[ORBIT] warning {rhinoObj.Id}: using bounding-box placeholder ({lastReason ?? "no mesh"})");
            return new RhinoMeshConverter().Convert(bboxMesh, context);
        }

        RhinoApp.WriteLine(
            $"[ORBIT] skipped {rhinoObj.Id}: {lastReason ?? "no conversion path"}");
        return null;
    }
}
