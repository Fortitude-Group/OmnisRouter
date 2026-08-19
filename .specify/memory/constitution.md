<!--
SYNC IMPACT REPORT
==================
Version change: 1.4.0 -> 1.5.0
Rationale:
  - 1.5.0: Added Principle XI (Establish the Mechanism Before Changing Code — diagnose
    from the system rather than from a model of it; the operative test is cost, so anything
    settleable by one query, one grep or one file read must be settled that way before being
    acted on or reported; extends to design, to reading the whole of what you open, to
    tracing data rather than intent, and to distinguishing verified from assumed) and
    Principle XII (Explain Every Number — every figure shown must answer what it is, why it
    matters and what follows; it is a deletion test as much as an addition test, charts must
    earn their place against a list, and a filtered count must say what it excludes). MINOR —
    two new principles added. Both were first written in a project fork and are general rather
    than product-specific, so they are promoted here. Amended at the owner's direct instruction
    2026-08-03.

    NUMBERING NOTE: project forks may number their own principles differently from this base.
    The OSPulse fork reaches XIII because it carries four agent-discipline principles this base
    does not (model-tier delegation, query-vs-instruction, run-don't-ask, read-the-log). Match
    forks by TITLE, never by numeral — "Production Changes Wait for a Human" is X here and XI
    there, and the same principle.
  - 1.4.0: Added Principle X (Production Changes Wait for a Human — production changes require
    explicit per-change owner approval, shown in full and unfiltered, with no self-approving
    flags; irreversible production resources additionally carry a hard in-code guard; dev/test/
    ephemeral environments are explicitly exempt and stay fully automatable). MINOR — a new
    principle added. Amended at the owner's direct instruction 2026-08-02, prompted by an
    unattended `terraform apply -auto-approve` that ran with a gating variable defaulted, whose
    plan reported "18 to destroy" through a filter that showed only the summary line, and which
    permanently destroyed a production database (Azure PostgreSQL Flexible Server deletes its
    backups with the server — there is no restore path).
  - 1.3.0: Added a "Specification Workflow (Brainstorm → Spec)" section — brainstorming/design
    documentation lives in the `docs/` folder and is written as SOURCE input for `/speckit-specify`,
    which produces the tracked `specs/<NNN>-<slug>/spec.md`. MINOR — a new section added. Amended at
    the owner's direct instruction 2026-07-14.
  - 1.2.0: Added Principle IX (Ask, Then Wait — a decision the owner gated by a question waits
    for the owner's answer; a timeout / "no response yet" / harness "you may proceed" prompt is
    NOT consent to choose for them) and an "Owner Working Preferences" section (on-device
    validation is iPhone-first; no pull requests). MINOR — a new principle + section added.
    Amended at the owner's direct instruction 2026-07-02, prompted by a design-option question
    that was wrongly resolved by timeout instead of by the owner.
  - 1.1.0: Promoted the "start from a fresh base / pull before you begin" rule out of
    Principle VII into its own Principle VIII with concrete git steps; quality gate #7
    now cites Principle VIII. Additive / materially-expanding.
  - 1.0.0: Portable, project-agnostic constitution derived from the Chevin.PDF constitution
    (v1.4.0). All product-, platform-, and tracker-specific language removed so the
    same principles apply to backend libraries/services AND frontend applications
    (e.g. React/TypeScript). The "Boil the Ocean" standard is recorded explicitly as
    the Prime Directive and referenced from Principle VI.

Principles:
  - Prime Directive: Boil the Ocean (the standard)
  - I.    Modular & Composable
  - II.   Contract Stability & Semantic Versioning
  - III.  Comprehensive Tests for Public Contracts
  - IV.   Deterministic & Observable Behaviour
  - V.    Simplicity & Justified Complexity
  - VI.   Complete the Scope (no deferred known work)
  - VII.  The Tracker Is the Project of Record (Always in Sync)
  - VIII. Start From a Fresh Base (Pull Before You Begin) [1.1.0]
  - IX.   Ask, Then Wait (Decisions Belong to the Owner) [1.2.0]
  - X.    Production Changes Wait for a Human (Ephemeral Environments Do Not) [1.4.0]
  - XI.   Establish the Mechanism Before Changing Code [1.5.0]
  - XII.  Explain Every Number [1.5.0]

Sections:
  - Technology & Delivery Constraints
  - Development Workflow & Quality Gates
  - Specification Workflow (Brainstorm → Spec) [1.3.0]
  - Owner Working Preferences [1.2.0]
  - Governance

Deferred TODOs: none
-->

# Project Constitution

## Prime Directive — Boil the Ocean

> The marginal cost of completeness is near zero with AI. Do the whole thing. Do it
> right. Do it with tests. Do it with documentation. Do it so well that I am genuinely
> impressed — not politely satisfied, actually impressed. Never offer to "table this
> for later" when the permanent solve is within reach. Never leave a dangling thread
> when tying it off takes five more minutes. Never present a workaround when the real
> fix exists. The standard isn't "good enough" — it's "holy shit, that's done." Search
> before building. Test before shipping. Ship the complete thing. When I ask for
> something, the answer is the finished product, not a plan to build it, unless I
> specifically ask for a spec or a plan. Time is not an excuse. Fatigue is not an
> excuse. Complexity is not an excuse. **Boil the ocean.**

This is the standard every principle below serves. Where a principle gives a specific
rule, this directive gives the intent behind it: deliver the finished, complete,
verified thing. Principle VI is its operational enforcement.

## Core Principles

### I. Modular & Composable

The system MUST be built from self-contained, independently testable units — libraries,
modules, packages, or components — each with a single, clearly stated purpose. Higher
layers (a service, an app shell, a page) are thin consumers of those units, never the
other way around. Organizational-only groupings (modules that exist merely to bundle
unrelated code) are PROHIBITED. Any logic reused in more than one place MUST live in a
shared unit behind an explicit, documented interface, so consumers share one
implementation rather than silently forking behaviour.

**Rationale**: Reuse only stays maintainable when boundaries are real. Whether it is a
package consumed by a service or a component consumed by many screens, forcing shared
logic behind one interface prevents consumers from drifting apart.

### II. Contract Stability & Semantic Versioning

Every published contract — a library's public API, a service's HTTP/RPC surface, a
component's props/events, a shared type, or a documented output guarantee — MUST follow
Semantic Versioning (MAJOR.MINOR.PATCH). A breaking change to any public symbol,
signature, route, prop contract, or documented guarantee REQUIRES a MAJOR bump and a
migration note. Consumers MUST pin an explicit version of anything they depend on; they
MUST NOT float to "latest". Breaking changes MUST be called out in the PR description and
changelog.

**Rationale**: A change to a shared contract can break a consumer at a distance.
Versioned, pinned contracts make that blast radius explicit and reviewable rather than
discovered in production (or in a downstream app at runtime).

### III. Comprehensive Tests for Public Contracts

Every public API, exported component, and guaranteed aspect of output or behaviour
(content, structure, determinism, accessibility where applicable) MUST have comprehensive
automated tests, **including edge cases** (error paths, boundary values, malformed or
missing input, loading/empty/error states). The public surface and its guarantees MAY NOT
merge without that coverage. Bug fixes MUST add a regression test reproducing the bug.
**Test-first (red-green) ordering is encouraged but NOT mandatory** — what matters is
thorough coverage at merge time, not the order in which tests and implementation were
written. Internal/private helpers MAY be covered at the author's discretion.

**Rationale**: The public contract is what other code and other people depend on; it is
exactly the surface where a regression is most expensive. Coverage — breadth and edge
cases — is the goal; mandating failing-first ceremony adds process without adding safety.

### IV. Deterministic & Observable Behaviour

Given identical inputs, state, and configuration, the system MUST produce stable,
predictable output — the same data and props yield the same result. Nondeterminism
(embedded timestamps, random IDs, time- or locale-dependent rendering, non-stable
ordering) MUST be opt-in, isolated, and documented so output can be snapshot- or
golden-tested. Every meaningful operation MUST emit structured logs/telemetry and surface
actionable errors; a consumer MUST be able to trace a request or user action to the
operation that produced (or failed to produce) the result.

**Rationale**: Without determinism, output cannot be trusted or regression-tested; without
observability, a failure is a black box. Both are prerequisites for trusting behaviour in
production — whether the artifact is a generated document, an API response, or a rendered
UI.

### V. Simplicity & Justified Complexity

Start with the simplest design that satisfies the requirement; apply YAGNI. Added
abstraction (new layers, patterns, dependencies, packages, state stores, or build steps)
MUST be justified against a concrete present need, with the simpler rejected alternative
recorded. Unjustified complexity is a merge blocker.

**Rationale**: Real systems accrete edge cases on their own; gratuitous architectural
complexity on top compounds into something no one can change safely. Complexity must earn
its place.

### VI. Complete the Scope (no deferred known work)

Known, in-scope work MUST be completed within the same change that surfaces it — not tabled
"for later". When a gap, inconsistency, or missing piece is identified inside the agreed
scope (a requirement with thin test coverage, an unmapped task, a follow-on edit implied by
a change just made, a missing loading/error state, an un-wired prop), it MUST be addressed
in that change. "Easy to add later" is not a valid reason to defer in-scope work. Only work
that is genuinely *out* of the current scope — a separately-tracked future feature, a
dependency owned by another team, or work explicitly deprioritised by the owner — may be
scheduled rather than done now, and when it is deferred the reason and its tracking location
MUST be recorded.

**Rationale**: This is the Prime Directive made enforceable. Deferred "small" items silently
accumulate into the gaps that break a release and erode trust in the spec/plan/tasks
artifacts. Closing known work in-change keeps every artifact an honest, complete picture and
prevents a backlog of invisible debt that no quality gate can see. **Boil the ocean** —
finish the thing.

### VII. The Tracker Is the Project of Record (Always in Sync)

The project tracker (issue board / project management tool — e.g. Azure DevOps, Jira, GitHub
Issues/Projects, Linear) MUST, at all times, reflect the local project-management state.
Whenever local task/feature/PM artifacts change — task lists, the spec/plan, scope lists, or
any backlog/planning data in the repo — the corresponding tracker items (Epic → Feature →
Story → Task or the tool's equivalent), their **states**, and the active **sprint/iteration**
MUST be updated in the *same* change. Specifically:

- A task completed locally MUST be **closed** on its ticket; a new local task MUST be
  **created** on the board under the correct parent; re-scoped or removed work MUST be
  reflected (state change / re-parent / removed).
- Project-management data — sprints, iteration assignment, and completion **dates/times** —
  MUST be kept current. When reconstructing history, dates are derived from the git commit
  timeline, not invented.
- The board, backlog, and sprint are the live source of truth; a board that disagrees with
  the local task list is a defect to fix, not tolerate.

**Traceability MUST be bidirectional via git commit hashes.** Every commit message references
the work item(s) it advances (using the tracker's linking convention, e.g. `#123` or
`AB#123`, so the tool auto-links the commit), and every work item records the delivering
commit hash(es) in its description/discussion and via completion dates taken from those
commits. No work is "done" until both the board state and the commit↔work-item linkage
reflect it.

Starting work also requires an up-to-date base — see **Principle VIII**.

**Rationale**: A project is judged by its tracked, auditable delivery as much as its code. If
the tracker drifts from local tasks/spec, planning, reporting, and the merge gate all operate
on a lie. Tying every work item to the commit that delivered it makes status, history, and
"who changed what, when" verifiable rather than asserted — and keeps the board, the repo, and
the sprint a single coherent record.

### VIII. Start From a Fresh Base (Pull Before You Begin)

Before beginning ANY new task you MUST update your local repository from the remote and base
the work on the current tip of the shared integration branch — never on a stale local clone.
Starting from a stale base is exactly what produces the avoidable merge conflicts and
"works-on-my-machine" regressions this rule exists to prevent. Concretely, at the start of
each task:

1. **Fetch and fast-forward the integration branch** (the branch work merges back into — e.g.
   `main`/`master`/`develop`): `git fetch origin` then `git pull --ff-only` while on that
   branch. If the pull cannot fast-forward, reconcile (rebase/merge) before continuing — do
   not start new work on a diverged base.
2. **Branch from that fresh tip**: create the task/feature branch from the just-updated
   integration branch (`git switch -c <branch> origin/<integration>`), so the new branch's
   base is current HEAD.
3. **When resuming an existing branch**, first re-sync it onto the latest integration tip
   (`git fetch` then rebase or merge the integration branch in) before adding new commits, so
   long-lived branches do not drift.
4. **Verify before you commit work onto it**: confirm the base is current (e.g.
   `git status` shows up to date with the remote, or `git log` shows the remote's latest
   integration commit as an ancestor) rather than assuming.

A clean local working tree is a precondition: stash or commit in-progress changes before
pulling so the fast-forward is not blocked. This applies whether work is solo or shared —
on a shared branch, turns are taken on top of one another, so a stale base silently
reintroduces problems teammates already fixed.

**Rationale**: The cheapest merge conflict is the one that never happens. Pulling first costs
seconds; discovering mid-review that a branch was cut from a week-old base costs a rebase, a
re-test, and often a reintroduced bug. Making "fresh base" an explicit, verified step keeps
every branch a small, current delta against shared HEAD instead of a divergent fork.

### IX. Ask, Then Wait (Decisions Belong to the Owner)

When you pose a question to the owner that gates a decision — a design or UX choice, a scope
call, an ambiguous requirement, any point where the owner's answer changes what you build or
ship — you MUST wait for the owner's answer before acting on that decision. A timeout, a "no
response yet", or a harness prompt suggesting you may proceed on your own judgement is NOT
consent: silence is not an answer. You MAY continue clearly-independent work that does not
depend on the pending answer, but the gated decision itself waits. Do not resolve the question
by guessing and moving on; if it is genuinely blocking and the owner is unavailable, stop and
say you are blocked rather than manufacturing a choice and presenting it as settled.

**Rationale**: The purpose of asking is to get the owner's answer; proceeding without it defeats
the question and usually costs more — the owner must now notice, undo, and redirect — than
waiting ever would. Choosing on the owner's behalf and calling it done erodes trust. When a
decision is genuinely the owner's, the correct behaviour under uncertainty is to wait, or to
surface the block — never to substitute your own pick.

### X. Production Changes Wait for a Human (Ephemeral Environments Do Not)

Any change to a **production** environment MUST be approved by the owner, in advance, for that
specific change. This covers every mechanism that can alter production: infrastructure apply,
deployment, schema migration, configuration or secret change, feature-flag flip, and any direct
data mutation.

Approval has a shape, and all four parts are mandatory:

1. **Show the change first.** Produce the plan, diff, or migration list and present it *in full*.
   Never filter, grep, tail or summarise the output the approval is based on — the destructive
   line is exactly the line a filter drops.
2. **Wait for an explicit yes.** Per Principle IX, silence is not consent. Approval is
   per-change: a yes to one apply is not a yes to the next one, however similar.
3. **No self-approving flags.** `-auto-approve`, `--yes`, `--force`, `--no-verify` and every other
   confirmation-skipping switch are FORBIDDEN against production, including inside scripts.
4. **Use the project's own deployment path.** Where a repository ships a deploy/release script,
   that script is the supported route; hand-rolling the underlying tool bypasses the variables,
   ordering and safety checks the script exists to encode.

**Irreversible production resources MUST additionally carry a hard guard in code** — a
`prevent_destroy` lifecycle block, deletion lock, or equivalent — so that a wrong invocation fails
to produce a destructive plan at all, rather than producing one that a human is expected to catch.
Removing such a guard is its own reviewed change, never a flag on a command line. A resource whose
loss cannot be undone (a database whose backups die with it, a store holding data absent from the
repository) MUST be identified as such and guarded before it holds anything real.

**Non-production environments are deliberately exempt.** Dev, test, preview and ephemeral
environments exist to be created, broken and destroyed without ceremony; automate them freely,
including auto-approved applies and scripted teardown. The distinction is not formality — it is
whether an error is recoverable by re-running a script or is permanent.

**Rationale**: production is the only environment where a mistake cannot be undone by rebuilding
it. Every other principle here optimises for moving fast; this one exists because the cost
function is different when the blast radius includes data that exists nowhere else. The failure
mode is never a considered decision to destroy something — it is an unattended tool acting on an
incomplete instruction, with the warning present in output nobody read. Requiring a human to see
the change and say yes is the cheapest possible check against that, and the only one that holds
when the automation itself is what is wrong.

### XI. Establish the Mechanism Before Changing Code

Before editing code to fix a defect, the mechanism MUST be established **from the
system itself** — the data, the log, the failing query, the code path — and never
inferred from a model of how the system probably works.

**The test is cost.** If a claim can be settled by one query, one grep, or opening
one file, it MUST be settled that way before it is acted on or reported. A wrong
diagnosis is almost always one command away from the right one, and that command
is cheaper than the wrong fix which follows it.

**This applies to DESIGN, not only to debugging.** Before building a filter,
transform or query, work out what it produces against the real data. Some designs
are refutable by arithmetic before a line is written — "show the dependency graph
filtered to direct dependencies and everything they reach" is the whole graph,
because every package is reachable from the direct set.

**Read the whole of what you open.** A column's semantics, a variable's default,
an off-by-one — these live in the source, not in memory. Editing a file without
reading the part that defines the value being edited is how the same bug ships
twice.

**Trace the data, not the intent.** A value threaded to one consumer is not
threaded to all of them. When a change is meant to affect a particular view,
follow the value to that view before claiming it does.

**Separate what was verified from what was assumed.** "I checked X and it shows
Y" and "I believe Y" are different claims and MUST NOT be reported in the same
voice. The owner acts on both, so a hypothesis presented as fact costs them the
work of discovering it was not.

**Rationale**: the failure this prevents is not carelessness — it is confident
reasoning over an incomplete model, which produces fluent, plausible, wrong
answers faster than checking would have produced right ones. The owner then pays
twice: once for the wrong fix, and again for the time spent discovering it was
wrong. Speed is worth nothing when the answer is unfounded, and the verification
that would have grounded it is usually seconds of work.

### XII. Explain Every Number

Any figure, chart or badge shown to a user MUST be able to answer three questions
on the surface itself: **what is this**, **why does it matter**, and **what
follows from it**. The third is what separates a product from a dashboard, and is
where most surfaces are weakest.

**This is a DELETION test as much as an addition test.** If no plain sentence can
be written explaining why a figure would change the reader's decision, the figure
does not belong on the surface. Cutting it is the correct outcome, not labelling
it. What makes a screen unreadable is rarely missing labels — it is quantities
displayed because they happened to have been collected.

**Charts must earn their place against a list.** Three or four labelled figures do
not need one: a chart spends a column of height encoding what the number already
states, degrades to two enormous bars and two invisible ones whenever values
differ by an order of magnitude, and leaves nowhere to put an explanation. Use a
chart where it shows what a table cannot — a trend, or a relationship.

**A filtered or derived count must say what it excludes.** Silently hiding most of
a set is a lie of omission however sensible the default.

**Rationale**: this is a correctness check, not presentation polish. Forcing the
explanation has repeatedly surfaced arithmetic faults that a full test suite
passed over — a number nobody can explain is frequently a number that is wrong,
and making someone write the sentence is how that comes to light.

## Technology & Delivery Constraints

- **State the target, don't assume it**: the language/runtime versions, target framework or
  browser support matrix, and build toolchain MUST be declared in project/config files, not
  implied. Each release line targets an explicit, supported set.
- **One source of truth per artifact**: each shipped artifact (package, service, app bundle)
  builds from this repository; shared logic lives in one place (Principle I) and is consumed,
  not copied.
- **Dependency discipline**: third-party dependencies MUST be pinned to explicit versions and
  reviewed for licence compatibility and maintenance health before adoption. Prefer the
  platform/standard library over a new dependency when the gap is small.
- **Intentional public surface**: what is exported (public API, routes, exported components,
  published types) MUST be deliberate and documented; accidental exposure of internal symbols
  is a defect.

## Development Workflow & Quality Gates

These gates are STRICT and BLOCK merge:

1. **Constitution check**: every plan MUST pass a Constitution Check before design and again
   after design. A violation requires an entry in the plan's Complexity Tracking table, or the
   work does not proceed.
2. **Tests green**: the full test suite MUST pass. Public-contract and determinism tests
   (Principle III/IV) MUST exist and pass.
3. **Lint & build clean**: lint/format and a production build MUST pass with no new warnings
   the project treats as errors.
4. **Versioning**: any public-contract change MUST carry the correct SemVer bump and
   changelog/migration note (Principle II).
5. **Review**: at least one reviewer MUST verify principle compliance. Reviewers MAY NOT waive
   a principle; an exception requires recording the justification in the PR and the Complexity
   Tracking table.
6. **Tracker sync & commit-hash traceability** (Principle VII): local task/feature/PM changes
   MUST be mirrored to the tracker board/sprint in the same change, and commits MUST be linked
   to their work items by hash (commit → work item; work item → delivering commit hash). A
   board that disagrees with the local task list, or completed work with no commit↔work-item
   link, BLOCKS merge.
7. **Fresh base** (Principle VIII): a task MUST be started from a freshly pulled, up-to-date
   integration branch, and a resumed branch MUST be re-synced onto the latest integration tip
   before new commits. Work committed on a stale base that causes avoidable merge conflicts is
   a process defect.

## Specification Workflow (Brainstorm → Spec)

Exploratory design and the governed specification are DISTINCT artifacts with distinct homes:

- **Brainstorming/design documentation lives in the `docs/` folder** (the brainstorming default is
  `docs/superpowers/specs/YYYY-MM-DD-<topic>-design.md`). It captures the exploration, the options
  weighed, and the decisions locked with the owner.
- **These design docs are SOURCE documents for `/speckit-specify`, not the specification itself.**
  Write them so they can be fed directly into `/speckit-specify`: an authoritative, self-contained
  feature description — goals, the locked decisions (with the chosen option), explicit non-goals /
  out-of-scope, and any constraints — phrased as INPUT to spec generation rather than as the final
  spec.
- **The tracked specification is produced by `/speckit-specify`** at `specs/<NNN>-<slug>/spec.md`
  and is the source of truth the `/speckit-*` Constitution-Check gates evaluate. The originating
  design doc is retained and referenced from the spec's Input line; it never replaces the Spec Kit
  spec.

**Rationale**: Keeping exploratory design in `docs/` and the governed spec under `specs/` keeps the
source of truth unambiguous while preserving the brainstorm that produced it. Formatting the design
as a `/speckit-specify` source doc makes the hand-off from ideation to specification lossless and
repeatable.

## Owner Working Preferences

Operational defaults specific to this owner's environment. These are not universal engineering
principles — they live in the constitution so they persist and apply across every project the
owner runs, but they describe *how this owner works*, not a claim about software in general.

- **On-device validation is iPhone-first.** When a change needs checking on a physical device,
  build and deploy to the **iPhone first**; only after reporting that result do you build/deploy
  to Android. (The concrete per-platform deploy commands live in the global `CLAUDE.md` "MAUI
  apps → physical phones" section.)
- **No pull requests.** The owner does not use PRs. Commit and push directly to the working
  branch (merge feature branches into `master` and push); never open or offer a PR. (Also
  recorded in the global `CLAUDE.md` behavioural rules.)

## Governance

This constitution supersedes other development practices for any project that adopts it. Where
a convention and this document conflict, this document wins.

**Amendments**: Changes MUST be proposed via PR, state the rationale, and receive maintainer
approval. On merge, the version is bumped per the policy below, the `Last Amended` date is
updated, and a Sync Impact Report is recorded at the top of this file. Dependent templates or
docs flagged in that report MUST be reconciled in the same or an immediately following PR.

**Versioning policy** (of this constitution):
- **MAJOR**: backward-incompatible governance change — a principle removed or redefined in a
  way that invalidates prior compliance.
- **MINOR**: a new principle or section added, or existing guidance materially expanded.
- **PATCH**: clarifications, wording, and non-semantic refinements.

**Compliance review**: Principle adherence is verified at every PR via the quality gates above.
Repeated or systemic violations MUST be raised with maintainers and addressed before further
feature work proceeds.

**Version**: 1.5.0 | **Ratified**: 2026-06-17 | **Last Amended**: 2026-08-03
