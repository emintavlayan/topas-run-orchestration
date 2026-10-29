#!/usr/bin/env bash
set -Eeuo pipefail

# Called by deploy-app.ps1; archive and this script are uploaded to a unique temporary directory.
deploy_path=${1:?Missing deployment path}
service_name=${2:?Missing service name}
health_url=${3:?Missing health URL}
expected_hash=${4:?Missing archive hash}
work_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
archive="$work_dir/application.tar.gz"
backup="${deploy_path}.backup-$(date -u +%Y%m%dT%H%M%S)-$$"
failed="${deploy_path}.failed-$(date -u +%Y%m%dT%H%M%S)-$$"
stage=''
stopped=0
previous_moved=0
installed=0
completed=0

# /// Restores the previous application when a deployment fails after stopping its service.
on_exit() {
    local status=$?
    trap - EXIT
    set +e
    if (( status != 0 && stopped )); then
        echo 'Deployment failed; restoring the previous application.' >&2
        journalctl -u "$service_name" -n 30 --no-pager >&2
        if (( installed )); then
            sudo systemctl stop "$service_name"
            if ! mv -- "$deploy_path" "$failed"; then
                echo "Cannot move the failed application aside. Previous app remains at: $backup" >&2
                exit "$status"
            fi
            echo "Failed application retained at: $failed" >&2
        fi
        if (( previous_moved )); then
            if ! mv -- "$backup" "$deploy_path"; then
                echo "Cannot restore the previous application. Backup remains at: $backup" >&2
                exit "$status"
            fi
        fi
        if sudo systemctl start "$service_name" && systemctl is-active --quiet "$service_name"; then
            echo 'Previous application restored; service started.' >&2
        else
            echo "Previous service did not start; inspect journalctl -u $service_name." >&2
        fi
    fi
    if [[ -n "$stage" && -d "$stage" ]]; then
        rm -rf -- "$stage"
    fi
    exit "$status"
}
trap on_exit EXIT

[[ "$deploy_path" = /* && "$deploy_path" != '/' && "$deploy_path" != */ ]] || {
    echo 'Deployment path must be an absolute application directory without a trailing slash.' >&2
    exit 1
}
for command in tar sha256sum systemctl sudo curl; do
    command -v "$command" >/dev/null
done
[[ -f "$deploy_path/Server.dll" && -f "$deploy_path/appsettings.json" ]] || {
    echo "Existing application or production appsettings.json missing: $deploy_path" >&2
    exit 1
}
configured_directory=$(systemctl show "$service_name" -p WorkingDirectory --value)
[[ "$configured_directory" = "$deploy_path" ]] || {
    echo "Service WorkingDirectory does not match deployment path: $configured_directory" >&2
    exit 1
}
printf '%s  %s\n' "$expected_hash" "$archive" | sha256sum --check --status

stage=$(mktemp -d "${deploy_path}.staging.XXXXXX")
tar -xzf "$archive" -C "$stage"
for required_file in Server.dll Server.deps.json Server.runtimeconfig.json public/index.html; do
    [[ -s "$stage/$required_file" ]] || { echo "Package file missing or empty: $required_file" >&2; exit 1; }
done
for configuration in "$deploy_path"/appsettings*.json; do
    [[ -f "$configuration" ]] && cp -p -- "$configuration" "$stage/"
done

sudo -v
# Mark the service before stopping so an interrupted/failed stop also attempts recovery.
stopped=1
sudo systemctl stop "$service_name"
mv -- "$deploy_path" "$backup"
previous_moved=1
mv -- "$stage" "$deploy_path"
installed=1
sudo systemctl start "$service_name"

for attempt in $(seq 1 30); do
    if systemctl is-active --quiet "$service_name" && curl --fail --silent --max-time 2 "$health_url" >/dev/null; then
        completed=1
        break
    fi
    sleep 1
done
(( completed )) || { echo 'Application did not become healthy after restart.' >&2; exit 1; }
systemctl status "$service_name" --no-pager --full
printf 'Deployment verified. Previous application retained at: %s\n' "$backup"
