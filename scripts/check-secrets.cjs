const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const { redact } = require('./archive-safety.cjs');
// Scan tracked and untracked nonignored files, without printing matched values.
const files = execFileSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], { encoding: 'utf8' }).split('\0').filter(Boolean);
const hits = [];
for (const file of new Set(files)) {
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) continue;
  if (!/\.(md|json|jsonl|ts|cs|cjs|js|txt|yml|html|ps1|example)$/.test(file)) continue;
  const value = fs.readFileSync(file, 'utf8');
  if (redact(value) !== value) hits.push(path.normalize(file));
}
console.log(`Credential-pattern files: ${hits.length}`);
for (const file of hits) console.error(file);
if (hits.length) process.exitCode = 1;
