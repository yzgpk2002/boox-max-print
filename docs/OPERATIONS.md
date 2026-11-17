# 维护、日志与排障

## 0.5.0 新选项排查

- 自动翻页不动：点击开始/继续后，在 3 秒准备时间内点击源网页/文档正文；切换窗口或打开控制窗后需要继续以重新锁定。底部单独显示翻页状态，`logs/scroll.log` 记录锁定和停止原因。系统滚轮设为整页/禁用时，按行模式会提示停止。某些源应用可能不处理定向窗口消息，不能仅凭发送函数返回成功判断已经滚动。Chrome 专用测试见 `tools/test-chrome-scroll.ps1`，同时观察 `tests/fixtures/chrome-scroll.html` 中的计数和位移。
- 全刷：每 N 张实际新图计数；画面不变时不会累积。0 关闭定期全刷，“立即全刷”仍可用。状态 fullRefreshCount 只统计成功调用，fullRefreshError 记录接口错误。BOOX 日志标签 BooxMaxPrint 的 `Full refresh requested` 是调用记录。
- 文件名：检查任务元数据的 documentName、devicePath、titleRepair。`repaired-windows-title-low-byte-loss` 表示命中已知损坏，originalFile 指向保留原件。不要删除原件以排障；跳过原因不表示页面损坏。旧文件或阅读器缓存不会自动改名。
- 定期全刷实机测试：先运行 `tools/make-image-fixtures.ps1`，再运行 `node tools/verify-refresh.mjs`；会用生成测试卡覆盖当前 BOOX 图片，结束后暂停，报告保存在 build。仅在准备验证时执行。
- 交互自动翻页测试：`tools/test-auto-scroll.ps1` 仅创建独立生成文本窗口；需要把该测试窗口置前，会等待最多 60 秒，否则在发输入前失败。它不验证浏览器/Word/WPS 的实现差异。

本文区分开发记录和运行日志：`CHANGELOG.md` 记录功能与修复；`VALIDATION.md` 记录验证证据；`docs/logs` 存放脱敏后的结构化验收数据。实际运行日志在根目录 `logs/`，不进入 Git。

## 1. 文件与保留策略

| 路径 | 内容 | 注意事项 |
|---|---|---|
| `.local/wifi.json` | 设备身份、配对 token、地址、模式 | 私密，不分享完整文件 |
| `.local/image-client.json` | 本机图片桥接 URL/key | 私密，本机发送器读取 |
| `.local/image-settings.json` | 全部配置：间隔、整屏/区域、方向、增强、灰度、对比度、光标、自动翻页、全刷 | 修改和退出时原子保存；首次默认 0.5 秒；不恢复自动运行 |
| `.local/booxmaxprint.keystore` | Android 更新签名 | 私密，丢失会影响覆盖更新 |
| `.local/storage-reserve-baseline.json` | 专用迁移基线与恢复阶段 | 只在执行迁移脚本时存在，不要随意删除 |
| `spool/BOOX-*.pdf` | 电脑保留的打印 PDF | 可能包含用户文档；不自动清理 |
| `spool/job-*.json` | 任务状态、标题、文件、设备路径 | 可能包含文档标题与地址；不直接公开 |
| `logs/printer.log`、`printer-error.log` | 打印 Node 进程 stdout / stderr | 启动脚本重定向；重启会覆盖，不是自动轮转 |
| `logs/images.log`、`images-error.log` | 图片桥接 stdout / stderr | 记录启动/进程错误；不承诺逐帧性能明细 |
| `logs/install-printer.log` | 安装打印机 PowerShell transcript | 追加模式 |
| `logs/*.pid` | 启动脚本保存的进程号 | PID 可能复用；停止前须校验命令行 |
| `build/image-performance.txt` | 手工保存的性能测试输出 | 不是正式应用持续写出的性能日志 |
| `build/*/transfer.json` | 无线性能脚本回执样本 | 测试脚本生成，不能代表物理刷新时刻 |

设备 PDF 位于 `/sdcard/Documents/BooxMaxPrint`；图片只有点“保存”才写 `/sdcard/Pictures/BooxMaxPrint`。接收应用私有状态和签名不在公开仓库中。

当前不提供日志大小轮转、PDF 定期清理或故障上报服务。需要保留故障证据时，先把相关日志复制到本机归档，再重启。清理前确认没有在途任务，且需要的文档已备份。

## 2. 只读健康检查

```powershell
$print = Invoke-RestMethod http://127.0.0.1:8631/health
[pscustomobject]@{Name=$print.name; Uri=$print.uri; RecentJobCount=@($print.jobs).Count}
Invoke-RestMethod http://127.0.0.1:8633/health
node tools/check-wifi.mjs
```

打印 `/health` 的完整响应包含近期任务元数据，可能含标题，公开提问时不要直接粘贴。检查无线命令会输出设备私网地址；分享前可替换为 `<BOOX_IP>`。

读取最近日志：

