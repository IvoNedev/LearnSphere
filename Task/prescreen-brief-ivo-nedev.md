# AI-Powered Training Content Generator for an EdTech Platform

_For: Ivo Nedev_

_Language: C# • Due in 7 days • Suggested time: ~8h_

## Overview
LearnSphere is a rapidly growing EdTech company providing online video courses for corporate training. Our clients' Learning & Development (L&D) managers are tasked with ensuring their employees retain key information from these courses. Currently, they manually watch hours of video, transcribe the content, and then spend even more time writing summaries and multiple-choice quizzes. This manual process is a significant bottleneck, preventing them from scaling their training programs effectively. To solve this, LearnSphere wants to prototype a tool that automates the creation of this supplementary learning material.

Your task is to build a proof-of-concept web application that allows an L&D manager to submit a long text transcript and receive an AI-generated summary and a multiple-choice quiz. You will build a backend service in C# that orchestrates the interaction with a Large Language Model (LLM) and a simple frontend UI to provide the user interface. The backend should expose a clean API that the frontend will consume to submit the transcript and display the generated content.

We are looking for a well-structured, testable, and thoughtfully designed solution. The emphasis is on the backend architecture, API design, and the quality of the interaction with the AI service. The frontend should be functional and demonstrate the end-to-end workflow, but it does not need to be visually polished. Your README file is a critical part of the deliverable, as it gives us insight into your design process, trade-offs, and operational thinking.

## Deliverables
- A public GitHub repository or GitHub Gist URL containing your complete solution.
- A backend service written in C#/.NET that exposes a RESTful API for transcript processing.
- A simple web-based frontend UI (e.g., using React, or plain HTML/CSS/JS) that allows a user to submit a transcript and view the generated summary and quiz.
- A comprehensive README.md file that includes: setup and running instructions, an explanation of your design choices and trade-offs, and a brief note on which AI assistants were used and how.
- The complete, unedited conversation log(s) from any AI assistant (e.g., ChatGPT, GitHub Copilot Chat) used during the project, committed to the repository.

## Suggested tools / libraries
- Backend: C# 12 / .NET 8, ASP.NET Core Web API
- AI Service: OpenAI API, Azure OpenAI, or another LLM provider. You may use a free tier or mock the API if you don't have an account.
- Data Storage: EF Core with an in-memory provider or SQLite for simplicity.
- Frontend: React + Vite, or plain HTML/CSS/JavaScript using the `fetch` API.
- Containerization: Docker

## On AI assistants & follow-up
- We encourage you to use AI assistants like GitHub Copilot or ChatGPT. They are standard tools in modern software development, and we want to see how you leverage them.
- Please commit the full, unedited conversation log from your AI assistant(s) to your repository. In your README, briefly describe which tools you used and for what parts of the project.
- We value your design thinking and ability to articulate trade-offs. The explanation in your README is as important as the code itself.
- Be prepared to walk us through any part of your solution, explain the code's purpose, and discuss the design decisions you made during the follow-up technical interview.

## How to submit
Reply to this email with **one** public GitHub repository or gist URL containing your solution.

<!-- AI-EVALUATOR:PRESCREEN-META {"language":"C#","dueDays":7,"timeBudgetHours":8} -->