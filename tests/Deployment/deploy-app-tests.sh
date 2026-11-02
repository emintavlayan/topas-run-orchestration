#!/usr/bin/env bash
set -Eeuo pipefail
repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
test_root=$(mktemp -d)
trap 'rm -rf -- "$test_root"' EXIT
mkdir "$test_root/bin"

cat > "$test_root/bin/systemctl" <<'MOCK'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$TEST_COMMANDS"
case "$1" in
    show) printf '%s\n' "$TEST_APP" ;;
    start)
        if [[ ${TEST_START_FAIL:-0} = 1 && $(cat "$TEST_APP/Server.dll") = new-server ]]; then exit 1; fi ;;
esac
MOCK
cat > "$test_root/bin/sudo" <<'MOCK'
#!/usr/bin/env bash
[[ "$1" = '-v' ]] && exit 0
exec "$@"
MOCK
cat > "$test_root/bin/curl" <<'MOCK'
#!/usr/bin/env bash
[[ ${TEST_HEALTH_FAIL:-0} != 1 ]]
MOCK
printf '#!/usr/bin/env bash\nexit 0\n' > "$test_root/bin/sleep"
printf '#!/usr/bin/env bash\nexit 0\n' > "$test_root/bin/journalctl"
chmod +x "$test_root/bin/"*
export PATH="$test_root/bin:$PATH"

# /// Exercises deployment against a temporary application and mock service commands.
run_case() {
    local name=$1
    local expected_status=$2
    export TEST_APP="$test_root/$name/app"
    export TEST_COMMANDS="$test_root/$name/commands.log"
    mkdir -p "$TEST_APP/public" "$test_root/$name/upload" "$test_root/$name/package/public"
    printf old-server > "$TEST_APP/Server.dll"
    printf production-settings > "$TEST_APP/appsettings.json"
    printf production-overrides > "$TEST_APP/appsettings.Production.json"
    printf old-page > "$TEST_APP/public/index.html"
    local package="$test_root/$name/package"
    printf new-server > "$package/Server.dll"
    printf dependencies > "$package/Server.deps.json"
    printf runtime > "$package/Server.runtimeconfig.json"
    printf new-page > "$package/public/index.html"
    printf wrong-windows-settings > "$package/appsettings.json"
    local upload="$test_root/$name/upload"
    cp "$repository_root/scripts/deploy-app.sh" "$upload/"
    tar -czf "$upload/application.tar.gz" -C "$package" .
    local checksum
    checksum=$(sha256sum "$upload/application.tar.gz" | cut -d ' ' -f 1)
    if [[ "$name" = bad-checksum ]]; then checksum=$(printf '0%.0s' {1..64}); fi
    export TEST_START_FAIL=0 TEST_HEALTH_FAIL=0
    [[ "$name" = startup-failure ]] && TEST_START_FAIL=1
    [[ "$name" = health-failure ]] && TEST_HEALTH_FAIL=1
    local status=0
    bash "$upload/deploy-app.sh" "$TEST_APP" test.service http://localhost:5000/ "$checksum" > "$test_root/$name/result.log" 2>&1 || status=$?
    if [[ "$expected_status" = success ]]; then
        [[ "$status" = 0 ]]
        [[ $(cat "$TEST_APP/Server.dll") = new-server ]]
        [[ $(cat "$TEST_APP/public/index.html") = new-page ]]
        grep -q 'Deployment verified' "$test_root/$name/result.log"
        [[ $(cat "$TEST_APP".backup-*/Server.dll) = old-server ]]
    else
        [[ "$status" != 0 ]]
        [[ $(cat "$TEST_APP/Server.dll") = old-server ]]
        if [[ "$name" = bad-checksum ]]; then
            ! grep -q '^stop ' "$TEST_COMMANDS"
        else
            grep -q 'Previous application restored' "$test_root/$name/result.log"
            [[ $(cat "$TEST_APP".failed-*/Server.dll) = new-server ]]
        fi
    fi
    [[ $(cat "$TEST_APP/appsettings.json") = production-settings ]]
    [[ $(cat "$TEST_APP/appsettings.Production.json") = production-overrides ]]
    printf 'PASS %s\n' "$name"
}
run_case success success
run_case startup-failure failure
run_case health-failure failure
run_case bad-checksum failure
