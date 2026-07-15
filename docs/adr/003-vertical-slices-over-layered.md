# ADR-003: Vertical slices over layered architecture
Date: 2026-07-15 · Status: Accepted

## Context
Classic layered architecture (Controllers/Services/Repositories) spreads one feature across many folders and encourages abstraction-for-its-own-sake. This project optimizes for feature velocity and readable diffs: one feature should be one reviewable unit.

## Decision
Inside each module, one feature = one folder (endpoint + command/query + handler + validator together, one file where practical). Handlers use the module's DbContext directly. Domain rules live on entities ("DDD-lite"), not in handlers or services.

## Alternatives considered
- **N-layer with repositories/services**: rejected — indirection without benefit; EF Core's DbContext already is a repository + unit of work.
- **Full Clean Architecture**: rejected — the interface/implementation ceremony costs more than it returns at this project size.

## Consequences
- Easier: features are added, reviewed, and deleted as self-contained folders; new-contributor navigation is trivial.
- Harder: shared logic must be extracted deliberately (into Domain or Shared) instead of having a default "Services" dumping ground; slices must not import other slices.
