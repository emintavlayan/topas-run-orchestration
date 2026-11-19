# Run Control

Open **Run Control** after submitting a batch in **Run**. Select the batch to follow each node. The page refreshes every 10 seconds while open; **Refresh** requests the latest shared snapshot. Batch choices refresh every minute. No new daemon is required: the existing web application's systemd service hosts the monitor.

Each node row shows current PHSP/Slurm task IDs, whole-node CPU and RAM use, completed tasks, elapsed wall time and estimated remaining time. Expand a row for running tasks, waiting tasks in manifest order, failures, and completed tasks. **View log** reads the last 64 KiB of that generated run's TOPAS log. Monitoring is read-only.

## Understanding progress and estimates

- The completed count requires a successful TOPAS timing footer. When Slurm accounting is available, the array element must also have completed with exit status `0:0`. Failed/cancelled tasks stay visible and do not count as completed.
- The bar separates completed, failed and running task counts. It does not invent fractional progress inside a PHSP simulation.
- Slurm can run multiple array elements on a node and start them in a different order from the manifest. Expanded waiting tasks show planned order, not a promise of scheduler execution order.
- Remaining time uses the median successful task duration on that node within this batch, distributes waiting tasks across the currently running task slots, and assumes that concurrency continues. Batch remaining time is the slowest participating node, not the sum of node times. Queue start time is not predicted.
- Before enough evidence exists, or when tasks fail, become unknown/suspended, stop running, or exceed the typical duration, the page explains why an estimate is unavailable. Completion timestamps and elapsed times remain stable after finishing. Estimates are approximate, particularly when PHSP files differ substantially in work.

## Ubuntu server setup

The application service account needs `squeue` and `sacct` on its `PATH`, access to the generated manifests/logs under `Tsebt:AppRoot`, and permission to query its jobs. Slurm accounting must be configured by the cluster administrator. The monitor queries expanded array elements once per batch snapshot, not once per row/browser. New submissions also persist each accepted node job ID immediately, so monitoring survives service restarts and partial submissions. Existing batches are matched using their stored IDs, exact job names and manifests. Live queue queries are scoped to the service account; a known node submission is matched by its exact array ID, even when another job has the same name.

CPU/RAM measurements are actual whole-node use from Linux `/proc/stat` and `/proc/meminfo`; they include other processes on the node. CPU needs two samples, so it initially shows `—`. RAM uses `MemAvailable` and the hover detail shows used/total GiB.

For remote nodes, the service account must already have non-interactive SSH access and verified entries in its `known_hosts`. Test as that same account:

```bash
sudo -u topas ssh -T -oBatchMode=yes -oStrictHostKeyChecking=yes -oConnectTimeout=3 node01 'cat /proc/stat /proc/meminfo'
sudo -u topas squeue --array --noheader
```

Replace `topas` and `node01` with your configured service user and node. The app never enrolls host keys or asks for passwords. Local-host measurements do not need SSH. Remote probes time out after five seconds; an unavailable measurement does not stop task monitoring. Nodes must be reachable from the app host; a browser running on Windows can still view all server-side measurements.

To disable node probes, add this to the existing systemd service environment and restart the service:

```ini
Environment=Tsebt__Monitoring__NodeTelemetry=false
```

The equivalent JSON setting is `"Monitoring": { "NodeTelemetry": false }` inside the existing `"Tsebt"` object. See [systemd setup](systemd-deployment.md) for the service configuration and restart commands. Slurm task monitoring remains enabled.

## Missing or stale information

The page retains its last successful snapshot after a refresh error and displays the timestamp. Scheduler/accounting failures appear as warnings. A healthy completed log can recover completion when accounting is unavailable; incomplete submitted tasks are marked **Unknown**, rather than guessed to be waiting. Missing manifests prevent reliable Slurm element matching and produce a warning. Preserve `runs/{batch}/run_manifest_*.tsv`, generated metadata and raw logs.

If CPU/RAM is unavailable, check SSH as the service account and inspect the hover diagnostic. If Slurm is unavailable under systemd but works in your shell, check the service account, Slurm configuration and service `PATH`. See [troubleshooting](troubleshooting.md).
