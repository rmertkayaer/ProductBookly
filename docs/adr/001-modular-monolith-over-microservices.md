# ADR-001: Modular monolith over microservices
Date: 2026-07-15 · Status: Accepted

## Context
Bookly needs clear architectural boundaries (it's a portfolio piece proving design skill), but it is built and operated by one person on free-tier infrastructure. Microservices would multiply deployment, networking, and observability cost for zero user-facing benefit at this scale.

## Decision
One deployable ASP.NET Core application composed of isolated modules (Core, Scheduling). Modules communicate only through published contract interfaces and in-process domain events — the same seams microservices would use, without the distributed-systems tax.

## Alternatives considered
- **Microservices**: rejected — operational overhead (N pipelines, N deployments, service discovery, network failure modes) with a team of one.
- **Plain layered monolith**: rejected — no enforced boundaries means the codebase degrades into a big ball of mud; there is nothing to demonstrate.

## Consequences
- Easier: single deployment, single debugging surface, refactoring across module boundaries when contracts change.
- Harder: discipline is required to keep modules isolated — mitigated by architecture tests (NetArchTest, arriving M2) that fail the build on violations.
- Revisit: if a module ever needs independent scaling, the contract + event seams are where it splits off.
