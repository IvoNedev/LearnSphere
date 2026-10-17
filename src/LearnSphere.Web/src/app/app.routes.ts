import { Routes } from '@angular/router';
export const routes: Routes = [
 {path:'', loadComponent: () => import('./features/generate/generate.page').then(m => m.GeneratePage)},
 {path:'generations/:id', loadComponent: () => import('./features/generation/generation.page').then(m => m.GenerationPage)},
 {path:'**', redirectTo:''}
];
