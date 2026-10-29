# Troubleshooting

[README](../README.md) · [All documentation](index.md)

Start with the failed tab or command. Keep the exact error, batch ID and collection path.

| Symptom | Check / next action |
|---|---|
| `git pull --ff-only` does not contain a feature | Check `git branch --show-current`; a feature must be on your tracking branch or merged into it |
| Pull refuses local changes | Inspect `git status --short` and `git diff`; preserve or commit your work before changing branches |
| Port 5000 returns only a status message | For development, open the Vite UI at port 8080; for production, verify the bundled `public/index.html` exists |
| No templates listed | Check the server's effective AppRoot and its configured template folder, not the repository directory |
| Wrong data root or SQLite cannot open | Check `appsettings.json`, environment overrides, relative database separators and service-account permissions |
| New batch seed looks unexpected | Next seed comes from SQLite; JSON supplies only the initial seed when the database is empty |
| Generate reports collisions | Inspect the named input/run paths and batch metadata. Do not delete data blindly to reuse an ID |
| Run preflight reports existing CSV/log files | The target already contains execution artifacts. Inspect the batch before any resubmission |
| Some node submissions succeeded before `sbatch` failed | Check Slurm job IDs/queue first; the app's batch status may not reflect those already accepted jobs |
| Collect reports missing/empty CSV | Inspect the raw run folder and log. The same discovered scorer family is required in each retained run |
| Collect reports a log issue | Every retained run needs a log containing the successful TOPAS timing footer; a CSV alone is insufficient |
| Collect set is unbalanced | Exclude a complete node or complete phase-space set; retain the same node set per phase space |
| Unexpected `default` scorer | Old manual suffix-less CSV copies are also discovered; distinguish deliberate copies from originals |
| Scorer names collide | Distinct suffixes resolving to the same output folder are rejected, including case-only differences |
| Uncertainty cannot be calculated | At least two valid retained raw-batch CSVs per scorer are needed; their rows/coordinates must align |
| Analysis cannot derive geometry | Read its reason; check retained manifests and `inputs/{batch}/{runId}.txt`. Unsupported expressions/rotations and inconsistent binning require an explicit mapping |
| Historical/default output has no automatic mapping | Its suffix or manifest may not identify the original scorer. Inspect the original input, then save the explicit mapping |
| Curve disappears after normalization | Inspect its inline error; the selected normalization point may be outside the range or have non-positive dose |
| Metric is unavailable | Check sampled depth range, field edges, CAX and evaluation region; no extrapolation is performed |
| Analysis refuses a volume | Use a 1D scorer. Automatic 3D slicing is not implemented |
| Browser still shows the old UI | Refresh with Ctrl+F5; confirm deployment rebuilt and copied the browser bundle |
| PowerShell blocks the deploy script | Use the per-invocation command in [deployment](how-to-deploy.md#windows-powershell) |
| Deploy rejects WorkingDirectory | Confirm the service points to the intended app directory before changing either target |
| Deployment fails after service stop | Read the rollback message and service journal; a backup and possibly a failed replacement are retained |

## Server checks from Windows PowerShell

```powershell
ssh fysiker@172.31.245.206 "systemctl status topas-run-orchestration.service --no-pager"
ssh fysiker@172.31.245.206 "journalctl -u topas-run-orchestration.service -n 100 --no-pager"
ssh fysiker@172.31.245.206 "systemctl show topas-run-orchestration.service -p WorkingDirectory -p ExecStart --no-pager"
```

For another installation, substitute its SSH target and service name. These commands inspect service state; they do not submit TOPAS jobs or modify simulation data.

## A useful problem report

Include the app revision (`git rev-parse --short HEAD`), failed tab/command, exact error, batch/scorer/collection identifiers, and relevant log excerpts. Describe exclusions, normalization and mapping overrides if they affect the result. Avoid pasting credentials or unrelated clinical data.