```powershell
Get-Content logs/printer.log -Tail 50
Get-Content logs/printer-error.log -Tail 50
Get-Content logs/images-error.log -Tail 50
```

典型打印事件：listening、ipp、delivered、delivery-error、queue-error、request-error。事件时间使用 ISO 8601 UTC；文档验收日期使用 Asia/Shanghai，比较时注意时区。`ipp` 表示收到请求，`delivered` 表示传输/打开回执通过，不是用户已读完文档。

发送器的逐帧状态显示在界面，不额外保存每张截图。需要量化性能时，用生成的测试图运行基准，避免把桌面截图作为公开日志。

## 3. 常见故障

| 现象 | 检查与处理 |
|---|---|
| 打印列表没有 BOOX Max | 检查 8631 健康接口；重新运行 Install-Printer.cmd 并完成系统 UAC；不必改默认打印机 |
| BOOX 显示存储不可用 | 关闭 USB 大容量存储共享，保留 USB 调试；确认设备存储已 mounted |
| Wi-Fi 不通 | 同网段/非隔离网络、接收器已启用、设备未被系统强行停止；先 check-wifi，不先删除配对 |
| 无线传输失败后没走 USB | 若上传已经开始，这是防止重复打开的设计；先查看 BOOX 与任务状态再重印 |
| Windows 重启后没启动 | 只支持当前用户登录后；确认启动目录快捷方式存在、项目未移动、Node 可找到、环境变量有效 |
| BOOX 开机没有接收 | 内部安装、enabled=true、Wi-Fi 已连接；旧固件广播可能延后；手动停用或系统强停需明确恢复 |
| 图片暂停后不再刷新 | 接收 PDF、退出图片页面、拖动或放大都可能暂停；回发送端点“继续” |
| 暂停最后画面仍是控制窗 | 确认使用 0.4.1+；暂停应先隐藏，再补送；网络失败时可能保留上一张成功画面 |
| 方向切换短暂异常 | 旧固件重排实测可需数秒，等稳定后检查；切换方向不会自行恢复暂停会话 |
| 0.5 秒仍有延迟 | 检查频率不是刷新保证；区分处理、传输回执与墨水屏；Wi-Fi 样本存在约 2.2 秒长尾 |
| 文字锯齿 | 避免把黑白高对比当作抗锯齿；保留灰度，在源应用放大/选择中等字重；换 BOOX 字体不能改变截图 |
| 光标不显示 | 开启选项、指针位于截图覆盖范围且当前可见；单张文件不叠加；隔离/非交互测试环境可能没有可见光标 |
| 快捷键无效 | Ctrl+Alt+B / Ctrl+Alt+P 可能被别的软件占用，可用按钮或托盘操作 |
| APK 覆盖安装失败 | 检查内部空间、相同签名及安装输出的 Success；旧 adb 可能失败却返回退出码 0 |

## 4. 停止、重启与卸载入口

`Stop-Printer.cmd` 只停止打印 Node 服务，不停止图片桥接，也不删除队列、PDF 或配对。图片发送器通过“退出”停止自动截图并尝试远端暂停。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/remove-startup.ps1
```

这只移除当前用户登录启动，不删除应用或文档。BOOX 接收界面的“停止接收”会记住停用状态。

重启图片桥接前，先退出发送器，按 `logs/images.pid` 找进程并校验它的完整命令行确实属于当前项目的 `desktop/image-server.mjs`；不能按进程名批量结束所有 Node。重新运行 Start-Images.cmd。更换 exe 前先等待旧发送器真正退出，文件复制成功后核对版本再启动，避免文件锁导致旧版被误认为新版。

## 5. 测试日志与实机副作用

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/test-images.ps1 |
  Tee-Object -FilePath build/image-performance.txt
```

算法测试生成合成文字图、逐像素比较和耗时输出。`-Desktop` 读取真实光标和极小截图用于内存测试，不移动鼠标；需交互桌面。测试入口报告失败后以非零码退出，避免测试异常留系统模态框。

真实传输基准：

```powershell
node tools/benchmark-image-transfer.mjs build/optimized-frames
```

它会改变 BOOX 当前图片、开启测试会话并最终暂停。`tools/verify-images.mjs` 还会切换方向并测试拒绝行为；`tools/print-test.ps1` 会生成真实 PDF。这些不是后台健康探针。

## 6. 对外分享记录

可分享版本、操作步骤、失败阶段、脱敏错误文本、耗时统计、生成的测试图。不要附 `.local`、签名密钥、完整 `/health`、用户 PDF、桌面截图或整包日志。公开验收样例在 [docs/logs/acceptance-2026-11-16.json](logs/acceptance-2026-11-16.json)，它是人工整理的事实摘要，不冒充原始遥测。

发布前先仅暂存要公开的文件，再执行 `node tools/check-publication.mjs`。它检查 Git 索引中的禁入目录、常见凭据特征、实际本机配对值、设计图标之外的图片及文档链接，不打印密钥。此检查不能代替对文档内容的人工审核；检测通过后再提交和推送。
