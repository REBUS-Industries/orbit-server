import { readdir, readFile } from 'node:fs/promises';
import { join, resolve, basename } from 'node:path';
import Fastify from 'fastify';
import MarkdownIt from 'markdown-it';

const md = new MarkdownIt({ html: false, linkify: true, typographer: true });
const app = Fastify({ logger: true });

const docsDir = resolve(process.env.DOCS_DIR ?? join(process.cwd(), '../docs/api'));
const publicBaseUrl = (process.env.PUBLIC_BASE_URL ?? 'https://orbit.rebus.industries').replace(/\/+$/, '');

const NAV = [
  { slug: 'index', title: 'Overview' },
  { slug: 'authentication', title: 'Authentication' },
  { slug: 'projects-models-versions', title: 'Projects, models & versions' },
  { slug: 'objects', title: 'Objects (REST)' },
  { slug: 'comments-discussions', title: 'Comments & discussions' },
  { slug: 'graphql-reference', title: 'GraphQL reference' },
  { slug: 'subscriptions-permissions', title: 'Subscriptions & permissions' },
  { slug: 'legacy-api', title: 'Legacy API' },
  { slug: 'limitations', title: 'Limitations' },
];

function slugToFile(slug) {
  const name = slug === 'index' ? 'README.md' : `${slug}.md`;
  return join(docsDir, name);
}

function renderPage(title, bodyHtml, activeSlug) {
  const nav = NAV.map(({ slug, title: t }) => {
    const href = slug === 'index' ? '/docs/' : `/docs/${slug}`;
    const active = slug === activeSlug ? ' class="active"' : '';
    return `<a href="${href}"${active}>${t}</a>`;
  }).join('\n');

  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>${title} — ORBIT API</title>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=JetBrains+Mono:wght@400;500&display=swap" rel="stylesheet" />
  <style>
    :root {
      --bg: #ffffff; --fg: #1a1d23; --fg-muted: #5b6270; --accent: #2563eb;
      --border: #e2e6eb; --code-bg: #f5f7fa; --code-fg: #1a1d23; --nav-bg: #0e1116;
    }
    @media (prefers-color-scheme: dark) {
      :root {
        --bg: #0e1116; --fg: #e4e6eb; --fg-muted: #9ca3af; --border: #2a2f37;
        --code-bg: #161a20; --code-fg: #e4e6eb;
      }
    }
    * { box-sizing: border-box; }
    html, body { margin: 0; padding: 0; background: var(--bg); color: var(--fg); font-family: Inter, system-ui, sans-serif; }
    .layout { display: flex; min-height: 100vh; }
    nav.sidebar {
      width: 260px; flex-shrink: 0; background: var(--nav-bg); color: #fff;
      padding: 24px 0; position: sticky; top: 0; height: 100vh; overflow-y: auto;
    }
    nav.sidebar .brand { padding: 0 20px 20px; font-weight: 700; letter-spacing: 0.04em; font-size: 14px; }
    nav.sidebar .brand-dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: var(--accent); margin-right: 8px; }
    nav.sidebar a {
      display: block; padding: 8px 20px; color: #d4d4d8; text-decoration: none; font-size: 14px;
    }
    nav.sidebar a:hover, nav.sidebar a.active { color: #fff; background: rgba(255,255,255,0.06); }
    main { flex: 1; max-width: 880px; padding: 48px 40px 96px; line-height: 1.6; }
    main h1, main h2, main h3 { font-weight: 700; line-height: 1.25; margin-top: 2em; margin-bottom: 0.6em; }
    main h1 { font-size: 2.1em; margin-top: 0; }
    main h2 { font-size: 1.5em; border-bottom: 1px solid var(--border); padding-bottom: 0.3em; }
    main a { color: var(--accent); }
    main code { font-family: 'JetBrains Mono', ui-monospace, monospace; font-size: 0.9em; background: var(--code-bg); padding: 2px 6px; border-radius: 3px; }
    main pre { background: var(--code-bg); padding: 16px 20px; border-radius: 6px; overflow-x: auto; border: 1px solid var(--border); }
    main pre code { background: transparent; padding: 0; }
    main table { border-collapse: collapse; width: 100%; font-size: 0.92em; margin: 1em 0; }
    main th, main td { border: 1px solid var(--border); padding: 8px 12px; text-align: left; vertical-align: top; }
    main th { background: var(--code-bg); }
    main blockquote { margin: 1em 0; padding: 0.4em 1em; border-left: 4px solid var(--accent); color: var(--fg-muted); background: var(--code-bg); }
    .top-links { padding: 0 20px 16px; font-size: 12px; }
    .top-links a { color: #9ca3af; text-decoration: none; }
    @media (max-width: 768px) {
      .layout { flex-direction: column; }
      nav.sidebar { width: 100%; height: auto; position: static; }
    }
  </style>
</head>
<body>
  <div class="layout">
    <nav class="sidebar">
      <div class="brand"><span class="brand-dot"></span>ORBIT API</div>
      <div class="top-links">
        <a href="${publicBaseUrl}/graphql" target="_blank" rel="noopener">GraphQL endpoint ↗</a>
      </div>
      ${nav}
    </nav>
    <main>${bodyHtml}</main>
  </div>
</body>
</html>`;
}

async function renderSlug(slug) {
  const file = slugToFile(slug);
  const markdown = await readFile(file, 'utf-8');
  const titleMatch = markdown.match(/^#\s+(.+)$/m);
  const title = titleMatch?.[1] ?? slug;
  const body = md.render(markdown);
  return { title, html: renderPage(title, body, slug) };
}

app.get('/health', async () => ({ ok: true }));

for (const path of ['/docs', '/docs/']) {
  app.get(path, async (_req, reply) => {
    const { html } = await renderSlug('index');
    reply.header('cache-control', 'public, max-age=60').type('text/html; charset=utf-8');
    return html;
  });
}

app.get('/docs/:slug', async (req, reply) => {
  const slug = req.params.slug.replace(/\.md$/, '');
  if (slug === 'index') {
    reply.redirect('/docs/');
    return;
  }
  try {
    const { html } = await renderSlug(slug);
    reply.header('cache-control', 'public, max-age=300').type('text/html; charset=utf-8');
    return html;
  } catch {
    reply.code(404);
    return { error: 'page not found' };
  }
});

app.get('/docs/:slug.md', async (req, reply) => {
  const slug = req.params.slug.replace(/\.md$/, '');
  try {
    const markdown = await readFile(slugToFile(slug === 'index' ? 'index' : slug), 'utf-8');
    reply.header('cache-control', 'public, max-age=300').header('access-control-allow-origin', '*');
    reply.type('text/markdown; charset=utf-8');
    return markdown;
  } catch {
    reply.code(404);
    return { error: 'page not found' };
  }
});

const port = Number(process.env.PORT ?? 3080);
const host = process.env.HOST ?? '0.0.0.0';
await app.listen({ port, host });
