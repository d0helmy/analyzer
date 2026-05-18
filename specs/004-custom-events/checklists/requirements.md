# Specification Quality Checklist: Custom Events

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-05-18
**Feature**: [spec.md](../spec.md)

## Content Quality

- [X] No implementation details (languages, frameworks, APIs)
- [X] Focused on user value and business needs
- [X] Written for non-technical stakeholders
- [X] All mandatory sections completed

## Requirement Completeness

- [X] No [NEEDS CLARIFICATION] markers remain
- [X] Requirements are testable and unambiguous
- [X] Success criteria are measurable
- [X] Success criteria are technology-agnostic (no implementation details)
- [X] All acceptance scenarios are defined
- [X] Edge cases are identified
- [X] Scope is clearly bounded
- [X] Dependencies and assumptions identified

## Feature Readiness

- [X] All functional requirements have clear acceptance criteria
- [X] User scenarios cover primary flows
- [X] Feature meets measurable outcomes defined in Success Criteria
- [X] No implementation details leak into specification

## Notes

### Content Quality

- The spec names contract-level types from the dependency graph (`PageviewCaptured`, `IAnonymizationCascadeStep`, slice-002's `IAnalyticsEventStateProvider` + `AnalyticsEventReceipt`, slice-003's `IAnalyzerSessionResolver` + `AnalyticsSession`) and Analyzer's own new contract surface (`AnalyticsCustomEvent`). These are not implementation details in the spec sense — they are public contract names the feature must satisfy, equivalent to slice 002 / slice 003 naming `IAnalyticsEventStateProvider` by interface name. Concrete HTTP routes, controller class names, validation-attribute names, and EF/NPoco specifics are deferred to plan.md.

### Requirement Completeness

- One spec-level decision deferred to plan.md research (consistent with the no-stopping directive): the resolver-touch semantic on custom-event POSTs — `lastActivityUtc` may or may not advance without incrementing `pageviewCount`. Documented in Assumptions as a plan-time decision; not blocking on clarification.
- Two load-bearing design decisions resolved as Assumptions per the no-stopping directive:
  1. **Cascade-step semantic** — chose hard-delete (matches slice-002 receipt cascade) rather than soft-anonymise (slice-003 session pattern). Rationale: custom events are per-row engagement signals with no aggregate-load-bearing role.
  2. **Write path is synchronous** (NOT queued) — the page-script POST's HTTP 202 is the contract that the row persisted; asynchronous enqueue would violate that contract.

### Feature Readiness

- All nine Success Criteria pair to one or more acceptance scenarios across US1–US3: SC-001 ↔ US1 AS1/AS5; SC-002 ↔ US1 AS3; SC-003 covers the cross-cutting throughput envelope; SC-004 ↔ US2 AS1; SC-005 ↔ US3 AS1; SC-006 ↔ US3 AS2; SC-007 ↔ US3 AS4; SC-008 ↔ pinning baseline; SC-009 names the test corpus.
- Each FR is observable through at least one acceptance scenario, an edge case, or an SC measurement. FR-008's audit-log emission is exercised by US3 AS4 + SC-007.
