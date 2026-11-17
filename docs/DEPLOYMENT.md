# 从源码部署与构建

本仓库不含作者的配对配置、APK 签名密钥、打印文件或编译产物。克隆后需构建和首次配对。下面使用 PowerShell，从项目根目录执行；示例路径、SDK 版本、设备序列号应换成实际值。

## 1. 环境

| 环境 | 要求与已测情况 |
|---|---|
| Windows | 有 Microsoft IPP Class Driver、Add-Printer 的环境；安装队列需要管理员权限 |
| Node.js | 代码要求 20+，开发机验证为 24.14；无 npm 第三方依赖 |
| .NET Framework | 4.x，含 Framework64 的 csc.exe、WinForms、System.Drawing、HttpClient |
| Android | 实测 BOOX Max Android 4.4.4 / ARMv7 / 1200×1600；原生阅读器组件须匹配 |
| USB 配对 | Android platform-tools / adb，设备已授权 USB 调试、关闭 USB 大容量存储共享 |
| Android 构建 | 已测 JDK 21、Build Tools 35、android-35 android.jar；输出 minSdk 19、targetSdk 22、v1 签名 |
| 网络 | 电脑与 BOOX 能直接访问的同一可信 Wi-Fi；访客网络/AP 隔离可能阻断通信 |

```powershell
git clone https://github.com/yzgpk2002/boox-max-print.git
Set-Location boox-max-print
node --version
node --test tests/*.test.mjs
```

仓库中部分脚本保留最初开发机的 ADB、SDK、JDK 默认路径；换电脑必须显式传参或设置环境变量，不能假定这些路径存在。

## 2. 构建 Windows 发送器

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build-images.ps1
```

输出 `dist/BooxImages.exe`，图标从 `desktop/assets` 嵌入，无需另外复制图标文件。发送器通过 `Start-Images.cmd` 启动，本机桥接先生成 `.local/image-client.json`；不要在首次启动桥接前直接运行 exe。项目位置可变，但应保留 `dist` 与 `desktop`、`.local` 的相对结构。

图标需要修改时，编译 `tools/BuildImageIcons.cs` 为控制台程序，再以 `desktop/assets` 为输出目录执行，最后重新构建发送器。源文件不依赖下载素材。

## 3. 构建 Android APK

项目使用 javac → d8 → aapt → zipalign → apksigner，不使用 Gradle。标准 SDK 布局可显式指定工具和平台 jar：

```powershell
$sdk = 'E:\Android\sdk'
$jdk = 'E:\Java\jdk-21'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build-android.ps1 `
  -SdkRoot $sdk -JdkRoot $jdk `
  -BuildToolsDir "$sdk\build-tools\35.0.0" `
  -AndroidJar "$sdk\platforms\android-35\android.jar"
```

输出 `dist/booxmaxprint-wifi.apk`。未传覆盖参数时，兼容旧开发布局：`<SdkRoot>/android-15/` 存放构建工具、`<SdkRoot>/android-35/android.jar` 为平台 jar；该目录名字不是 Android 最低版本声明。

首次构建会创建 `.local/booxmaxprint.keystore`。它是本地开发签名，脚本使用固定开发密码；不要把密钥发布到仓库。保存同一份密钥才能覆盖更新已安装版本，换签名不能直接覆盖安装。Android 4.4 需要 APK v1 签名，构建脚本已启用并验证。

## 4. 首次 USB 安装与配对

在 BOOX 上连接 Wi-Fi，启用 USB 调试并授权电脑，关闭 USB 存储共享。先列出设备，再明确指定 BOOX 序列号，避免误选其他手机。

```powershell
$adb = 'E:\Android\platform-tools\adb.exe'
& $adb devices
$serial = 'YOUR_BOOX_SERIAL'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/install-receiver.ps1 `
  -Adb $adb -Serial $serial
