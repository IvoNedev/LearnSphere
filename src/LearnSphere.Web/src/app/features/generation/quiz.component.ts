import { Component, input, signal } from '@angular/core';
import { Question } from '../../core/models';
@Component({ selector: 'app-quiz', templateUrl: './quiz.component.html' })
export class QuizComponent {
  readonly questions = input.required<Question[]>();
  readonly answers = signal<Record<number, number>>({});
  readonly checked = signal(false);
  readonly showKey = signal(false);
  choose(question: number, option: number) {
    this.answers.update((a) => ({ ...a, [question]: option }));
    this.checked.set(false);
  }
  score() {
    return this.questions().filter((q, i) => this.answers()[i] === q.correctAnswerIndex).length;
  }
}
