#!/usr/bin/env bash
# Manual deploy helper — run on the VM directly
set -e
STACK_DIR="$(dirname "$0")/.."
cd "$STACK_DIR"
git pull origin main
docker compose pull
docker compose up -d --remove-orphans
docker image prune -f
echo "Deploy complete: $(date)"
