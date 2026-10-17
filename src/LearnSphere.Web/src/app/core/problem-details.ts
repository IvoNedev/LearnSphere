import { HttpErrorResponse } from '@angular/common/http';
export function problemDetails(error: unknown): { message: string; fields: Record<string, string[]> } {
  if (error instanceof HttpErrorResponse) {
    return { message: error.error?.title || (error.status === 0 ? 'Cannot reach the server. Check that the API is running.' : 'The request failed. Please try again.'), fields: error.error?.errors || {} };
  }
  return { message: 'Something went wrong. Please try again.', fields: {} };
}
