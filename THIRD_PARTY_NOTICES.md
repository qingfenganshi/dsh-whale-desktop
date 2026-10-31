# THIRD_PARTY_NOTICES

本项目是 **DeepSeek-Balance-Whale-Widget** 的衍生作品。以下列出上游项目信息与
本衍生作品各自的权利归属。

## 1. 上游项目（必须保留）

| 项目 | 内容 |
|---|---|
| 名称 | **DeepSeek-Balance-Whale-Widget** |
| 作者 | **MeteorNOX** |
| 仓库 | https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget |
| 许可证 | **MIT License** |
| 版权声明 | `Copyright (c) 2026 MeteorNOX` |
| 本衍生作品基于的版本 | `0.5.1` |

### 为什么可以衍生

上游项目发布在 **MIT 许可**下。MIT 允许修改、再分发、以及以不同条款发布衍生
作品，**唯一强制义务是**：在衍生作品中保留原版权声明与许可声明。

本项目的 [`LICENSE`](LICENSE) 已包含上述义务，保留了原版权行。

### 上游项目的「素材例外」声明（重要）

上游 `PROVENANCE.md` 明确声明：

- **代码**（`lib/`、`cordis.patch.yml`、`package.json`、文档）：MIT
- **`assets/**` 与 `icon.png`**：**不适用 MIT**，按 as-is 随插件分发，仅用于运行本插件，**不授予再许可**
- 上游的美术素材为 AI 工具生成、经人工挑选裁切，**原始出处已不可考**

因此本项目：

1. **不对 `assets/` 与 `icon.png` 主张任何额外版权或再许可**
2. 原样保留这些文件用于插件运行
3. 阅读者若要**复用/改造这些美术素材**，应当自行联系上游作者，而不是依据本项目的 MIT 许可

## 2. 本衍生作品的新增部分

以下内容为本衍生作品原创，随本项目以 MIT 发布：

| 文件 | 说明 |
|---|---|
| `lib/index.js` 中的对话相关代码段 | 小鲸鱼对话接口、API Key 管理、推理等级、桌面程序启停 |
| `assets/whale-widget.js` 中的对话气泡、授权气泡音、桌面入口 | 见各段 `v90x` 注释标记 |
| `desktop/DesktopWhale.Core.cs` | 桌面小鲸鱼主程序（WinForms + 分层窗口） |
| `desktop/DesktopWhale.Bubble.cs` | 桌面版椭圆气泡对话窗口 |
| `README.md`、本文件 | 文档 |

## 3. 第三方运行时依赖

| 依赖 | 用途 | 许可 |
|---|---|---|
| .NET Framework 4.x（Windows 自带） | 编译/运行桌面小程序 | Microsoft 许可 |
| Win32 API（user32 / gdi32 / winmm） | 透明窗口、椭圆裁剪、音效播放 | 系统 API |
| Node.js 内置模块（fs / path / http） | DSH 插件宿主代码 | Node.js MIT |
| DeepSeek 官方 API | 对话与余额 | 由使用者自行申请 Key，遵循 DeepSeek 服务条款 |

**本项目不打包也不分发任何第三方二进制依赖。** 桌面小程序需由使用者自行编译
（或使用 Release 中提供的构建产物）。

## 4. 商标

"DeepSeek" 是 DeepSeek 公司的商标。本项目名称中的 "DeepSeek" 仅用于说明
**兼容对象**，不表示与 DeepSeek 公司存在任何隶属、赞助或背书关系。

## 5. 联系

若您是上游作者或素材权利人，认为本项目某处使用不当，请在本仓库提交 Issue，
我们会在核实后立即处理（包括移除相关文件）。
