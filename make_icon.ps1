Add-Type -AssemblyName System.Drawing

function Create-BrainlabIcon {
    param ([string]$outputPath)

    $sizes = @(256, 128, 64, 48, 32, 16)
    $bitmaps = @()

    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        # Background: Dark rounded rectangle with glowing border
        $bgRect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $radius = [int]($size * 0.18)
        $diameter = $radius * 2

        $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
        $path.AddArc($size - $diameter, 0, $diameter, $diameter, 270, 90)
        $path.AddArc($size - $diameter, $size - $diameter, $diameter, $diameter, 0, 90)
        $path.AddArc(0, $size - $diameter, $diameter, $diameter, 90, 90)
        $path.CloseFigure()

        # Dark gradient background (#0A0A0C to #16161A)
        $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $bgRect,
            [System.Drawing.Color]::FromArgb(255, 10, 10, 12),
            [System.Drawing.Color]::FromArgb(255, 25, 25, 30),
            45.0
        )
        $g.FillPath($bgBrush, $path)

        # Subtle inner glow / border
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(180, 200, 200, 220), [float]($size * 0.03))
        $g.DrawPath($pen, $path)

        # Brain / DTI Fiber Tracts & DICOM Slice Merger Motif
        # Draw 3D stacked DICOM Slices (subtle isometric grid)
        $slicePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(100, 140, 140, 160), [float]($size * 0.02))
        for ($i = 0; $i -lt 3; $i++) {
            $offsetY = [float]($size * (0.65 - $i * 0.12))
            $pts = @(
                [System.Drawing.PointF]::new([float]($size * 0.2), $offsetY),
                [System.Drawing.PointF]::new([float]($size * 0.5), [float]($offsetY - $size * 0.1)),
                [System.Drawing.PointF]::new([float]($size * 0.8), $offsetY),
                [System.Drawing.PointF]::new([float]($size * 0.5), [float]($offsetY + $size * 0.1))
            )
            $g.DrawPolygon($slicePen, $pts)
        }

        # DTI Neural Fiber Stream Curves (Red/Green/Blue Directional Colors of DTI!)
        # Red = Right-Left (CST / Corpus Callosum), Green = Anterior-Posterior, Blue = Superior-Inferior
        $fiberPenRed = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230, 240, 60, 60), [float]($size * 0.05))
        $fiberPenGreen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230, 60, 220, 120), [float]($size * 0.05))
        $fiberPenBlue = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230, 60, 140, 255), [float]($size * 0.05))
        $fiberPenGold = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 245, 245, 250), [float]($size * 0.06))

        # Vertical brain stem / CST fibers (Blue)
        $g.DrawBeziers($fiberPenBlue, @(
            [System.Drawing.PointF]::new([float]($size * 0.5), [float]($size * 0.85)),
            [System.Drawing.PointF]::new([float]($size * 0.48), [float]($size * 0.6)),
            [System.Drawing.PointF]::new([float]($size * 0.35), [float]($size * 0.35)),
            [System.Drawing.PointF]::new([float]($size * 0.25), [float]($size * 0.2))
        ))
        $g.DrawBeziers($fiberPenBlue, @(
            [System.Drawing.PointF]::new([float]($size * 0.5), [float]($size * 0.85)),
            [System.Drawing.PointF]::new([float]($size * 0.52), [float]($size * 0.6)),
            [System.Drawing.PointF]::new([float]($size * 0.65), [float]($size * 0.35)),
            [System.Drawing.PointF]::new([float]($size * 0.75), [float]($size * 0.2))
        ))

        # Corpus callosum arch (Red)
        $g.DrawBeziers($fiberPenRed, @(
            [System.Drawing.PointF]::new([float]($size * 0.2), [float]($size * 0.45)),
            [System.Drawing.PointF]::new([float]($size * 0.35), [float]($size * 0.25)),
            [System.Drawing.PointF]::new([float]($size * 0.65), [float]($size * 0.25)),
            [System.Drawing.PointF]::new([float]($size * 0.8), [float]($size * 0.45))
        ))

        # Anterior-Posterior association fibers (Green)
        $g.DrawBeziers($fiberPenGreen, @(
            [System.Drawing.PointF]::new([float]($size * 0.3), [float]($size * 0.65)),
            [System.Drawing.PointF]::new([float]($size * 0.5), [float]($size * 0.4)),
            [System.Drawing.PointF]::new([float]($size * 0.7), [float]($size * 0.65)),
            [System.Drawing.PointF]::new([float]($size * 0.8), [float]($size * 0.75))
        ))

        # Central fusion core / Brainlab node (Glowing Gold/Silver Dot)
        $coreSize = [float]($size * 0.14)
        $coreRect = New-Object System.Drawing.RectangleF([float]($size * 0.5 - $coreSize / 2), [float]($size * 0.42 - $coreSize / 2), $coreSize, $coreSize)
        $coreBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 240, 240, 250))
        $g.FillEllipse($coreBrush, $coreRect)
        $g.DrawEllipse($fiberPenGold, $coreRect)

        $g.Dispose()
        $bitmaps += $bmp
    }

    # Save multi-icon structure
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    # ICONDIR structure
    $bw.Write([uint16]0) # Reserved
    $bw.Write([uint16]1) # Type 1 = ICO
    $bw.Write([uint16]$bitmaps.Count) # Count

    $offset = 6 + ($bitmaps.Count * 16)
    $pngStreams = @()

    foreach ($bmp in $bitmaps) {
        $pngStream = New-Object System.IO.MemoryStream
        $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngBytes = $pngStream.ToArray()
        $pngStreams += $pngBytes

        $w = if ($bmp.Width -ge 256) { 0 } else { [byte]$bmp.Width }
        $h = if ($bmp.Height -ge 256) { 0 } else { [byte]$bmp.Height }

        $bw.Write([byte]$w)
        $bw.Write([byte]$h)
        $bw.Write([byte]0) # Color count
        $bw.Write([byte]0) # Reserved
        $bw.Write([uint16]1) # Planes
        $bw.Write([uint16]32) # Bpp
        $bw.Write([uint32]$pngBytes.Length)
        $bw.Write([uint32]$offset)

        $offset += $pngBytes.Length
    }

    foreach ($pngBytes in $pngStreams) {
        $bw.Write($pngBytes)
    }

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($outputPath, $ms.ToArray())

    foreach ($bmp in $bitmaps) { $bmp.Dispose() }
    $bw.Dispose()
    $ms.Dispose()

    Write-Host "Created $outputPath successfully!"
}

Create-BrainlabIcon -outputPath "c:\Users\marku\Desktop\NewDicomMerger\app_icon.ico"
