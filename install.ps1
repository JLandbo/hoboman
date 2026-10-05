$publish = Join-Path $PSScriptRoot "publish"
$exe = Join-Path $publish "Hoboman.exe"
$cli = Join-Path $publish "hoboman-cli.exe"
# Unsaved requests only live in the open app, so it is closed by the user instead of stopped.
while (Get-Process Hoboman -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) {
    Read-Host "Hoboman kører. Luk Hoboman, og tryk Enter"
}
# An AI program keeps the MCP server running while it is open, and a running exe cannot be replaced.
while (Get-Process hoboman-cli -ErrorAction SilentlyContinue | Where-Object Path -eq $cli) {
    Read-Host "hoboman-cli kører, fx som MCP-server i hamster-pet eller Claude Code. Luk dem, og tryk Enter"
}
foreach ($project in "Hoboman", "Hoboman.Cli") {
    dotnet publish "$PSScriptRoot\src\$project" -c Release -o $publish
    if ($LASTEXITCODE -ne 0) { throw "Publish af $project fejlede." }
}
# A theme already in the folder is kept, as the user may have changed it.
$themes = New-Item -ItemType Directory -Force (Join-Path $publish "themes")
foreach ($theme in Get-ChildItem "$PSScriptRoot\src\Hoboman\Themes\*.json") {
    if (-not (Test-Path (Join-Path $themes $theme.Name))) { Copy-Item $theme.FullName $themes }
}

# AI programs reach Hoboman through its MCP server, so it is registered for the user in Claude Code when that is installed.
if (Get-Command claude -ErrorAction SilentlyContinue) {
    # Hoboman is installed by now, so a Claude config that cannot be read or changed is only told of.
    try {
        $claudeFolder = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $HOME ".claude" }
        $configFile = if ($env:CLAUDE_CONFIG_DIR) { Join-Path $env:CLAUDE_CONFIG_DIR ".claude.json" } else { Join-Path $HOME ".claude.json" }
        $registered = if (Test-Path $configFile) { (Get-Content $configFile -Raw -Encoding UTF8 | ConvertFrom-Json).mcpServers.hoboman }
        if ($registered.command -ne $cli -or "$($registered.args)" -ne "mcp") {
            if ($registered) { claude mcp remove --scope user hoboman | Out-Null }
            claude mcp add --scope user hoboman $cli mcp
            if ($LASTEXITCODE -ne 0) { throw "claude mcp add fejlede." }
        }
        # The tools that only read are allowed, so the user is asked only before something is sent, run or changed.
        $settingsFile = Join-Path $claudeFolder "settings.json"
        $settings = if (Test-Path $settingsFile) { Get-Content $settingsFile -Raw -Encoding UTF8 | ConvertFrom-Json }
        if (-not $settings) { $settings = [pscustomobject]@{} }
        if (-not $settings.permissions) { $settings | Add-Member permissions ([pscustomobject]@{}) }
        $allowed = @($settings.permissions.allow | Where-Object { $_ })
        $missing = @("list", "show", "history", "log", "check", "guide" | ForEach-Object { "mcp__hoboman__$_" } | Where-Object { $_ -notin $allowed })
        if ($missing) {
            $settings.permissions | Add-Member allow @($allowed + $missing) -Force
            New-Item -ItemType Directory -Force $claudeFolder | Out-Null
            [IO.File]::WriteAllText($settingsFile, ($settings | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
        }
        Write-Host "Hoboman er registreret som MCP-server i Claude Code."
    }
    catch {
        Write-Warning "Hoboman kunne ikke registreres som MCP-server i Claude Code: $($_.Exception.Message)"
    }
}

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut("$([Environment]::GetFolderPath('Programs'))\Hoboman.lnk")
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $publish
$shortcut.Save()
Write-Host "Genvej oprettet i Start-menuen: $($shortcut.FullName)"
explorer.exe $exe
