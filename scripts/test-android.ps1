[CmdletBinding()]
param([switch]$SkipBuild,[switch]$SkipUi,[string]$Serial)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'android-common.ps1')
$environment=Get-PaperNoteAndroidEnvironment
$sourceAssertions=@(
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='AddTool(toolRow, InkCanvasTool.Select';Label='select tool'},
  @{Path='src\PaperNote.Mobile\Controls\InkCanvasView.cs';Pattern='SelectedObjectId';Label='selected object state'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='EraserModeButton';Label='eraser mode control'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='InkOpacityButton';Label='opacity control'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\NativeInkCanvasView.cs';Pattern='DrawSelection';Label='selection adorners'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\NativeInkCanvasView.cs';Pattern='CanvasSnapshot';Label='unified history snapshot'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\NativeInkCanvasView.cs';Pattern='InkEditingService.ErasePartial';Label='partial eraser'},
  @{Path='src\PaperNote.Core\Services\PageObjectEditingService.cs';Pattern='Duplicate';Label='shared object editing'},
  @{Path='src\PaperNote.Core\Services\InkEditingService.cs';Pattern='SmoothStroke';Label='stroke smoothing'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\NativeInkCanvasView.cs';Pattern='PageSelectionService.SelectByPolygon';Label='freeform mixed lasso'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\NativeInkCanvasView.cs';Pattern='PageSelectionService.Move';Label='mixed selection move'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\NativeInkCanvasView.cs';Pattern='PageSelectionService.Resize';Label='mixed selection resize'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\NativeInkCanvasView.cs';Pattern='PageSelectionService.Rotate';Label='mixed selection rotation'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='PageSelectionService.Transfer';Label='cross-page mixed selection transfer'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\NativeInkCanvasView.cs';Pattern='UpdateSelectionStyle';Label='mixed selection batch style'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\NativeInkCanvasView.cs';Pattern='_selectedObjectIds';Label='multi object selection'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\NativeInkCanvasView.cs';Pattern='GroupSelection';Label='object grouping'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\NativeInkCanvasView.cs';Pattern='UngroupSelection';Label='object ungrouping'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\AndroidPageRenderer.cs';Pattern='IsContentVisible';Label='layer visibility rendering'},
  @{Path='src\PaperNote.Mobile\Platforms\Android\AndroidPageRenderer.cs';Pattern='GetEffectiveOpacity';Label='layer opacity rendering'},
  @{Path='src\PaperNote.Core\Services\PdfImportCacheService.cs';Pattern='MarkCancelledAsync';Label='resumable PDF import cache'},
  @{Path='src\PaperNote.Core\Services\PdfPageRangeService.cs';Pattern='MaximumImportPageCount = 500';Label='500-page PDF range support'},
  @{Path='src\PaperNote.Mobile\Services\AndroidPdfService.cs';Pattern='PrepareImportAsync';Label='staged Android PDF import'},
  @{Path='src\PaperNote.Mobile\Services\AndroidPdfService.cs';Pattern='cache.TryReadPage';Label='Android PDF cache resume'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='PdfImportCancelButton';Label='visible Android PDF cancellation'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='PdfPageRangeService.Parse';Label='Android PDF page range selection'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='PdfStudyMenuButton';Label='PDF study menu automation id'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='搜索 PDF 文本';Label='PDF text search menu'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='书签与大纲';Label='bookmark and outline menu'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='当前页内部链接';Label='internal PDF links menu'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='批注列表';Label='annotation list menu'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='页面批量管理';Label='page batch menu'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='按页码范围导出 PDF';Label='range PDF export menu'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.cs';Pattern='按页码范围提取为笔记本';Label='range notebook extraction menu'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.PdfStudy.cs';Pattern='OfflineSearchService';Label='offline PDF search'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.PdfStudy.cs';Pattern='PdfDocumentContentService.ResolveInternalLinks';Label='internal PDF link resolution'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.PdfStudy.cs';Pattern='PageAnnotationService.Build';Label='annotation index'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.PdfStudy.cs';Pattern='PageBatchService.Duplicate';Label='Android batch duplicate'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.PdfStudy.cs';Pattern='PageBatchService.Delete';Label='Android batch delete'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.PdfStudy.cs';Pattern='PageBatchService.ExtractDocument';Label='Android notebook extraction'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.PdfStudy.cs';Pattern='PdfPageRangeService.Parse';Label='Android range parser'},
  @{Path='src\PaperNote.Mobile\Pages\EditorPage.PdfStudy.cs';Pattern='ExportAndShareAsync';Label='Android export and share'},
  @{Path='src\PaperNote.Core\Models\NotebookDocument.cs';Pattern='PdfText';Label='stored PDF text'},
  @{Path='src\PaperNote.Core\Models\NotebookDocument.cs';Pattern='PdfLinks';Label='stored PDF links'},
  @{Path='src\PaperNote.Core\Models\NotebookDocument.cs';Pattern='Comments';Label='stored page comments'},
  @{Path='src\PaperNote.Core\Models\NotebookDocument.cs';Pattern='OutlineEntries';Label='stored PDF outline'},
  @{Path='src\PaperNote.Core\Services\PdfDocumentContentService.cs';Pattern='UglyToad.PdfPig';Label='PdfPig PDF reader'},
  @{Path='src\PaperNote.Core\Services\PdfDocumentContentService.cs';Pattern='AttachToImportedPages';Label='PDF content attachment'},
  @{Path='src\PaperNote.Mobile\Services\AndroidPdfService.cs';Pattern='PdfDocumentContentService.ExtractAsync';Label='Android PDF content extraction'}
)
foreach($assertion in $sourceAssertions){
  $path=Join-Path $environment.RepoRoot $assertion.Path
  if(-not(Test-Path -LiteralPath $path)){throw "Android source assertion file missing: $($assertion.Path)"}
  if(-not(Select-String -LiteralPath $path -SimpleMatch $assertion.Pattern -Quiet)){throw "Android source assertion failed: $($assertion.Label)"}
}
Write-Host 'ANDROID SOURCE STATIC CHECK PASS'
if(-not $SkipBuild){ & (Join-Path $PSScriptRoot 'build-android.ps1') }
function U([int[]]$c){-join($c|ForEach-Object{[char]$_})}
$uiNew="$(U @(0xFF0B)) $(U @(0x65B0,0x5EFA))";$uiConfirm='OK';$uiPen=U @(0x94A2,0x7B14);$uiEraser=U @(0x6A61,0x76AE,0x64E6);$uiSelected=" $([char]0x2713)"
$uiPenSelected="$uiPen$uiSelected";$uiEraserSelected="$uiEraser$uiSelected";$uiHighlighter=U @(0x8367,0x5149,0x7B14);$uiHighlighterSelected="$uiHighlighter$uiSelected"
$uiUndo=U @(0x64A4,0x9500);$uiRedo=U @(0x91CD,0x505A);$uiLibrary=U @(0x8D44,0x6599,0x5E93)
$uiSettings=U @(0x8BBE,0x7F6E);$uiFinger=U @(0x5141,0x8BB8,0x624B,0x6307,0x4E66,0x5199);$uiFingerOn="$(U @(0x624B,0x6307,0xFF1A,0x5F00))";$uiFingerOff="$(U @(0x624B,0x6307,0xFF1A,0x5173))"
$uiWidth18="$(U @(0x7C97,0x7EC6)) 18";$uiWidth26="$(U @(0x7C97,0x7EC6)) 26";$uiWidth6="$(U @(0x7C97,0x7EC6)) 6";$uiWidthSheet=U @(0x7B14,0x8FF9,0x7C97,0x7EC6);$uiThick=U @(0x7C97)
$uiColor="$(U @(0x989C,0x8272)) $([char]0x25CF)";$uiColorSheet=U @(0x58A8,0x8FF9,0x989C,0x8272);$uiBlue=U @(0x84DD,0x8272)
$uiNotebook=U @(0x65B0,0x7B14,0x8BB0);$uiNewPage="$(U @(0xFF0B)) $(U @(0x65B0,0x9875))"
$uiPageTwo="2/2 $(U @(0x9875))";$uiMore=U @(0x66F4,0x591A)
$uiAddShape=U @(0x6DFB,0x52A0,0x5F62,0x72B6);$uiArrow=U @(0x7BAD,0x5934)
$package='com.jiejietech.papernote';$apk=Join-Path $environment.RepoRoot 'artifacts\android\PaperNote-Android-1.0.0.apk'
if(-not(Test-Path -LiteralPath $apk)){throw "APK not found: $apk"}
$badging=(& $environment.Aapt2 dump badging $apk 2>&1|Out-String)
if($LASTEXITCODE-ne 0){throw 'APK metadata inspection failed.'}
foreach($required in @("name='$package'","versionCode='1'","versionName='1.0.0'","minSdkVersion:'23'","targetSdkVersion:'36'","application: label='PaperNote' icon='res/mipmap","native-code: 'arm64-v8a' 'armeabi-v7a' 'x86' 'x86_64'")){if($badging-notlike "*$required*"){throw "APK metadata assertion failed: $required"}}
$signature=(& $environment.ApkSigner verify --verbose --print-certs $apk 2>&1|Out-String)
if($LASTEXITCODE-ne 0-or $signature-notmatch 'Verified using v[12] scheme'){throw 'APK signature check failed.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($apk)
try{
  $entryNames=@($archive.Entries|ForEach-Object{$_.FullName})
  foreach($requiredEntry in @('assets/THIRD-PARTY-NOTICES.md','assets/legal/third-party/PdfPig-LICENSE.txt')){
    if($entryNames-notcontains $requiredEntry){throw "APK bundled notice missing: $requiredEntry"}
  }
  $noticeEntry=$archive.GetEntry('assets/THIRD-PARTY-NOTICES.md')
  $noticeReader=[IO.StreamReader]::new($noticeEntry.Open(),[Text.Encoding]::UTF8)
  try{$noticeText=$noticeReader.ReadToEnd()}finally{$noticeReader.Dispose()}
  if($noticeText-notmatch 'PdfPig' -or $noticeText-notmatch 'Apache-2.0'){throw 'APK PdfPig notice is incomplete.'}
  $assemblyEntry=$archive.Entries|Where-Object{$_.FullName-like 'lib/arm64-v8a/libassemblies*.so'}|Select-Object -First 1
  if(-not $assemblyEntry){throw 'APK arm64 assembly store is missing.'}
  $assemblyStream=$assemblyEntry.Open();$assemblyMemory=[IO.MemoryStream]::new()
  try{$assemblyStream.CopyTo($assemblyMemory);$assemblyText=[Text.Encoding]::ASCII.GetString($assemblyMemory.ToArray())}finally{$assemblyMemory.Dispose();$assemblyStream.Dispose()}
  if($assemblyText-notmatch 'UglyToad\.PdfPig'){throw 'PdfPig assembly was not found in the APK assembly store.'}
}finally{$archive.Dispose()}
Write-Host 'ANDROID APK STATIC CHECK PASS';if($SkipUi){return}
$devices=@(& $environment.Adb devices|ForEach-Object{if($_-match '^(\S+)\s+device$'){$Matches[1]}})
if([string]::IsNullOrWhiteSpace($Serial)){$Serial=$devices|Where-Object{$_-like 'emulator-*'}|Select-Object -First 1}
if([string]::IsNullOrWhiteSpace($Serial)){Write-Host 'ANDROID RUNTIME CHECK SKIPPED: no running emulator was found.';return}
if($Serial-notlike 'emulator-*'){throw 'Automated UI checks are restricted to Android emulators.'}
function A([string[]]$Arguments,[switch]$AllowFailure){$oldPreference=$ErrorActionPreference;$ErrorActionPreference='Continue';try{$o=& $environment.Adb -s $Serial @Arguments 2>&1;$e=$LASTEXITCODE}finally{$ErrorActionPreference=$oldPreference};if(-not $AllowFailure-and $e-ne 0){throw "ADB failed: $($Arguments-join ' ')$([Environment]::NewLine)$($o|Out-String)"};[pscustomobject]@{ExitCode=$e;Output=@($o)}}
$uiPath=Join-Path ([IO.Path]::GetTempPath()) "papernote-ui-$PID.xml";$remote='/sdcard/papernote-ui.xml'
$art=Join-Path $environment.RepoRoot 'artifacts\android';New-Item -ItemType Directory -Path $art -Force|Out-Null
Get-ChildItem -LiteralPath $art -Filter 'debug-*' -File -ErrorAction SilentlyContinue|Remove-Item -Force
$stageIndex=0
function D{(A @('shell','uiautomator','dump',$remote)).Output|Out-Null;(A @('pull',$remote,$uiPath)).Output|Out-Null;[xml](Get-Content -LiteralPath $uiPath -Raw -Encoding UTF8)}
function Stage([string]$Name){
  $script:stageIndex++
  $safe=($Name -replace '[^a-zA-Z0-9_-]','-').Trim('-')
  $prefix=('{0:D2}-{1}' -f $script:stageIndex,$safe)
  Write-Host "ANDROID TEST STEP $($script:stageIndex): $Name"
  try{D|Out-Null;Copy-Item -LiteralPath $uiPath -Destination (Join-Path $art "debug-$prefix-ui.xml") -Force}catch{Write-Warning "UI dump failed at ${Name}: $($_.Exception.Message)"}
  try{(A @('shell','screencap','-p','/sdcard/papernote-stage.png')).Output|Out-Null;(A @('pull','/sdcard/papernote-stage.png',(Join-Path $art "debug-$prefix.png"))).Output|Out-Null}catch{Write-Warning "Screenshot failed at ${Name}: $($_.Exception.Message)"}
}
function Assert-Resumed([string]$Context){
  $state=((A @('shell','dumpsys','activity','activities')).Output|Out-String)
  if($state-notmatch "(?im)(topResumedActivity|ResumedActivity|mResumedActivity).*?$([regex]::Escape($package))"){throw "PaperNote is not resumed at: $Context"}
}
function F([string]$Text,[switch]$Contains){$nodes=@((D).SelectNodes('//node'));if($Contains){$nodes|Where-Object{([string]$_.text).Contains($Text)}|Select-Object -First 1}else{$nodes|Where-Object{[string]$_.text-eq $Text}|Select-Object -First 1}}
function W([string]$Text,[switch]$Contains,[int]$Timeout=15){$end=[DateTime]::UtcNow.AddSeconds($Timeout);do{try{$n=F $Text -Contains:$Contains;if($n){return $n}}catch{};Start-Sleep -Milliseconds 500}while([DateTime]::UtcNow-lt $end);throw "UI text timeout: $Text"}
function Center($n){if(([string]$n.bounds)-notmatch '^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$'){throw 'Invalid bounds'};[pscustomobject]@{X=[int]((([int]$Matches[1])+([int]$Matches[3]))/2);Y=[int]((([int]$Matches[2])+([int]$Matches[4]))/2)}}
function TapNode($n){$p=Center $n;(A @('shell','input','tap',[string]$p.X,[string]$p.Y)).Output|Out-Null}
function Tap([string]$Text,[switch]$Contains){TapNode (W $Text -Contains:$Contains)}
function Enabled([string]$Text,[bool]$Expected){$actual=[string](W $Text).enabled-eq 'true';if($actual-ne $Expected){throw "Expected $Text enabled=$Expected, got $actual"}}
(A @('logcat','-c')).Output|Out-Null;$install=A @('install','-r',$apk) -AllowFailure
if($install.ExitCode-ne 0-or($install.Output-join [Environment]::NewLine)-notmatch 'Success'){(A @('uninstall',$package) -AllowFailure).Output|Out-Null;$install=A @('install',$apk);if(($install.Output-join [Environment]::NewLine)-notmatch 'Success'){throw 'APK install failed.'}}
(A @('shell','pm','clear',$package)).Output|Out-Null;$resolved=(A @('shell','cmd','package','resolve-activity','--brief',$package)).Output
$activity=($resolved|Where-Object{$_-match '^.+/.+$'}|Select-Object -Last 1).Trim();if(!$activity){throw 'Launcher activity not found.'}
(A @('shell','am','start','-W','-n',$activity)).Output|Out-Null;W 'PaperNote'|Out-Null;Stage 'library-fresh-launch';Assert-Resumed 'fresh launch'
Tap $uiNew;W $uiConfirm|Out-Null;Stage 'new-notebook-dialog';Tap $uiConfirm;W $uiPenSelected|Out-Null;W $uiFingerOn|Out-Null;Stage 'editor-created-toolbar-visible';Assert-Resumed 'new notebook editor'
Enabled $uiUndo $false;Tap $uiHighlighter;W $uiHighlighterSelected|Out-Null;W $uiWidth18|Out-Null;Tap $uiWidth18;W $uiWidthSheet|Out-Null;Tap $uiThick;W $uiWidth26|Out-Null
Tap $uiColor;W $uiColorSheet|Out-Null;Tap $uiBlue;W $uiColor|Out-Null;Tap $uiPen;W $uiPenSelected|Out-Null;W $uiWidth6|Out-Null;Stage 'toolbar-tool-width-color-interactions'
$size=((A @('shell','wm','size')).Output|Out-String);$sizeMatches=[regex]::Matches($size,'(\d+)x(\d+)');if($sizeMatches.Count-eq 0){throw 'Screen size unavailable.'}
$screenSize=$sizeMatches[$sizeMatches.Count-1];$w=[int]$screenSize.Groups[1].Value;$h=[int]$screenSize.Groups[2].Value;$x1=[int]($w*.35);$x2=[int]($w*.68);$y1=[int]($h*.48);$y2=[int]($h*.53)
Tap $uiEraser;W $uiEraserSelected|Out-Null;(A @('shell','input','swipe',[string]$x1,[string]$y1,[string]$x2,[string]$y2,'350')).Output|Out-Null;Start-Sleep -Milliseconds 700;Enabled $uiUndo $false;Tap $uiPen;W $uiPenSelected|Out-Null;Stage 'eraser-miss-keeps-history-clean'
Tap $uiFingerOn;W $uiFingerOff|Out-Null;(A @('shell','input','swipe',[string]$x2,[string]$y2,[string]$x1,[string]$y1,'450')).Output|Out-Null;Start-Sleep -Milliseconds 800;Enabled $uiUndo $false;Stage 'finger-writing-disabled-pan'
Tap $uiFingerOff;W $uiFingerOn|Out-Null;(A @('shell','input','swipe',[string]$x1,[string]$y1,[string]$x2,[string]$y2,'450')).Output|Out-Null;Start-Sleep -Milliseconds 1000;Enabled $uiUndo $true;Tap $uiUndo;Enabled $uiRedo $true;Tap $uiRedo;Stage 'default-finger-draw-undo-redo'
Tap $uiLibrary -Contains;W $uiSettings|Out-Null;Stage 'returned-to-library';Assert-Resumed 'returned to library'
Tap $uiSettings;W $uiFinger|Out-Null;Stage 'settings-opened';Assert-Resumed 'settings page'
$toggle=@((D).SelectNodes('//node'))|Where-Object{[string]$_.checkable-eq 'true' -and (([string]$_.text).Contains($uiFinger) -or ([string]$_.'content-desc').Contains($uiFinger))}|Select-Object -First 1
if(!$toggle){$toggle=@((D).SelectNodes('//node'))|Where-Object{[string]$_.checkable-eq 'true'}|Select-Object -First 1}
if(!$toggle-or [string]$toggle.checked-ne 'true'){throw 'Finger drawing should be enabled by default and synchronized to settings.'};TapNode $toggle;Start-Sleep -Milliseconds 500;Stage 'finger-writing-disabled-in-settings'
Assert-Resumed 'before leaving settings';(A @('shell','input','keyevent','4')).Output|Out-Null;W $uiLibrary|Out-Null;Stage 'back-from-settings';Assert-Resumed 'after leaving settings'
Tap $uiNotebook -Contains;W $uiPenSelected|Out-Null;W $uiFingerOff|Out-Null;Tap $uiFingerOff;W $uiFingerOn|Out-Null;Stage 'editor-reopened-toolbar-preference-synced'
Tap $uiNewPage;W $uiPageTwo -Contains|Out-Null;Stage 'second-page-added';Tap $uiMore;W $uiAddShape|Out-Null;Tap $uiAddShape;W $uiArrow|Out-Null;Tap $uiArrow;W $uiPageTwo -Contains|Out-Null;Stage 'shape-added'
Tap $uiLibrary -Contains;W $uiLibrary|Out-Null;Stage 'library-before-restart';(A @('shell','am','force-stop',$package)).Output|Out-Null;(A @('shell','am','start','-W','-n',$activity)).Output|Out-Null
W $uiNotebook -Contains|Out-Null;Stage 'library-after-restart';Tap $uiNotebook -Contains;W $uiPageTwo -Contains|Out-Null;Stage 'editor-after-restart';Assert-Resumed 'restart persistence'
(A @('shell','screencap','-p','/sdcard/papernote-runtime.png')).Output|Out-Null;(A @('pull','/sdcard/papernote-runtime.png',(Join-Path $art 'PaperNote-Android-runtime.png'))).Output|Out-Null
D|Out-Null;Copy-Item -LiteralPath $uiPath -Destination (Join-Path $art 'PaperNote-Android-runtime-ui.xml') -Force
(A @('shell','input','keyevent','4')).Output|Out-Null;W $uiLibrary|Out-Null;Assert-Resumed 'system back from editor'
(A @('shell','input','keyevent','4')).Output|Out-Null;Start-Sleep -Seconds 1
$state=((A @('shell','dumpsys','activity','activities')).Output|Out-String);if($state-match "(?im)(topResumedActivity|ResumedActivity|mResumedActivity).*?$([regex]::Escape($package))"){throw 'Root system Back did not leave PaperNote.'}
$logs=((A @('logcat','-d','-v','brief')).Output|Out-String);if($logs-match 'FATAL EXCEPTION'-or $logs-match "AndroidRuntime.*Process: $([regex]::Escape($package))"){Set-Content -LiteralPath (Join-Path $art 'PaperNote-Android-logcat.txt') -Value $logs -Encoding UTF8;throw 'Android crash detected.'}
@('PaperNote Android background runtime test',"serial=$Serial","activity=$activity",'freshInstall=pass','launch=pass','toolbarVisibleAndClickable=pass','toolSelection=pass','widthAndColorControls=pass','fingerDrawingDefault=pass','fingerPanToggle=pass','settingsSync=pass','eraserMissHistory=pass','undoRedo=pass','addPage=pass','addShape=pass','restartPersistence=pass','systemBackNavigation=pass','rootBackExit=pass','fatalException=none')|Set-Content -LiteralPath (Join-Path $art 'PaperNote-Android-runtime-report.txt') -Encoding UTF8
Get-ChildItem -LiteralPath $art -Filter 'debug-*' -File -ErrorAction SilentlyContinue|Remove-Item -Force
Remove-Item -LiteralPath $uiPath -Force -ErrorAction SilentlyContinue;Write-Host 'ANDROID BACKGROUND RUNTIME TEST PASS'
