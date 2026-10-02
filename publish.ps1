$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'CursorLine.csproj'
$outputDirectory = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    -o $outputDirectory

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$publishedExe = Join-Path $outputDirectory 'CursorLine.exe'
if (-not (Test-Path $publishedExe)) {
    throw "Expected single-file executable was not produced: $publishedExe"
}

Write-Output "Published self-contained app: $publishedExe"
