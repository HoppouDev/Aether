$ErrorActionPreference = 'Stop'

$first = 0
$prepareOnly = $args.Count -gt 0 -and $args[0] -ceq 'setup'
if ($args.Count -gt 0 -and $args[0] -ceq 'watch') {
    # .NET hot reload: dotnet watch builds the mod, then runs this launcher (the project's RunCommand, through
    # aether.cmd) with its hot reload agent's startup hook, and sends each saved edit to the game.
    $watchArgs = @($args | Select-Object -Skip 1)
    if ($watchArgs.Count -gt 0 -and $watchArgs[0] -eq '--') {
        $watchArgs = @($watchArgs | Select-Object -Skip 1)
    }
    & dotnet watch --project (Join-Path $PSScriptRoot 'ModProjectName.csproj') --non-interactive run -- @watchArgs
    exit $LASTEXITCODE
}
if ($prepareOnly -or ($args.Count -gt 0 -and $args[0] -ceq 'run')) {
    $first++
}
if ($args.Count -gt $first) {
    if ($args[$first] -in '-h', '--help') {
        Write-Output 'Usage: Run .\aether.ps1 [run | setup | watch] [-- game-args...]'
        Write-Output 'On first use, clone and build Aether under .aether/client.'
        Write-Output 'Use setup to prepare the client without starting the game.'
        Write-Output 'Use watch to run the game with .NET hot reload of the mod (needs the ASP.NET Core runtime).'
        exit 0
    }
    if ($args[$first] -eq '--') {
        $first++
    }
}
$gameArgs = @($args | Select-Object -Skip $first)

$configuration = $env:AETHER_CONFIGURATION
if (-not $configuration) {
    $configuration = 'Debug'
}
if ($configuration -cnotin 'Debug', 'Release') {
    throw 'AETHER_CONFIGURATION must be Debug or Release.'
}

$clientRoot = $env:AETHER_ROOT
$localRoot = Join-Path $PSScriptRoot '.aether-root'
if (-not $clientRoot -and (Test-Path -LiteralPath $localRoot -PathType Leaf)) {
    $clientRoot = ([string](Get-Content -LiteralPath $localRoot -Raw)).Trim()
}
$managed = -not $clientRoot
if ($managed) {
    $workDirectory = Join-Path $PSScriptRoot '.aether'
    $clientRoot = Join-Path $workDirectory 'client'
    if (-not (Test-Path -LiteralPath $clientRoot)) {
        New-Item -ItemType Directory -Path $workDirectory -Force | Out-Null
        $repository = $env:AETHER_REPOSITORY
        if (-not $repository) {
            $repository = 'https://github.com/HoppouDev/Aether.git'
        }
        & git clone -- $repository $clientRoot
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}
if (-not [IO.Path]::IsPathRooted($clientRoot)) {
    $clientRoot = Join-Path $PSScriptRoot $clientRoot
}
$clientRoot = (Resolve-Path -LiteralPath $clientRoot).ProviderPath
$launcher = Join-Path $clientRoot 'aether.ps1'
$bootstrap = Join-Path $clientRoot 'client/Bootstrap'
if (-not (Test-Path -LiteralPath (Join-Path $bootstrap 'Aether.Bootstrap.csproj') -PathType Leaf)) {
    $bootstrap = Join-Path $clientRoot 'Bootstrap'
}
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $bootstrap 'Aether.Bootstrap.csproj') -PathType Leaf)) {
    throw 'The Aether checkout needs a mod-capable revision with the bootstrap project.'
}

$clientBuild = Join-Path $bootstrap "bin/$configuration/net10.0"
$saveDir = Join-Path $PSScriptRoot '.aether/saves'

# Under .\aether.ps1 watch, dotnet watch's hot reload agent is a startup hook for the game only: the
# builds below are .NET processes too, and would take the agent's connection.
$startupHooks = $env:DOTNET_STARTUP_HOOKS
Remove-Item Env:DOTNET_STARTUP_HOOKS -ErrorAction SilentlyContinue

# Built on every run (incremental), so a pulled checkout re-applies its patches and is rebuilt.
if ($managed) {
    Push-Location -LiteralPath $clientRoot
    try {
        if (-not (Test-Path -LiteralPath 'src/Terraria/Main.cs' -PathType Leaf)) {
            & $launcher setup
            if ($LASTEXITCODE -ne 0) {
                exit $LASTEXITCODE
            }
        }
        $buildArgs = @('build')
        if ($configuration -eq 'Release') {
            $buildArgs += '--release'
        }
        & $launcher @buildArgs
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }
}
if ($prepareOnly) {
    & dotnet msbuild $PSScriptRoot -t:PrepareAetherApi "-p:AetherClientDirectory=$clientBuild"
    exit $LASTEXITCODE
}

& dotnet build $PSScriptRoot -t:InstallMod "-p:AetherClientDirectory=$clientBuild" "-p:AetherSaveDirectory=$saveDir"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($startupHooks) {
    $env:DOTNET_STARTUP_HOOKS = $startupHooks
}
# AETHER_LAUNCH_OPTIONS: extra `aether launch` options, such as --profile, split into words.
$launchOptions = @()
if ($env:AETHER_LAUNCH_OPTIONS) {
    $launchOptions = @($env:AETHER_LAUNCH_OPTIONS -split '\s+' | Where-Object { $_ })
}

Push-Location -LiteralPath $clientRoot
try {
    $launchArgs = @('launch', '--configuration', $configuration, '--save-dir', $saveDir) + $launchOptions + @('--') + $gameArgs
    & $launcher @launchArgs
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
exit $exitCode
