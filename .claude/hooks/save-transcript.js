#!/usr/bin/env node
// Archives available project conversations into Context/ as sanitized publication copies.
// Credentials and internal reasoning are removed; originals remain in the platform session stores.
//
//   Context/Claude/raw/*.jsonl        verbatim copies of Claude Code session transcripts (+ subagent dirs)
//   Context/Claude/YYYY-MM-DD-<id>.md full readable rendering: every message, tool call input and tool result
//   Context/Codex/raw/*.jsonl         verbatim copies of Codex sessions whose cwd is this project
//   Context/Codex/full/YYYY-MM-DD-<id>.md  full readable rendering of those Codex sessions
//   Context/README.md                 index of everything above
//
// The raw .jsonl files are the unedited source of truth; the .md files are lossless-in-content views
// (internal bookkeeping records such as API request metadata are only in the raw files).
//
// Hook usage: Stop/SessionEnd hook, hook JSON on stdin. Manual usage: node save-transcript.js
// Both modes sync every session; unchanged sessions are skipped.

const fs = require('fs');
const os = require('os');
const path = require('path');
const { redact, sanitizeJsonl } = require('../../scripts/archive-safety.cjs');

function writeArchive(file, text) {
  fs.writeFileSync(file, file.endsWith('.jsonl') ? sanitizeJsonl(text) : redact(text));
}

const projectDir = path.resolve(__dirname, '..', '..');
const contextDir = path.join(projectDir, 'Context');
const claudeOut = path.join(contextDir, 'Claude');
const codexOut = path.join(contextDir, 'Codex');

// ---------- helpers ----------

function readJsonl(file) {
  const out = [];
  for (const line of sanitizeJsonl(fs.readFileSync(file, 'utf8')).split('\n')) {
    if (!line.trim()) continue;
    try { out.push(JSON.parse(line)); } catch { /* partial line while being written */ }
  }
  return out;
}

