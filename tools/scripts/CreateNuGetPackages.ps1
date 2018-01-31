$srcPath = [System.IO.Path]::GetFullPath(($PSScriptRoot + '\..\..\src'))

# delete existing packages
Remove-Item $PSScriptRoot\*.nupkg

nuget pack $srcPath\SenseNet.Search.Lucene29\SenseNet.Search.Lucene29.csproj -properties Configuration=Release -OutputDirectory $PSScriptRoot
nuget pack $srcPath\SenseNet.Search.Lucene29.Common\SenseNet.Search.Lucene29.Common.csproj -properties Configuration=Release -OutputDirectory $PSScriptRoot
nuget pack $srcPath\SenseNet.Search.Lucene29.Local\SenseNet.Search.Lucene29.Local.csproj -properties Configuration=Release -OutputDirectory $PSScriptRoot
