# Enables the repository git hooks (secret scan before every commit).
Set-Location (git rev-parse --show-toplevel)
git config core.hooksPath .githooks
Write-Host "Git hooks enabled (.githooks)."
