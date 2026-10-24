=====================================================
 FolderSync Tool v5.0  —  LAN backup, transfer, chat
=====================================================

WHAT'S NEW IN v5.0 (all 13 roadmap items)
-----------------------------------------
 1. Deadline reminders      Tasks due within 60 min trigger a tray
                            notification once; overdue tasks ping once more
                            and turn red in the Work tab.
 2. Cancel buttons          "Cancel backup" (Sync tab) and "Cancel send"
                            (LAN Transfer tab) stop the current operation;
                            interrupted LAN sends keep a .fst_part file.
 3. Add PC by IP            LAN Transfer tab: "Add PC by IP..." probes the
                            other machine over TCP, reads its name and adds
                            it permanently (survives restarts; firewalls that
                            block UDP broadcast are no longer a problem).
 4. Per-pair file types      Each pair can have its own filter (empty = use
                            the global filter). Editor has one-click chips
                            for .docx .xlsx .pptx .pdf .jpg .png .zip.
 5. Speed + ETA             The transfer status shows percent, throughput
                            (e.g. 3.2 MB/s) and estimated time remaining.
 6. AES-256 encryption      Settings: "Encrypt LAN transfers" + key
                            generation. Both PCs must share the key
                            (Settings -> Copy on PC 1, paste the key file
                            contents into Settings on PC 2). Encrypts files,
                            chat and tasks. Encrypted sends do not resume.
 7. Checksum + resume       Every LAN file transfer is verified with
                            SHA-256. Interrupted plain transfers resume from
                            where they stopped (.fst_part). Corrupt data is
                            rejected and deleted automatically.
 8. Versioning              Enable "Keep versions" per pair (or for received
                            files): overwritten files are archived to a
                            _versions folder with a timestamp, older copies
                            pruned (count configurable in Settings).
 9. VSS shadow copy         Per-pair option copies LOCKED files (Outlook
                            PST, open databases) through a Volume Shadow
                            Copy. Requires running FolderSyncTool as
                            administrator; falls back to normal copy.
10. Better cloud detection  OneDrive (registry + env), Google Drive and
                            Dropbox folders are resolved more reliably for
                            the destination quick-pick. Files you back up
                            into those folders still sync to the cloud
                            automatically (the cloud client does the upload).
11. Group chat              Chat tab: "All PCs (broadcast)" sends to every
                            discovered PC; messages show the sender name.
12. Task enhancements       Edit and delete tasks, mark done manually,
                            list sorted by deadline, sender shown, DONE
                            confirmations return from the other PC.
13. Dark mode               Settings -> "Dark mode", applies instantly to
                            all open windows and is remembered.

Files, chat and tasks are still stored under:
  %AppData%\FolderSyncTool\settings.ini / tasks.ini
Received LAN files: Documents\FolderSyncTool\Received\<sender>\

-----------------------------------------------------
 "UNKNOWN PUBLISHER" WARNING WHEN RUNNING THE EXE?
-----------------------------------------------------
The exe is NOT digitally signed (a code-signing certificate costs money
per year), so Windows SmartScreen shows "Unknown publisher" the first
time you run it. It is a policy notice, not a virus finding.

Option A - just run it once (recommended for personal use)
  1. In the blue SmartScreen popup click "More info".
  2. A "Run anyway" button appears - click it.
  You see this only on the first run; afterwards Windows remembers.
  If instead a UAC prompt appears, click "Yes".

Option B - self-sign (test machines / lab PCs)
  In PowerShell (admin):
    $c = New-SelfSignedCertificate -Type CodeSigningCert `
          -Subject "CN=FolderSyncTool" -CertStoreLocation Cert:\CurrentUser\My
    $pwd = ConvertTo-SecureString -String "pick-a-password" -Force -AsPlainText
    Export-PfxCertificate -Cert $c -FilePath .\fst.pfx -Password $pwd
    Import-PfxCertificate -FilePath .\fst.pfx -CertStoreLocation Cert:\LocalMachine\Root -Password $pwd
    (download signtool from the Windows SDK, then:)
    signtool sign /fd SHA256 /a /f .\fst.pfx /p pick-a-password FolderSyncTool.exe
  Then re-download/run - publisher shows as FolderSyncTool.

Option C - real code-signing certificate (for distribution to others)
  Buy an OV or EV code-signing certificate (Sectigo, DigiCert, Certum...,
  roughly USD 70-400/yr), then:
    signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 FolderSyncTool.exe
  EV certificates build SmartScreen reputation fastest.

-----------------------------------------------------
 REBUILDING FROM SOURCE
-----------------------------------------------------
src\ contains the full C# source (net48, WinForms).
With the .NET SDK installed, run Build.bat - or:
    dotnet build src -c Release
The app is a single portable exe; no installer needed.

-----------------------------------------------------
 FIRST STEPS
-----------------------------------------------------
1. Sync tab:  "Add pair..." -> pick source and destination (cloud folder
   quick-pick available) -> "Backup now" to test -> set a schedule.
2. LAN tab:   run the tool on both PCs, select a PC, drop files onto the
   list to send. "Settings..." for encryption/versions/dark mode.
3. Chat/Work: pick a PC or "All PCs"; create tasks with deadlines and
   priorities and send them to the other PC.

TIPS
----
* First LAN connection blocked? Allow FolderSyncTool.exe through the
  Windows firewall (private networks) on both PCs, or add peers by IP.
* Encrypted mode: generate the key on one PC only, then use "Copy" and
  paste it into the other PC's Settings - keys must match.
* "Cancel backup" stops the current pair run cleanly; partial temp files
  (.fst_tmp) are removed automatically.
