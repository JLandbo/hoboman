$publish = Join-Path $PSScriptRoot "publish"
$exe = Join-Path $publish "Hoboman.exe"
# Unsaved requests only live in the open app, so it is closed by the user instead of stopped.
while (Get-Process Hoboman -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) {
    Read-Host "Hoboman kører. Luk Hoboman, og tryk Enter"
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

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut("$([Environment]::GetFolderPath('Programs'))\Hoboman.lnk")
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $publish
$shortcut.Save()
Write-Host "Genvej oprettet i Start-menuen: $($shortcut.FullName)"
explorer.exe $exe
