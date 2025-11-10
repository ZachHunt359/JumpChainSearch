$oldPath = [Environment]::GetEnvironmentVariable('PATH', 'Machine')
if ($oldPath -notlike '*gs10.03.1*') {
    $newPath = $oldPath + ';C:\Program Files\gs\gs10.03.1\bin'
    [Environment]::SetEnvironmentVariable('PATH', $newPath, 'Machine')
    Write-Host 'Ghostscript added to system PATH'
} else {
    Write-Host 'Ghostscript already in PATH'
}
