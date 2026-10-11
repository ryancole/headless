<#
.SYNOPSIS
    Builds and starts the game server, the Python stock bot and the Godot game client.

.DESCRIPTION
    The server opens in its own window (stop it with Ctrl-C there); it's left alone if already running.
    The bot opens in its own window too, flying Player One's ship (Prospector) to -BotTarget. The game
    runs in this terminal, so its log appears here. Other arguments are passed on to the game, e.g. to
    connect as Player Two without starting a bot:

        etc/run.ps1 -NoBot --token=dev-player-2

    Set GODOT to use a different Godot (.NET edition) console executable.

.PARAMETER NoBot
    Don't start the bot.

.PARAMETER BotTarget
    Where the bot flies, as "x,y" in metres.
#>

param(
    [switch]$NoBot,
    [string]$BotTarget = '500,300',
    [Parameter(ValueFromRemainingArguments)][string[]]$GameArgs = @()
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$godot = $env:GODOT ?? 'C:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
if (-not (Test-Path $godot)) { throw "Godot not found at '$godot'. Set GODOT to its console executable." }
if (-not $NoBot -and -not (Get-Command python -ErrorAction SilentlyContinue)) { throw 'Python not found. Install Python 3.10+ or pass -NoBot.' }

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

# The bot retries until the server is up. A second bot for the same ship takes over and the first one stops.
if (-not $NoBot) {
    Start-Process python -WorkingDirectory "$root/src/bots/python/hello" -ArgumentList 'hello.py', '--token', 'dev-token-1', '--target', $BotTarget
}

& $godot --path "$root/src/game" -- @GameArgs
