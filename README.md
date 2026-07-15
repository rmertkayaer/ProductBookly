# Bookly — Engineering Roadmap & Product Design

> A multi-tenant appointment scheduling platform, built as a modular monolith.
> This document is the single source of truth for scope, architecture, and milestones.

## Run it

```bash
git clone https://github.com/rmertkayaer/ProductBookly.git && cd ProductBookly
docker compose up -d          # Postgres 16 on host port 5433 (5432 may be taken by a native install)
dotnet run --project src/Bookly.Api
# → http://localhost:<port>/swagger · /health · /health/ready · /api/v1/ping
```

Migrations are applied automatically on startup in Development only. EF commands always name the context and project (two DbContexts from M3 on):

```bash
dotnet ef migrations add <Name> --project src/Modules/Core/Bookly.Core --startup-project src/Bookly.Api --context CoreDbContext --output-dir Persistence/Migrations
```
> **Owner:** you. **Timeline:** ~8–10 weeks at 8 focused hours/day. **Budget:** $0 (free tiers only).

---

## 1. Vision & Goals

### What we're building
A SaaS-style booking system where a business owner (a barber shop, a clinic, a tutor) can define services, staff, and working hours, and their customers can book, cancel, and reschedule appointments online.

### Why this project (career goal)
Demonstrate, with working code, the skills a strong mid-level .NET engineer is expected to have:

| Skill you want to prove | Where it shows up |
|---|---|
| Modular monolith | Two bounded contexts (Core, Scheduling) with enforced boundaries |
| EF Core + PostgreSQL | Schema-per-module, migrations, global query filters |
| Auth & Authorization | ASP.NET Identity, JWT, policy-based RBAC (Owner/Staff/Customer) |
| Vertical slices | Feature folders: one folder = request + handler + validator |
| Swagger / OpenAPI | Versioned, documented API from day one |
| Docker | Full stack via `docker compose up`, containerized deploy |
| Unit + integration tests | xUnit, WebApplicationFactory, Testcontainers (real Postgres in CI) |
| OpenTelemetry | Traces + metrics + structured logs, exported to Grafana Cloud |

### The three rules (from the SWOT analysis — do not break these)
1. **Every milestone ends with something demoable.** Deployable, tested, and visibly better than last week.
2. **No technology enters the project before the problem it solves exists.** RabbitMQ, Redis, SignalR are *expansion content*, not the base game.
3. **Milestones never break previous milestones.** The only planned retrofit is multi-tenancy enforcement (M7), and it is isolated and test-gated on purpose.

### Explicitly out of scope for v1 (deferred, not deleted)
RabbitMQ/MassTransit, Redis, SignalR, Commerce module, Google OAuth, Dapper, gRPC, CosmosDB, Kubernetes/Helm, feature flags, Bicep. See §7 for when each unlocks.

---

## 2. Architecture

### 2.1 Style: Modular Monolith
One deployable ASP.NET Core application. Two modules for v1:

- **Core** — tenants, users, identity, roles, notifications (email).
- **Scheduling** — services, staff, availability, appointments.

**The boundary rule (this is the whole point):**
- Each module owns its own PostgreSQL schema (`core.*`, `scheduling.*`).
- A module NEVER touches another module's tables, entities, or DbContext.
- Cross-module communication happens only through:
  1. **Contract interfaces** the module publishes (e.g. `ICoreQueries.GetTenant(tenantId)`) — synchronous, in-process.
  2. **In-process domain events** via MediatR notifications (e.g. `AppointmentBooked`) — this is the seam where RabbitMQ slots in later without redesign.

### 2.2 Solution structure

