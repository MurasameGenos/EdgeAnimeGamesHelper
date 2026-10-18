# 攻略小窗 · 独立 WebView2 浏览器

C# Windows 浏览器，默认主页为 **https://www.bilibili.com/**。可以搜索和登录 Bilibili，用置顶沉浸小窗观看攻略。

[下载最新 Windows 版本](https://github.com/MurasameGenos/EdgeAnimeGamesHelper/releases/latest) · [MIT 许可证](LICENSE) · [第三方组件](THIRD_PARTY_NOTICES.md)

## 使用

1. 从上面的 Release 下载 Windows ZIP。**先从托盘退出旧程序**，再解压新版 ZIP，运行 `GuideBrowser.exe`。EXE 旁的三个 DLL 请一起保留。
2. 打开一个视频并开始播放。
3. 按 `8` 或点击“沉浸”，只显示视频，隐藏标题栏、工具栏及边距，按视频比例调整窗口并直接贴住当前显示器最底部。横向位置会尽量保留。再次按 `8` 恢复原界面、大小和位置。

| 按键 | 窗口显示时 |
| --- | --- |
| `` ` `` / `~`（Esc 下方同一物理键） | 暂停 / 继续视频 |
| `5` | 后退 5 秒 |
| `6` | 前进 5 秒 |
| `7` | 隐藏窗口 |
| `8` | 进入 / 退出沉浸 |

**隐藏后只保留 `7` 恢复窗口。** `~ / 5 / 6 / 8` 的全局注册会被释放，正常传给其他应用；隐藏期间不能控制视频或切换沉浸。视频继续按隐藏前的状态播放。按 `7` 恢复后，重新启用其他快捷键。

数字主键盘和开启 Num Lock 的数字小键盘均支持。`9` 没有绑定功能。播放快捷键在普通浏览器和沉浸模式下均生效，不依赖其他模式。窗口始终允许鼠标操作，没有鼠标穿透切换。

输入包含这些按键的网址、搜索内容或验证码前，可点击底部“暂停热键”，输入后再点击“恢复热键”。托盘中也可以暂停全部热键、显示浏览器、重置位置或退出。若某个按键已被其他程序占用，会显示冲突提示。

普通界面可拖动标题栏、从边缘等比缩放，接近屏幕边缘放开鼠标时吸附。底部吸附使用完整显示器底边，不再停在任务栏上沿。沉浸使用实际 HWND 坐标定位，在网页布局完成后重新吸附并校验窗口底部，后续宽高比变化、轮询和隐藏恢复也保持贴底。需要调整位置或大小时，可先按 `8` 恢复浏览器界面。

## 浏览器

- 后退、前进、刷新、Bilibili 主页与地址栏。
- `Ctrl+L` 定位地址栏，`F5` 刷新，`Alt+左/右` 导航。
- 新窗口链接在当前窗口打开，网页全屏停留在应用窗口内。
- 沉浸保留原媒体元素，支持原生 `video` 和提供标准媒体接口的 Bilibili `bwp-video`，不下载或重新抓取视频。
- 没有可用视频时不会隐藏界面；导航、视频消失或播放错误时退出沉浸，恢复操作入口。

登录状态与日常 Edge / Chrome 独立，第一次需在应用中自行登录。数据保存在 `%LOCALAPPDATA%\EdgeAnimeGamesHelper\WebView2`，窗口位置在同一父目录的 `window.json`。重复启动会唤回已运行的实例，因此更新前请退出旧版本。

## 环境与边界

Windows 10 / 11 x64，需 .NET Framework 4.8 和 Microsoft Edge WebView2 Evergreen Runtime。缺少运行时会显示 [微软官方下载入口](https://developer.microsoft.com/microsoft-edge/webview2/)。发行包无需 .NET SDK、Node.js 或 Python。

游戏请使用无边框全屏或窗口模式；普通置顶窗口不能保证覆盖独占全屏。网站对付费视频和嵌入式浏览器的限制仍然适用。沉浸仅显示视频，网页另行叠加的弹幕和字幕不保证出现。

程序无浏览器扩展、Native Messaging 宿主、服务或开机启动项。网页不能调用原生宿主对象或原生消息，摄像头、麦克风、位置权限默认拒绝。

旧插件及原助手的源码、安装文件和注册项已清理。`cleanup-old.cmd` 仅保留为旧版清理入口，不删除新版登录数据或共享 WebView2 Runtime。若浏览器列表仍有旧扩展条目，可自行移除；浏览器配置文件未被直接修改。

## 构建与验证

使用 Git 克隆仓库后，在 Windows 项目目录中执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1 -Package
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1
# Windows 限制后台测试窗取得前台时，允许手动激活专用测试窗后继续
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1 -InteractiveFocus
# 当前程序正在运行时，可在其他目录构建，避免覆盖运行中的 EXE
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1 -SkipTests -Package -OutputRoot .\dist\publish
```

构建使用 Windows 自带 C# 编译器，首次从官方 NuGet 获取固定版本 SDK 并检查 SHA-256；测试需要 Node.js。运行目录为 `dist/GuideBrowser/`。只构建和打包时，可使用 `Build.ps1 -SkipTests -Package`。

GitHub Actions 在 Windows 上运行脚本单元测试并编译、打包应用。需要真实桌面的窗口与全局按键检查在本地执行。

测试与实际程序使用同一套控制绑定，通过真实 Windows `SendInput` 验证按键、视频、隐藏释放和窗口吸边，详见 [验收记录](docs/VALIDATION.md)。
