# Trackstorm Agent Instructions

## Required routing

Read [workflow](docs/workflow.md) for assignment scope, Jira authority and approved requirement changes, branch selection, verification, independent engineering review and delivery. Read the relevant sections of [engineering standards](docs/standards.md) before changing code, and [critique](docs/critique.md) before the runtime/experiential completion review. These documents own their rules; this file routes to them.

For feature work, inspect the [feature index](docs/features/README.md), then read only documents relevant to the assigned system and its affected integrations. Follow nested `AGENTS.md` routing in the directories being changed.

## Specialized guidance

- EOS identity, lobbies, P2P, deployment or network verification: use the feature index's networking routes and [EOS development setup](docs/eos-development.md).
- Dependencies, assets or redistribution: start at the canonical [third-party registry](THIRD_PARTY.md), which links detailed notices and manifests.
- Earlier test results or delivery evidence: see the [historical verification index](docs/verification/README.md); current completion checks are defined by workflow.
