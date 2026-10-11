<#
.SYNOPSIS
    Builds and starts the game server and the Godot game client.

.DESCRIPTION
    The server opens in its own window (stop it with Ctrl-C there); it's left alone if already running.
    The game runs in this terminal, so its log appears here. Arguments are passed on to the game,
    e.g. to connect as Player Two:

        etc/run.ps1 --token=dev-player-2

    Set GODOT to use a different Godot (.NET edition) console executable.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$godot = $env:GODOT ?? 'C:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
if (-not (Test-Path $godot)) { throw "Godot not found at '$godot'. Set GODOT to its console executable." }

# Godot doesn't build C# when running a project from the command line, so build both up front.
dotnet build "$root/src/server/Headless.Server" --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build "$root/src/game" --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (Get-Process Headless.Server -ErrorAction SilentlyContinue) {
    Write-Host 'Server already running.'
} else {
    Start-Process dotnet -WorkingDirectory $root -ArgumentList 'run', '--no-build', '--project', 'src/server/Headless.Server'
}

& $godot --path "$root/src/game" -- @args
