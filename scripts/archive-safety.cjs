// Publication copies omit secrets and internal reasoning. Original platform files are untouched.
const credentialPatterns = [ /AQ\.[A-Za-z0-9_-]{30,}/g, /AIza[A-Za-z0-9_-]{30,}/g, /sk-(?:proj-)?[A-Za-z0-9_-]{30,}/g ];
function redact(text) {
  return credentialPatterns.reduce((value, pattern) => value.replace(pattern, '[REDACTED_CREDENTIAL]'), text);
}
function internal(record) {
  return record?.type === 'reasoning' || record?.type === 'thinking' || record?.type === 'redacted_thinking' ||
    (record?.type === 'response_item' && internal(record.payload)) ||
    (record?.type === 'message' && record.role === 'assistant' && record.channel === 'analysis') ||
    (record?.type === 'event_msg' && /reasoning/.test(record.payload?.type || ''));
}
function clean(value) {
  if (!value || typeof value !== 'object') return value;
  if (Array.isArray(value)) return value.filter(x => !internal(x)).map(clean);
  return Object.fromEntries(Object.entries(value).filter(([key]) => key !== 'encrypted_content').map(([key, item]) => [key, clean(item)]));
}
function sanitizeJsonl(text) {
  return text.split('\n').filter(Boolean).flatMap(line => {
    try { const value = JSON.parse(redact(line)); return internal(value) ? [] : [JSON.stringify(clean(value))]; }
    catch { return []; } // In-progress partial records are picked up on the next sync.
  }).join('\n') + '\n';
}
module.exports = { redact, sanitizeJsonl };
