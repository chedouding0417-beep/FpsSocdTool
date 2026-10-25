$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$desktop = [Environment]::GetFolderPath('Desktop')
$target = Join-Path $root 'FpsSocdTool.exe'
$shortcutPath = Join-Path $desktop 'FPS SOCD 急停助手.lnk'

if (-not (Test-Path -LiteralPath $target)) {
    throw "找不到 $target，请先编译。"
}

$menuShortcutPath = Join-Path $desktop 'FPS SOCD 控制菜单.lnk'

$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    try {
        $shortcut.TargetPath = $target
        $shortcut.WorkingDirectory = $root
        $shortcut.IconLocation = "$(Join-Path $root 'app.ico'),0"
        $shortcut.Description = 'Ctrl+Shift+O 开启/暂停；松开 A/D 自动反向轻点'
        $shortcut.Save()
    }
    finally {
        if ($shortcut -ne $null) {
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut)
        }
    }

    $menuShortcut = $shell.CreateShortcut($menuShortcutPath)
    try {
        $menuShortcut.TargetPath = $target
        $menuShortcut.WorkingDirectory = $root
        $menuShortcut.IconLocation = "$(Join-Path $root 'app.ico'),0"
        $menuShortcut.Arguments = '--show-menu'
        $menuShortcut.Description = '打开 FPS SOCD 急停助手控制菜单'
        $menuShortcut.Save()
    }
    finally {
        if ($menuShortcut -ne $null) {
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($menuShortcut)
        }
    }
}
finally {
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
}

Write-Host "已创建快捷方式：$shortcutPath"
Write-Host "已创建快捷方式：$menuShortcutPath"