function fence(text, lang = '') {
  const s = String(text ?? '');
  const longest = Math.max(2, ...(s.match(/`+/g) || []).map((m) => m.length));
  const f = '`'.repeat(longest + 1);
  return `${f}${lang}\n${s}\n${f}`;
}

function details(summary, body) {
  return `<details>\n<summary>${summary}</summary>\n\n${body}\n\n</details>`;
}

function utc(ts) {
  if (!ts) return '';
  const d = typeof ts === 'number' ? new Date(ts < 1e12 ? ts * 1000 : ts) : new Date(ts);
  return isNaN(d) ? '' : d.toISOString().replace('T', ' ').slice(0, 19) + ' UTC';
}

function isUpToDate(src, dest) {
  // Always refresh publication copies so updated redaction rules also cover older sessions.
  return false;
}

function copyRecursive(src, dest) {
  const st = fs.statSync(src);
  if (st.isDirectory()) {
    fs.mkdirSync(dest, { recursive: true });
    for (const name of fs.readdirSync(src)) copyRecursive(path.join(src, name), path.join(dest, name));
  } else if (!isUpToDate(src, dest)) {
    fs.mkdirSync(path.dirname(dest), { recursive: true });
    if (src.endsWith('.jsonl')) writeArchive(dest, fs.readFileSync(src, 'utf8'));
    else fs.copyFileSync(src, dest);
  }
}

function walk(dir, pred, acc = []) {
  if (!fs.existsSync(dir)) return acc;
  for (const name of fs.readdirSync(dir)) {
    const p = path.join(dir, name);
    const st = fs.statSync(p);
    if (st.isDirectory()) walk(p, pred, acc);
    else if (pred(p)) acc.push(p);
  }
  return acc;
}

const sameDir = (a, b) => path.resolve(a).toLowerCase() === path.resolve(b).toLowerCase();

// ---------- Claude Code ----------

function claudeProjectDirs(hintTranscript) {
  const dirs = new Set();
  if (hintTranscript && fs.existsSync(hintTranscript)) dirs.add(path.dirname(hintTranscript));
  const root = path.join(os.homedir(), '.claude', 'projects');
  const sanitized = projectDir.replace(/[^A-Za-z0-9]/g, '-').toLowerCase();
  if (fs.existsSync(root)) {
    for (const name of fs.readdirSync(root)) {
      if (name.toLowerCase() === sanitized) dirs.add(path.join(root, name));
    }
  }
  return [...dirs];
}

function renderClaudeBlock(block) {
  switch (block.type) {
    case 'text':
      return block.text;
    case 'thinking':
      return block.thinking ? details('Thinking', block.thinking) : '';
    case 'redacted_thinking':
      return '_(redacted thinking block — present in raw transcript)_';
    case 'tool_use':
      return details(`Tool call: <code>${block.name}</code>`, fence(JSON.stringify(block.input, null, 2), 'json'));
    case 'tool_result': {
      const c = block.content;
      const text = typeof c === 'string' ? c
        : Array.isArray(c) ? c.map((x) => (x.type === 'text' ? x.text : `[${x.type}]`)).join('\n') : JSON.stringify(c);
      return details(`Tool result${block.is_error ? ' (error)' : ''}`, fence(text));
    }
    case 'image':
      return '_[image]_';
    default:
      return details(`Block: ${block.type}`, fence(JSON.stringify(block, null, 2), 'json'));
  }
}

function renderClaude(file) {
  const entries = readJsonl(file);
  const msgs = entries.filter((e) => e.type === 'user' || e.type === 'assistant' || e.type === 'system' || e.type === 'attachment');
  const first = msgs.find((e) => e.type === 'user' && e.message);
  if (!first) return null;

  const sessionId = first.sessionId || path.basename(file, '.jsonl');
  const date = (first.timestamp || new Date().toISOString()).slice(0, 10);
  const aiTitle = [...entries].reverse().find((e) => e.type === 'ai-title');
  let title = '';

  const body = [];
  for (const e of msgs) {
    const when = utc(e.timestamp);
    const side = e.isSidechain ? ' · subagent' : '';
    if (e.type === 'attachment') {
      body.push(details(`Attachment${when ? ` · ${when}` : ''}`, fence(JSON.stringify(e.attachment, null, 2), 'json')));
      continue;
    }
    if (e.type === 'system') {
      body.push(`### System${side}${when ? ` · ${when}` : ''}\n\n${fence(e.content ?? JSON.stringify(e, null, 2))}`);
      continue;
    }
    const content = e.message && e.message.content;
    const blocks = typeof content === 'string' ? [{ type: 'text', text: content }] : Array.isArray(content) ? content : [];
    const onlyResults = blocks.length && blocks.every((b) => b.type === 'tool_result');
    let heading;
    if (e.type === 'assistant') heading = `### Claude${side}`;
    else if (onlyResults) heading = `### Tool output${side}`;
    else if (e.isMeta) heading = `### Injected context${side}`;
    else {
      heading = `## User${side}`;
      if (!title && !e.isSidechain) {
        const t = blocks.filter((b) => b.type === 'text').map((b) => b.text).join(' ')
          .replace(/<[^>]+>[\s\S]*?<\/[^>]+>/g, '').trim();
        title = t.split('\n')[0].slice(0, 80);
      }
    }
    const rendered = blocks.map(renderClaudeBlock).filter(Boolean).join('\n\n');
    if (rendered) body.push(`${heading}${when ? ` · ${when}` : ''}\n\n${rendered}`);
  }

  title = (aiTitle && (aiTitle.title || aiTitle.aiTitle)) || title || sessionId;
  const out = `${date}-${sessionId.slice(0, 8)}.md`;
  const md =
    `# ${title}\n\n` +
    `- Tool: Claude Code\n- Date: ${date}\n- Session: \`${sessionId}\`\n` +
    `- Sanitized transcript: [raw/${path.basename(file)}](raw/${path.basename(file)})\n\n` +
    `Rendering of available conversation records. Credentials and internal reasoning are omitted; see AI-LOGS.md. ` +
    `Collapsed sections hold tool inputs/outputs.\n\n---\n\n` +
    body.join('\n\n') + '\n';
  fs.mkdirSync(claudeOut, { recursive: true });
  writeArchive(path.join(claudeOut, out), md);
  return { file: out, title, date, tool: 'Claude Code', dir: 'Claude' };
}

function syncClaude(hintTranscript) {
  const rawDir = path.join(claudeOut, 'raw');
  for (const dir of claudeProjectDirs(hintTranscript)) {
    for (const name of fs.readdirSync(dir)) {
      if (!name.endsWith('.jsonl')) continue;
      const src = path.join(dir, name);
      const dest = path.join(rawDir, name);
      const rendered = fs.existsSync(claudeOut) &&
        fs.readdirSync(claudeOut).some((f) => f.endsWith(`-${name.slice(0, 8)}.md`));
      const subDir = path.join(dir, name.replace(/\.jsonl$/, ''));
      if (fs.existsSync(subDir)) copyRecursive(subDir, path.join(rawDir, path.basename(subDir)));
      if (isUpToDate(src, dest) && rendered) continue;
      copyRecursive(src, dest);
      renderClaude(src);
    }
  }
}

// ---------- Codex ----------

function codexSessionFiles() {
  const roots = [path.join(os.homedir(), '.codex', 'sessions'), path.join(os.homedir(), '.codex', 'archived_sessions')];
  const files = [];
  for (const r of roots) {
    for (const f of walk(r, (p) => p.endsWith('.jsonl'))) {
      const fd = fs.openSync(f, 'r');
      const buf = Buffer.alloc(16384);
      const n = fs.readSync(fd, buf, 0, buf.length, 0);
      fs.closeSync(fd);
      const m = buf.toString('utf8', 0, n).match(/"cwd":"((?:[^"\\]|\\.)*)"/);
      if (m && sameDir(JSON.parse(`"${m[1]}"`), projectDir)) files.push(f);
    }
  }
  return files;
}

function codexText(content) {
  if (typeof content === 'string') return content;
  if (!Array.isArray(content)) return JSON.stringify(content, null, 2);
  return content.map((c) => c.text ?? (c.type ? `[${c.type}]` : JSON.stringify(c))).join('\n');
}

function renderCodex(file) {
  const entries = readJsonl(file);
  const meta = entries.find((e) => e.type === 'session_meta');
  const id = (meta && (meta.payload.id || meta.payload.session_id)) || path.basename(file, '.jsonl');
  const date = ((meta && meta.timestamp) || new Date().toISOString()).slice(0, 10);
  let title = '';
  const body = [];

  if (meta) {
    const { base_instructions, ...rest } = meta.payload;
    body.push(details('Session metadata', fence(JSON.stringify(rest, null, 2), 'json')));
    if (base_instructions) {
      const bi = typeof base_instructions === 'string' ? base_instructions : base_instructions.text ?? JSON.stringify(base_instructions, null, 2);
      body.push(details('Base instructions', fence(bi)));
    }
  }

  for (const e of entries) {
    if (e.type === 'event_msg' && e.payload.type === 'user_message' && !title) {
      const m = String(e.payload.message || '');
      const req = m.split('## My request:')[1] || m;
      title = req.trim().split('\n')[0].slice(0, 80);
    }
    if (e.type !== 'response_item') continue;
    const p = e.payload;
    const when = utc(e.timestamp);
    const ts = when ? ` · ${when}` : '';
    switch (p.type) {
      case 'message': {
        const text = codexText(p.content);
        if (p.role === 'user') body.push(`## User${ts}\n\n${text}`);
        else if (p.role === 'assistant') body.push(`### Codex${p.phase ? ` (${p.phase})` : ''}${ts}\n\n${text}`);
        else body.push(details(`${p.role} message${ts}`, fence(text)));
        break;
      }
      case 'reasoning': {
        const summary = (p.summary || []).map((s) => s.text).filter(Boolean).join('\n\n');
        body.push(summary ? details(`Reasoning summary${ts}`, summary)
          : '_(reasoning — encrypted by the provider; stored verbatim in the raw transcript)_');
        break;
      }
      case 'custom_tool_call':
      case 'function_call':
        body.push(details(`Tool call: <code>${p.name}</code>${ts}`, fence(p.input ?? p.arguments ?? '')));
        break;
      case 'custom_tool_call_output':
      case 'function_call_output': {
        const o = p.output;
        const text = typeof o === 'string' ? o : Array.isArray(o) ? codexText(o) : JSON.stringify(o, null, 2);
        body.push(details(`Tool result${ts}`, fence(text)));
        break;
      }
      default:
        body.push(details(`${p.type}${ts}`, fence(JSON.stringify(p, null, 2), 'json')));
    }
  }

  title = title || id;
  const out = `${date}-${String(id).slice(0, 8)}.md`;
  const rawName = path.basename(file);
  const md =
    `# ${title}\n\n` +
    `- Tool: OpenAI Codex\n- Date: ${date}\n- Session: \`${id}\`\n` +
    `- Sanitized transcript: [../raw/${rawName}](../raw/${rawName})\n\n` +
    `Rendering of available conversation records. Credentials and internal reasoning are omitted; see AI-LOGS.md.\n\n---\n\n` +
    body.join('\n\n') + '\n';
  const fullDir = path.join(codexOut, 'full');
  fs.mkdirSync(fullDir, { recursive: true });
  writeArchive(path.join(fullDir, out), md);
}

function syncCodex() {
  const rawDir = path.join(codexOut, 'raw');
  for (const src of codexSessionFiles()) {
    const dest = path.join(rawDir, path.basename(src));
    if (isUpToDate(src, dest)) continue;
    copyRecursive(src, dest);
    renderCodex(src);
  }
}

// ---------- index ----------

function firstHeading(file) {
  return fs.readFileSync(file, 'utf8').split('\n')[0].replace(/^#\s*/, '');
}

function writeIndexes() {
  const list = (dir, rel) => (fs.existsSync(dir) ? fs.readdirSync(dir) : [])
    .filter((f) => /^\d{4}-\d{2}-\d{2}-.*\.md$/.test(f)).sort()
    .map((f) => `- [${f.slice(0, 10)} — ${firstHeading(path.join(dir, f))}](${rel}${f})`);

  const claude = list(claudeOut, '');
  if (fs.existsSync(claudeOut)) {
    fs.writeFileSync(path.join(claudeOut, 'README.md'),
      '# Claude Code conversation archive\n\n' +
      'Available Claude Code sessions for this project, sanitized for credentials and internal reasoning (see AI-LOGS.md), saved by the ' +
      'Stop/SessionEnd hooks in `.claude/settings.json` (`.claude/hooks/save-transcript.js`).\n\n' +
      '- `raw/*.jsonl` — sanitized transcript copies\n' +
      '- `*.md` — full readable rendering of each session\n\n## Sessions\n\n' + claude.join('\n') + '\n');
  }

  const codex = list(path.join(codexOut, 'full'), 'Codex/full/');
  const index =
    '# AI-assistant conversation logs\n\n' +
    'Every AI-assistant session used on this project, archived automatically by `.claude/hooks/save-transcript.js` ' +
    '(runs after every Claude Code turn; run `node .claude/hooks/save-transcript.js` manually to sync at any time).\n\n' +
    'The `raw/` folders contain publication copies with credentials redacted and internal reasoning omitted. ' +
    'The Markdown files render those available records. These are not unedited exports; see AI-LOGS.md for exceptions and coverage.\n\n' +
    '## Claude Code\n\n' + (list(claudeOut, 'Claude/').join('\n') || '_none_') + '\n\n' +
    '## OpenAI Codex\n\n' + (codex.join('\n') || '_none_') + '\n\n' +
    'Codex also keeps hand-written session notes in `Codex/` per `AGENTS.md`.\n';
  fs.mkdirSync(contextDir, { recursive: true });
  fs.writeFileSync(path.join(contextDir, 'README.md'), index);
}

// ---------- main ----------

function main(hintTranscript) {
  try { syncClaude(hintTranscript); } catch (err) { console.error('claude sync failed:', err.message); }
  try { syncCodex(); } catch (err) { console.error('codex sync failed:', err.message); }
  writeIndexes();
}

if (process.stdin.isTTY) {
  main(process.argv[2]);
} else {
  let input = '';
  process.stdin.on('data', (d) => (input += d));
  process.stdin.on('end', () => {
    let data = {};
    try { data = JSON.parse(input || '{}'); } catch { /* manual run with piped junk */ }
    main(data.transcript_path || process.argv[2]);
  });
}
