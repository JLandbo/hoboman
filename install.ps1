$publish = Join-Path $PSScriptRoot "publish"
$exe = Join-Path $publish "Hoboman.exe"
# Unsaved requests only live in the open app, so it is closed by the user instead of stopped.
while (Get-Process Hoboman -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) {
    Read-Host "Hoboman kører. Luk Hoboman, og tryk Enter"
}
dotnet publish "$PSScriptRoot\src\Hoboman" -c Release -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish fejlede." }

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut("$([Environment]::GetFolderPath('Programs'))\Hoboman.lnk")
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $publish
$shortcut.Save()
Write-Host "Genvej oprettet i Start-menuen: $($shortcut.FullName)"
explorer.exe $exe
