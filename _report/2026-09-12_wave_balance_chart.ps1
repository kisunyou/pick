Add-Type -AssemblyName System.Drawing
$rows = Import-Csv (Join-Path $PSScriptRoot '2026-09-12_wave_balance_scenarios.csv') | Where-Object { $_.assumed_success_rate -eq '0.6' }
$bitmap = New-Object System.Drawing.Bitmap 1200,650
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::White)
$font = New-Object System.Drawing.Font 'Arial',12
$title = New-Object System.Drawing.Font 'Arial',20
$grid = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(225,228,231)),1
$targetPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(218,118,52)),2
$targetPen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash
$modelPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(0,134,135)),3
$shade = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240,244,245))
$g.DrawString('Pick: Three-Wave Difficulty Curve', $title, [System.Drawing.Brushes]::Black, 70, 24)
$g.DrawString('Comparison model, not measured player time | stages 1-3 unchanged', $font, [System.Drawing.Brushes]::DimGray, 70, 64)
$left=75; $top=110; $width=1050; $height=445
function X([double]$stage) { return [single]($left+($stage-1)/35*$width) }
function Y([double]$minutes) { return [single]($top+$height-$minutes/25*$height) }
$g.FillRectangle($shade, (X 1), [single]$top, [single]((X 3)-(X 1)), [single]$height)
foreach ($range in @(@(10,13),@(21,27))) {
    $g.FillRectangle($shade,(X $range[0]),[single]$top,[single]((X $range[1])-(X $range[0])),[single]$height)
}
for($minutes=0;$minutes -le 25;$minutes+=5) {
    $g.DrawLine($grid,[single]$left,(Y $minutes),[single]($left+$width),(Y $minutes))
    $g.DrawString([string]$minutes,$font,[System.Drawing.Brushes]::DimGray,30,[single]((Y $minutes)-10))
}
foreach($stage in @(1,3,4,9,13,20,27,36)) {
    $g.DrawString([string]$stage,$font,[System.Drawing.Brushes]::DimGray,[single]((X $stage)-8),565)
}
$points = [System.Drawing.PointF[]]@($rows | ForEach-Object { [System.Drawing.PointF]::new((X ([int]$_.stage)),(Y ([double]$_.median_minutes))) })
$targets = [System.Drawing.PointF[]]@($rows | Where-Object { [int]$_.stage -ge 4 } | ForEach-Object { [System.Drawing.PointF]::new((X ([int]$_.stage)),(Y ([double]$_.target_minutes))) })
$g.DrawLines($targetPen,$targets)
$g.DrawLines($modelPen,$points)
$g.DrawString('Minutes',$font,[System.Drawing.Brushes]::DimGray,12,88)
$g.DrawString('Stage',$font,[System.Drawing.Brushes]::DimGray,1090,591)
$g.DrawLine($modelPen,75,615,115,615)
$g.DrawString('Model median (60% success, 18 sec / pull)',$font,[System.Drawing.Brushes]::Black,125,604)
$g.DrawLine($targetPen,625,615,665,615)
$g.DrawString('Design target',$font,[System.Drawing.Brushes]::Black,675,604)
$bitmap.Save((Join-Path $PSScriptRoot '2026-09-12_wave_balance_curve.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bitmap.Dispose()
$font.Dispose()
$title.Dispose()
$grid.Dispose()
$targetPen.Dispose()
$modelPen.Dispose()
$shade.Dispose()