```
bookly/
├── docs/
│   ├── adr/                        # Architecture Decision Records
│   └── ENGINEERING-ROADMAP.md      # this file
├── src/
│   ├── Bookly.Api/                 # composition root: DI, middleware,
│   │                               # Swagger, auth setup, module registration
│   ├── Bookly.Shared/              # cross-cutting ONLY: Result<T>, base
│   │                               # Entity, IDomainEvent, ITenantContext
│   ├── Modules/
│   │   ├── Core/
│   │   │   ├── Bookly.Core/                # domain + features
│   │   │   │   ├── Features/
│   │   │   │   │   ├── Auth/
│   │   │   │   │   │   ├── Register/       # Register.cs (endpoint +
│   │   │   │   │   │   │                   # command + handler + validator)
│   │   │   │   │   │   └── Login/
│   │   │   │   │   └── Tenants/
│   │   │   │   ├── Domain/                 # User, Tenant entities
│   │   │   │   └── Persistence/            # CoreDbContext, migrations
│   │   │   └── Bookly.Core.Contracts/      # public interfaces + DTOs + events
│   │   └── Scheduling/
│   │       ├── Bookly.Scheduling/
│   │       │   ├── Features/
│   │       │   │   ├── Services/
│   │       │   │   ├── Staff/
│   │       │   │   ├── Availability/
│   │       │   │   └── Appointments/
│   │       │   ├── Domain/
│   │       │   └── Persistence/            # SchedulingDbContext
│   │       └── Bookly.Scheduling.Contracts/
├── frontend/                        # React (Vite), deliberately ugly until M10
├── tests/
│   ├── Bookly.Core.Tests/                  # unit
│   ├── Bookly.Scheduling.Tests/            # unit (availability engine lives here)
│   └── Bookly.IntegrationTests/            # WebApplicationFactory + Testcontainers
├── docker-compose.yml
├── Dockerfile
└── .github/workflows/ci.yml
```

**Enforcement:** `Bookly.Scheduling` may reference `Bookly.Core.Contracts` but never `Bookly.Core`. Add an architecture test (NetArchTest.Rules) in M2 that fails the build if anyone cheats. This one test is your "the boundaries are real" interview evidence.

### 2.3 Vertical slices (inside a module)
One feature = one folder = one file where practical:

```csharp
// Features/Appointments/BookAppointment/BookAppointment.cs
public static class BookAppointment
{
    public record Command(Guid StaffId, Guid ServiceId, DateTime SlotStartUtc) : IRequest<Result<Guid>>;

    public class Validator : AbstractValidator<Command> { /* FluentValidation */ }

    internal sealed class Handler : IRequestHandler<Command, Result<Guid>>
    {
        // loads domain entities, calls domain logic, saves, raises AppointmentBooked
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/appointments", async (Command cmd, ISender sender) => ...);
}
```

No repositories-for-the-sake-of-repositories, no 5-layer soup. The handler uses the module's DbContext directly. Domain rules (overlap detection, cancellation rules) live on the entities, not in the handler — that's the "DDD-lite" part.

**CQRS-lite:** commands go through domain entities; read endpoints (dashboard, slot listing) are plain EF/LINQ projections to DTOs. No separate read database, no event sourcing.

### 2.4 Multi-tenancy strategy (decided now, enforced in M7)
- Every tenant-owned entity has a `TenantId` column **from its first migration** (additive later = painful; present-but-unused now = free).
- A `ITenantContext` abstraction exists from M2; during M2–M6 it returns a seeded `DemoTenant` id.
- In M7: `ITenantContext` reads the tenant claim from the JWT, and **EF Core global query filters** on both DbContexts enforce `WHERE TenantId = @current` on every query automatically — so "forgetting the WHERE clause" becomes structurally impossible.
- M7 ships with a dedicated leak test: request as Tenant B, assert Tenant A's data cannot appear.

### 2.5 API conventions
- REST, versioned from day one: `/api/v1/...` (Asp.Versioning packages).
- Swagger/OpenAPI (Swashbuckle) from M0, with JWT auth support in the UI from M1.
- Errors: RFC 7807 ProblemDetails everywhere. Handlers return `Result<T>`; a single mapping layer converts failures to 400/404/409.
- All timestamps stored and transmitted in UTC. Timezone conversion is a frontend/display concern. Write this in ADR-004 and never revisit it.

### 2.6 Tech stack (v1)

