# 鲨壁 ShabiLite

鲨壁是一款面向 Windows 10/11 的轻量动态壁纸管理器。它使用 WPF 构建界面，通过 LibVLC 播放 MP4 动态壁纸，并支持使用 SteamCMD 下载 Wallpaper Engine 创意工坊内容。

## 功能

- 导入本地 MP4，并复制到独立壁纸库
- 鼠标悬停预览、选择、删除壁纸
- 通过 Steam 创意工坊链接下载视频壁纸
- 铺满、完整显示和拉伸三种缩放方式
- 静音播放、暂停和继续
- 最小化到系统托盘
- 浅紫色界面与自定义窗口标题栏

## 环境要求

- 64 位 Windows 10 或 Windows 11
- .NET Framework 4.7.2 运行环境
- 构建时使用 .NET 8 SDK 或 Visual Studio 2022

## 构建

```powershell
dotnet restore Shabi.csproj
dotnet build Shabi.csproj -c Release -r win-x64
```

构建结果位于：

```text
bin/Release/net472/win-x64/
```

LibVLC 原生运行库由 `VideoLAN.LibVLC.Windows` NuGet 包提供。发布软件时请保留生成目录内的依赖文件，不能只复制主程序 EXE。

## SteamCMD

创意工坊下载需要 SteamCMD 和有效的 Steam 授权。程序不会保存 Steam 密码，SteamCMD 产生的授权缓存应只保存在本机或受控的私有存储中。

仓库已忽略以下运行数据：

- SteamCMD 程序与授权缓存
- 本地设置和日志
- 下载或导入的壁纸文件
- 编译输出和第三方运行库

## 项目结构

```text
Assets/                 应用图标
Interop/                Windows 桌面与窗口外观接口
Services/               设置、日志、SteamCMD、托盘和壁纸库服务
MainWindow.xaml         主界面
SettingsWindow.xaml     设置界面
WallpaperPlayer.cs      LibVLC 壁纸播放窗口
pack-lite.ps1           轻量版整理脚本
```

## 说明

本仓库暂未声明开源许可证。未经版权方许可，不代表授予复制、修改或再发布权利。