```

脚本以 `install -r -f` 内部安装，启动接收界面并调用配对脚本。成功后 `.local/wifi.json` 留在电脑，Android 私有配置保存相同 token/deviceId。此后可拔 USB。

只需重新配对时：

```powershell
$env:BOOX_ADB = $adb
$env:BOOX_SERIAL = $serial
node tools/pair-wifi.mjs
node tools/check-wifi.mjs
```

复用现有 `.local/wifi.json` 会保留现有身份；不要通过删除配置来试探故障。无线探测成功后再考虑打印/图片测试。

## 5. 安装打印队列及自启动

如需保留 USB 后备，在启动服务的同一会话设置实际 `BOOX_ADB`、`BOOX_SERIAL`。登录自启动需把值保存为当前用户环境变量；已经运行的进程不会自动获得新环境值。

```powershell
$env:BOOX_ADB = $adb
$env:BOOX_SERIAL = $serial
[Environment]::SetEnvironmentVariable('BOOX_ADB', $adb, 'User')
[Environment]::SetEnvironmentVariable('BOOX_SERIAL', $serial, 'User')
```

双击 `Install-Printer.cmd`。它先启动服务，再请求 UAC 管理员授权添加 `BOOX Max` 队列，不改默认打印机。服务地址为 `http://127.0.0.1:8631/ipp/print`。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/install-startup.ps1
```

这只创建当前用户启动目录快捷方式，不是登录前 Windows 服务。登录后同时启动打印桥接与图片桥接，不自动截图。

Android 接收服务在“已配对、已启用、内部安装”的前提下恢复。已测旧固件需要联网广播启动，开机桌面出现后可能还需等待网络就绪。用户点“停止接收”后不会自动重启；系统“强行停止”后需要再次打开应用。

## 6. 使用及设置

网页 Ctrl+P → BOOX Max → PDF 保存并在 BOOX 原生阅读器打开。Word/WPS 可尝试，但尚未独立验收。

图片：运行 `Start-Images.cmd`，首次默认主显示器整屏，无需框选。选择方向、增强及是否显示光标后点“开始自动”；窗口收至托盘。Ctrl+Alt+B 发送截图，Ctrl+Alt+P 暂停/继续。首次默认 0.5 秒，此后所有配置从 `.local/image-settings.json` 恢复，包括检查间隔、整屏/选区、自动翻页和全刷选项。文件发送不包含鼠标指针。重新打开软件不会自行截图或翻页。

自动翻页可设每多少秒滚动几行或整页；点击目标网页正文后按 Ctrl+Alt+R 直接开始，再次按下只停止翻页，图片继续发送。Ctrl+Alt+P 为整体暂停/继续。也可勾选自动翻页、点开始/继续，在随后 3 秒内点击目标正文。切换窗口、显示控制窗或退出会停止滚动；回到正文后用 R 快捷键重新绑定。地址栏和编辑框不能作为目标。Chrome 普通页面的两种模式已获用户确认；其他源应用和嵌套滚动页面仍需实测。

0.5.4 两端更新后，控制窗口和托盘中的“BOOX 回桌面”可停止发送/翻页并打开设备原生桌面；需要继续图片时点“继续”。托盘找不到时重新运行 Start-Images.cmd，会打开原控制窗口并补回图标。升级旧版时须先退出旧进程，新版激活机制无法唤回不支持该协议的旧进程。

0.5.6 两端更新后，底部显示电脑及 BOOX 的 Wi-Fi 名称与认证连接状态。“重新配对 (USB)”要求连接指定 BOOX 并授权 USB 调试，配对过程不恢复截图；成功后手动开始即可。旧接收器无法提供 BOOX SSID 时显示名称暂不可用。Windows 网络名称读取权限不足也会明确显示，不自动改变系统权限。

全局刷新设为每 N 张新图一次，0 为关闭；“立即全刷”在暂停时也可用。该功能需更新 Android 接收器至 0.5.0；旧接收器不支持新增控制指令，升级时两端一起更新。0.1/0.2 秒仅缩短检查周期，不保证物理帧率。已有中文乱码文件请重新打印，不自动覆盖旧文件或清除阅读器缓存。

| 环境变量 | 作用 |
|---|---|
| BOOX_ADB / BOOX_SERIAL | USB 后备与配对的明确目标 |
| BOOX_PORT | IPP 端口，默认 8631；安装队列地址需一致 |
| BOOX_SPOOL | 电脑打印存储目录，默认项目 spool |
| BOOX_WIFI_CONFIG | 配对配置路径，默认 `.local/wifi.json` |
| BOOX_TRANSPORT | PDF 的 auto / wifi / usb，覆盖配置 mode |
| BOOX_IMAGE_PORT | 图片桥接端口，默认 8633；现有启动探测脚本固定 8633，改端口需同步修改 |
| BOOX_TEST_MODE | 仅协议测试设为 1，跳过真实 PDF 投递；日常使用必须不设置 |

## 7. 更新与回滚

先退出图片发送器，再构建 exe，避免 Windows 文件锁。不要删除 `.local`：其中含配对、签名与图片设置。更新桥接源码需要重启对应进程才生效。更新 APK 使用相同签名和 `install -r -f`，保留应用数据。

回滚时保留上一版 exe/APK/源码及同一签名；不要靠清空配对或卸载应用解决普通更新问题。部署迁移和测试命令的副作用见 [维护与日志](OPERATIONS.md)。

`tools/install-internal-with-reserve.ps1` 是特定旧固件存储告警的专用迁移工具：临时修改存储预留并重启两次，结束恢复基线。它不是正常安装步骤，不能在未确认对应问题的其他设备上直接使用。

## 8. 验证

```powershell
node --test tests/*.test.mjs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/test-images.ps1
```

原生光标与隐藏窗口生命周期检查必须在交互式 Windows 桌面执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/test-images.ps1 -Desktop
```

`tools/print-test.ps1` 会创建真实两页打印任务；`tools/verify-images.mjs` 会改变 BOOX 画面/方向并在结束暂停，不应当作无副作用的连通性检查。真机图片验证前先执行 `tools/make-image-fixtures.ps1`。详细范围见 [VALIDATION.md](VALIDATION.md)。
