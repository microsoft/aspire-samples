#!/usr/bin/env bash

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
aspire_cli="${ASPIRE_CLI:-aspire}"

cd "$repo_root"

mapfile -t apphosts < <(git ls-files ':(glob)samples/**/apphost.mts' | sort)
for apphost in "${apphosts[@]}"; do
    sample_dir="$(dirname "$apphost")"
    echo "::group::Restore Aspire SDK for $sample_dir"
    if (
        cd "$sample_dir"
        "$aspire_cli" restore --apphost . --non-interactive --nologo
    ); then
        echo "::endgroup::"
    else
        exit_code=$?
        echo "::endgroup::"
        echo "::error file=$apphost::Aspire restore failed for $sample_dir"
        exit "$exit_code"
    fi
done

mapfile -t lockfiles < <(git ls-files ':(glob)samples/**/package-lock.json' | sort)
for lockfile in "${lockfiles[@]}"; do
    package_dir="$(dirname "$lockfile")"
    package_json="$package_dir/package.json"

    if [[ ! -f "$package_json" ]]; then
        echo "::error file=$lockfile::No package.json found next to committed lockfile"
        exit 1
    fi

    echo "::group::npm ci $package_dir"
    if (
        cd "$package_dir"
        npm ci --no-audit --no-fund
    ); then
        echo "::endgroup::"
    else
        exit_code=$?
        echo "::endgroup::"
        echo "::error file=$lockfile::npm ci failed for $package_dir"
        exit "$exit_code"
    fi
done

for lockfile in "${lockfiles[@]}"; do
    package_dir="$(dirname "$lockfile")"

    if [[ -f "$package_dir/apphost.mts" ]]; then
        continue
    fi

    if (
        cd "$package_dir"
        node -e "const p = JSON.parse(require('fs').readFileSync('package.json', 'utf8')); process.exit(p.scripts?.build ? 0 : 1)"
    ); then
        echo "::group::npm run build $package_dir"
        if (
            cd "$package_dir"
            npm run build
        ); then
            echo "::endgroup::"
        else
            exit_code=$?
            echo "::endgroup::"
            echo "::error file=$package_dir/package.json::npm run build failed for $package_dir"
            exit "$exit_code"
        fi
    fi
done

echo "Validated ${#lockfiles[@]} JavaScript lockfiles and ${#apphosts[@]} TypeScript AppHosts."