| Concern | Choice | Introduced |
|---|---|---|
| Runtime | .NET 10 (LTS), C# 14 | M0 |
| API | ASP.NET Core Minimal APIs (+ MediatR, FluentValidation) | M0 |
| Database | PostgreSQL 16, schema-per-module | M0 |
| ORM | EF Core (Npgsql) | M0 |
| API docs | Swagger / OpenAPI + versioning | M0 |
| Auth | ASP.NET Identity + JWT | M1 |
| AuthZ | Policy-based RBAC (Owner/Staff/Customer) | M2 |
| Frontend | React + Vite, unstyled until it works | M1 |
| Containers | Docker + docker-compose | M0 (Postgres) / M8 (full stack) |
| Unit tests | xUnit + NSubstitute | M2 (arch tests), heavy from M4 |
| Integration tests | WebApplicationFactory + Testcontainers | M5 |
| CI | GitHub Actions | M0 (build), M9 (full test matrix) |
| Email | SendGrid free tier, synchronous | M6 |
| Observability | Serilog + OpenTelemetry → Grafana Cloud | M11 |
| Hosting | Azure Container Apps + Neon/Supabase free Postgres | M10 |

---
## 3. Milestones

Each milestone follows the same contract: **Goal → Tasks → Definition of Done → Tests → ADRs → Deferred.**
Estimated pace: 3–7 working days each at 8h/day *while learning properly*. If a milestone finishes early, do NOT pull work forward — polish tests and docs instead.

---

### M0 — Skeleton (nothing for users, everything for you)
**Goal:** `git clone` → `docker compose up -d` (Postgres) → `dotnet run` → green health check. CI builds on every push.

Tasks:
1. Create solution + project structure from §2.2 (empty Scheduling module is fine).
2. docker-compose with Postgres 16 only.
3. `CoreDbContext` with one throwaway entity, first migration, applied on startup (dev only — document that prod will use a migration step).
4. Health checks: `/health` (liveness) + `/health/ready` (DB reachable).
5. Swagger UI at `/swagger`, API versioning wired (`/api/v1/ping`).
6. GitHub Actions: restore, build, `dotnet test` (zero tests is fine — the pipe exists).
7. README with a 5-line "run it" section. ADR folder with template.

**Done when:** a stranger can clone the repo and get a 200 from `/health/ready` in under 5 minutes.
**ADRs:** 001 Modular monolith over microservices · 002 Schema-per-module · 003 Vertical slices over layered architecture.
**Pitfall:** don't gold-plate the skeleton. No BaseController, no generic repository, no "common" library graveyard. `Bookly.Shared` stays under ~5 files.

---

### M1 — Authentication (single role)
**Goal:** a real person registers and logs in through the React UI and calls a protected endpoint.

