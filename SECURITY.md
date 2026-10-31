# Security

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub's "Report a vulnerability" (security advisories) on this repository. Do not open a public issue. You will get a reply within a week.

## What Fulcrum does with your data

- Fulcrum sends no telemetry and has no phone-home.
- State content (text and images) goes only to the provider you configure.
- API keys are read from `JevOptions.ApiKey` / `OpenAIOptions.ApiKey` or the `JEV_API_KEY` / `OPENAI_API_KEY` environment variables, sent only as the `Authorization` header to the configured endpoint, and never logged.
- Provider error messages include up to the first 500 characters of the response body.
