### args[0] => Solution/Base Path
### args[1] => Payload Path
### args[2] => App Binary (published) Path

$cmd = $args[2]
$dest = $args[1]
$ErrorActionPreference = 'Stop'
& $cmd --writeConfig $dest | Out-Null
if ($LASTEXITCODE -ne 0) { throw "生成默认配置失败 (exit $LASTEXITCODE)" }
