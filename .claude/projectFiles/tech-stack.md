# Tech Stack

## Backend
- ASP.NET Core 8, Clean Architecture
- EF Core 8 (Npgsql), MediatR (CQRS), FluentValidation, JWT Bearer
- Polly v8 (retry + circuit breaker), Hangfire (opsiyonel, `Hangfire:Enabled=false`)
- Serilog (console logging)
- xUnit + Moq + FluentAssertions (438 test)

## Frontend
- React 18, TypeScript
- shadcn/ui + Tailwind CSS (dark mode)
- Zustand (auth state), React Query / TanStack Query (server state)
- Axios (API client, JWT interceptor)
- Vite (build tool)

## Altyapı
- Neon PostgreSQL (pooled bağlantı, `timestamp with time zone`)
- Railway (container deploy, health check `/health`)
- GitHub Actions (CI/CD — test → build → security scan → deploy)
- Docker (Dockerfile, port 8080)
