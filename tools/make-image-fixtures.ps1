$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot=Split-Path $PSScriptRoot -Parent
$folder=Join-Path $projectRoot 'build\image-fixtures';New-Item -ItemType Directory -Force -Path $folder | Out-Null
foreach($number in 1,2,3){
    $bitmap=New-Object System.Drawing.Bitmap(1200,1600)
    $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
    $title=New-Object System.Drawing.Font('Microsoft YaHei',42)
    $text=New-Object System.Drawing.Font('Microsoft YaHei',26)
    try {
        $graphics.Clear([System.Drawing.Color]::White)
        $graphics.DrawString("BOOX IMAGE $number",$title,[System.Drawing.Brushes]::Black,70,80)
        $graphics.DrawString([regex]::Unescape('\u4f4e\u9891\u56fe\u7247\u663e\u793a\u6d4b\u8bd5'),$title,[System.Drawing.Brushes]::Black,70,180)
        $graphics.DrawString('0.5 / 1 / 2 / 5 / 10 seconds',$text,[System.Drawing.Brushes]::Black,70,290)
        $graphics.DrawString('Wi-Fi | Android 4.4.4 | 1200 x 1600',$text,[System.Drawing.Brushes]::Black,70,360)
        $graphics.FillRectangle([System.Drawing.Brushes]::Black,70,500,220*$number,120)
        for($i=0;$i -lt 8;$i++){$brush=New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($i*32,$i*32,$i*32));$graphics.FillRectangle($brush,70+$i*130,750,130,150);$brush.Dispose()}
        $graphics.DrawRectangle([System.Drawing.Pens]::Black,70,1000,1000,350)
        $graphics.DrawLine([System.Drawing.Pens]::Black,70,1000,1070,1350)
        $graphics.DrawString('ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789',$text,[System.Drawing.Brushes]::Black,70,1440)
        $bitmap.Save((Join-Path $folder "frame-$number.png"),[System.Drawing.Imaging.ImageFormat]::Png)
    }finally{$graphics.Dispose();$bitmap.Dispose();$title.Dispose();$text.Dispose()}
}
Get-ChildItem $folder | Select-Object Name,Length
