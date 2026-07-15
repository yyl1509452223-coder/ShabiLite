param(
    [string]$Source = (Join-Path $PSScriptRoot 'bin\Release\net472\win-x64'),
    [string]$Destination = (Join-Path (Split-Path $PSScriptRoot -Parent) 'outputs\shabi-ultralite'),
    [string]$RemoteServerUrl,
    [string]$RemoteServerSettingsPath
)

$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$sourcePath = [System.IO.Path]::GetFullPath($Source)
$destinationPath = [System.IO.Path]::GetFullPath($Destination)

if (-not $sourcePath.StartsWith($workspace, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Source must stay inside the workspace: $sourcePath"
}
if (-not $destinationPath.StartsWith($workspace, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Destination must stay inside the workspace: $destinationPath"
}
if (-not (Get-ChildItem -LiteralPath $sourcePath -Filter '*.exe' -File | Select-Object -First 1)) {
    throw "Release build not found: $sourcePath"
}

if (-not (Test-Path -LiteralPath $destinationPath)) {
    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
}

Get-ChildItem -LiteralPath $sourcePath -File |
    Where-Object { $_.Extension -notin @('.pdb', '.xml') } |
    Copy-Item -Destination $destinationPath -Force

$vlcSource = Join-Path $sourcePath 'libvlc\win-x64'
$vlcTarget = Join-Path $destinationPath 'libvlc\win-x64'
New-Item -ItemType Directory -Path $vlcTarget -Force | Out-Null

$nativeFiles = @(
    'libvlc.dll',
    'libvlccore.dll'
)

$plugins = @(
    'plugins\access\libfilesystem_plugin.dll',
    'plugins\access\libidummy_plugin.dll',

    'plugins\audio_filter\libaudio_format_plugin.dll',
    'plugins\audio_filter\libdolby_surround_decoder_plugin.dll',
    'plugins\audio_filter\libgain_plugin.dll',
    'plugins\audio_filter\libheadphone_channel_mixer_plugin.dll',
    'plugins\audio_filter\libsimple_channel_mixer_plugin.dll',
    'plugins\audio_filter\libspeex_resampler_plugin.dll',
    'plugins\audio_filter\libtospdif_plugin.dll',
    'plugins\audio_filter\libtrivial_channel_mixer_plugin.dll',
    'plugins\audio_filter\libugly_resampler_plugin.dll',
    'plugins\audio_mixer\libfloat_mixer_plugin.dll',
    'plugins\audio_mixer\libinteger_mixer_plugin.dll',
    'plugins\audio_output\libadummy_plugin.dll',
    'plugins\audio_output\libdirectsound_plugin.dll',
    'plugins\audio_output\libmmdevice_plugin.dll',
    'plugins\audio_output\libwasapi_plugin.dll',

    'plugins\codec\libavcodec_plugin.dll',
    'plugins\codec\libd3d11va_plugin.dll',
    'plugins\codec\libdxva2_plugin.dll',
    'plugins\codec\libmft_plugin.dll',

    'plugins\d3d11\libdirect3d11_filters_plugin.dll',
    'plugins\d3d9\libdirect3d9_filters_plugin.dll',

    'plugins\demux\libes_plugin.dll',
    'plugins\demux\libh26x_plugin.dll',
    'plugins\demux\libmp4_plugin.dll',

    'plugins\logger\libconsole_logger_plugin.dll',

    'plugins\packetizer\libpacketizer_copy_plugin.dll',
    'plugins\packetizer\libpacketizer_h264_plugin.dll',
    'plugins\packetizer\libpacketizer_hevc_plugin.dll',
    'plugins\packetizer\libpacketizer_mpeg4audio_plugin.dll',
    'plugins\packetizer\libpacketizer_mpeg4video_plugin.dll',
    'plugins\packetizer\libpacketizer_mpegaudio_plugin.dll',

    'plugins\stream_filter\libcache_block_plugin.dll',
    'plugins\stream_filter\libcache_read_plugin.dll',
    'plugins\stream_filter\libprefetch_plugin.dll',

    'plugins\video_chroma\libchain_plugin.dll',
    'plugins\video_chroma\libi420_10_p010_plugin.dll',
    'plugins\video_chroma\libi420_nv12_plugin.dll',
    'plugins\video_chroma\libi420_rgb_plugin.dll',
    'plugins\video_chroma\libi420_rgb_sse2_plugin.dll',
    'plugins\video_chroma\libi420_yuy2_plugin.dll',
    'plugins\video_chroma\libi420_yuy2_sse2_plugin.dll',
    'plugins\video_chroma\libi422_i420_plugin.dll',
    'plugins\video_chroma\libi422_yuy2_plugin.dll',
    'plugins\video_chroma\libi422_yuy2_sse2_plugin.dll',
    'plugins\video_chroma\librv32_plugin.dll',
    'plugins\video_chroma\libswscale_plugin.dll',
    'plugins\video_chroma\libyuy2_i420_plugin.dll',
    'plugins\video_chroma\libyuy2_i422_plugin.dll',

    'plugins\video_filter\libcanvas_plugin.dll',
    'plugins\video_filter\libcroppadd_plugin.dll',
    'plugins\video_filter\libdeinterlace_plugin.dll',
    'plugins\video_filter\libscale_plugin.dll',
    'plugins\video_filter\libtransform_plugin.dll',

    'plugins\video_output\libdirect3d11_plugin.dll',
    'plugins\video_output\libdirect3d9_plugin.dll',
    'plugins\video_output\libdirectdraw_plugin.dll',
    'plugins\video_output\libdrawable_plugin.dll',
    'plugins\video_output\libvdummy_plugin.dll',
    'plugins\video_output\libwingdi_plugin.dll',
    'plugins\video_output\libwinhibit_plugin.dll'
)

foreach ($relativePath in ($nativeFiles + $plugins)) {
    $from = Join-Path $vlcSource $relativePath
    if (-not (Test-Path -LiteralPath $from)) {
        throw "Required VLC module is missing: $relativePath"
    }
    $to = Join-Path $vlcTarget $relativePath
    New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $from -Destination $to -Force
}

$readme = @(
    'Shabi V2.3 Lite',
    '',
    '1. Run the EXE in this folder.',
    '2. Windows 10/11 x64 only.',
    '3. Local H.264/H.265 MP4 is recommended.',
    '4. Keep the entire folder together.',
    '5. Exit older versions from the tray before starting.'
) -join [Environment]::NewLine
Set-Content -LiteralPath (Join-Path $destinationPath 'README.txt') -Value $readme -Encoding UTF8

if ($RemoteServerUrl -and $RemoteServerSettingsPath) {
    $settingsPath = [System.IO.Path]::GetFullPath($RemoteServerSettingsPath)
    if (-not (Test-Path -LiteralPath $settingsPath)) {
        throw "Server settings not found: $settingsPath"
    }
    $serverSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath $settingsPath | ConvertFrom-Json
    if (-not $serverSettings.ApiKey) {
        throw 'Server API key is missing.'
    }
    $remoteProfile = [string]::Join([Environment]::NewLine, @(
        ('url=' + $RemoteServerUrl.TrimEnd('/'))
        ('key=' + $serverSettings.ApiKey)
    ))
    Set-Content -LiteralPath (Join-Path $destinationPath 'remote-access.ini') -Value $remoteProfile -Encoding UTF8
}

$files = Get-ChildItem -LiteralPath $destinationPath -File -Recurse
[pscustomobject]@{
    Path = $destinationPath
    Files = $files.Count
    SizeMB = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 2)
}
