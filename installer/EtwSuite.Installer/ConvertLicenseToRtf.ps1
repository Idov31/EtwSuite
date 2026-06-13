param(
    [Parameter(Mandatory = $true)]
    [string] $InputPath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$licenseText = Get-Content -LiteralPath $InputPath -Raw
$escapedText = $licenseText.Replace('\', '\\').Replace('{', '\{').Replace('}', '\}')
$escapedText = $escapedText -replace "`r`n|`n|`r", '\par '

$rtf = "{\rtf1\ansi\deff0{\fonttbl{\f0 Consolas;}}\f0\fs18 $escapedText}"
$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

Set-Content -LiteralPath $OutputPath -Value $rtf -Encoding ASCII
