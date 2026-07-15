# ADR-002: Schema-per-module in one PostgreSQL instance
Date: 2026-07-15 · Status: Accepted

## Context
Each module owns its data and no module may query another module's tables. We need that ownership to be visible and enforceable at the database level without paying for multiple database instances.

## Decision
One PostgreSQL database; each module gets its own schema (`core.*`, `scheduling.*`) with its own DbContext and its own migration history table (`__ef_migrations_history` inside the module's schema).

## Alternatives considered
- **One shared schema**: rejected — table ownership becomes convention only, and cross-module joins become one lazy query away.
- **Database-per-module**: rejected — cross-database consistency and free-tier cost for no benefit at this scale.

## Consequences
- Easier: `\dt core.*` shows exactly what Core owns; migrations are independent per module; a future extraction to a separate database is mechanical.
- Harder: two DbContexts means every `dotnet ef` command must name `--context` and `--project` explicitly (commands documented in README).
