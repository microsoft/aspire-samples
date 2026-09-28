#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || -z "$1" ]]; then
    echo "Usage: bash build/install-aspire.sh <install-directory>" >&2
    exit 1
fi

# This staging build has stable-shaped package/archive versions but is not a GA release.
build="13.6.0-preview.1.26475.12"
version="13.6.0"
commit="34db30a7d3733229da64a403dfc4e4e4d7a1a43b"
install_path="$1"

case "$(uname -s)" in
    Linux) os="linux" ;;
    Darwin) os="osx" ;;
    MINGW*|MSYS*|CYGWIN*) os="win" ;;
    *) echo "Unsupported operating system: $(uname -s)" >&2; exit 1 ;;
esac
case "$(uname -m)" in
    x86_64|amd64) arch="x64" ;;
    arm64|aarch64) arch="arm64" ;;
    *) echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

extension="tar.gz"
executable="aspire"
if [[ "$os" == "win" ]]; then
    extension="zip"
    executable="aspire.exe"
fi
archive="aspire-cli-$os-$arch-$version.$extension"
temporary_dir="$(mktemp -d)"
trap 'rm -f "$temporary_dir/$archive" "$temporary_dir/checksum"; rmdir "$temporary_dir"' EXIT

curl --fail --silent --show-error --location --retry 3 \
    "https://ci.dot.net/public/aspire/$build/$archive" -o "$temporary_dir/$archive"
curl --fail --silent --show-error --location --retry 3 \
    "https://ci.dot.net/public-checksums/aspire/$build/$archive.sha512" -o "$temporary_dir/checksum"
expected="$(tr -d '\r\n' < "$temporary_dir/checksum" | tr '[:upper:]' '[:lower:]')"
if command -v sha512sum >/dev/null 2>&1; then
    actual="$(sha512sum "$temporary_dir/$archive" | cut -d ' ' -f 1)"
else
    actual="$(shasum -a 512 "$temporary_dir/$archive" | cut -d ' ' -f 1)"
fi
if [[ "$actual" != "$expected" ]]; then
    echo "SHA512 mismatch for $archive" >&2
    exit 1
fi

mkdir -p "$install_path"
if [[ "$os" == "win" ]]; then
    unzip -oq "$temporary_dir/$archive" -d "$install_path"
else
    tar -xzf "$temporary_dir/$archive" -C "$install_path"
fi
installed_version="$("$install_path/$executable" --version)"
if [[ "$installed_version" != "$version+$commit"* ]]; then
    echo "Unexpected Aspire CLI identity: $installed_version" >&2
    exit 1
fi
echo "Installed Aspire $installed_version to $install_path (PATH unchanged)."
