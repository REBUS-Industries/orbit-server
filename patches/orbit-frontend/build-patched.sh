#!/usr/bin/env sh
# Build orbit-frontend-patched from the LOCAL base image (registry layers are
# GC'd from GHCR, so BuildKit can't rebuild; we derive via docker commit).
#
# Patch: default viewer camera azimuth rotated ~180deg
# (SmoothOrbitControls.setOrbit 2.356 -> 5.498 rad; same direction as -0.785,
# chosen length-neutral so the identity bundle size is unchanged) so models
# open facing the front instead of the rear 3/4 corner. Polar (.955) unchanged.
#
# Nitro serves /_nuxt assets from a build-time manifest in
# server/chunks/nitro/nitro.mjs (etag/size/encoding) and prefers the
# precompressed .br/.gz sibling. So we rewrite the .js, regenerate .br/.gz, and
# update all three manifest etag+size entries. All compression + hashing is done
# with the image's own node (zlib brotli + crypto), no host deps.
set -eu
VER="${ORBIT_FRONTEND_VERSION:-v2.4.9}"
BASE="ghcr.io/rebus-orbit/orbit-frontend:${VER}"
TARGET="orbit-frontend-patched:${VER}"
WORK="$(mktemp -d)"
cleanup(){ docker rm -f fe-x fe-build >/dev/null 2>&1 || true; rm -rf "$WORK"; }
trap cleanup EXIT
docker rm -f fe-x fe-build >/dev/null 2>&1 || true
docker create --name fe-x "$BASE" >/dev/null
docker cp fe-x:/speckle-server/public/_nuxt "$WORK/_nuxt"
docker cp fe-x:/speckle-server/server/chunks/nitro/nitro.mjs "$WORK/nitro.mjs"
docker rm fe-x >/dev/null

cat > "$WORK/patch.cjs" <<'NODEEOF'
const fs=require('fs'),zlib=require('zlib'),crypto=require('crypto');
const dir='/w/_nuxt';
const OLD='setOrbit(2.356,.955)', NEW='setOrbit(5.498,.955)';
// find the entry chunk
const jsName=fs.readdirSync(dir).find(f=>f.endsWith('.js') && fs.readFileSync(dir+'/'+f,'utf8').includes(OLD));
if(!jsName){ console.error('ERR: literal not found in any _nuxt .js'); process.exit(1); }
const jsPath=dir+'/'+jsName;
const orig=fs.readFileSync(jsPath);
function etag(buf){ const h=crypto.createHash('sha1').update(buf).digest('base64').substring(0,27); return '"'+buf.length.toString(16)+'-'+h+'"'; }
// verify etag algorithm against the existing manifest before touching anything
let n=fs.readFileSync('/w/nitro.mjs','utf8');
const origEtagJson=JSON.stringify(etag(orig));
if(!n.includes(origEtagJson)){ console.error('ERR: etag algo mismatch for '+jsName+' (computed '+origEtagJson+')'); process.exit(1); }
console.log('etag algo verified against manifest:', origEtagJson);
// patch (length-neutral) + regenerate compressed
const js=Buffer.from(orig.toString('utf8').split(OLD).join(NEW),'utf8');
if(js.length!==orig.length){ console.error('ERR: patch not length-neutral'); process.exit(1); }
fs.writeFileSync(jsPath, js);
const br=zlib.brotliCompressSync(js,{params:{[zlib.constants.BROTLI_PARAM_QUALITY]:11}});
const gz=zlib.gzipSync(js,{level:9});
fs.writeFileSync(jsPath+'.br', br);
fs.writeFileSync(jsPath+'.gz', gz);
const meta={};
meta['/_nuxt/'+jsName]={size:js.length,etag:etag(js)};
meta['/_nuxt/'+jsName+'.br']={size:br.length,etag:etag(br)};
meta['/_nuxt/'+jsName+'.gz']={size:gz.length,etag:etag(gz)};
for(const key of Object.keys(meta)){
  const m=meta[key];
  const reKey=key.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
  const re=new RegExp('("'+reKey+'":\\s*\\{[\\s\\S]*?"etag":\\s*)"(?:\\\\.|[^"\\\\])*"([\\s\\S]*?"size":\\s*)\\d+');
  const before=n;
  n=n.replace(re,(_,p1,p2)=>p1+JSON.stringify(m.etag)+p2+m.size);
  if(n===before){ console.error('ERR: manifest entry not updated: '+key); process.exit(1); }
}
fs.writeFileSync('/w/nitro.mjs',n);
console.log('patched '+jsName+' + .br/.gz + manifest:', JSON.stringify(meta));
NODEEOF

docker run --rm --user 0:0 -v "$WORK":/w --entrypoint /nodejs/bin/node "$BASE" /w/patch.cjs

docker create --name fe-build "$BASE" >/dev/null
docker cp "$WORK/_nuxt/." fe-build:/speckle-server/public/_nuxt
docker cp "$WORK/nitro.mjs" fe-build:/speckle-server/server/chunks/nitro/nitro.mjs
docker commit fe-build "$TARGET" >/dev/null
docker rm fe-build >/dev/null
echo "built $TARGET"
docker image inspect "$TARGET" --format 'OK {{.Id}}'
