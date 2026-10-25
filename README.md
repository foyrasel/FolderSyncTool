# FolderSync Tool

**Scheduled incremental backups + LAN file transfer, chat and work tasks — one lightweight Windows tray app.**

![Version](https://img.shields.io/badge/version-5.0-blue) ![Platform](https://img.shields.io/badge/platform-Windows-lightgrey) ![.NET](https://img.shields.io/badge/.NET%20Framework-4.8-purple)

FolderSync Tool keeps your important folders backed up to another drive, a network share or your OneDrive/Google Drive folder on a schedule — and adds fast peer-to-peer file transfer, chat and team task assignment between PCs on the same network. No server, no account, no cloud middleman: your data stays on your machines.

---

## ✨ Features

### 🗂 Backup & Sync tab
- **Multiple backup pairs** — back up as many source → destination folders as you need, each independently enabled/disabled
- **Smart incremental copy** — only copies files that changed (size or timestamp), never re-copies the whole folder
- **Scheduling** — Daily (at a set time), Interval (every N minutes), or run at logoff; missed schedules run automatically at next start
- **Mirror mode** — optionally delete destination files that no longer exist in the source
- **File type filters** — global filter plus per-pair overrides (e.g. back up only `.docx .xlsx .pptx .pdf .jpg .png .zip`); one-click chips in the pair editor
- **File versioning** — keep the last N old versions of every changed file under `<dest>\_versions\` (per-pair, configurable count)
- **VSS shadow copy** — copy files that are locked by other programs (Outlook PSTs, open documents) via Windows Volume Shadow Copy (per-pair option)
- **Cloud-aware** — detects OneDrive / Google Drive folders and offers them as destinations
- **Copy log** — every action is written to a timestamped log; running window shows last result and next scheduled run

### 📡 LAN Transfer tab
- **Auto-discovery** — PCs on the same network find each other automatically (UDP broadcast, port `50110`)
- **Add PC by IP** — for networks that block broadcast: enter the IP, the app probes it and adds it permanently
- **Drag & drop sending** — drop files or whole folders (subfolders preserved) onto the PC list
- **Live progress** — per-file percent, throughput (e.g. `3.2 MB/s`) and estimated time remaining, with **Cancel**
- **Integrity** — every file is verified with SHA-256 on both ends; mismatch is reported loudly
- **Resume** — interrupted sends keep a `.fst_part` file and continue from where they stopped
- **AES-256 encryption** — optional end-to-end encryption of everything on the wire (files, chat, tasks); share the generated key between your PCs

### 💬 Chat tab
- LAN chat with any discovered PC
- **Broadcast** to all PCs at once ("All PCs" recipient)

### ✅ Work tab
- Assign tasks to any PC with **deadline** and **priority** (Low / Normal / High)
- Tasks arrive as notifications; track status (pending / done), sort by deadline, edit or delete
- **Deadline reminders** — tasks due within 60 minutes trigger a tray notification; overdue tasks ping once more and turn red

---

## 🚀 Quick start

1. Download **[`FolderSyncTool.exe`](FolderSyncTool.exe)** (right-click → *Save link as…*) from this repository
2. Put it in any folder — no installation needed
3. Run it. It lives in the system tray; right-click the tray icon for options

> **Windows says "Unknown publisher"?** The exe is not code-signed yet, so SmartScreen shows this notice the first time. Click **More info → Run anyway**, or see *README.txt* for signing options (self-sign or buy a certificate).

### Requirements
- Windows 7 SP1 or newer (Windows 10/11 recommended)
- .NET Framework 4.8 (preinstalled on Windows 10 1903+ and all Windows 11)

### First backup in 30 seconds
1. **Backup & Sync** tab → **Add pair**
2. Pick a source folder and a destination (another drive, USB disk, or a folder inside OneDrive/Google Drive)
3. Optionally set the file type filter and schedule → **Save** → **Run now**

### LAN transfer setup
1. Install and run FolderSync Tool on **both PCs** (same network)
2. Both appear in the **LAN Transfer** list automatically — if not, use **Add PC by IP…**
3. **Windows Firewall** must allow the app on private networks (it requests permission on first run); transfer uses TCP `50111`, discovery UDP `50110`
4. *(Optional but recommended)* Open **Settings → Generate key** on PC 1, copy the key text into Settings on PC 2, and enable *Encrypt LAN transfers* on both

Files you receive land in `Documents\FolderSyncTool\Received\<sender-name>\`.

---

## ⚙️ Where settings live

| What | Where |
|---|---|
| Settings, backup pairs, schedule | `%AppData%\FolderSyncTool\settings.ini` |
| Work tasks | `%AppData%\FolderSyncTool\tasks.ini` |
| Received files | `Documents\FolderSyncTool\Received\<sender>\` |
| Old file versions | `<destination>\_versions\` |
| Transfer parts (resume) | `<destination>\<file>.fst_part` |
| Copy log | shown in-app, also `%AppData%\FolderSyncTool\copy.log` |

---

## 🔨 Building from source

```bash
git clone https://github.com/foyrasel/FolderSyncTool.git
cd FolderSyncTool
dotnet build -c Release
# output: bin/Release/net48/FolderSyncTool.exe
```

Requirements: .NET SDK 8+ (the project targets .NET Framework 4.8 / WinForms, `LangVersion 7.3`). On Windows you can also open the folder in Visual Studio and press F5. `Build.bat` automates the release build and optionally signs the exe with `signtool`.

---

## 📁 Project layout

```
FolderSyncTool.exe         Ready-to-run program
Build.bat                  Release build script (+ optional signing step)
README.txt                 Offline user guide (usage + signing)
app.ico                    Application icon
src/
  Program.cs               Entry point, single-instance mutex
  AppSettings.cs           Settings/pairs INI storage, schedule types
  ScheduleLogic.cs         When should a pair run next?
  SyncEngine.cs            Incremental copy, mirror, versioning, filters
  CloudFolders.cs          OneDrive / Google Drive detection
  WorkItem.cs              Work tasks model + tasks.ini store
  LanDiscovery.cs          UDP discovery, manual peers, TCP name probe
  LanTransfer.cs           TCP transfer protocol, SHA-256, resume, AES
  VssHelper.cs             Volume Shadow Copy snapshot copy
  MainForm.*.cs            UI (Sync / Transfer / Chat / Tasks tabs)
  SettingsForm.cs          Global settings dialog
  PairEditorForm.cs        Add/edit backup pair dialog
  TaskEditorForm.cs        Add/edit work task dialog
```

---

## 📜 Version history

| Version | Highlights |
|---|---|
| **1.0** | Scheduled incremental backup, file type filter, copy log, tray |
| **2.0** | Multiple backup pairs, cloud folder detection |
| **3.0** | Redesigned 4-tab UI, LAN transfer between PCs |
| **4.0** | Chat tab, work tasks with deadline & priority |
| **5.0** | Deadline reminders, cancel buttons, add PC by IP, per-pair filters, speed + ETA, AES-256 encryption, SHA-256 + resume, file versioning, VSS shadow copy, WebDAV groundwork, group broadcast, task editor, dark mode |

---

*© 2026 Rasel Mia — all rights reserved.*
