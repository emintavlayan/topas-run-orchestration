# Documentation

[README](../README.md)

## Follow a task

| Task | Guide |
|---|---|
| Complete your first simulation batch | [Daily workflow](daily-workflow.md) |
| Start the app locally | [Local setup](onboarding.md) |
| Connect the app to your folders and cluster | [Configuration](configuration.md) |
| Read and compare collected results | [Analysis guide](analysis-guide.md) |
| Find a CSV, understand its columns or recover a batch identity | [Data reference](data-reference.md) |
| Update an existing server | [Deployment](how-to-deploy.md) |
| Install the service for the first time | [systemd setup](systemd-deployment.md) |
| Resolve a failure | [Troubleshooting](troubleshooting.md) |
| Develop and test changes | [Developer guide](development.md) |

## Inspect exact behavior

These specifications describe implementation details; the task guides above explain how to use them.

| Reference | Contents |
|---|---|
| [Architecture](app-behaviour-spec.md) | Responsibilities, persistence and runtime boundaries |
| [Generate](generate-behaviour-spec.md) | Seeds, template expansion and collision checks |
| [Run](run-behaviour-spec.md) | Node manifests, Slurm scripts and submission behavior |
| [Collect](collect-behaviour-spec.md) | Discovery, exclusions, merge algorithms and uncertainty equations |
| [Analyse](analyse-behaviour-spec.md) | Input parsing, coordinate inference and metric definitions |
| [Interface flows](wizard-ux-flow-spec.md) | Wizard steps and analysis workspace behavior |
| [Test coverage](test-coverage.md) | Automated checks and their limits |
| [F# doctrine](safe-light-fsharp-doctrine.md) | Small pure functions, Result errors and responsibility boundaries |

## Source of truth

The repository contains the application and its deployment helpers. TOPAS templates and CAD assets are configured external inputs, maintained separately. Changing a current template does not rewrite previously generated inputs. A guide's example batch ID or server path is not a required naming convention for a new installation.
