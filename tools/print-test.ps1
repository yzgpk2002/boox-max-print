$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$doc = New-Object System.Drawing.Printing.PrintDocument
$doc.PrinterSettings.PrinterName = 'BOOX Max'
if (!$doc.PrinterSettings.IsValid) { throw 'BOOX Max printer is not installed.' }
$doc.DocumentName = 'BOOX Max - Chinese and multipage test'
$doc.PrintController = New-Object System.Drawing.Printing.StandardPrintController
$doc.DefaultPageSettings.Margins = New-Object System.Drawing.Printing.Margins(55,55,55,55)
$script:pageNumber = 0
$titleFont = New-Object System.Drawing.Font('Microsoft YaHei',24)
$textFont = New-Object System.Drawing.Font('Microsoft YaHei',14)
$smallFont = New-Object System.Drawing.Font('Microsoft YaHei',10)
$chineseTitle = [regex]::Unescape('\u7535\u8111\u6253\u5370\uff0c\u7535\u7eb8\u4e66\u9605\u8bfb')
$chineseText = [regex]::Unescape('\u8fd9\u662f\u4e00\u4efd\u4e24\u9875\u6d4b\u8bd5\u6587\u6863\u3002\u4e2d\u6587\u3001\u82f1\u6587\u548c\u56fe\u5f62\u5e94\u6e05\u6670\u663e\u793a\u3002')
$doc.add_PrintPage({
  param($sender,$event)
  $script:pageNumber++
  $g=$event.Graphics
  $g.DrawString('BOOX MAX PRINT', $titleFont, [System.Drawing.Brushes]::Black, 55, 60)
  $g.DrawString($chineseTitle, $titleFont, [System.Drawing.Brushes]::Black, 55, 115)
  $g.DrawString(('Page {0} of 2' -f $script:pageNumber), $textFont, [System.Drawing.Brushes]::Black, 55, 185)
  $g.DrawString($chineseText, $textFont, [System.Drawing.Brushes]::Black, 55, 230)
  $g.DrawString('Windows print -> BOOX Max -> Wi-Fi / USB -> PDF reader', $textFont, [System.Drawing.Brushes]::Black, 55, 275)
  $g.DrawString('ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789', $textFont, [System.Drawing.Brushes]::Black, 55, 320)
  $g.DrawRectangle([System.Drawing.Pens]::Black, 55, 380, 600, 180)
  $g.DrawLine([System.Drawing.Pens]::Black,55,380,655,560)
  $g.DrawEllipse([System.Drawing.Pens]::Black, 280, 400, 140, 140)
  $g.DrawString('Print verification - 2026-11-16', $smallFont, [System.Drawing.Brushes]::Black, 55, 620)
  if ($script:pageNumber -eq 2) {
    $g.DrawString([regex]::Unescape('\u7b2c\u4e8c\u9875\uff1a\u7ffb\u9875\u6d4b\u8bd5\u6210\u529f'), $titleFont,[System.Drawing.Brushes]::Black,55,700)
  }
  $event.HasMorePages = $script:pageNumber -lt 2
})
try { $doc.Print(); Write-Output 'Submitted two-page test to BOOX Max.' }
finally { $doc.Dispose(); $titleFont.Dispose(); $textFont.Dispose(); $smallFont.Dispose() }
