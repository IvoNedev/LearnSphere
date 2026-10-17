export type Status = 'Queued' | 'Processing' | 'Succeeded' | 'Failed';
export interface CreateRequest {
  title?: string;
  transcript: string;
  useChunking?: boolean;
}
export interface ListItem {
  id: string;
  title?: string;
  status: Status;
  generationMode: string;
  createdAt: string;
}
export interface Question {
  prompt: string;
  options: string[];
  correctAnswerIndex: number;
  explanation: string;
  sourceExcerpt: string;
  excerptVerified: boolean;
}
export interface Generation extends ListItem {
  startedAt?: string;
  completedAt?: string;
  summary?: { overview: string; keyPoints: string[] };
  questions?: Question[];
  metadata?: {
    provider: string;
    model: string;
    promptVersion: string;
    transcriptChars: number;
    inputTokens: number | null;
    outputTokens: number | null;
    thoughtTokens: number | null;
    durationMs: number;
    repairAttempted: boolean;
  };
  error?: {
    code: string;
    message: string;
    retryable: boolean;
    resumeStage?: string;
    retryAfterSeconds?: number;
  };
}
export interface Capabilities {
  minTranscriptChars: number;
  maxTranscriptChars: number;
  maxTitleChars: number;
  maxRequestBodyBytes: number;
  questionCount: number;
  optionsPerQuestion: number;
  maxExcerptWords: number;
  pollIntervalMs: number;
  maxPollAttempts: number;
  pollRequestTimeoutMs: number;
  chunkingEnabled: boolean;
  chunkTargetChars: number;
  maxChunks: number;
  generationMode: string;
}
