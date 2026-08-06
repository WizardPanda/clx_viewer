# Clx Viewer

> [English](README.md) | **简体中文**

一个轻量、快速启动的 Windows 查看器，用于 **Clinx `.clx` 化学发光图像** —— 内置图像查看器加 Explorer 缩略图扩展，全部基于 [`clxcpp`](https://github.com/WizardPanda/clinx_format_cpp) C++ 解析器，导出的图像数据与仪器原始文件**逐像素一致**。

![Clx Viewer](docs/viewer-screenshot.png)

## 功能特性

- **打开与浏览** `.clx` 文件 —— 多选打开、文件夹文件列表、上一个/下一个、拖放、从资源管理器双击打开、单实例。
- **三种视图**，可实时切换：
  - **荧光（Fluo）** —— 荧光通道，默认**反色**显示（Western blot 效果）。
  - **明场（Bright）** —— 明场通道（黑色底板上的白色胶片）。
  - **合并（Merged）** —— 荧光信号从明场中**减去**，条带显示为亮膜上的深色标记；使用未反色的荧光，采用与反色视图相同的 min/max。
- **双滑块范围调节** + **自动**按钮 —— min/max 实时更新图像；数值框也可编辑。
- **自动 min/max**，针对此类数据调优：
  - *荧光：* 背景显示为干净浅灰（约 234/255），而非纯白，条带保持深色清晰。
  - *明场：* 胶片渲染到接近 90% 亮度，即使胶片只占画面一小部分，标记依然可见。
- **元数据面板** —— 样本名、采集时间、曝光、软件、构建日期、每张图像描述符、文件名元数据、尾部信息，按需显示。
- **导出** —— 逐像素一致的 16 位 TIFF 与处理后的 8 位 PNG（见 [导出命名](#导出命名)），成功导出时有非阻塞提示。
- **缩放与平移**（滚轮、拖动、双击适应窗口）以及状态栏中的原始像素值读数。
- **Explorer 缩略图** —— 原生 `IThumbnailProvider` 渲染荧光预览，无额外徽标。

## 环境要求

- Windows 10/11（x64）
- VS 2022 Build Tools（MSVC + CMake + Ninja）
- .NET 8 SDK（仅构建需要；自包含安装包已捆绑运行时）
- `clinx_format_cpp` 子模块

## 从 Release 安装

每个 [GitHub Release](https://github.com/WizardPanda/clx_viewer/releases) 都附带预构建产物：

| 产物 | 说明 |
|---|---|
| `*-fd-installer.exe` | 按用户安装；需要 .NET 8 Desktop Runtime（缺失时自动安装） |
| `*-sc-installer.exe` | 按用户安装；捆绑 .NET 8 运行时（无依赖） |
| `*-fd-portable.zip` | 便携文件夹；需要 .NET 8 Desktop Runtime |
| `*-sc-portable.zip` | 便携文件夹，捆绑 .NET 8 运行时 |

安装程序会在当前用户下注册 Explorer 缩略图处理程序与 `.clx` 文件关联（无需管理员权限），并附带合适的卸载程序。便携 zip 中包含 `register-portable.ps1` / `unregister-portable.ps1`，可按需进行 shell 集成。

## 从源码构建

```powershell
git submodule update --init --recursive
.\tools\build.ps1          # 构建 native + WPF 查看器；将 clxreader.dll 放到 exe 旁边
.\viewer\bin\x64\Release\net8.0-windows\ClxViewer.exe  <capture.clx>
```

运行 `.\tools\register.ps1` 进行 shell 集成（或 `.\tools\unregister.ps1` 撤销）。

### Release 产物（安装包 + 便携 zip）

```powershell
.\installer\prepare.ps1 -AppVersion 1.0.0   # 生成 FD/SC 安装包与便携 zip 到 .\dist
```

### 构建产物

| 产物 | 位置 |
|---|---|
| 查看器 | `viewer\bin\x64\Release\net8.0-windows\ClxViewer.exe` |
| 原生读取库 | `build-native\bin\clxreader.dll`（同时复制到 exe 旁） |
| 缩略图提供程序 | `build-native\bin\ClinxThumbnailProvider.dll` |

## 键盘快捷键

| 快捷键 | 操作 |
|---|---|
| `Ctrl+O` | 打开 `.clx` 文件 |
| `Ctrl+E` | 将当前视图导出为 PNG |
| `Ctrl+Shift+E` | 导出原始 16 位 TIFF |
| `Alt+E` | 选择导出文件夹 |
| `←` / `→` | 上一个 / 下一个文件 |
| `F` / `B` / `M` | 荧光 / 明场 / 合并视图 |
| `A` | 自动 min/max |
| `I` | 反色荧光颜色 |
| `Ctrl+W` | 关闭 |

（工具栏上的键盘图标按钮会显示同样的快捷键速查表。）

## 导出命名

- **原始 TIFF** —— `<stem>_<i>_16bit.tif`，每张嵌入图像一个；总是覆盖，避免重复。
- **处理后的 PNG** —— `<stem>_<view>_min<low>_max<high>.png`，其中 `view` 为 `fluo`、`bf` 或 `merged`。

默认导出到 `.clx` 文件旁边，或通过 `Alt+E` 选择文件夹。

## Explorer 缩略图预览

`ClinxThumbnailProvider.dll` 是一个进程内 COM `IThumbnailProvider`：使用 `clxcpp` 解析 `.clx`，并以与查看器相同的自动级别渲染荧光通道（透明边距）。它在 **HKCU**（当前用户，无需管理员权限）注册，且不绘制自己的徽标，因此 `.clx` 作为普通文件类型 —— Explorer 根据全局**“在缩略图上显示文件图标”**文件夹选项应用常规缩略图图标覆盖。

## 仓库结构

```
native/clxreader/       C-ABI DLL over clxcpp (clxreader.dll)
native/shellext/        COM IThumbnailProvider (ClinxThumbnailProvider.dll)
third_party/clinx_format_cpp/   clxcpp parser (git submodule)
viewer/                 .NET 8 WPF application
tools/                  build / register / unregister / refresh-thumbnails / icon / screenshot scripts
assets/                 brand icon
docs/                   screenshot
```

解析器作为 git 子模块引入，并通过 CMake `add_subdirectory` 引用，因此导出的像素与仪器 TIFF **逐字节一致**。

## 备注

- **缩略图缓存**：首次注册后，资源管理器可能仍显示通用图标，直到缓存刷新 —— 运行 `.\tools\refresh-thumbnails.ps1`，或在文件夹中离开再返回。
- **自动级别常量**位于 `viewer/Services/AutoLevels.cs`，并与 `native/shellext/thumbnail_provider.cpp` 中的实现保持一致 —— 修改时需同步。
- **无头导出**（用于构建检查）：`ClxViewer.exe --selftest-export <outdir> <file.clx>` 在不显示窗口的情况下导出原始 TIFF 与三种视图。
- `.clx` 格式为逆向工程得到（见 `clinx_format_cpp/docs`）；已在 clxcpp 测试套件中用真实仪器样本验证。