Tasks:
1. ASP.NET Identity on `CoreDbContext` (Identity tables in `core` schema).
2. `Features/Auth/Register` and `Features/Auth/Login` slices → JWT with sensible lifetime; refresh tokens deferred (write it down, don't build it).
3. JWT bearer auth in the API + "Authorize" button working in Swagger.
4. React: register page, login page, token in memory + localStorage, one protected `/dashboard` placeholder, logout.
5. Seed script/endpoint for a dev user.

**Done when:** you sign up and log in through the actual UI (not Postman), and an unauthenticated request to the protected endpoint gets 401.
**Tests:** handler unit tests (register validation, duplicate email); first integration test can wait for M5.
**ADR:** 004 UTC everywhere · 005 JWT over cookie sessions (and why refresh tokens are deferred).
**Pitfall:** Identity + JWT wiring takes longer than you think (the SWOT called this). Budget 4–5 days, feel good if it's 3.

---

### M2 — Roles & RBAC + the boundary police
**Goal:** Owner, Staff, Customer roles exist; UI visibly changes per role; module boundaries are enforced by a failing test, not by discipline.

Tasks:
1. Seed the three roles; role claim into the JWT; policy-based authorization (`RequireOwner`, `RequireStaffOrOwner`).
2. `ITenantContext` in `Bookly.Shared`, returning seeded `DemoTenant` (per §2.4). Every new entity from here on carries `TenantId`.
3. NetArchTest architecture tests: Scheduling must not reference Core internals; Contracts projects must not reference EF Core.
4. React: nav/menu renders differently for Owner vs Customer.

**Done when:** logging in as Owner vs Customer shows different screens, and adding an illegal cross-module reference breaks the build.
**ADR:** 006 Policy-based RBAC · 007 Tenant context now, enforcement later.

---

### M3 — Business setup (Services & Staff CRUD)
**Goal:** an Owner manages the catalog. First real Scheduling-module code.

Tasks:
1. `SchedulingDbContext`, `scheduling` schema, separate migrations folder.
2. Entities: `Service` (name, duration, price), `StaffMember` (name, active). Both carry `TenantId`.
3. Slices: create/edit/list/deactivate for both. Owner-only policies.
4. React: two plain admin tables with forms. Ugly on purpose.

**Done when:** Owner adds "Haircut, 30 min" and staff member "Ali" through the UI and sees them listed after refresh.
**Pitfall:** two DbContexts + two migration sets needs discipline: `dotnet ef migrations add X --context SchedulingDbContext -o Persistence/Migrations`. Write the exact commands into the README now.

---

### M4 — Availability engine (the hardest logic; give it respect)
**Goal:** given working hours + service duration + existing appointments, compute correct open slots. Pure domain logic, heavily unit tested, **no booking yet**.

Tasks:
1. `WorkingHours` (staff, day-of-week, start, end — support split shifts: Mon 9–12 and 13–17).
2. Slot generation: pure, stateless domain service → `IReadOnlyList<Slot>` for (staff, service, date). No I/O inside; inputs go in, slots come out. This is what makes it trivially testable.
3. Edge-case unit tests (this is the milestone's real deliverable): back-to-back appointments, appointment overlapping shift end, duration longer than any gap, zero working hours, DST boundary dates, split shifts.
4. Read endpoint `GET /api/v1/staff/{id}/slots?serviceId=&date=`.
5. React: date + staff picker rendering real computed slots.

**Done when:** the slot picker shows correct slots and the edge-case test suite passes. Target 20+ meaningful unit tests here.
**ADR:** 008 Slot computation on read vs materialized slots (compute on read; Redis caching is the future optimization — name it, don't build it).

---

### M5 — Booking (the big one)
**Goal:** a Customer books a slot, end-to-end, and double-booking is impossible — proven by a test, not by hope.

Tasks:
1. `Appointment` entity with domain invariants (must fit within availability, must not overlap same staff).
2. `BookAppointment` slice: validate → check conflicts → create → save.
3. **Concurrency safety (the interview story):** two simultaneous requests for the last slot → exactly one succeeds. Use a PostgreSQL unique/exclusion constraint or serializable transaction + retry; the DB is the final referee, not C# checks.
4. Raise `AppointmentBooked` as an in-process MediatR notification (no consumer needs to exist yet — the seam is the point).
5. Integration testing starts here: WebApplicationFactory + Testcontainers (real Postgres). The concurrency test fires two parallel requests and asserts one 201 + one 409.
6. React: click slot → confirm → confirmation screen; slot disappears from picker.

**Done when:** browse → pick → book works end-to-end locally, and the parallel double-booking integration test passes reliably.
**ADR:** 009 Concurrency strategy for booking (constraint vs lock vs optimistic — document what you chose and what you rejected).
**Pitfall:** the classic mistake is checking availability in C# and inserting without a DB-level guard. Race conditions don't care about your if-statement.

---

### M6 — Cancel, reschedule, confirmation email
**Goal:** the app feels like a product.

Tasks:
1. Cancel (frees the slot — verify in M4's picker) and reschedule (atomic cancel+book) slices, with domain rules (e.g. no cancelling past appointments) on the entity.
2. "My appointments" page for Customers; today/upcoming list for Owner.
3. Synchronous confirmation email via SendGrid free tier on booking, behind an `IEmailSender` interface. **Do not** add a queue. When email latency annoys you, that pain is M12's justification.

**Done when:** cancelling an appointment makes its slot reappear in the picker, and booking sends a real email to your inbox.
**ADR:** 010 Synchronous email now, async later (record the tradeoff explicitly).

---

### M7 — Multi-tenancy enforcement (the only retrofit — isolated and test-gated)
**Goal:** real tenant isolation. A business signs up and gets its own walled garden.

Tasks:
1. Tenant signup slice: creates Tenant + Owner user; `tenant_id` claim in JWT.
2. `ITenantContext` now reads the JWT claim (DemoTenant remains only for seeding/tests).
3. EF Core global query filters on every tenant-owned entity in both contexts; SaveChanges interceptor stamps `TenantId` on inserts.
4. **The leak test:** seed Tenant A and Tenant B with data; authenticated as B, hit every list/read endpoint; assert zero A records appear. Parameterize it over endpoints so new endpoints are covered automatically.

**Done when:** the leak test passes, and two tenants you created manually in the UI can't see each other's staff, services, or appointments.
**ADR:** 011 Row-level multi-tenancy via global query filters (vs schema-per-tenant / db-per-tenant).
**Pitfall:** `IgnoreQueryFilters()` is now a loaded gun — grep for it in code review; ideally add an arch test banning it outside admin/migration code.

---

### M8 — Dockerize the whole stack
**Goal:** `docker compose up` → API + frontend + Postgres from nothing.

Tasks:
1. Multi-stage Dockerfile for the API (sdk → aspnet runtime image); Dockerfile or static build for React.
2. Compose wires services: connection strings via env vars, `localhost` becomes service names (the classic gotcha — expect one confusing evening), healthcheck-based `depends_on`.
3. Config strategy: appsettings → env vars → user-secrets locally. No secrets in the repo, ever.

**Done when:** on a machine with only Docker installed, `docker compose up` gives you a bookable app in the browser.

---

### M9 — CI that proves it works
**Goal:** every push runs the full test suite, including integration tests against real Postgres.

Tasks:
1. GitHub Actions: build → unit tests → integration tests (Testcontainers works on GitHub-hosted runners) → docker image build.
2. Fail the build on test failure. Optionally: coverage report, PR checks required before merge to main.
3. Push images to GitHub Container Registry on main.

**Done when:** a PR shows green because tests ran in CI, and a deliberately broken test blocks the merge.

---

### M10 — Deploy live 🚀
**Goal:** book a real appointment from your phone on a public URL; the confirmation email lands.

Tasks:
1. Managed Postgres: Neon or Supabase free tier (cheaper/simpler free tier than Azure's managed Postgres).
2. Azure Container Apps: API container (free grant: 180K vCPU-s / 360K GiB-s / 2M requests/month). Frontend: Azure Static Web Apps free tier (simpler than containerizing React for prod).
3. Secrets via Container Apps secrets/env vars (Key Vault is a later polish item — note it, skip it).
4. GitHub Actions deploy job on main: build → push image → update container app.
5. Migration strategy for prod: run migrations as a startup gate or a dedicated job step — decide and write ADR-012.

**Done when:** the phone test passes. Then **STOP. Celebrate. Record a 3-minute demo video. Polish the README.** This is already a strong portfolio project.
**Pitfall:** the SWOT's warning applies here hardest — Azure config (ingress, env vars, CORS between SWA and the API) can eat days. It's one-time pain; don't let it demoralize you.

---

### M11 — Observability (Serilog + OpenTelemetry → Grafana Cloud)
**Goal:** when something is slow or broken in prod, you can see why.

Tasks:
1. Serilog structured logging (request logs, tenant id + user id enrichment — never log tokens/PII).
2. OpenTelemetry: ASP.NET Core + HttpClient + Npgsql/EF instrumentation; OTLP export to Grafana Cloud free tier.
3. One custom metric (`bookings_created_total`) and one custom trace span around slot computation.
4. A tiny Grafana dashboard: request rate, p95 latency, error rate, bookings/day.

**Done when:** you book an appointment in prod and can find its full trace (HTTP → handler → SQL) in Grafana.
**ADR:** 013 Vendor-neutral OTEL over cloud-locked APM.

---

## 4. Expansion content (unlock only when the pain is real)

| Unlock | Technology | The pain that justifies it |
|---|---|---|
| M12 | RabbitMQ (CloudAMQP) + MassTransit + worker service | Booking requests are slow because email is synchronous; reminders need scheduled background work. `AppointmentBooked` already exists as an event — you're swapping the transport, not redesigning. |
| M13 | Redis (Upstash) + k6 | k6 load test shows slot computation hammering Postgres. Cache slot lookups; invalidate on booking/cancel — and now you get to tell a true cache-invalidation war story. |
| M14 | SignalR | Two people staring at the same booking page see stale slots. Live slot updates + admin notifications. |
| M15 | Commerce module | The payoff for the boundary discipline: a third bounded context reusing Core (tenants/auth/email) without touching Scheduling. |
| M16 | Resume extras | Google OAuth (real OIDC flow), Dapper (only if a reporting query genuinely defeats EF), one deliberate internal gRPC call, Helm charts + minikube (local only, no paid cluster), OWASP Top 10 hardening pass. |

---

## 5. Definition of Done (every milestone, no exceptions)
- [ ] Compiles; all tests green locally **and** in CI (from M9)
- [ ] New behavior covered by tests (unit at minimum; integration from M5)
- [ ] `docker compose up` still works (from M8: full stack)
- [ ] Deployed (from M10)
- [ ] README updated if setup/run changed
- [ ] Relevant ADR written (20 minutes, one page)
- [ ] A 30-second "what's new" note or clip — your Friday demo ritual

## 6. Working agreements (anti-burnout rules)
1. **Friday is demo day.** No new code — deploy, record, write down what you learned. This is your dopamine mechanism, protect it.
2. **Ugly frontend until M10.** Unstyled forms and default buttons. CSS is the "two weeks choosing a logging library" trap in disguise.
3. **Timebox research to 1 day per decision.** Write the ADR, move on. The ADR can be revised; the lost week cannot.
4. **When stuck 2+ hours:** commit WIP to a branch, write the problem down in one paragraph, take a walk, then ask (or ask Claude) with that paragraph.
5. **Scope creep goes to `docs/LATER.md`,** not into the sprint. Every "wouldn't it be cool if" is one line in that file.

## 7. ADR index (write these as you go)
| # | Title | Milestone |
|---|---|---|
| 001 | Modular monolith over microservices | M0 |
| 002 | Schema-per-module in one PostgreSQL instance | M0 |
| 003 | Vertical slices over layered architecture | M0 |
| 004 | UTC everywhere; timezone is a display concern | M1 |
| 005 | JWT bearer auth; refresh tokens deferred | M1 |
| 006 | Policy-based RBAC | M2 |
| 007 | TenantId columns now, enforcement in M7 | M2 |
| 008 | Compute slots on read (no materialized slot table) | M4 |
| 009 | Booking concurrency: DB as the final referee | M5 |
| 010 | Synchronous email now, async when it hurts | M6 |
| 011 | Row-level multi-tenancy via global query filters | M7 |
| 012 | Production migration strategy | M10 |
| 013 | Vendor-neutral OpenTelemetry over cloud APM | M11 |

**ADR template** (`docs/adr/template.md`):
```markdown
# ADR-NNN: Title
Date: YYYY-MM-DD · Status: Accepted

## Context
What problem forced a decision, in 2–4 sentences.

## Decision
What we chose.

## Alternatives considered
What we rejected and the one-line reason each.

## Consequences
What gets easier, what gets harder, what we might revisit.
```

## 8. Risk register (from the SWOT — keep these visible)
| Risk | Mitigation baked into this plan |
|---|---|
| Burnout / losing momentum | 11 small demoable wins instead of 1 giant one; Friday demo ritual |
| Architecture-first, features-never | M0 is capped; arch tests replace endless structure debates |
| Forgetting a TenantId filter | Global query filters + parameterized leak test (M7) |
| Double-booking race condition | DB-level constraint + explicit parallel integration test (M5) |
| Azure deployment eating a week | Isolated in M10, after everything already works locally in Docker |
| Two-DbContext migration confusion | Exact EF commands documented in README at M3 |
| Analysis paralysis | 1-day research timebox + ADR closes every open question |
