# Windows launcher for the patch workflow CLI (the equivalent of ./aether).
# Needs uv, the .NET SDK and Git for Windows (its usr\bin provides GNU diff and patch).
$ErrorActionPreference = 'Stop'

if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    Write-Error 'uv not found on PATH. Install it from https://docs.astral.sh/uv/'
    exit 1
}

# GNU diff/patch ship with Git for Windows, but outside Git Bash they aren't on PATH.
if (-not ((Get-Command diff.exe -ErrorAction SilentlyContinue) -and (Get-Command patch.exe -ErrorAction SilentlyContinue))) {
    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($git) {
        # git.exe lives in <Git>\cmd or <Git>\mingw64\bin; usr\bin is under <Git>.
        $dir = Split-Path $git.Source
        while ($dir) {
            $usrBin = Join-Path $dir 'usr\bin'
            if ((Test-Path (Join-Path $usrBin 'patch.exe')) -and (Test-Path (Join-Path $usrBin 'diff.exe'))) {
                $env:PATH = "$usrBin;$env:PATH"
                break
            }
            $dir = Split-Path $dir
        }
    }
}

& uv run --quiet --project $PSScriptRoot aether @args
exit $LASTEXITCODE
