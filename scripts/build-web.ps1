$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '../src/LearnSphere.Web')
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Angular build failed' }
} finally { Pop-Location }
