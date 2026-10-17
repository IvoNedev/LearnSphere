export type Status = 'Queued' | 'Processing' | 'Succeeded' | 'Failed';
export interface CreateRequest {
  title?: string;
  transcript: string;
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
  error?: { code: string; message: string; retryable: boolean };
}
