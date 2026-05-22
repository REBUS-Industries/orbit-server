using System.Security.Cryptography;
using Rhino;
using Rhino.DocObjects;
using Rhino.Render;
using DocTexture = Rhino.DocObjects.Texture;
using OrbitRenderMaterial = Orbit.Objects.Other.RenderMaterial;

namespace OrbitConnector.Rhino.Converters;

/// <summary>
/// Extracts PBR texture maps from Rhino render materials and attaches
/// <c>@blob:SHA256</c> placeholders to <see cref="RenderMaterial"/>.
/// Mirrors RebusWorkstationAgent / 3DConvert writer_speckle.py logic.
/// </summary>
internal static class RhinoMaterialHelper
{
    /// <summary>
    /// Process-wide temp dir used by the <c>SaveAsImage</c> fallback in
    /// <see cref="ResolveTexturePath"/>. Files written here are kept until
    /// the pipeline finishes uploading them as blobs; the OS cleans the temp
    /// root on a schedule so we do not need an aggressive sweeper.
    /// </summary>
    private static readonly string EmbeddedTextureCacheDir =
        Path.Combine(Path.GetTempPath(), "PRISM.Agent", "tex-cache");

    /// <summary>
    /// Resolve a texture file path from a <see cref="RenderTexture"/>.
    /// <para>
    /// Tries three strategies in order:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <c>SimulatedTexture(Allow).Filename</c> — Rhino's file cache.
    ///     Works for file-referenced textures and for embedded textures whose
    ///     SimulatedTexture entry has already been hydrated by the interactive
    ///     viewport. Returns <c>null</c> in PRISM headless mode for embedded
    ///     textures (the v0.1.14 failure mode).
    ///   </item>
    ///   <item>
    ///     <c>RenderContent.Filename</c> — the source path declared by the
    ///     texture content. Covers file-referenced textures whose simulated
    ///     cache entry is missing but whose original path exists on disk.
    ///   </item>
    ///   <item>
    ///     <c>RenderTexture.SaveAsImage(path, w, h, depth)</c> (Rhino 6.15+) —
    ///     explicitly materialises the texture (embedded blob or procedural)
    ///     into a PNG under <see cref="EmbeddedTextureCacheDir"/>. This is the
    ///     headless workaround that the ORBIT plug-in does not need (RDK keeps
    ///     its texture cache warm in interactive Rhino).
    ///   </item>
    /// </list>
    /// </summary>
    private static string? ResolveTexturePath(RenderTexture rt, Action<string>? log = null)
    {
        // Strategy 1: Rhino's SimulatedTexture cache (file-referenced + already-
        // hydrated embedded textures).
        try
        {
            var simTex = rt.SimulatedTexture(RenderTexture.TextureGeneration.Allow);
            var p = simTex?.Filename;
            var exists = !string.IsNullOrWhiteSpace(p) && File.Exists(p);
            log?.Invoke(
                $"      [resolve#1 SimulatedTexture(Allow)] path={(string.IsNullOrEmpty(p) ? "<null>" : p)} exists={exists}");
            if (exists) return p;
        }
        catch (Exception ex)
        {
            log?.Invoke($"      [resolve#1 SimulatedTexture(Allow)] threw {ex.GetType().Name}: {ex.Message}");
        }

        // Strategy 2: RenderContent.Filename — declared source path of a
        // file-based texture. (Rhino 7.4+, inherited via RenderContent.)
        try
        {
            var fn = rt.Filename;
            var exists = !string.IsNullOrWhiteSpace(fn) && File.Exists(fn);
            log?.Invoke(
                $"      [resolve#2 RenderContent.Filename] path={(string.IsNullOrEmpty(fn) ? "<null>" : fn)} exists={exists}");
            if (exists) return fn;
        }
        catch (Exception ex)
        {
            log?.Invoke($"      [resolve#2 RenderContent.Filename] threw {ex.GetType().Name}: {ex.Message}");
        }

        // Strategy 3: SaveAsImage — write the texture (embedded or procedural)
        // to a temp PNG. This is the embedded-texture fallback that fixes the
        // PRISM headless texture-loss bug (v0.1.16 / Fix 3).
        //
        // SaveAsImage was renamed from the never-existed `WriteImageFile` we
        // tried in v0.1.11 (and which failed to compile). Signature confirmed
        // against RhinoCommon 8.31 XML doc:
        //     bool SaveAsImage(string FullPath, int width, int height, int depth)
        // available since Rhino 6.15.
        try
        {
            int width = 1024, height = 1024, depth = 0;
            try
            {
                rt.PixelSize(out width, out height, out depth);
                if (width <= 0) width = 1024;
                if (height <= 0) height = 1024;
            }
            catch (Exception sizeEx)
            {
                log?.Invoke(
                    $"      [resolve#3 SaveAsImage] PixelSize threw {sizeEx.GetType().Name}; defaulting to 1024x1024");
            }

            Directory.CreateDirectory(EmbeddedTextureCacheDir);
            var outPath = Path.Combine(EmbeddedTextureCacheDir, $"{Guid.NewGuid():N}.png");

            bool ok;
            try
            {
                ok = rt.SaveAsImage(outPath, width, height, depth);
            }
            catch (Exception ex)
            {
                log?.Invoke($"      [resolve#3 SaveAsImage] threw {ex.GetType().Name}: {ex.Message}");
                return null;
            }

            var fi = File.Exists(outPath) ? new FileInfo(outPath) : null;
            var present = ok && fi is { Length: > 0 };
            log?.Invoke(
                $"      [resolve#3 SaveAsImage] {width}x{height}x{depth} ok={ok} size={(fi?.Length ?? 0)}B " +
                $"path={outPath} usable={present}");
            if (present) return outPath;

            // Clean up zero-byte output so we do not accumulate junk.
            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { /* best-effort */ }
        }
        catch (Exception ex)
        {
            log?.Invoke($"      [resolve#3 SaveAsImage] outer threw {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Resolve a <see cref="DocTexture"/> (returned by
    /// <c>Material.PhysicallyBased.GetTexture()</c>, <c>Material.GetTexture()</c>
    /// or <c>SimulatedMaterial.GetTexture()</c>) to a file path on disk.
    /// <para>
    /// Fast path: <c>FileReference.FullPath</c> when the artist-specified path
    /// still exists on the workstation.
    /// </para>
    /// <para>
    /// Embedded-texture fallback (v0.1.19): if the file path is missing or
    /// stale (e.g. the original lived in a Dropbox folder the workstation
    /// never had), look up the underlying <see cref="RenderTexture"/> by
    /// <see cref="DocTexture.Id"/> via
    /// <see cref="RenderContent.FromId(RhinoDoc, Guid)"/> and route it through
    /// <see cref="ResolveTexturePath"/>, which contains the
    /// <c>SaveAsImage</c> path that materialises the .3dm-embedded bitmap to
    /// a PNG under <see cref="EmbeddedTextureCacheDir"/>.
    /// </para>
    /// <para>
    /// The PBR / Bitmap / SimMat strategies could not previously trigger the
    /// SaveAsImage fallback because it only existed in
    /// <see cref="ResolveTexturePath"/> and those strategies never went
    /// through it — they only checked <c>tex.FileReference.FullPath</c>
    /// directly.
    /// </para>
    /// </summary>
    private static string? ResolveDocTextureWithFallback(
        RhinoDoc doc,
        DocTexture? tex,
        string strategyTag,
        Action<string>? log)
    {
        if (tex == null) return null;

        var p = tex.FileReference?.FullPath;
        if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
            return p;

        if (tex.Id == Guid.Empty)
        {
            log?.Invoke($"      [{strategyTag} fallback] tex.Id=Guid.Empty — no RenderContent to look up, trying File3dm.EmbeddedFiles");
        }
        else
        {
            try
            {
                var content = RenderContent.FromId(doc, tex.Id);
                if (content is not RenderTexture rt)
                {
                    log?.Invoke(
                        $"      [{strategyTag} fallback] RenderContent.FromId({tex.Id})=" +
                        $"{(content == null ? "<null>" : content.GetType().Name)} — cannot SaveAsImage");
                }
                else
                {
                    log?.Invoke(
                        $"      [{strategyTag} fallback] found RenderTexture by Id={tex.Id} — attempting SaveAsImage via ResolveTexturePath");
                    var resolved = ResolveTexturePath(rt, log);
                    if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                    {
                        long bytes = new FileInfo(resolved).Length;
                        log?.Invoke(
                            $"      [{strategyTag} fallback] SaveAsImage → wrote {bytes} bytes to {resolved}");
                        return resolved;
                    }

                    log?.Invoke(
                        $"      [{strategyTag} fallback] SaveAsImage failed (ResolveTexturePath returned null/missing)");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke(
                    $"      [{strategyTag} fallback] threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        // v0.1.20 fix: when the bitmap lives only in the File3dm archive's
        // EmbeddedFiles table (not in the RDK content registry that
        // RenderContent.FromId / doc.RenderTextures expose), reach into
        // File3dm.EmbeddedFiles directly via File3dm.Read(doc.Path) and
        // match by basename / full path.
        var embedded = TryEmbeddedFile(doc, tex, strategyTag, log);
        if (!string.IsNullOrWhiteSpace(embedded))
            return embedded;

        return null;
    }

    /// <summary>
    /// Dump everything we can find about the document's render content + any
    /// embedded files. Called once per conversion when the first material
    /// fails texture resolution, so we can see which mechanism Rhino is
    /// using to keep the embedded bitmap available to the interactive
    /// viewport (and which mechanism the connector should be querying).
    /// </summary>
    private static bool _docProbeEmitted;
    public static void ResetDocProbe()
    {
        _docProbeEmitted = false;
        _embeddedFileCache.Clear();
        _embeddedFileCacheLoaded = false;
    }
    private static void DumpDocRenderContent(RhinoDoc doc, Action<string>? log)
    {
        if (_docProbeEmitted || log is null) return;
        _docProbeEmitted = true;

        log("    [DOC-PROBE] -- exhaustive RDK / embedded-file probe --");

        // doc.RenderTextures: the canonical RDK render-texture content list.
        try
        {
            var rts = doc.RenderTextures;
            var count = rts?.Count() ?? 0;
            log($"    [DOC-PROBE] doc.RenderTextures.Count={count}");
            int i = 0;
            foreach (var c in rts ?? Enumerable.Empty<RenderContent>())
            {
                if (c is RenderTexture rt)
                {
                    var fn = SafeGet(() => rt.Filename);
                    var slot = SafeGet(() => rt.ChildSlotName);
                    log($"    [DOC-PROBE] RenderTexture[{i}] Id={rt.Id} TypeName='{rt.TypeName}' Filename='{fn}' ChildSlot='{slot}'");
                }
                else
                {
                    log($"    [DOC-PROBE] RenderTexture[{i}] type={c?.GetType().Name} Id={c?.Id}");
                }
                i++;
            }
        }
        catch (Exception ex)
        {
            log($"    [DOC-PROBE] doc.RenderTextures threw {ex.GetType().Name}: {ex.Message}");
        }

        // doc.RenderMaterials children — walk full RDK content tree to find
        // textures hidden under materials whose top-level FirstChild was null.
        try
        {
            var rms = doc.RenderMaterials;
            var count = rms?.Count() ?? 0;
            log($"    [DOC-PROBE] doc.RenderMaterials.Count={count}");
            int i = 0;
            foreach (var c in rms ?? Enumerable.Empty<RenderContent>())
            {
                log($"    [DOC-PROBE] RenderMaterial[{i}] Id={c.Id} TypeName='{c.TypeName}' Name='{c.Name}'");
                try
                {
                    var ch = c.FirstChild;
                    int j = 0;
                    while (ch != null)
                    {
                        var fn = SafeGet(() => ch is RenderTexture rrtt ? rrtt.Filename : null);
                        log($"    [DOC-PROBE]   child[{j}] Id={ch.Id} type={ch.GetType().Name} slot='{ch.ChildSlotName}' fn='{fn}'");
                        ch = ch.NextSibling;
                        j++;
                    }
                    if (j == 0) log($"    [DOC-PROBE]   (no children)");
                }
                catch (Exception ex)
                {
                    log($"    [DOC-PROBE]   children iteration threw {ex.GetType().Name}: {ex.Message}");
                }
                i++;
            }
        }
        catch (Exception ex)
        {
            log($"    [DOC-PROBE] doc.RenderMaterials threw {ex.GetType().Name}: {ex.Message}");
        }

        // Bitmap table (legacy embedded bitmaps).
        try
        {
            log($"    [DOC-PROBE] doc.Bitmaps.Count={doc.Bitmaps.Count}");
            for (int i = 0; i < doc.Bitmaps.Count; i++)
            {
                var bm = doc.Bitmaps[i];
                log($"    [DOC-PROBE] Bitmap[{i}] FileName='{bm.FileName}'");
            }
        }
        catch (Exception ex)
        {
            log($"    [DOC-PROBE] doc.Bitmaps threw {ex.GetType().Name}: {ex.Message}");
        }

        // For each Material in doc.Materials, probe the PhysicallyBased
        // textures and dump every property of the resulting Texture object.
        try
        {
            for (int i = 0; i < doc.Materials.Count; i++)
            {
                var m = doc.Materials[i];
                if (!m.IsPhysicallyBased || m.PhysicallyBased is null) continue;
                var pbr = m.PhysicallyBased;
                (TextureType type, string label)[] slots =
                [
                    (TextureType.PBR_BaseColor, "basecolor"),
                    (TextureType.PBR_Emission,  "emission"),
                    (TextureType.PBR_Roughness, "roughness"),
                    (TextureType.PBR_Metallic,  "metallic"),
                ];
                foreach (var (t, lbl) in slots)
                {
                    DocTexture? tex = null;
                    try { tex = pbr.GetTexture(t); } catch { /* skip */ }
                    if (tex == null) continue;
                    log($"    [DOC-PROBE] mat[{i}] '{m.Name}' slot={lbl} Texture.Id={tex.Id} Type={tex.TextureType}");
                    var fr = tex.FileReference;
                    if (fr != null)
                    {
                        log(
                            $"    [DOC-PROBE]   FileReference FullPath='{SafeGet(() => fr.FullPath)}' " +
                            $"RelativePath='{SafeGet(() => fr.RelativePath)}'");
                    }
                    try { log($"    [DOC-PROBE]   FileName='{tex.FileName}'"); } catch { /* ignore */ }
                    // Try to use the texture Id against doc.RenderTextures by
                    // direct iteration (FromId is broken).
                    try
                    {
                        var match = doc.RenderTextures?.FirstOrDefault(rc => rc.Id == tex.Id);
                        log($"    [DOC-PROBE]   doc.RenderTextures.firstOrDefault(Id={tex.Id}) → {(match == null ? "<null>" : match.GetType().Name)}");
                    }
                    catch (Exception ex)
                    {
                        log($"    [DOC-PROBE]   RenderTextures lookup threw {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log($"    [DOC-PROBE] doc.Materials probe threw {ex.GetType().Name}: {ex.Message}");
        }

        // File3dm.EmbeddedFiles probe — the .3dm archive's embedded-bitmap
        // table that interactive Rhino falls back to when the bitmap is not
        // in the RDK content registry. This is the table the v0.1.20 fix
        // reads from in TryEmbeddedFile / EnsureEmbeddedFileCache.
        try
        {
            var docPath = doc.Path;
            if (string.IsNullOrWhiteSpace(docPath) || !File.Exists(docPath))
            {
                log($"    [DOC-PROBE] doc.Path='{docPath}' — File3dm.EmbeddedFiles probe skipped (doc not on disk)");
            }
            else
            {
                using var f3dm = global::Rhino.FileIO.File3dm.Read(docPath);
                if (f3dm == null)
                {
                    log("    [DOC-PROBE] File3dm.Read returned null — cannot probe EmbeddedFiles");
                }
                else
                {
                    var ef = f3dm.GetType().GetProperty("EmbeddedFiles")?.GetValue(f3dm)
                             as System.Collections.IEnumerable;
                    if (ef == null)
                    {
                        log("    [DOC-PROBE] File3dm.EmbeddedFiles property not available on this RhinoCommon");
                    }
                    else
                    {
                        Directory.CreateDirectory(EmbeddedTextureCacheDir);
                        int idx = 0;
                        int extracted = 0;
                        foreach (var entry in ef)
                        {
                            idx++;
                            string? entryName = null;
                            try { entryName = entry.GetType().GetProperty("Filename")?.GetValue(entry) as string; }
                            catch { /* ignore */ }
                            var baseName = string.IsNullOrWhiteSpace(entryName) ? "<null>" : Path.GetFileName(entryName);
                            var outPath = Path.Combine(
                                EmbeddedTextureCacheDir,
                                $"{Guid.NewGuid():N}_{MakeFileNameSafe(baseName)}");
                            bool ok = false;
                            string? winningCandidate = null;
                            try
                            {
                                ok = TryExtractEmbeddedEntry(
                                    ef, entry, idx, outPath, log,
                                    out winningCandidate);
                            }
                            catch (Exception ex)
                            {
                                log($"    [DOC-PROBE] EmbeddedFile[{idx}] candidate-chain threw {ex.GetType().Name}: {ex.Message}");
                            }
                            var size = File.Exists(outPath) ? new FileInfo(outPath).Length : 0L;
                            log($"    [DOC-PROBE] EmbeddedFile[{idx}] Filename='{entryName}' → '{outPath}' ok={ok} size={size}B via '{winningCandidate ?? "<none>"}'");
                            if (ok) extracted++;
                        }
                        log($"    [DOC-PROBE] File3dm.EmbeddedFiles scanned={idx} extracted={extracted}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log($"    [DOC-PROBE] File3dm.EmbeddedFiles probe threw {ex.GetType().Name}: {ex.Message}");
        }

        log("    [DOC-PROBE] -- end --");
    }

    private static string SafeGet(Func<string?> f)
    {
        try { return f() ?? "<null>"; }
        catch (Exception ex) { return $"<threw {ex.GetType().Name}>"; }
    }

    /// <summary>
    /// Probe the four texture-discovery strategies for any texture reference
    /// whose <c>FileReference.FullPath</c> is set but does not exist on this
    /// workstation. Used by <see cref="AttachTextures"/> to emit a clear
    /// <c>[ORBIT-TEXTURE-MISSING]</c> diagnostic when the .3dm references
    /// linked textures that the headless agent cannot read (the typical
    /// "Dropbox path on the artist's machine, not on PC02" failure mode).
    /// </summary>
    private static List<(string slot, string path)> CollectUnresolvedTexturePaths(
        global::Rhino.DocObjects.Material mat, RhinoDoc doc)
    {
        var found = new List<(string slot, string path)>();
        void Add(string slot, DocTexture? tex)
        {
            var p = tex?.FileReference?.FullPath;
            if (!string.IsNullOrWhiteSpace(p) && !File.Exists(p!))
                found.Add((slot, p!));
        }

        try
        {
            if (mat.IsPhysicallyBased && mat.PhysicallyBased is not null)
            {
                var pbr = mat.PhysicallyBased;
                Add("basecolor", pbr.GetTexture(TextureType.PBR_BaseColor));
                Add("emission",  pbr.GetTexture(TextureType.PBR_Emission));
                Add("roughness", pbr.GetTexture(TextureType.PBR_Roughness));
                Add("metallic",  pbr.GetTexture(TextureType.PBR_Metallic));
            }
        }
        catch { /* best-effort probe */ }

        try { Add("basecolor", mat.GetTexture(TextureType.Bitmap)); }
        catch { /* best-effort probe */ }

        return found;
    }

    public static void AttachTextures(
        RhinoObject rhinoObj,
        OrbitRenderMaterial rm,
        RhinoDoc doc,
        IDictionary<string, string> pendingBlobFiles,
        Action<string>? log = null)
    {
        try
        {
            var mat = rhinoObj.GetMaterial(true);
            if (mat is null)
            {
                log?.Invoke($"  [tex] obj {rhinoObj.Id} mat=<null>; nothing to attach");
                return;
            }

            var renderMat = mat.RenderMaterial;
            var slotsAttached = new HashSet<string>();

            log?.Invoke(
                $"  [tex] obj {rhinoObj.Id} mat='{mat.Name}' idx={mat.Index} pbr={mat.IsPhysicallyBased} " +
                $"renderMat={(renderMat is null ? "<null>" : renderMat.TypeName)}");

            void AttachSlot(string slot, string path)
            {
                if (!File.Exists(path))
                {
                    log?.Invoke($"      AttachSlot SKIPPED ({slot}): file vanished at {path}");
                    return;
                }
                var hashHex = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                var added = pendingBlobFiles.TryAdd(hashHex, path);
                var size = new FileInfo(path).Length;
                log?.Invoke(
                    $"      AttachSlot OK ({slot}): hash={hashHex[..16]}… size={size}B " +
                    $"path={path} added={added} (PendingBlobFiles={pendingBlobFiles.Count})");
                var blobRef = $"@blob:{hashHex}";

                switch (slot)
                {
                    case "basecolor":
                        rm.BaseColorTexture = blobRef;
                        rm.DiffuseTexture   = blobRef;
                        // NOTE: do NOT override rm.Diffuse here. The previous
                        // value (0xFF000000 pure black) bricked rendering in
                        // every PBR shader that does `output = texture *
                        // baseColorFactor`, because texture × (0,0,0) = (0,0,0)
                        // — the model renders pitch black even though the
                        // texture blob uploaded and patched fine. The post-
                        // attach promotion at the bottom of AttachTextures
                        // upgrades a pure-black diffuse to opaque white only
                        // when a basecolor texture has actually been attached;
                        // any real diffuse tint set by BuildCurrentRenderMaterial
                        // (e.g. layer colour, PBR base-colour, or MTL `Kd`)
                        // is now preserved end-to-end.
                        slotsAttached.Add("basecolor");
                        break;
                    case "emission":
                        rm.EmissiveTexture       = blobRef;
                        rm.PbrEmissionTexture    = blobRef;
                        rm.EmissiveTextureOffset = new List<double> { 0.0, 0.0 };
                        rm.EmissiveTextureRepeat = new List<double> { 1.0, 1.0 };
                        slotsAttached.Add("emission");
                        break;
                    case "roughness":
                        rm.RoughnessTexture = blobRef;
                        slotsAttached.Add("roughness");
                        break;
                    case "metallic":
                        rm.MetalnessTexture = blobRef;
                        slotsAttached.Add("metallic");
                        break;
                    case "bump":
                        rm.NormalTexture = blobRef;
                        slotsAttached.Add("bump");
                        break;
                    case "opacity":
                        rm.OpacityTexture = blobRef;
                        slotsAttached.Add("opacity");
                        break;
                    default:
                        rm[$"{slot}Texture"] = blobRef;
                        slotsAttached.Add(slot);
                        break;
                }
            }

            static string ClassifySlot(string slotName)
            {
                var s = slotName.ToLowerInvariant();
                // Rhino's legacy (non-PBR) material exposes its single bitmap
                // via the RDK child slot named "bitmap-texture" — that bitmap
                // IS the diffuse / base-colour channel (verified against OBJ /
                // MTL imports where Kd / map_Kd produce exactly this slot
                // name). Without the StartsWith("bitmap") prong here, the
                // RDK traversal classifies the slot as "other_bitmap-texture"
                // and stores the texture under an unknown dynamic property
                // that the SDK's TextureBlobPatcher leaves unpatched —
                // leaking a literal "@blob:HASH" placeholder onto the wire.
                if (s.Contains("base") || s == "diffuse" || s == "color" || s.StartsWith("bitmap"))
                    return "basecolor";
                if (s.Contains("roughness")) return "roughness";
                if (s.Contains("metallic") || s.Contains("metalness")) return "metallic";
                if (s.Contains("emission") || s.Contains("emissive")) return "emission";
                if (s.Contains("bump") || s.Contains("normal")) return "bump";
                if (s.Contains("alpha") || s.Contains("opacity")) return "opacity";
                return $"other_{s}";
            }

            // Strategy 1: RDK FirstChild/NextSibling traversal
            if (renderMat is not null)
            {
                try
                {
                    var child = renderMat.FirstChild;
                    if (child is null)
                        log?.Invoke("    [strat1 RDK] FirstChild=<null> — RDK reports no child textures");
                    int strat1Visited = 0;
                    while (child is not null)
                    {
                        strat1Visited++;
                        if (child is RenderTexture rt)
                        {
                            var slotName = rt.ChildSlotName ?? string.Empty;
                            var slot = ClassifySlot(slotName);
                            log?.Invoke(
                                $"    [strat1 RDK] child #{strat1Visited} slotName='{slotName}' classified='{slot}' " +
                                $"already={slotsAttached.Contains(slot)}");
                            if (!slotsAttached.Contains(slot) || slot.StartsWith("other_"))
                            {
                                try
                                {
                                    var p = ResolveTexturePath(rt, log);
                                    if (!string.IsNullOrWhiteSpace(p))
                                        AttachSlot(slot, p);
                                    else
                                        log?.Invoke($"      [strat1 RDK] resolve returned null for slot '{slot}'");
                                }
                                catch (Exception ex)
                                {
                                    log?.Invoke($"      [strat1 RDK] AttachSlot threw {ex.GetType().Name}: {ex.Message}");
                                }
                            }
                        }
                        else
                        {
                            log?.Invoke(
                                $"    [strat1 RDK] child #{strat1Visited} not a RenderTexture (type={child.GetType().Name})");
                        }
                        child = child.NextSibling;
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"    [strat1 RDK] traversal threw {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Strategy 2: PhysicallyBased channel API
            if (mat.IsPhysicallyBased && mat.PhysicallyBased is not null)
            {
                var pbr = mat.PhysicallyBased;
                (TextureType type, string slot)[] pbrMap =
                [
                    (TextureType.PBR_BaseColor, "basecolor"),
                    (TextureType.PBR_Emission,  "emission"),
                    (TextureType.PBR_Roughness, "roughness"),
                    (TextureType.PBR_Metallic,  "metallic"),
                ];
                foreach (var (texType, slot) in pbrMap)
                {
                    if (slotsAttached.Contains(slot))
                    {
                        log?.Invoke($"    [strat2 PBR] {texType} → '{slot}' SKIP (already attached)");
                        continue;
                    }
                    try
                    {
                        var tex = pbr.GetTexture(texType);
                        var p = tex?.FileReference?.FullPath;
                        var present = !string.IsNullOrWhiteSpace(p);
                        var exists = present && File.Exists(p!);
                        log?.Invoke(
                            $"    [strat2 PBR] {texType} → '{slot}' " +
                            $"GetTexture={(tex == null ? "<null>" : "ok")} " +
                            $"FullPath={(present ? p : "<null>")} exists={exists}");
                        if (exists)
                        {
                            AttachSlot(slot, p!);
                        }
                        else if (tex != null)
                        {
                            // FullPath was missing or stale (e.g. artist's Dropbox
                            // folder not on workstation). The .3dm may still embed
                            // the bitmap — try SaveAsImage on the underlying
                            // RenderTexture (v0.1.19 fix).
                            var rescued = ResolveDocTextureWithFallback(doc, tex, $"strat2 PBR {texType}", log);
                            if (!string.IsNullOrWhiteSpace(rescued))
                                AttachSlot(slot, rescued!);
                        }
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"    [strat2 PBR] {texType} threw {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            else
            {
                log?.Invoke("    [strat2 PBR] skipped — material is not PhysicallyBased");
            }

            // Strategy 3: legacy Bitmap (file path)
            if (!slotsAttached.Contains("basecolor"))
            {
                try
                {
                    var tex = mat.GetTexture(TextureType.Bitmap);
                    var p = tex?.FileReference?.FullPath;
                    var present = !string.IsNullOrWhiteSpace(p);
                    var exists = present && File.Exists(p!);
                    log?.Invoke(
                        $"    [strat3 Bitmap] GetTexture={(tex == null ? "<null>" : "ok")} " +
                        $"FullPath={(present ? p : "<null>")} exists={exists}");
                    if (exists)
                    {
                        AttachSlot("basecolor", p!);
                    }
                    else if (tex != null)
                    {
                        var rescued = ResolveDocTextureWithFallback(doc, tex, "strat3 Bitmap", log);
                        if (!string.IsNullOrWhiteSpace(rescued))
                            AttachSlot("basecolor", rescued!);
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"    [strat3 Bitmap] threw {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Strategy 4: SimulatedMaterial fallback
            if (!slotsAttached.Contains("basecolor") && renderMat is not null)
            {
                try
                {
                    var simMat = renderMat.ToMaterial(
                        RenderTexture.TextureGeneration.Allow);
                    DocTexture? simTex = null;
                    if (mat.IsPhysicallyBased && simMat?.IsPhysicallyBased == true)
                        simTex = simMat.PhysicallyBased?.GetTexture(TextureType.PBR_BaseColor);
                    if (simTex == null)
                        simTex = simMat?.GetTexture(TextureType.Bitmap);
                    var p = simTex?.FileReference?.FullPath;
                    var present = !string.IsNullOrWhiteSpace(p);
                    var exists = present && File.Exists(p!);
                    log?.Invoke(
                        $"    [strat4 SimMat] simMat={(simMat == null ? "<null>" : "ok")} " +
                        $"simTex={(simTex == null ? "<null>" : "ok")} " +
                        $"FullPath={(present ? p : "<null>")} exists={exists}");
                    if (exists)
                    {
                        AttachSlot("basecolor", p!);
                    }
                    else if (simTex != null)
                    {
                        var rescued = ResolveDocTextureWithFallback(doc, simTex, "strat4 SimMat", log);
                        if (!string.IsNullOrWhiteSpace(rescued))
                            AttachSlot("basecolor", rescued!);
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"    [strat4 SimMat] threw {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (slotsAttached.Count == 0)
            {
                // Build a "what we DID find but couldn't resolve" trail so the
                // user can immediately see whether the source material has no
                // textures at all (fine), or has texture references whose
                // files are not available on this workstation (actionable —
                // embed them in the .3dm or sync the texture folder).
                var unresolved = CollectUnresolvedTexturePaths(mat, doc);
                if (unresolved.Count == 0)
                {
                    log?.Invoke(
                        $"  [tex] obj {rhinoObj.Id} mat='{mat.Name}' → NO TEXTURES ATTACHED " +
                        "(material has no texture references — colour-only material)");
                }
                else
                {
                    log?.Invoke(
                        $"  [tex] obj {rhinoObj.Id} mat='{mat.Name}' → " +
                        "[ORBIT-TEXTURE-MISSING] material references " +
                        $"{unresolved.Count} texture file(s) that do NOT exist on this " +
                        "workstation (PRISM.Agent cannot upload textures from paths it " +
                        "cannot read). Embed the textures in the .3dm (Rhino: File → Save " +
                        "As → 'Save textures' option) or place them on a path the " +
                        "workstation can access:");
                    foreach (var (slot, path) in unresolved)
                    {
                        log?.Invoke($"      missing-file slot='{slot}' path='{path}'");
                    }
                    // Fire the doc-level probe once so we can see where the
                    // embedded bitmap data actually lives in this .3dm.
                    DumpDocRenderContent(doc, log);
                }
                return;
            }

            log?.Invoke(
                $"  [tex] obj {rhinoObj.Id} mat='{mat.Name}' → attached slots: " +
                $"[{string.Join(", ", slotsAttached)}]");

            // Texture-slot rescue. If RDK / PBR strategies only found a texture
            // in the "emission" slot but the Rhino material has NO genuine
            // emission colour (i.e. the user never set the material to glow —
            // it's just a base-colour bitmap that Rhino's slot classifier put
            // in the wrong bucket), promote that texture to be the base-colour
            // texture instead. Otherwise the viewer renders the texture as
            // additive glow on top of an unrelated diffuse colour and the
            // model looks much brighter than the source Rhino viewport.
            var emissionIsRealGlow = mat.EmissionColor.R != 0
                                  || mat.EmissionColor.G != 0
                                  || mat.EmissionColor.B != 0;
            if (slotsAttached.Contains("emission")
                && !slotsAttached.Contains("basecolor")
                && !emissionIsRealGlow
                && !string.IsNullOrEmpty(rm.EmissiveTexture))
            {
                var blobRef = rm.EmissiveTexture!;
                rm.BaseColorTexture     = blobRef;
                rm.DiffuseTexture       = blobRef;
                // Diffuse left untouched — see the note in AttachSlot("basecolor"):
                // overriding to pure black would zero-out every PBR shader that
                // computes `output = texture * baseColorFactor`. The post-attach
                // promotion below upgrades a pure-black diffuse to opaque white
                // only when needed, preserving any real diffuse tint that was
                // already on the material.
                rm.EmissiveTexture      = null;
                rm.PbrEmissionTexture   = null;
                rm.EmissiveTextureOffset = null;
                rm.EmissiveTextureRepeat = null;
                slotsAttached.Add("basecolor");
                slotsAttached.Remove("emission");
                log?.Invoke("    [rescue] promoted emission→basecolor (mat.EmissionColor is black)");
            }

            // Conservative emissive handling: only mark the material as glowing
            // when Rhino itself reports a non-black emission colour. We never
            // synthesise a white emission to "boost" a base-colour texture —
            // doing so makes everything ~2× brighter than the source Rhino
            // viewport (the original v2.4.0 bug we shipped to old Speckle).
            if (slotsAttached.Contains("emission") && emissionIsRealGlow)
            {
                rm.EmissiveIntensity = 1.0;
            }
            else if (slotsAttached.Contains("emission"))
            {
                // Emission texture exists but Rhino's emission colour is black —
                // suppress glow so the viewer doesn't render an unintended
                // additive layer. (Anything in `emissiveTexture` will still be
                // multiplied by emissive colour = black = no glow.)
                rm.Emissive = 4278190080L;
                rm.EmissiveIntensity = 0.0;
            }

            // Post-attach diffuse promotion. Three.js-style PBR shaders compute
            //   finalAlbedo = baseColorTexture * baseColorFactor
            // so a pure-black diffuse (0xFF000000) renders pitch black even
            // when a perfectly valid `baseColorTexture` + `diffuseTexture` are
            // attached. This is exactly what bricked the OBJ-bundle texture
            // upload in the first PRISM .zip ingest (job cdc3a979-3025-...:
            // server-side root JSON had `diffuse=4278190080` paired with a
            // patched short blob id — viewer rendered solid black despite all
            // upstream stages succeeding). Mirrors the same guard in the
            // Python 3DConvert/app/writer_speckle.py:_build_render_material
            // and the RebusWorkstationAgent JobProcessor.cs flow.
            //
            // Only promote when a real basecolor texture has been attached AND
            // the current diffuse is the all-zero RGB sentinel: any non-black
            // tint (layer colour, PBR baseColor, MTL Kd) is preserved as-is so
            // it can modulate the texture exactly the way Rhino renders it.
            if (slotsAttached.Contains("basecolor") && (rm.Diffuse & 0x00FFFFFFL) == 0L)
            {
                rm.Diffuse = 4294967295L; // 0xFFFFFFFF — opaque white
                log?.Invoke("    [diffuse-promote] basecolor texture attached + diffuse was pure black → promoted to opaque white so the texture renders at full brightness");
            }

            // PBR scalars from Rhino material when available
            if (mat.IsPhysicallyBased && mat.PhysicallyBased is not null)
            {
                var pbr = mat.PhysicallyBased;
                rm.Roughness = pbr.Roughness;
                rm.Metalness = pbr.Metallic;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"  [tex] obj {rhinoObj.Id} AttachTextures top-level threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // v0.1.20 fix: File3dm.EmbeddedFiles fallback. When the source .3dm was
    // saved with the `Save textures` option but the bitmap is NOT exposed via
    // the RDK content registry (doc.RenderTextures / RenderContent.FromId /
    // RenderMaterials children), the only way to recover it is to lazy-open
    // the .3dm as a File3dm via `Rhino.FileIO.File3dm.Read(doc.Path)` and walk
    // `f3dm.EmbeddedFiles`, extracting each entry to a temp file and matching
    // against the missing texture path (full path or basename, case-insensitive).
    // -------------------------------------------------------------------------

    private static readonly Dictionary<string, string> _embeddedFileCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _embeddedFileCacheLoaded;

    private static string? TryEmbeddedFile(
        RhinoDoc doc,
        DocTexture tex,
        string strategyTag,
        Action<string>? log)
    {
        var refFullPath = tex.FileReference?.FullPath ?? string.Empty;
        var refBaseName = string.IsNullOrWhiteSpace(refFullPath)
            ? string.Empty
            : Path.GetFileName(refFullPath);

        if (string.IsNullOrWhiteSpace(refFullPath) && string.IsNullOrWhiteSpace(refBaseName))
        {
            log?.Invoke(
                $"      [{strategyTag} embeddedfile] tex has no FileReference.FullPath — nothing to match");
            return null;
        }

        EnsureEmbeddedFileCache(doc, log);

        if (!string.IsNullOrWhiteSpace(refFullPath)
            && _embeddedFileCache.TryGetValue(refFullPath, out var hitFull)
            && File.Exists(hitFull))
        {
            log?.Invoke(
                $"      [{strategyTag} embeddedfile] matched full path '{refFullPath}' → {hitFull}");
            return hitFull;
        }

        if (!string.IsNullOrWhiteSpace(refBaseName)
            && _embeddedFileCache.TryGetValue(refBaseName, out var hitBase)
            && File.Exists(hitBase))
        {
            log?.Invoke(
                $"      [{strategyTag} embeddedfile] matched basename '{refBaseName}' → {hitBase}");
            return hitBase;
        }

        log?.Invoke(
            $"      [{strategyTag} embeddedfile] no EmbeddedFiles entry matched " +
            $"(fullPath='{refFullPath}' baseName='{refBaseName}' cacheSize={_embeddedFileCache.Count})");
        return null;
    }

    private static void EnsureEmbeddedFileCache(RhinoDoc doc, Action<string>? log)
    {
        if (_embeddedFileCacheLoaded) return;
        _embeddedFileCacheLoaded = true;

        try
        {
            var docPath = doc.Path;
            if (string.IsNullOrWhiteSpace(docPath) || !File.Exists(docPath))
            {
                log?.Invoke(
                    $"      [embeddedfile cache] doc.Path='{docPath}' — cannot open File3dm to read EmbeddedFiles");
                return;
            }

            using var f3dm = global::Rhino.FileIO.File3dm.Read(docPath);
            if (f3dm == null)
            {
                log?.Invoke(
                    $"      [embeddedfile cache] File3dm.Read('{docPath}') returned null — cache empty");
                return;
            }

            // Reflective access keeps the build green if a future RhinoCommon
            // hides File3dm.EmbeddedFiles or its element type.
            var ef = f3dm.GetType().GetProperty("EmbeddedFiles")?.GetValue(f3dm)
                     as System.Collections.IEnumerable;
            if (ef == null)
            {
                log?.Invoke(
                    "      [embeddedfile cache] File3dm.EmbeddedFiles property not available on this RhinoCommon");
                return;
            }

            Directory.CreateDirectory(EmbeddedTextureCacheDir);
            int scanned = 0;
            int extracted = 0;
            foreach (var entry in ef)
            {
                scanned++;
                try
                {
                    var entryName = entry.GetType().GetProperty("Filename")?.GetValue(entry) as string;
                    if (string.IsNullOrWhiteSpace(entryName))
                    {
                        log?.Invoke(
                            $"      [embeddedfile cache] entry #{scanned} has no Filename — skipping");
                        continue;
                    }

                    var baseName = Path.GetFileName(entryName);
                    var outPath = Path.Combine(
                        EmbeddedTextureCacheDir,
                        $"{Guid.NewGuid():N}_{MakeFileNameSafe(baseName)}");

                    var ok = TryExtractEmbeddedEntry(
                        ef, entry, scanned, outPath, log,
                        out var winningCandidate);

                    if (!ok || !File.Exists(outPath) || new FileInfo(outPath).Length == 0)
                    {
                        log?.Invoke(
                            $"      [embeddedfile cache] entry '{entryName}' all extraction candidates failed → '{outPath}'");
                        try { if (File.Exists(outPath)) File.Delete(outPath); } catch { /* best-effort */ }
                        continue;
                    }

                    var size = new FileInfo(outPath).Length;
                    _embeddedFileCache[entryName] = outPath;
                    _embeddedFileCache[baseName] = outPath;
                    extracted++;
                    log?.Invoke(
                        $"      [embeddedfile cache] cached '{entryName}' (basename='{baseName}') → {outPath} size={size}B via '{winningCandidate}'");
                }
                catch (Exception ex)
                {
                    log?.Invoke(
                        $"      [embeddedfile cache] entry #{scanned} threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
            }

            log?.Invoke(
                $"      [embeddedfile cache] populated from File3dm '{docPath}': scanned={scanned} extracted={extracted}");
        }
        catch (Exception ex)
        {
            log?.Invoke(
                $"      [embeddedfile cache] outer threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Try a chain of candidate APIs to extract a single
    /// <c>File3dm.EmbeddedFiles</c> entry to <paramref name="outPath"/>.
    /// First non-null/non-empty result wins; the winning candidate name is
    /// returned in <paramref name="winningCandidate"/> so a follow-up patch
    /// can collapse the chain to a single direct call.
    /// <para>
    /// On the very first call (process-wide), dumps the table type, entry
    /// type, properties, and methods so the next job's logs reveal exactly
    /// which API the live RhinoCommon exposes.
    /// </para>
    /// </summary>
    private static bool _embeddedTypeInfoDumped;

    private static bool TryExtractEmbeddedEntry(
        object table,
        object entry,
        int oneBasedIndex,
        string outPath,
        Action<string>? log,
        out string? winningCandidate)
    {
        winningCandidate = null;

        // One-shot type-info probe.
        if (!_embeddedTypeInfoDumped)
        {
            _embeddedTypeInfoDumped = true;
            try
            {
                var tt = table.GetType();
                log?.Invoke($"      [embeddedfile cache] table type = {tt.FullName}, assembly={tt.Assembly.GetName().Name}");
                foreach (var p in tt.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    log?.Invoke($"      [embeddedfile cache]   table-prop {p.PropertyType.Name} {p.Name} (CanRead={p.CanRead} CanWrite={p.CanWrite})");
                foreach (var m in tt.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    var ps = string.Join(", ", m.GetParameters().Select(pp => $"{pp.ParameterType.Name} {pp.Name}"));
                    log?.Invoke($"      [embeddedfile cache]   table-method {m.ReturnType.Name} {m.Name}({ps})");
                }

                var et = entry.GetType();
                log?.Invoke($"      [embeddedfile cache] entry type = {et.FullName}, assembly={et.Assembly.GetName().Name}");
                foreach (var p in et.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    log?.Invoke($"      [embeddedfile cache]   prop {p.PropertyType.Name} {p.Name} (CanRead={p.CanRead} CanWrite={p.CanWrite})");
                foreach (var m in et.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    var ps = string.Join(", ", m.GetParameters().Select(pp => $"{pp.ParameterType.Name} {pp.Name}"));
                    log?.Invoke($"      [embeddedfile cache]   method {m.ReturnType.Name} {m.Name}({ps})");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"      [embeddedfile cache] type-info dump threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        var et2 = entry.GetType();

        // Strategy 1a: bytes-returning instance properties.
        foreach (var name in new[] { "Bytes", "Content", "Data" })
        {
            try
            {
                var p = et2.GetProperty(name);
                if (p == null || !p.CanRead) continue;
                var raw = p.GetValue(entry);
                if (raw is byte[] bytes && bytes.Length > 0)
                {
                    File.WriteAllBytes(outPath, bytes);
                    log?.Invoke($"      [embeddedfile cache] candidate 'prop:{name}' result=ok size={bytes.Length}B");
                    winningCandidate = $"prop:{name}";
                    return true;
                }
                log?.Invoke($"      [embeddedfile cache] candidate 'prop:{name}' result=null/empty (type={raw?.GetType().Name ?? "<null>"})");
            }
            catch (Exception ex)
            {
                log?.Invoke($"      [embeddedfile cache] candidate 'prop:{name}' threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Strategy 1b: bytes/Stream-returning parameterless methods.
        foreach (var name in new[] { "GetBytes", "GetContents", "Read" })
        {
            try
            {
                var m = et2.GetMethod(name, Type.EmptyTypes);
                if (m == null) continue;
                var raw = m.Invoke(entry, Array.Empty<object>());
                if (raw is byte[] bytes && bytes.Length > 0)
                {
                    File.WriteAllBytes(outPath, bytes);
                    log?.Invoke($"      [embeddedfile cache] candidate 'method:{name}()' result=ok bytes={bytes.Length}B");
                    winningCandidate = $"method:{name}()";
                    return true;
                }
                if (raw is System.IO.Stream stream)
                {
                    using (var fs = File.Create(outPath))
                    {
                        stream.CopyTo(fs);
                    }
                    try { stream.Dispose(); } catch { /* best-effort */ }
                    if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                    {
                        log?.Invoke($"      [embeddedfile cache] candidate 'method:{name}()->Stream' result=ok size={new FileInfo(outPath).Length}B");
                        winningCandidate = $"method:{name}()->Stream";
                        return true;
                    }
                }
                log?.Invoke($"      [embeddedfile cache] candidate 'method:{name}()' result=null/empty (type={raw?.GetType().Name ?? "<null>"})");
            }
            catch (Exception ex)
            {
                log?.Invoke($"      [embeddedfile cache] candidate 'method:{name}()' threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Strategy 2: entry-level path-taking methods.
        // Note: 'Write' is included for completeness, but we know it does not
        // exist on the live RhinoCommon entry type (job 15463fa2 confirmed).
        foreach (var name in new[] { "SaveToFile", "Save", "SaveAs", "Extract", "ExtractToFile", "WriteToFile", "CopyTo", "Write" })
        {
            try
            {
                var m = et2.GetMethod(name, new[] { typeof(string) });
                if (m == null) continue;
                var ret = m.Invoke(entry, new object[] { outPath });
                bool fileOk = File.Exists(outPath) && new FileInfo(outPath).Length > 0;
                bool boolOk = ret is bool b && b;
                if (boolOk || fileOk)
                {
                    log?.Invoke($"      [embeddedfile cache] candidate 'method:{name}(string)' result=ok ret={ret} size={(fileOk ? new FileInfo(outPath).Length : 0)}B");
                    winningCandidate = $"method:{name}(string)";
                    return true;
                }
                log?.Invoke($"      [embeddedfile cache] candidate 'method:{name}(string)' result=fail ret={ret} fileOk={fileOk}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"      [embeddedfile cache] candidate 'method:{name}(string)' threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Strategy 3: table-level (int, string) methods that operate on the
        // current index. Many Rhino tables expose I/O at the table layer
        // rather than the entry.
        var tt2 = table.GetType();
        int zeroBasedIndex = oneBasedIndex - 1;

        foreach (var name in new[] { "Save", "SaveAs", "Extract", "ExtractToFile", "Write" })
        {
            try
            {
                var m = tt2.GetMethod(name, new[] { typeof(int), typeof(string) });
                if (m == null) continue;
                var ret = m.Invoke(table, new object[] { zeroBasedIndex, outPath });
                bool fileOk = File.Exists(outPath) && new FileInfo(outPath).Length > 0;
                bool boolOk = ret is bool b && b;
                if (boolOk || fileOk)
                {
                    log?.Invoke($"      [embeddedfile cache] candidate 'table:{name}(int,string)' result=ok ret={ret} size={(fileOk ? new FileInfo(outPath).Length : 0)}B");
                    winningCandidate = $"table:{name}(int,string)";
                    return true;
                }
                log?.Invoke($"      [embeddedfile cache] candidate 'table:{name}(int,string)' result=fail ret={ret} fileOk={fileOk}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"      [embeddedfile cache] candidate 'table:{name}(int,string)' threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Strategy 3b: table-level (int) -> byte[] methods.
        foreach (var name in new[] { "GetBytes", "GetItem", "Read" })
        {
            try
            {
                var m = tt2.GetMethod(name, new[] { typeof(int) });
                if (m == null) continue;
                var raw = m.Invoke(table, new object[] { zeroBasedIndex });
                if (raw is byte[] bytes && bytes.Length > 0)
                {
                    File.WriteAllBytes(outPath, bytes);
                    log?.Invoke($"      [embeddedfile cache] candidate 'table:{name}(int)' result=ok bytes={bytes.Length}B");
                    winningCandidate = $"table:{name}(int)->byte[]";
                    return true;
                }
                if (raw is System.IO.Stream stream)
                {
                    using (var fs = File.Create(outPath))
                    {
                        stream.CopyTo(fs);
                    }
                    try { stream.Dispose(); } catch { /* best-effort */ }
                    if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                    {
                        log?.Invoke($"      [embeddedfile cache] candidate 'table:{name}(int)->Stream' result=ok size={new FileInfo(outPath).Length}B");
                        winningCandidate = $"table:{name}(int)->Stream";
                        return true;
                    }
                }
                log?.Invoke($"      [embeddedfile cache] candidate 'table:{name}(int)' result=null/empty (type={raw?.GetType().Name ?? "<null>"})");
            }
            catch (Exception ex)
            {
                log?.Invoke($"      [embeddedfile cache] candidate 'table:{name}(int)' threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        return false;
    }

    private static string MakeFileNameSafe(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "embedded.bin";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        var s = new string(chars);
        if (s.Length > 80) s = s.Substring(0, 80);
        return string.IsNullOrWhiteSpace(s) ? "embedded.bin" : s;
    }
}
