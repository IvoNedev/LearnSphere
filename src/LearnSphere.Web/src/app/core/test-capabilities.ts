import { Capabilities } from './models';
// Deliberately nonproduction limits: components must follow the API payload, not embedded defaults.
export const TEST_CAPS: Capabilities = {
  minTranscriptChars: 20,
  maxTranscriptChars: 1000,
  maxTitleChars: 30,
  maxRequestBodyBytes: 4096,
  questionCount: 5,
  optionsPerQuestion: 4,
  maxExcerptWords: 40,
  pollIntervalMs: 1,
  maxPollAttempts: 3,
  pollRequestTimeoutMs: 100,
  chunkingEnabled: true,
  chunkTargetChars: 500,
  maxChunks: 20,
  generationMode: 'mock',
};
