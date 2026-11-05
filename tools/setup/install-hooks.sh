#!/usr/bin/env bash
# Enables the repository git hooks (secret scan before every commit).
cd "$(git rev-parse --show-toplevel)" && git config core.hooksPath .githooks && echo "Git hooks enabled (.githooks)."
