#!/usr/bin/env bash
# Install global .NET diagnostic tools used by run-profile.sh.
set -euo pipefail

export PATH="$PATH:$HOME/.dotnet/tools"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK not found. Install .NET first." >&2
  exit 1
fi

install_or_update() {
  local id="$1"
  if dotnet tool list -g | awk '{print $1}' | grep -qx "$id"; then
    echo "Updating $id..."
    dotnet tool update -g "$id"
  else
    echo "Installing $id..."
    dotnet tool install -g "$id"
  fi
}

install_or_update dotnet-trace
install_or_update dotnet-counters

echo
echo "Installed tools:"
dotnet-trace --version
dotnet-counters --version
echo
echo "Ensure ~/.dotnet/tools is on PATH (this shell already has it via this script)."
echo "Then run: ./profiling/run-profile.sh"
