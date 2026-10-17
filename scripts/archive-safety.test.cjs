const { test } = require('node:test');
const assert = require('node:assert/strict');
const { redact, sanitizeJsonl } = require('./archive-safety.cjs');
test('credentials are removed even when embedded in a tool result', () => {
  const sample = 'AQ.' + 'x'.repeat(40);
  assert.equal(redact(`value=${sample}`), 'value=[REDACTED_CREDENTIAL]');
});
test('publication JSONL omits internal reasoning and preserves user messages', () => {
  const input = [{type:'response_item',payload:{type:'reasoning',encrypted_content:'private'}}, {type:'response_item',payload:{type:'message',role:'assistant',channel:'analysis',content:'private'}}, {type:'response_item',payload:{type:'message',role:'user',content:'hello'}}].map(JSON.stringify).join('\n');
  const output = sanitizeJsonl(input); assert.ok(!output.includes('private')); assert.ok(output.includes('hello'));
});
